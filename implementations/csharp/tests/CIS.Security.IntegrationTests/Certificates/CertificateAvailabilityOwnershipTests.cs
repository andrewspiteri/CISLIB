using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Security;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
[DoNotParallelize]
public sealed class CertificateAvailabilityOwnershipTests
{
    [TestMethod]
    public async Task ConcurrentCutoffAndProviderDisposalWaitForClosePrologueAndRetainItsOriginalFault()
    {
        using var certificates = new CertificateFixture();
        await using var heartbeat = new ManualHeartbeat();
        await using var health = new HealthcheckService(new());
        using var lifetime = new ControlledLifetime();
        var expected = new InvalidOperationException("owned close prologue fault");
        var factory = new HeldCloseFactory(heartbeat, expected);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHeartbeatOwnerFactory>(factory);
        services.AddSingleton<IHealthMonitorFactory>(health);
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        var common = CertificateFixture.CommonPolicy();
        SecurityProfile original = common.Profiles.Single();
        common.Profiles.Clear();
        common.Profiles.Add(new(original.Alias, original.Kind, original.Roles, original.AuthenticationScheme,
            original.IdentityMappings, requiredForReadiness: true));
        services.AddCisSecurity(common);
        services.AddCisCertificateAuthentication(CertificateFixture.Profile, CertificateFixture.Scheme,
            new CertificateInboundOptions(certificates.Trust()));
        ServiceProvider provider = services.BuildServiceProvider();
        foreach (IStartable participant in provider.GetServices<IStartable>())
        {
            await participant.StartAsync(CancellationToken.None);
        }
        Task cutoff = Task.Run(lifetime.StopApplication);
        Task? disposal = null;
        try
        {
            await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            disposal = Task.Run(async () => await provider.DisposeAsync());
            // This holds an actual user close prologue while the actual provider tries disposal.
            // The old path entered owner.Dispose before that prologue returned, then blocked later
            // in CancellationTokenRegistration.Dispose; task incompletion alone was insufficient.
            await Task.WhenAny(factory.Disposed.Task, Task.Delay(TimeSpan.FromMilliseconds(150)));
            Assert.IsFalse(factory.Disposed.Task.IsCompleted,
                "The owner must not be disposed while its first close prologue still owns work.");
        }
        finally
        {
            factory.Release.TrySetResult();
            await cutoff.WaitAsync(TimeSpan.FromSeconds(10));
            if (disposal is not null)
            {
                InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => disposal);
                Assert.AreSame(expected, observed);
            }
            else
            {
                await provider.DisposeAsync();
            }
        }
        Assert.IsTrue(factory.Disposed.Task.IsCompleted);
    }

    [TestMethod]
    public async Task OutgoingReadinessExpiresWithItsClientIssuerWhileTheActualLeafGaugeExpiryRemainsLater()
    {
        using var certificates = new CertificateFixture();
        using RSA rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Short client authority " + Guid.NewGuid().ToString("N"), rootKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using X509Certificate2 root = rootRequest.CreateSelfSigned(certificates.Now.AddMinutes(-5), certificates.Now.AddHours(1));
        using RSA leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=Longer signed leaf", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(CertificateFixture.Identity));
        leafRequest.CertificateExtensions.Add(names.Build());
        // The native signature-generator overload deliberately emits a valid-now leaf longer
        // than its issuer. A requested leaf duration cannot establish issuer validity.
        using X509Certificate2 publicLeaf = leafRequest.Create(root.SubjectName,
            X509SignatureGenerator.CreateForRSA(rootKey, RSASignaturePadding.Pkcs1), certificates.Now.AddMinutes(-1),
            certificates.Now.AddHours(2), RandomNumberGenerator.GetBytes(16));
        using X509Certificate2 leaf = publicLeaf.CopyWithPrivateKey(leafKey);
        await using var source = new PemSourceFixture(certificates, "https://localhost:44444", required: true);
        File.WriteAllText(source.RootFile, root.ExportCertificatePem());
        source.Write(leaf);
        await source.Host.StartAsync();
        Assert.IsTrue(source.Ready);
        source.Clock.Advance(TimeSpan.FromMinutes(90));
        await source.PollAsync(metadataOnly: true);
        Assert.AreEqual(SecurityProfileAvailability.Expired, source.Status.Availability);
        Assert.IsFalse(source.Ready);
        Assert.AreEqual(leaf.NotAfter.ToUniversalTime(), source.Status.CredentialExpiresAtUtc?.UtcDateTime);
        Assert.IsTrue(source.Status.CredentialExpiresAtUtc > source.Clock.GetUtcNow(),
            "The status/gauge retains actual leaf expiry rather than substituting authority expiry.");
    }

    private sealed class ControlledLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => stopping.Cancel();
        public void Dispose() => stopping.Dispose();
    }

    private sealed class HeldCloseFactory(IHeartbeatOwnerFactory inner, Exception failure) : IHeartbeatOwnerFactory
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IHeartbeatOwner CreateOwner() => new HeldOwner(inner.CreateOwner(), this, failure);

        private sealed class HeldOwner(IHeartbeatOwner innerOwner, HeldCloseFactory owner, Exception failure) : IHeartbeatOwner
        {
            private int closeCalls;
            public IHeartbeatService Service => innerOwner.Service;
            public Task CloseAsync(CancellationToken waitCancellation = default)
            {
                if (Interlocked.Increment(ref closeCalls) == 1)
                {
                    owner.Entered.TrySetResult();
                    owner.Release.Task.GetAwaiter().GetResult();
                    throw failure;
                }
                return innerOwner.CloseAsync(waitCancellation);
            }
            public async ValueTask DisposeAsync()
            {
                owner.Disposed.TrySetResult();
                await innerOwner.DisposeAsync();
            }
        }
    }
}
