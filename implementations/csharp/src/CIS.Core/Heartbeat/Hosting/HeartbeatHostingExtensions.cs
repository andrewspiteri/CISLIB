using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CIS.Heartbeat.Hosting;

/// <summary>Connects one shared Heartbeat engine to explicitly ordered Generic Host lifecycle phases.</summary>
public static class HeartbeatHostingExtensions
{
    /// <summary>Installs Heartbeat after dependent hosted services, with nonconcurrent stopping.</summary>
    /// <remarks>Required subscriber initialization must finish before ApplicationStarted. Stopping handlers must retain dependencies until the barrier; always stop the host before disposing its root provider.</remarks>
    public static IServiceCollection AddCisHeartbeat(this IServiceCollection services, HeartbeatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(_ => new HeartbeatRuntime(options));
        services.TryAddSingleton(_ => new HeartbeatHostingOrder(
            services.LastOrDefault(item => item.ServiceType == typeof(IHostedService))?.ImplementationType == typeof(HeartbeatHostedService)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HeartbeatHostedService>());
        return services;
    }
}

internal sealed class HeartbeatHostedService(HeartbeatRuntime runtime, IHostApplicationLifetime lifetime,
    HeartbeatHostingOrder order, IOptions<HostOptions> hostOptions) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration started, stopping;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStopConcurrently || !order.IsLast)
        {
            throw new InvalidOperationException("Install AddCisHeartbeat after dependent hosted services and disable concurrent hosted stopping so its barrier precedes dependency teardown.");
        }
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static value => ((HeartbeatRuntime)value!).BeginStopping(), runtime);
        started = lifetime.ApplicationStarted.UnsafeRegister(static value => ((HeartbeatRuntime)value!).Activate(), runtime);
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // A canceled host budget cannot authorize subsequent dependency teardown.
    // External callers may cancel their own StopAsync wait on the runtime instead.
    public Task StoppingAsync(CancellationToken cancellationToken) => runtime.StopAsync();
    public Task StopAsync(CancellationToken cancellationToken) => runtime.StopAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await runtime.StopAsync().ConfigureAwait(false);
        started.Dispose();
        stopping.Dispose();
    }
}

// Inspect descriptor order once at composition; never construct hosted services again to inspect order.
internal sealed record HeartbeatHostingOrder(bool IsLast);
