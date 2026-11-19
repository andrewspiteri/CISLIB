using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Hosting;
using CIS.Startup.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

internal sealed class PemSourceFixture : IAsyncDisposable
{
    internal const string Profile = "pem-source";
    private readonly string directory;
    private readonly ManualHeartbeat heartbeat = new();
    private readonly HealthcheckService health;
    private bool disposed;
    private RecordingSecurityReporter? reporter;

    internal PemSourceFixture(CertificateFixture certificates, string origin, SecurityProfileRoles roles = SecurityProfileRoles.Outbound,
        bool required = false, bool writeCredential = true, Action<IServiceCollection>? configure = null,
        Action<RestClientOptions>? configureClient = null, ManualTimeProvider? clock = null)
    {
        Clock = clock ?? new(certificates.Now);
        directory = Path.Combine(Path.GetTempPath(), "pem-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        RootFile = Path.Combine(directory, "client-root.pem");
        ServerRootFile = Path.Combine(directory, "server-root.pem");
        ChainFile = Path.Combine(directory, "client-chain.pem");
        KeyFile = Path.Combine(directory, "client-key.pem");
        File.WriteAllText(RootFile, certificates.Root.ExportCertificatePem());
        File.WriteAllText(ServerRootFile, certificates.ServerRoot.ExportCertificatePem());
        if (writeCredential)
        {
            Initial = certificates.Leaf(certificates.Root);
            Write(Initial);
        }
        health = new(new HealthcheckOptions(), timeProvider: Clock);
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddSingleton<IHeartbeatOwnerFactory>(heartbeat);
        builder.Services.AddSingleton(health);
        builder.Services.AddSingleton<IHealthMonitorFactory>(health);
        builder.Services.AddSingleton<IHealthSnapshotSource>(health);
        var policy = new SecurityOptions { PolicyVersion = "pem-source-fixture" };
        policy.Profiles.Add(new(Profile, SecurityProfileKind.ClientCertificate, roles,
            authenticationScheme: (roles & SecurityProfileRoles.Inbound) != 0 ? "pem-receiver" : null,
            identityMappings: [new(new(CertificateFixture.Domain, CertificateFixture.Identity), "caller", "Testing")],
            outboundIdentity: (roles & SecurityProfileRoles.Outbound) != 0
                ? new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                    new(CertificateFixture.Domain, CertificateFixture.Identity)) : null,
            allowedOrigins: (roles & SecurityProfileRoles.Outbound) != 0 ? [origin] : null,
            requiredForReadiness: required));
        builder.Services.AddCisSecurity(policy);
        ServiceDescriptor original = builder.Services.Last(descriptor => descriptor.ServiceType == typeof(ISecurityProfileStatusReporter));
        builder.Services.Remove(original);
        builder.Services.AddSingleton<ISecurityProfileStatusReporter>(provider => reporter = new(
            (ISecurityProfileStatusReporter)original.ImplementationFactory!(provider)));
        builder.Services.AddCisCertificatePemSecurity(Profile, _ =>
        {
            FactoryCalls++;
            return new(new(CertificateFixture.Domain, RootFile, RootFile, revocation: CertificateRevocationPolicy.ShortLived),
                credential: new(ChainFile, KeyFile), serverTrustBundleFile: ServerRootFile,
                serverRevocationMode: X509RevocationMode.NoCheck);
        }, (roles & SecurityProfileRoles.Inbound) != 0 ? "pem-receiver" : null,
            _ => SecurityTestAssemblySetup.NativeKeys);
        if ((roles & SecurityProfileRoles.Outbound) != 0)
        {
            var client = new RestClientOptions("pem") { BaseUrl = origin, ResponseFormat = RestResponseFormat.Text, LogCalls = false };
            configureClient?.Invoke(client);
            builder.Services.AddCisCertificateRestClient(client, Profile);
            builder.Services.AddCisCertificateRestClient(new("pem-second")
            {
                BaseUrl = origin,
                ResponseFormat = RestResponseFormat.Text,
                LogCalls = false
            }, Profile);
        }
        configure?.Invoke(builder.Services);
        builder.Services.AddCisStartup();
        Host = builder.Build();
    }

    internal IHost Host
    {
        get;
    }
    internal ManualTimeProvider Clock
    {
        get;
    }
    internal X509Certificate2? Initial
    {
        get;
    }
    internal string RootFile
    {
        get;
    }
    internal string ServerRootFile
    {
        get;
    }
    internal string ChainFile
    {
        get;
    }
    internal string KeyFile
    {
        get;
    }
    internal int FactoryCalls
    {
        get; private set;
    }
    internal IRestClient Client => Host.Services.GetRequiredKeyedService<IRestClient>("pem");
    internal IRestClient SecondClient => Host.Services.GetRequiredKeyedService<IRestClient>("pem-second");
    internal IReadOnlyList<SecurityCertificateReloadOutcome> Reloads => reporter?.Snapshot() ?? [];
    internal bool Ready => health.CaptureSnapshot().Ready;
    internal SecurityProfileStatus Status => Host.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single();

    internal void Write(X509Certificate2 leaf, X509Certificate2? keyOf = null)
    {
        File.WriteAllText(ChainFile, leaf.ExportCertificatePem());
        using RSA key = (keyOf ?? leaf).GetRSAPrivateKey()!;
        File.WriteAllText(KeyFile, key.ExportPkcs8PrivateKeyPem());
    }

    internal async Task PollAsync(bool metadataOnly = false)
    {
        string name = (metadataOnly ? "Security expiry: " : "Certificate source: ") + Profile;
        ManualHeartbeatRegistration registration = heartbeat.Registrations.Single(value => value.Options.Name == name);
        ManualHeartbeatResult result = await heartbeat.TriggerAsync(registration.Subscriber);
        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, result.Outcome, result.Error?.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            await Host.StopAsync();
        }
        finally
        {
            try
            {
                if (Host is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync();
                }
                else
                {
                    Host.Dispose();
                }
            }
            finally
            {
                await heartbeat.DisposeAsync();
                await health.DisposeAsync();
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RecordingSecurityReporter(ISecurityProfileStatusReporter inner) : ISecurityProfileStatusReporter
    {
        private readonly object gate = new();
        private readonly List<SecurityCertificateReloadOutcome> transitions = [];

        public void Publish(SecurityProfileStatus status) => inner.Publish(status);

        public void RecordCertificateReload(string profile, SecurityCertificateReloadOutcome outcome)
        {
            lock (gate)
            {
                if (transitions.Count == 64)
                {
                    transitions.RemoveAt(0);
                }
                transitions.Add(outcome);
            }
            inner.RecordCertificateReload(profile, outcome);
        }

        internal IReadOnlyList<SecurityCertificateReloadOutcome> Snapshot()
        {
            lock (gate)
            {
                return transitions.ToArray();
            }
        }
    }
}
