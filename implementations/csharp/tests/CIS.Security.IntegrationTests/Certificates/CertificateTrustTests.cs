using System.Security.Cryptography.X509Certificates;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
public sealed class CertificateTrustTests
{
    [TestMethod]
    public void TrustSnapshotsPublicDerAndRequiresTheActualAuthorizedDirectIssuer()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 issuer = certificates.Authority("Approved issuer", certificates.Root);
        X509Certificate2 otherIssuer = certificates.Authority("Unapproved issuer", certificates.Root);
        X509Certificate2 approved = certificates.Leaf(issuer);
        X509Certificate2 unapproved = certificates.Leaf(otherIssuer);
        var roots = new List<X509Certificate2> { certificates.Root };
        var issuers = new List<X509Certificate2> { issuer };
        var policy = new CertificateTrustPolicy(CertificateFixture.Domain, roots, issuers,
            [otherIssuer], CertificateRevocationPolicy.ShortLived);
        roots.Clear();
        issuers.Clear();

        CertificateValidationResult success = CertificateTrustValidator.Validate(approved, policy, certificates.Now);
        Assert.IsTrue(success.Succeeded);
        Assert.AreEqual(new SecuritySubject(CertificateFixture.Domain, CertificateFixture.Identity), success.Subject);
        Assert.AreEqual(new DateTimeOffset(approved.NotAfter.ToUniversalTime()), success.ExpiresAtUtc);
        Assert.IsFalse(CertificateTrustValidator.Validate(unapproved, policy, certificates.Now).Succeeded,
            "An ExtraStore certificate helps construction but cannot authorize another leaf issuer.");
    }

    [TestMethod]
    public void SameDistinguishedNameFromAnotherRootCannotAuthenticate()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 impostorRoot = certificates.Authority(
            certificates.Root.GetNameInfo(X509NameType.SimpleName, forIssuer: false), uniqueName: false);
        Assert.AreEqual(certificates.Root.SubjectName.Name, impostorRoot.SubjectName.Name,
            "This adversarial issuer deliberately keeps the same DN with a different signing key.");
        X509Certificate2 leaf = certificates.Leaf(impostorRoot);
        CertificateValidationResult result = CertificateTrustValidator.Validate(leaf, certificates.Trust(), certificates.Now);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(SecurityFailureKind.CredentialInvalid, result.Failure?.Kind);
    }

    [DataRow("missing-san")]
    [DataRow("conflicting-san")]
    [DataRow("duplicate-san")]
    [DataRow("missing-client-eku")]
    [DataRow("ca-leaf")]
    [DataRow("no-digital-signature")]
    [DataRow("expired")]
    [DataRow("future")]
    [DataRow("too-long")]
    [TestMethod]
    public void IssuedLeafRestrictionsAreEnforced(string invalid)
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 leaf = certificates.Leaf(certificates.Root,
            identity: invalid == "missing-san" ? null : CertificateFixture.Identity,
            notBefore: invalid == "future" ? certificates.Now.AddHours(1) : certificates.Now.AddMinutes(-5),
            notAfter: invalid == "expired" ? certificates.Now.AddMinutes(-1)
                : invalid == "too-long" ? certificates.Now.AddDays(2) : certificates.Now.AddHours(12),
            ca: invalid == "ca-leaf", clientEku: invalid != "missing-client-eku",
            usage: invalid == "no-digital-signature" ? X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature,
            secondIdentity: invalid == "conflicting-san" ? CertificateFixture.DeniedIdentity
                : invalid == "duplicate-san" ? CertificateFixture.Identity : null);

        CertificateValidationResult result = CertificateTrustValidator.Validate(leaf, certificates.Trust(), certificates.Now);
        Assert.IsFalse(result.Succeeded, invalid);
        Assert.AreEqual(invalid == "expired" ? SecurityFailureKind.CredentialExpired : SecurityFailureKind.CredentialInvalid,
            result.Failure?.Kind);
        Assert.IsNull(result.Subject);
    }

    [TestMethod]
    public void ShortLivedChecksActualIssuedIntervalAndItsSmallerConfiguredCeiling()
    {
        using var certificates = new CertificateFixture();
        DateTimeOffset start = new(certificates.Now.Year, certificates.Now.Month, certificates.Now.Day,
            certificates.Now.Hour, certificates.Now.Minute, 0, TimeSpan.Zero);
        X509Certificate2 exact = certificates.Leaf(certificates.Root, notBefore: start, notAfter: start.AddHours(24));
        X509Certificate2 over = certificates.Leaf(certificates.Root, notBefore: start, notAfter: start.AddHours(24).AddSeconds(1));
        Assert.IsTrue(CertificateTrustValidator.Validate(exact, certificates.Trust(), certificates.Now).Succeeded);
        Assert.IsFalse(CertificateTrustValidator.Validate(over, certificates.Trust(), certificates.Now).Succeeded);
        var smaller = new CertificateTrustPolicy(CertificateFixture.Domain, [certificates.Root], [certificates.Root],
            revocation: CertificateRevocationPolicy.ShortLived, maximumShortLivedLifetime: TimeSpan.FromHours(1));
        Assert.IsFalse(CertificateTrustValidator.Validate(exact, smaller, certificates.Now).Succeeded);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CertificateTrustPolicy(CertificateFixture.Domain,
            [certificates.Root], [certificates.Root], revocation: CertificateRevocationPolicy.ShortLived,
            maximumShortLivedLifetime: TimeSpan.FromDays(2)));
    }

    [TestMethod]
    public async Task OnlineUnknownFailsBeforeNativeTicketEvenThoughTheHandlerBaselineUsesNoCheck()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 leaf = certificates.Leaf(certificates.Root);
        CertificateTrustPolicy online = certificates.Trust(CertificateRevocationPolicy.Online);
        CertificateValidationResult strict = CertificateTrustValidator.Validate(leaf, online, certificates.Now);
        Assert.IsFalse(strict.Succeeded, "The generated private issuer publishes no native revocation status.");
        Assert.AreEqual(SecurityFailureKind.ValidationUnavailable, strict.Failure?.Kind);

        var services = new ServiceCollection();
        CertificateFixture.Register(services, new(online));
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        foreach (IStartable participant in provider.GetServices<IStartable>())
        {
            await participant.StartAsync(CancellationToken.None);
        }
        Assert.AreEqual(X509RevocationMode.NoCheck,
            provider.GetRequiredService<IOptionsMonitor<CertificateAuthenticationOptions>>().Get(CertificateFixture.Scheme).RevocationMode);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Connection.ClientCertificate = leaf;
        AuthenticateResult authentication = await context.AuthenticateAsync(CertificateFixture.Scheme);
        Assert.IsFalse(authentication.Succeeded, "A real native handler cannot issue a ticket using its baseline alone.");
        Assert.IsNull(authentication.Ticket);
    }

    [TestMethod]
    public async Task RemovingTheStrictNativeEventOrInstallingAResultCacheFailsActivation()
    {
        using var certificates = new CertificateFixture();
        var services = new ServiceCollection();
        CertificateFixture.Register(services, new(certificates.Trust()));
        services.Configure<CertificateAuthenticationOptions>(CertificateFixture.Scheme,
            options => options.Events = new CertificateAuthenticationEvents());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            foreach (IStartable participant in provider.GetServices<IStartable>())
            {
                await participant.StartAsync(CancellationToken.None);
            }
        });

        var cached = new ServiceCollection();
        CertificateFixture.Register(cached, new(certificates.Trust()));
        new AuthenticationBuilder(cached).AddCertificateCache();
        await using ServiceProvider cachedProvider = cached.BuildServiceProvider();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            foreach (IStartable participant in cachedProvider.GetServices<IStartable>())
            {
                await participant.StartAsync(CancellationToken.None);
            }
        });
    }

    [TestMethod]
    public async Task NativeTicketRequiresExactMappedUriAndLateOptionsCannotRemoveTheStrictEvent()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 unmapped = certificates.Leaf(certificates.Root,
            CertificateFixture.Identity.Replace("caller", "%63aller", StringComparison.Ordinal));
        var services = new ServiceCollection();
        CertificateFixture.Register(services, new(certificates.Trust()));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Connection.ClientCertificate = unmapped;
        AuthenticateResult result = await context.AuthenticateAsync(CertificateFixture.Scheme);
        Assert.IsFalse(result.Succeeded, "Equivalent URI decoding cannot create an exact trusted identity or a native ticket.");

        var late = new ServiceCollection();
        CertificateFixture.Register(late, new(certificates.Trust()));
        late.PostConfigure<CertificateAuthenticationOptions>(CertificateFixture.Scheme,
            options => options.Events = new CertificateAuthenticationEvents());
        await using ServiceProvider lateProvider = late.BuildServiceProvider();
        Assert.ThrowsExactly<OptionsValidationException>(() => lateProvider
            .GetRequiredService<IOptionsMonitor<CertificateAuthenticationOptions>>().Get(CertificateFixture.Scheme));
    }

    [TestMethod]
    public async Task ReceivingStartupRejectsAnUnanchoredIssuerAndAnExpiredAuthoritySnapshot()
    {
        using var certificates = new CertificateFixture();
        X509Certificate2 unanchored = certificates.Authority("Different issuing root");
        var trust = new CertificateTrustPolicy(CertificateFixture.Domain, [certificates.Root], [unanchored],
            revocation: CertificateRevocationPolicy.ShortLived);
        foreach (bool expired in new[] { false, true })
        {
            var clock = new CIS.CommonTesting.ManualTimeProvider(certificates.Now);
            if (expired)
            {
                clock.Advance(TimeSpan.FromDays(31));
            }
            var services = new ServiceCollection();
            CertificateFixture.Register(services, new(expired ? certificates.Trust() : trust), clock);
            await using ServiceProvider provider = services.BuildServiceProvider();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                foreach (IStartable participant in provider.GetServices<IStartable>())
                {
                    await participant.StartAsync(CancellationToken.None);
                }
            });
        }
    }
}
