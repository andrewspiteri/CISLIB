using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Hosting;
using CIS.Startup.Hosting;
using TemporaryDirectory = CIS.TestSupport.TemporaryDirectory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
[DoNotParallelize]
public sealed class CertificateBootstrapPreparationTests
{
    [TestMethod]
    public async Task ActualPreProviderTlsAndFinalMeteredTransportRetainTrustAndRenewTheCurrentLeaf()
    {
        using var certificates = new CertificateFixture();
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates,
            business: context => context.Response.WriteAsync(context.Connection.ClientCertificate!.Thumbprint));
        using var files = new TemporaryDirectory();
        var clock = new ManualTimeProvider(certificates.Now);
        string origin = $"https://localhost:{server.BusinessPort}";
        X509Certificate2 initial = certificates.Leaf(certificates.Root);
        CertificatePemSecurityOptions options = WriteInputs(files.Path, certificates, initial);
        SecurityPolicySnapshot policy = Policy(origin);
        int baseline = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
        await using CertificatePemBootstrapPreparation prepared = await CertificatePemBootstrapPreparation.PrepareAsync(
            policy, PemSourceFixture.Profile, options, borrowedNativeKeys: SecurityTestAssemblySetup.NativeKeys, clock: clock);
        Assert.AreEqual(baseline + 1, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
        await using (var bootstrap = new HttpRestClient(ClientOptions("bootstrap", origin, prepared.Binding), prepared.Transport))
        {
            RestResult<string> result = await bootstrap.Get<string>("/business");
            Assert.IsTrue(result.Success, result.Context.FailureKind.ToString());
            Assert.AreEqual(initial.Thumbprint, result.Value);
        }
        await prepared.RetireBootstrapAsync();
        Assert.AreEqual(baseline, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
        File.Delete(options.ClientTrust.RootBundleFile);
        File.Delete(options.ServerTrustBundleFile!);

        await using var heartbeat = new ManualHeartbeat(capacity: 8);
        await using var health = new HealthcheckService(new(), timeProvider: clock);
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        Register(builder.Services, prepared, heartbeat, health, origin);
        IHost host = builder.Build();
        try
        {
            await prepared.ActivateFinalAsync(host.Services);
            await host.StartAsync();
            Assert.AreSame(policy, host.Services.GetRequiredService<SecurityPolicySnapshot>());
            IRestClient client = host.Services.GetRequiredKeyedService<IRestClient>("final");
            Assert.AreEqual(initial.Thumbprint, (await client.Get<string>("/business")).Value);
            string? initialConnection = server.LastConnection;
            X509Certificate2 replacement = certificates.Leaf(certificates.Root);
            WriteLeaf(options, replacement);
            ManualHeartbeatRegistration source = heartbeat.Registrations.Single(registration =>
                registration.Options.Name == "Certificate source: " + PemSourceFixture.Profile);
            Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, (await heartbeat.TriggerAsync(source.Subscriber)).Outcome);
            RestResult<string> renewed = await client.Get<string>("/business");
            Assert.IsTrue(renewed.Success);
            Assert.AreEqual(replacement.Thumbprint, renewed.Value);
            Assert.AreNotEqual(initialConnection, server.LastConnection);
            Assert.IsTrue(health.CaptureSnapshot().Ready);
        }
        finally
        {
            await DisposeHostAsync(host);
        }
        Assert.AreEqual(baseline, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectedSecondProviderCannotDisposeTheLegitimatePreparedOwner(bool differentPolicy)
    {
        using var certificates = new CertificateFixture();
        using var files = new TemporaryDirectory();
        const string origin = "https://localhost:44444";
        CertificatePemSecurityOptions options = WriteInputs(files.Path, certificates, certificates.Leaf(certificates.Root));
        await using CertificatePemBootstrapPreparation prepared = await CertificatePemBootstrapPreparation.PrepareAsync(
            Policy(origin), PemSourceFixture.Profile, options, borrowedNativeKeys: SecurityTestAssemblySetup.NativeKeys);
        await using var heartbeat = new ManualHeartbeat(capacity: 8);
        await using var health = new HealthcheckService(new());
        var firstServices = new ServiceCollection();
        Register(firstServices, prepared, heartbeat, health, origin);
        await using ServiceProvider first = firstServices.BuildServiceProvider();
        _ = first.GetRequiredKeyedService<IRestClient>("final");
        var rejectedServices = new ServiceCollection();
        Register(rejectedServices, prepared, heartbeat, health, origin);
        if (differentPolicy)
        {
            rejectedServices.AddSingleton(Policy(origin));
        }
        await using (ServiceProvider rejected = rejectedServices.BuildServiceProvider())
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => rejected.GetRequiredKeyedService<IRestClient>("final"));
        }
        Assert.IsFalse(prepared.Transport.IsAdmissionClosed, "The rejected provider never obtains disposal ownership.");
        await prepared.RetireBootstrapAsync();
    }

