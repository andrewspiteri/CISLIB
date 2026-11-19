using System.Net;
using System.Security.Cryptography.X509Certificates;
using CIS.CommonTesting;
using CIS.Security;
using CIS.Security.Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
public sealed class CertificateReceivingTests
{
    [TestMethod]
    public async Task RealNativeTlsPermitsCompleteGrantAndDeniesValidUngrantedIdentityWithoutEffect()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 permitted = certificates.Leaf(certificates.Root);
        X509Certificate2 denied = certificates.Leaf(certificates.Root, CertificateFixture.DeniedIdentity);
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        using HttpClient allowedClient = certificates.Client(permitted, server.BusinessPort);
        using HttpClient deniedClient = certificates.Client(denied, server.BusinessPort);
        using (HttpResponseMessage response = await allowedClient.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.AreEqual(1, server.Effects);
        Assert.AreEqual(new SecuritySubject(CertificateFixture.Domain, CertificateFixture.Identity), server.LastCaller?.Subject);
        Assert.AreEqual(InstanceEvidenceKind.NotProvided, server.LastCaller?.Instance.Kind);
        Assert.AreEqual(new DateTimeOffset(permitted.NotAfter.ToUniversalTime()), server.LastCaller?.Authentication.ExpiresAtUtc);
        using (HttpResponseMessage response = await deniedClient.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        Assert.AreEqual(1, server.Effects);
        SecurityProfileStatus status = server.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single();
        Assert.AreEqual(SecurityProfileAvailability.Available, status.Availability);
        Assert.IsNull(status.CredentialExpiresAtUtc, "A receiver-only profile has no outgoing leaf requirement.");
    }

    [DataRow("wrong-root")]
    [DataRow("wrong-san")]
    [DataRow("encoded-san")]
    [DataRow("missing-eku")]
    [DataRow("ca-leaf")]
    [DataRow("wrong-key-usage")]
    [DataRow("expired")]
    [DataRow("online-unavailable")]
    [TestMethod]
    public async Task RealTlsOrNativeAuthenticationRejectsInvalidClientsWithoutExecutingBusiness(string invalid)
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 issuer = invalid == "wrong-root" ? certificates.Authority("Untrusted client root") : certificates.Root;
        string identity = invalid == "wrong-san" ? "https://identity.example.test/applications/unmapped/environments/testing"
            : invalid == "encoded-san" ? CertificateFixture.Identity.Replace("caller", "%63aller", StringComparison.Ordinal)
            : CertificateFixture.Identity;
        X509Certificate2 leaf = certificates.Leaf(issuer, identity,
            notAfter: invalid == "expired" ? certificates.Now.AddMinutes(-1) : null,
            clientEku: invalid != "missing-eku", ca: invalid == "ca-leaf",
            usage: invalid == "wrong-key-usage" ? X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature);
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates,
            certificates.Trust(invalid == "online-unavailable" ? CertificateRevocationPolicy.Online : CertificateRevocationPolicy.ShortLived));
        using HttpClient client = certificates.Client(leaf, server.BusinessPort);
        try
        {
            using HttpResponseMessage response = await client.GetAsync("/business");
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, invalid);
        }
        catch (HttpRequestException)
        {
            // TLS rejection has no HTTP status. Do not invent a capability-denied wire result.
        }
        Assert.AreEqual(0, server.Effects, invalid);
    }

    [TestMethod]
    public async Task ExpiryIsRecheckedOnTheSameEstablishedTlsConnectionUsingCommonTestingClock()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 leaf = certificates.Leaf(certificates.Root, notAfter: certificates.Now.AddMinutes(2));
        var clock = new ManualTimeProvider(certificates.Now);
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates, clock: clock);
        using HttpClient client = certificates.Client(leaf, server.BusinessPort);
        using (HttpResponseMessage initial = await client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode);
        }
        string? established = server.LastConnection;
        Assert.IsNotNull(established);
        clock.Advance(TimeSpan.FromMinutes(3));
        using (HttpResponseMessage expired = await client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, expired.StatusCode);
        }
        Assert.AreEqual(1, server.Effects);
        Assert.AreEqual(established, server.LastObservedConnection,
            "The denied operation itself must arrive on the same established native TLS connection.");
    }

    [TestMethod]
    public async Task OperationalListenerCannotRunBusinessEvenWithSpoofedHostAndReservedProtectedPath()
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        using var probe = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.OperationalPort}") };
        using (HttpResponseMessage health = await probe.GetAsync("/_health"))
        {
            Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        }
        foreach (string path in new[] { "/business", "/_health/ready" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Host = $"localhost:{server.BusinessPort}";
            using HttpResponseMessage forbidden = await probe.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        Assert.AreEqual(0, server.Effects);
    }
}
