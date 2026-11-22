using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Images;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Infrastructure.Containers;

// Only selected service fixtures access these lazy owners. Discovery and fast tests perform no Docker I/O.
internal static class IntegrationServices
{
    internal const string RunLabel = "cislib.integration.run";
    internal const string OwnerLabel = "cislib.integration.owner";
    internal const string PostgreSqlImage = "postgres@sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba";
    internal const string RabbitMqImage = "rabbitmq@sha256:c987060f0120cfda8d1d0d1ef7725c9c8ced6a18e3efd558ac3e10419d5437b5";
    internal const string ReaperImage = "testcontainers/ryuk:0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0";
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    internal static string RunId { get; } = Guid.NewGuid().ToString("N");

    private static readonly object ProfileGate = new();
    private static readonly SemaphoreSlim DedicatedSlot = new(1, 1);
    private static readonly Lazy<Task<OwnedPostgresService>> Postgres = new(OwnedPostgresService.CreateSharedAsync);
    private static readonly Lazy<Task<OwnedRabbitService>> Rabbit = new(OwnedRabbitService.CreateSharedAsync);
    private static bool configured;

    internal static Task<OwnedPostgresService> GetPostgresAsync() => Postgres.Value;

    internal static Task<OwnedRabbitService> GetRabbitAsync() => Rabbit.Value;

    internal static async Task<IAsyncDisposable> AcquireDedicatedSlotAsync(CancellationToken cancellationToken)
    {
        await DedicatedSlot.WaitAsync(cancellationToken);
        return new DedicatedLease();
    }

    internal static void EnsureProfile()
    {
        lock (ProfileGate)
        {
            if (configured)
            {
                return;
            }
            if (!TestcontainersSettings.ResourceReaperEnabled)
            {
                throw new InvalidOperationException("Selected service tests require the Testcontainers Resource Reaper; remove the reaper-disabled override.");
            }
            TestcontainersSettings.ResourceReaperPrivilegedModeEnabled = false;
            TestcontainersSettings.ResourceReaperImage = new DockerImage(ReaperImage);
            TestcontainersSettings.WaitStrategyTimeout = TimeSpan.FromMinutes(2);
            IDockerEndpointAuthenticationConfiguration? endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
            if (endpoint is null)
            {
                throw new InvalidOperationException("Selected service tests require an available configured local Linux Docker engine or Docker Desktop in Linux-container mode.");
            }
            if (endpoint.Endpoint.Scheme is not ("unix" or "npipe") && !IsLoopback(endpoint.Endpoint.Host))
            {
                throw new InvalidOperationException("This service-test profile supports a configured local Docker engine. A non-loopback Docker endpoint needs a separately qualified transport-security profile.");
            }
            configured = true;
        }
    }

    internal static void RequireLoopback(string hostname)
    {
        if (!IsLoopback(hostname))
        {
            throw new InvalidOperationException("The owned service must resolve to loopback in this local test profile; arbitrary ambient endpoints are not accepted.");
        }
    }

    internal static void ConfigureLocalContainer(CreateContainerParameters parameters, int processLimit)
    {
        parameters.HostConfig ??= new();
        parameters.HostConfig.Memory = 512L * 1024 * 1024;
        parameters.HostConfig.MemorySwap = 512L * 1024 * 1024;
        parameters.HostConfig.NanoCPUs = 2_000_000_000;
        parameters.HostConfig.PidsLimit = processLimit;
        parameters.HostConfig.LogConfig = new()
        {
            Type = "json-file",
            Config = new Dictionary<string, string> { ["max-size"] = "4m", ["max-file"] = "2" }
        };
        if (parameters.HostConfig.PortBindings is null || parameters.HostConfig.PortBindings.Count == 0)
        {
            throw new InvalidOperationException("The typed service must declare its mapped protocol port before creation.");
        }
        foreach (IList<PortBinding> bindings in parameters.HostConfig.PortBindings.Values)
        {
            foreach (PortBinding binding in bindings)
            {
                binding.HostIP = "127.0.0.1";
            }
        }
    }

    internal static async Task VerifyAbsentAsync(string? containerId, string? volumeName, string? ownerId = null)
    {
        using var cancellation = new CancellationTokenSource(CleanupTimeout);
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder().Build();
        if (!string.IsNullOrEmpty(containerId))
        {
            try
            {
                await client.Containers.InspectContainerAsync(containerId, cancellation.Token);
                throw new InvalidOperationException("The explicitly disposed owned container is still present.");
            }
            catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
        if (!string.IsNullOrEmpty(volumeName))
        {
            try
            {
                await client.Volumes.InspectAsync(volumeName, cancellation.Token);
                throw new InvalidOperationException("The explicitly disposed owned volume is still present.");
            }
            catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
        if (ownerId is not null)
        {
            // Cancellation can lose a create response. Exact owner labels also cover an unobserved partial creation.
            var filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool>
                {
                    [RunLabel + "=" + RunId] = true,
                    [OwnerLabel + "=" + ownerId] = true
                }
            };
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = filters }, cancellation.Token);
            var volumes = await client.Volumes.ListAsync(new VolumesListParameters { Filters = filters }, cancellation.Token);
            if (containers.Count != 0 || volumes.Volumes?.Count > 0)
            {
                throw new InvalidOperationException("Resources carrying this exact service owner are still present after cleanup.");
            }
        }
    }

    internal static async Task DisposeAsync()
    {
        var errors = new List<Exception>();
        if (Rabbit.IsValueCreated)
        {
            await DisposeCreatedAsync(Rabbit.Value, errors);
        }
        if (Postgres.IsValueCreated)
        {
            await DisposeCreatedAsync(Postgres.Value, errors);
        }
        if (errors.Count != 0)
        {
            throw new AggregateException("Integration service ownership did not settle cleanly.", errors);
        }
    }

    private static async Task DisposeCreatedAsync<T>(Task<T> initialization, List<Exception> errors) where T : IAsyncDisposable
    {
        T owner;
        try
        {
            owner = await initialization;
        }
        catch
        {
            // The selected test already observes initialization failure; its factory awaited owned cleanup.
            return;
        }
        try
        {
            await owner.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private static bool IsLoopback(string hostname) => string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(hostname, out IPAddress? address) && IPAddress.IsLoopback(address);

    private sealed class DedicatedLease : IAsyncDisposable
    {
        private int released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                DedicatedSlot.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}

[TestClass]
public sealed class IntegrationContainerCleanup
{
    [AssemblyCleanup]
    public static Task CleanupAsync() => IntegrationServices.DisposeAsync();
}