    [TestMethod]
    public async Task PassiveConstructionAllowsPreparationButActivationRefusesAnUnretiredBootstrapBeforeHostObservers()
    {
        using var certificates = new CertificateFixture();
        using var files = new TemporaryDirectory();
        const string origin = "https://localhost:44444";
        CertificatePemSecurityOptions options = WriteInputs(files.Path, certificates, certificates.Leaf(certificates.Root));
        await using CertificatePemBootstrapPreparation prepared = await CertificatePemBootstrapPreparation.PrepareAsync(
            Policy(origin), PemSourceFixture.Profile, options, borrowedNativeKeys: SecurityTestAssemblySetup.NativeKeys);
        await using var heartbeat = new ManualHeartbeat(capacity: 8);
        await using var health = new HealthcheckService(new());
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        Register(builder.Services, prepared, heartbeat, health, origin);
        IHost host = builder.Build();
        try
        {
            _ = host.Services.GetRequiredKeyedService<IRestClient>("final");
            Assert.IsEmpty(heartbeat.Registrations);
            await Assert.ThrowsAsync<Exception>(() => host.StartAsync());
            Assert.IsEmpty(heartbeat.Registrations, "Failed handoff adds no expiry or source owner.");
        }
        finally
        {
            await DisposeHostAsync(host);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnusableInitialPemAndCanceledPreparationLeaveNoNativeKeyOwner(bool cancel)
    {
        using var certificates = new CertificateFixture();
        using var files = new TemporaryDirectory();
        CertificatePemSecurityOptions options = WriteInputs(files.Path, certificates, certificates.Leaf(certificates.Root));
        File.WriteAllText(options.Credential!.PrivateKeyFile, "not private material");
        int baseline = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
        using var cancellation = new CancellationTokenSource();
        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => CertificatePemBootstrapPreparation.PrepareAsync(
                Policy("https://localhost:44444"), PemSourceFixture.Profile, options,
                borrowedNativeKeys: SecurityTestAssemblySetup.NativeKeys, cancellationToken: cancellation.Token));
        }
        else
        {
            AuthenticationException error = await Assert.ThrowsExactlyAsync<AuthenticationException>(() =>
                CertificatePemBootstrapPreparation.PrepareAsync(Policy("https://localhost:44444"), PemSourceFixture.Profile,
                    options, borrowedNativeKeys: SecurityTestAssemblySetup.NativeKeys));
            Assert.DoesNotContain(files.Path, error.Message);
            Assert.IsNull(error.InnerException);
        }
        Assert.AreEqual(baseline, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
    }

    [TestMethod]
    public async Task BootstrapRetirementJoinsTheActualHeldNativeBodyBeforeReleasingItsLeaf()
    {
        using var certificates = new CertificateFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates, business: async context =>
        {
            context.Response.ContentLength = 2;
            await context.Response.WriteAsync("a");
            await context.Response.Body.FlushAsync();
            entered.TrySetResult();
            await release.Task;
            await context.Response.WriteAsync("b");
        });
        using var files = new TemporaryDirectory();
        string origin = $"https://localhost:{server.BusinessPort}";
        CertificatePemSecurityOptions options = WriteInputs(files.Path, certificates, certificates.Leaf(certificates.Root));
        int baseline = SecurityTestAssemblySetup.NativeKeys.LiveKeyCount;
        await using CertificatePemBootstrapPreparation prepared = await CertificatePemBootstrapPreparation.PrepareAsync(
            Policy(origin), PemSourceFixture.Profile, options, borrowedNativeKeys: SecurityTestAssemblySetup.NativeKeys);
        await using var bootstrap = new HttpRestClient(ClientOptions("held-bootstrap", origin, prepared.Binding), prepared.Transport);
        Task<RestResult<string>> call = bootstrap.Get<string>("/business");
        Task? retiring = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            retiring = prepared.RetireBootstrapAsync();
            Assert.IsFalse(retiring.IsCompleted);
            Assert.AreEqual(baseline + 1, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
        }
        finally
        {
            release.TrySetResult();
            await call;
            if (retiring is not null)
            {
                await retiring;
            }
        }
        Assert.AreEqual(baseline, SecurityTestAssemblySetup.NativeKeys.LiveKeyCount);
    }

