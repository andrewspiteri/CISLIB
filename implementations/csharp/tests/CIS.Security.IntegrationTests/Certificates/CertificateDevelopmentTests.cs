using System.Net;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
public sealed class CertificateDevelopmentTests
{
    [DataRow("canonical-environment")]
    [DataRow("native-production")]
    [DataRow("production-profile")]
    [TestMethod]
    public async Task DevelopmentActivationUsesCanonicalIdentityAndActualNativeHostEnvironment(string mismatch)
    {
        using var certificates = new CertificateFixture();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = mismatch == "native-production" ? Environments.Production : Environments.Development
        });
        builder.Logging.ClearProviders();
        var gate = new CertificateDevelopmentGate(mismatch == "canonical-environment" ? "NotTheCanonicalEnvironment"
            : EnvironmentOptions.EnvironmentName, [Environments.Development]);
        CertificateFixture.Register(builder.Services, new(certificates.Trust(CertificateRevocationPolicy.Development), gate),
            kind: mismatch == "production-profile" ? SecurityProfileKind.ClientCertificate : SecurityProfileKind.LocalDevelopment);
        builder.Services.AddCisStartup();
        using IHost host = builder.Build();
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [TestMethod]
    public async Task ExplicitDevelopmentReceiverStartsWithoutAnOutgoingLeafAndDoesNotActivateProduction()
    {
        using var certificates = new CertificateFixture();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Logging.ClearProviders();
        var gate = new CertificateDevelopmentGate(EnvironmentOptions.EnvironmentName, [Environments.Development]);
        CertificateFixture.Register(builder.Services, new(certificates.Trust(CertificateRevocationPolicy.Development), gate),
            kind: SecurityProfileKind.LocalDevelopment);
        builder.Services.AddCisStartup();
        using IHost host = builder.Build();
        try
        {
            await host.StartAsync();
            Assert.AreEqual(SecurityProfileAvailability.Available,
                host.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single().Availability);
        }
        finally
        {
            await host.StopAsync();
        }
        Assert.ThrowsExactly<ArgumentException>(() => new CertificateDevelopmentGate("unknown", [Environments.Development]));
        Assert.ThrowsExactly<ArgumentException>(() => new CertificateDevelopmentGate(EnvironmentOptions.EnvironmentName, [Environments.Production]));
        Assert.ThrowsExactly<ArgumentException>(() => new CertificateDevelopmentGate(EnvironmentOptions.EnvironmentName,
            [Environments.Development], [IPAddress.Parse("8.8.8.8")]));
        Assert.ThrowsExactly<ArgumentException>(() => new CertificateInboundOptions(certificates.Trust(CertificateRevocationPolicy.Development)));
    }

    [TestMethod]
    public void ListenerSelectionUsesActualAddressAndPortAndRejectsAmbiguousOrWildcardRoles()
    {
        var policy = new CertificateListenerPolicy([new(IPAddress.Loopback, 8443, CertificateFixture.Profile)],
            [new(IPAddress.Loopback, 8080)]);
        var request = new DefaultHttpContext();
        request.Connection.LocalIpAddress = IPAddress.Loopback;
        request.Connection.LocalPort = 8080;
        request.Request.Host = new HostString("localhost", 8443);
        Assert.IsNull(policy.SelectProfile(request));
        request.Connection.LocalPort = 8443;
        request.Request.Host = new HostString("attacker.example", 8080);
        Assert.AreEqual(CertificateFixture.Profile, policy.SelectProfile(request));
        request.Connection.LocalIpAddress = IPAddress.Parse("127.0.0.2");
        Assert.IsNull(policy.SelectProfile(request));
        Assert.ThrowsExactly<ArgumentException>(() => new CertificateListenerPolicy(
            [new(IPAddress.Loopback, 8443, CertificateFixture.Profile)], [new(IPAddress.Loopback, 8443)]));
        Assert.ThrowsExactly<ArgumentException>(() => new CertificateListenerBinding(IPAddress.Any, 8443, CertificateFixture.Profile));
    }

    [TestMethod]
    public async Task IsolatedDevelopmentUsesRealTlsAndItsCredentialCannotEnterTheProductionTrustDomain()
    {
        using var development = new CertificateFixture();
        using var production = new CertificateFixture();
        var leaf = development.Leaf(development.Root, notAfter: development.Now.AddDays(7));
        var gate = new CertificateDevelopmentGate(EnvironmentOptions.EnvironmentName, [Environments.Development]);
        await using CertificateTestServer local = await CertificateTestServer.StartAsync(development,
            development.Trust(CertificateRevocationPolicy.Development), development: gate, hostEnvironment: Environments.Development);
        await using CertificateTestServer service = await CertificateTestServer.StartAsync(production);
        using HttpClient localClient = development.Client(leaf, local.BusinessPort);
        using (HttpResponseMessage accepted = await localClient.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        }
        using HttpClient productionClient = production.Client(leaf, service.BusinessPort);
        try
        {
            using HttpResponseMessage rejected = await productionClient.GetAsync("/business");
            Assert.AreEqual(HttpStatusCode.Forbidden, rejected.StatusCode);
        }
        catch (HttpRequestException)
        {
            // The actual production TLS trust may reject before an HTTP response exists.
        }
        Assert.AreEqual(1, local.Effects);
        Assert.AreEqual(0, service.Effects);
    }

    [DataRow("127.0.0.1", false, true)]
    [DataRow("10.10.1.2", false, false)]
    [DataRow("10.10.1.2", true, true)]
    [DataRow("10.10.1.3", true, false)]
    [TestMethod]
    public async Task NativeDevelopmentEventChecksActualConnectionAddressWithExactPrivateOptIn(string address,
        bool allowPrivate, bool expected)
    {
        using var certificates = new CertificateFixture();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development
        });
        builder.Logging.ClearProviders();
        var gate = new CertificateDevelopmentGate(EnvironmentOptions.EnvironmentName, [Environments.Development],
            allowPrivate ? [IPAddress.Parse("10.10.1.2")] : null);
        CertificateFixture.Register(builder.Services, new(certificates.Trust(CertificateRevocationPolicy.Development), gate),
            kind: SecurityProfileKind.LocalDevelopment);
        using IHost host = builder.Build();
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Connection.LocalIpAddress = IPAddress.Parse(address);
        context.Connection.ClientCertificate = certificates.Leaf(certificates.Root);
        AuthenticateResult result = await context.AuthenticateAsync(CertificateFixture.Scheme);
        Assert.AreEqual(expected, result.Succeeded,
            "This native handler boundary test supplies connection facts; the separate TLS test proves real loopback negotiation.");
    }
}
