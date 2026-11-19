using System.Net;
using System.Security.Cryptography.X509Certificates;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
[DoNotParallelize]
public sealed class CertificatePemSourceTests
{
    [TestMethod]
    public async Task RegistrationAndClientConstructionDoNotReadTrustOrCreateNativeKeysBeforeStartup()
    {
        using var certificates = new CertificateFixture();
        await using var source = new PemSourceFixture(certificates, "https://localhost:44444", writeCredential: false);
        File.Delete(source.RootFile);
        File.Delete(source.ServerRootFile);
        int live = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
        Assert.AreEqual(0, source.FactoryCalls);
        RestResult<string> result = await source.Client.Get<string>("/business");
        Assert.AreEqual(1, source.FactoryCalls);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(RestFailureKind.Authentication, result.Context.FailureKind);
        Assert.AreEqual(SecurityFailureKind.CredentialUnavailable, result.Context.SecurityFailure?.Kind);
        Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.AreEqual(live, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
    }

    [TestMethod]
    public async Task ActualPemAdoptionServesTwoBorrowingAliasesAndReadsTrustOnlyOnce()
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}");
        await source.Host.StartAsync();
        Assert.IsTrue((await source.Client.Get<string>("/business")).Success);
        Assert.IsTrue((await source.SecondClient.Get<string>("/business")).Success);
        Assert.AreEqual(1, source.FactoryCalls);
        CollectionAssert.AreEqual(new[] { SecurityCertificateReloadOutcome.Adopted }, source.Reloads.ToArray());
        Assert.AreEqual(source.Initial!.NotAfter.ToUniversalTime(), source.Status.CredentialExpiresAtUtc?.UtcDateTime);
        File.Delete(source.RootFile);
        File.Delete(source.ServerRootFile);
        source.Write(certificates.Leaf(certificates.Root));
        await source.PollAsync();
        Assert.IsTrue((await source.Client.Get<string>("/business")).Success,
            "Credential adoption uses the immutable startup trust; it must not reread deleted trust files.");
    }

    [DataRow("key-mismatch")]
    [DataRow("wrong-identity")]
    [DataRow("wrong-root")]
    [DataRow("malformed")]
    [DataRow("oversized-key")]
    [DataRow("oversized-chain")]
    [TestMethod]
    public async Task BadReplacementKeepsTheValidCurrentGenerationAndReportsOneMaterialRejection(string invalid)
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}", required: true);
        await source.Host.StartAsync();
        X509Certificate2 replacement = certificates.Leaf(invalid == "wrong-root" ? certificates.Authority("Wrong replacement issuer") : certificates.Root,
            invalid == "wrong-identity" ? CertificateFixture.DeniedIdentity : CertificateFixture.Identity);
        source.Write(replacement, invalid == "key-mismatch" ? source.Initial : null);
        if (invalid == "malformed")
        {
            File.WriteAllText(source.KeyFile, "not a private key");
        }
        if (invalid == "oversized-key")
        {
            File.WriteAllBytes(source.KeyFile, new byte[64 * 1024 + 1]);
        }
        if (invalid == "oversized-chain")
        {
            File.WriteAllBytes(source.ChainFile, new byte[256 * 1024 + 1]);
        }
        int live = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
        await source.PollAsync();
        await source.PollAsync();
        Assert.AreEqual(live, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
        Assert.AreEqual(SecurityProfileAvailability.Available, source.Status.Availability);
        Assert.IsTrue(source.Ready, "A bad replacement does not invalidate the still usable current leaf.");
        Assert.IsTrue((await source.Client.Get<string>("/business")).Success);
        CollectionAssert.AreEqual(new[] { SecurityCertificateReloadOutcome.Adopted, SecurityCertificateReloadOutcome.Rejected }, source.Reloads.ToArray());
    }

    [TestMethod]
    public async Task FutureNotBeforeCandidateIsRetriedWithoutFileChangeAndExpiredMetadataDoesNoSourceIo()
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}", required: true);
        await source.Host.StartAsync();
        X509Certificate2 future = certificates.Leaf(certificates.Root, notBefore: certificates.Now.AddMinutes(2),
            notAfter: certificates.Now.AddHours(2));
        source.Write(future);
        await source.PollAsync();
        CollectionAssert.AreEqual(new[] { SecurityCertificateReloadOutcome.Adopted, SecurityCertificateReloadOutcome.Rejected }, source.Reloads.ToArray());
        source.Clock.Advance(TimeSpan.FromMinutes(3));
        await source.PollAsync();
        Assert.AreEqual(future.NotAfter.ToUniversalTime(), source.Status.CredentialExpiresAtUtc?.UtcDateTime);
        Assert.AreEqual(SecurityCertificateReloadOutcome.Adopted, source.Reloads.Last());
        File.Delete(source.ChainFile);
        File.Delete(source.KeyFile);
        int transitions = source.Reloads.Count;
        source.Clock.Advance(TimeSpan.FromHours(3));
        await source.PollAsync(metadataOnly: true);
        Assert.AreEqual(SecurityProfileAvailability.Expired, source.Status.Availability);
        Assert.IsFalse(source.Ready);
        Assert.AreEqual(transitions, source.Reloads.Count, "Expiry metadata does not touch source files or emit reload outcomes.");
    }

    [TestMethod]
    public async Task MissingInitialOutgoingLeafCanRecoverThroughTheExistingHeartbeatAndRequiredHealth()
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates);
        await using var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}", required: true,
            writeCredential: false);
        await source.Host.StartAsync();
        Assert.IsFalse(source.Ready);
        Assert.AreEqual(SecurityProfileAvailability.Unavailable, source.Status.Availability);
        Assert.AreEqual(SecurityFailureKind.CredentialUnavailable, (await source.Client.Get<string>("/business")).Context.SecurityFailure?.Kind);
        source.Write(certificates.Leaf(certificates.Root));
        await source.PollAsync();
        Assert.IsTrue(source.Ready);
        Assert.IsTrue((await source.Client.Get<string>("/business")).Success);
    }

    [DataRow(SecurityProfileRoles.Inbound)]
    [DataRow(SecurityProfileRoles.Both)]
    [TestMethod]
    public async Task EnabledRolesShareReadinessWithoutInventingAnIncomingOnlyOutgoingCredential(SecurityProfileRoles roles)
    {
        using var certificates = new CertificateFixture();
        await using var source = new PemSourceFixture(certificates, "https://localhost:44444", roles, required: true,
            writeCredential: roles == SecurityProfileRoles.Both);
        await source.Host.StartAsync();
        Assert.IsTrue(source.Ready);
        Assert.AreEqual(SecurityProfileAvailability.Available, source.Status.Availability);
        if (roles == SecurityProfileRoles.Inbound)
        {
            Assert.IsNull(source.Status.CredentialExpiresAtUtc);
            Assert.AreEqual(0, source.Reloads.Count);
        }
        File.Delete(source.RootFile);
        source.Clock.Advance(TimeSpan.FromDays(31));
        await source.PollAsync(metadataOnly: true);
        Assert.IsFalse(source.Ready);
        Assert.AreEqual(SecurityProfileAvailability.Expired, source.Status.Availability);
    }

    [DataRow("/_metrics", true)]
    [DataRow("/metrics", false)]
    [TestMethod]
    public async Task OperationalListenerDefaultsUseTheActualCollectorPath(string path, bool allowed)
    {
        var services = new ServiceCollection();
        await using ServiceProvider provider = services.BuildServiceProvider();
        var application = new ApplicationBuilder(provider);
        var policy = new CertificateListenerPolicy([new(IPAddress.Loopback, 8443, "fixture")], [new(IPAddress.Loopback, 8080)]);
        bool entered = false;
        application.UseCisCertificateListenerIsolation(policy);
        application.Run(_ => { entered = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Connection.LocalPort = 8080;
        context.Request.Path = path;
        await application.Build()(context);
        Assert.AreEqual(allowed, entered);
        Assert.AreEqual(allowed ? 200 : 403, context.Response.StatusCode);
    }

    [TestMethod]
    public async Task DeferredTrustCannotAcquireMaterialUntilItsSingleExplicitInitialization()
    {
        var binding = new RestSecurityBinding("deferred", new("test", "identity"), "policy",
            RestSecurityCredentialKind.ClientCertificate, ["https://localhost:44444"]);
        await using RestHttpTransport transport = RestHttpTransport.CreateCertificateBoundUninitializedTrust(binding);
        int factories = 0;
        await Assert.ThrowsAsync<Exception>(async () => await transport.TryAdoptCertificateAsync(_ =>
        {
            factories++;
            throw new AssertFailedException("An unprepared owner cannot construct material.");
        }));
        Assert.AreEqual(0, factories);
        transport.InitializeServerTrustOnce(null);
        Assert.ThrowsExactly<InvalidOperationException>(() => transport.InitializeServerTrustOnce(null));
    }
}