    private static void Register(IServiceCollection services, CertificatePemBootstrapPreparation prepared,
        ManualHeartbeat heartbeat, HealthcheckService health, string origin)
    {
        services.AddLogging();
        services.AddSingleton<TimeProvider>(prepared.TimeProvider);
        services.AddSingleton<IHeartbeatOwnerFactory>(heartbeat);
        services.AddSingleton(health);
        services.AddSingleton<IHealthMonitorFactory>(health);
        services.AddCisSecurity(prepared.Policy);
        services.AddCisCertificatePemSecurity(prepared);
        services.AddCisCertificateRestClient(ClientOptions("final", origin), PemSourceFixture.Profile);
        services.AddCisStartup();
    }

    private static RestClientOptions ClientOptions(string alias, string origin, RestSecurityBinding? binding = null)
        => new(alias)
        {
            BaseUrl = origin,
            ResponseFormat = RestResponseFormat.Text,
            LogCalls = false,
            SecurityBinding = binding
        };

    private static SecurityPolicySnapshot Policy(string origin)
    {
        var options = new SecurityOptions { PolicyVersion = "pre-provider-pem" };
        options.Profiles.Add(new(PemSourceFixture.Profile, SecurityProfileKind.ClientCertificate, SecurityProfileRoles.Outbound,
            outboundIdentity: new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                new(CertificateFixture.Domain, CertificateFixture.Identity)), allowedOrigins: [origin], requiredForReadiness: true));
        return SecurityPolicySnapshot.Create(options);
    }

    private static CertificatePemSecurityOptions WriteInputs(string directory, CertificateFixture certificates, X509Certificate2 leaf)
    {
        string root = Path.Combine(directory, "client-root.pem");
        string serverRoot = Path.Combine(directory, "server-root.pem");
        File.WriteAllText(root, certificates.Root.ExportCertificatePem());
        File.WriteAllText(serverRoot, certificates.ServerRoot.ExportCertificatePem());
        var options = new CertificatePemSecurityOptions(new(CertificateFixture.Domain, root, root,
            revocation: CertificateRevocationPolicy.ShortLived),
            new(Path.Combine(directory, "client-chain.pem"), Path.Combine(directory, "client-key.pem")),
            serverTrustBundleFile: serverRoot, serverRevocationMode: X509RevocationMode.NoCheck);
        WriteLeaf(options, leaf);
        return options;
    }

    private static void WriteLeaf(CertificatePemSecurityOptions options, X509Certificate2 leaf)
    {
        File.WriteAllText(options.Credential!.ChainFile, leaf.ExportCertificatePem());
        using RSA key = leaf.GetRSAPrivateKey()!;
        File.WriteAllText(options.Credential.PrivateKeyFile, key.ExportPkcs8PrivateKeyPem());
    }

    private static async Task DisposeHostAsync(IHost host)
    {
        try
        {
            await host.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
