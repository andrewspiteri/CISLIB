using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.Security.Entra;

[TestClass]
public sealed class EntraReadinessRecoveryTests
{
    [TestMethod]
    public async Task RequiredStartupFailureRecoversThroughHeartbeatWhileTheSameNativeHostBlocksBusiness()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        int healthy = 0;
        using var credential = new ControlledWorkloadCredential(_ => Volatile.Read(ref healthy) == 0
            ? ValueTask.FromException<AccessToken>(new CredentialUnavailableException("temporary fixture failure"))
            : ValueTask.FromResult(new AccessToken("usable-recovery", clock.GetUtcNow().AddMinutes(5))));
        await using ReadinessHost host = await ReadinessHost.StartAsync(credential, clock);

        using (HttpResponseMessage blocked = await host.Client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        }
        Assert.AreEqual(0, host.Effects);
        Assert.IsFalse(host.Ready);
        Assert.AreEqual(1, credential.Calls);

        Volatile.Write(ref healthy, 1);
        clock.Advance(TimeSpan.FromSeconds(1));
        ManualHeartbeatResult recovered = await host.CheckAsync();

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, recovered.Outcome);
        Assert.IsTrue(host.Ready);
        Assert.AreEqual(SecurityProfileAvailability.Available, host.Status.Availability);
        using (HttpResponseMessage permitted = await host.Client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.OK, permitted.StatusCode);
        }
        Assert.AreEqual(1, host.Effects);
        Assert.AreEqual(2, credential.Calls, "Recovery runs outside the blocked business endpoint, without restarting the host.");
    }

    [TestMethod]
    public async Task IdleExpiryIsNotProviderFailureAndAnActualFailedProbeCanRecoverOnTheSameNativeHost()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var credential = new ControlledWorkloadCredential(async _ =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 2)
            {
                entered.TrySetResult();
                await release.Task;
                throw new CredentialUnavailableException("temporary post-idle fixture failure");
            }
            return new AccessToken("usable-idle-recovery", clock.GetUtcNow().Add(call == 1 ? TimeSpan.FromSeconds(5) : TimeSpan.FromMinutes(5)));
        });
        await using ReadinessHost host = await ReadinessHost.StartAsync(credential, clock);
        using (HttpResponseMessage initial = await host.Client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode);
        }
        DateTimeOffset observedExpiry = host.Status.CredentialExpiresAtUtc!.Value;
        clock.Advance(TimeSpan.FromSeconds(10));
        Task<ManualHeartbeatResult> checking = host.CheckAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(observedExpiry <= clock.GetUtcNow());
            Assert.IsTrue(host.Ready, "Expiry of the last SDK result alone is not evidence that replacement acquisition is unavailable.");
            Assert.AreEqual(SecurityProfileAvailability.Available, host.Status.Availability);
        }
        finally
        {
            release.TrySetResult();
            await checking;
        }
        Assert.AreEqual(SecurityProfileAvailability.Unavailable, host.Status.Availability,
            "The actual SDK failure, rather than the idle timestamp, establishes unavailability.");
        Assert.AreEqual(observedExpiry, host.Status.CredentialExpiresAtUtc);
        using (HttpResponseMessage blocked = await host.Client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        }
        Assert.AreEqual(1, host.Effects);

        clock.Advance(TimeSpan.FromSeconds(1));
        ManualHeartbeatResult recovered = await host.CheckAsync();

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, recovered.Outcome);
        Assert.IsTrue(host.Ready);
        using (HttpResponseMessage permitted = await host.Client.GetAsync("/business"))
        {
            Assert.AreEqual(HttpStatusCode.OK, permitted.StatusCode);
        }
        Assert.AreEqual(2, host.Effects);
        Assert.AreEqual(3, credential.Calls);
        Assert.IsTrue(host.Status.CredentialExpiresAtUtc > clock.GetUtcNow());
    }

    // This fixture exists specifically to exercise the facade's real native readiness middleware.
    private sealed class ReadinessHost : IAsyncDisposable
    {
        private readonly string directory;
        private readonly CISApplication application;
        private readonly ManualHeartbeat heartbeat = new(capacity: 8);
        private HttpClient? client;
        private int effects;

        private ReadinessHost(WorkloadIdentityCredential credential, ManualTimeProvider clock)
        {
            directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "entra-readiness-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            Assembly assembly = typeof(EntraReadinessRecoveryTests).Assembly;
            File.Copy(assembly.Location, Path.Combine(directory, "Host.dll"));
            string manifest = Path.Combine(directory, "host.types.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                hostAssembly = assembly.FullName,
                assemblies = new[] { new { assemblyName = assembly.FullName, relativePath = "Host.dll" } }
            }));
            application = CISApplication.CreateWeb([], builder =>
            {
                builder.TypeManager = new TypeManagerOptions(assembly)
                {
                    AssemblyManifestPath = manifest,
                    PrecomputedContracts = [typeof(IConfigurationObject)]
                };
                builder.EnableOptionalIntegrations = false;
                builder.LoggingEnabled = false;
                builder.HeartbeatEnabled = false;
                builder.Host.Logging.ClearProviders();
                builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
                builder.Services.AddSingleton<TimeProvider>(clock);
                builder.Services.AddSingleton<IHeartbeatOwnerFactory>(heartbeat);
                LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
                var policy = new SecurityOptions { PolicyVersion = "required-entra-readiness" };
                policy.Profiles.Add(new(EntraNativeAuthenticationTests.Profile, SecurityProfileKind.EntraWorkloadIdentity,
                    SecurityProfileRoles.Outbound, outboundIdentity: new(local.Application, local.Environment,
                        new(EntraNativeAuthenticationTests.Issuer, EntraNativeAuthenticationTests.ObjectId)),
                    allowedOrigins: ["https://provider.invalid"], requiredForReadiness: true));
                builder.Services.AddCisSecurity(policy);
                builder.Services.AddCisEntraSecurity(EntraNativeAuthenticationTests.Profile, _ => new()
                {
                    TenantId = EntraNativeAuthenticationTests.Tenant,
                    Outbound = new()
                    {
                        ClientId = EntraNativeAuthenticationTests.ClientId,
                        ServicePrincipalObjectId = EntraNativeAuthenticationTests.ObjectId,
                        FederatedSubject = "system:serviceaccount:country-demo:country-worker",
                        FederatedTokenFile = Path.Combine(directory, "unused-borrowed-assertion"),
                        Scope = "api://country-api/.default"
                    }
                }, borrowedCredential: _ => credential);
            }, web => web.MapGet("/business", () =>
            {
                Interlocked.Increment(ref effects);
                return Results.Ok();
            }));
        }

        internal HttpClient Client => client ?? throw new InvalidOperationException("Await native host Startup.");
        internal int Effects => Volatile.Read(ref effects);
        internal bool Ready => application.Services.GetRequiredService<IHealthSnapshotSource>().CaptureSnapshot().Ready;
        internal SecurityProfileStatus Status => application.Services.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus().Single();

        internal static async Task<ReadinessHost> StartAsync(WorkloadIdentityCredential credential, ManualTimeProvider clock)
        {
            var host = new ReadinessHost(credential, clock);
            try
            {
                await host.application.StartAsync();
                string address = host.application.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.Single();
                host.client = new() { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        internal Task<ManualHeartbeatResult> CheckAsync() => heartbeat.TriggerAsync(heartbeat.Registrations.Single().Subscriber);

        public async ValueTask DisposeAsync()
        {
            client?.Dispose();
            try
            {
                await application.StopAsync();
            }
            finally
            {
                try
                {
                    await application.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        await heartbeat.DisposeAsync();
                    }
                    finally
                    {
                        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Path.GetFileName(directory)));
                        Assert.AreEqual(expected, directory);
                        File.Delete(Path.Combine(directory, "host.types.json"));
                        File.Delete(Path.Combine(directory, "Host.dll"));
                        Directory.Delete(directory);
                    }
                }
            }
        }
    }
}
