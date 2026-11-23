using CIS.Audit.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using CIS.Startup.Hosting;
using CIS.Metrics;
using CIS.Messaging;
using CIS.RestClient;
using CIS.Coordination.Hosting;
using CIS.Management.Hosting;

namespace CIS.Heartbeat.Hosting;

/// <summary>Connects one shared Heartbeat engine to explicitly ordered Generic Host lifecycle phases.</summary>
public static class HeartbeatHostingExtensions
{
    /// <summary>Installs Heartbeat after dependent hosted services, with nonconcurrent stopping.</summary>
    /// <remarks>Required subscriber initialization must finish before ApplicationStarted. Stopping handlers must retain dependencies until the barrier;
    /// always stop the host before disposing its root provider. The owner-factory alias shares this runtime. Register a borrowed concrete instance
    /// before this call so its alias also remains caller-owned; do not replace that instance afterward while retaining the older alias.</remarks>
    public static IServiceCollection AddCisHeartbeat(this IServiceCollection services, HeartbeatOptions? options = null)
        => AddCisHeartbeatCore(services, options, registerOwnerFactory: true);

    internal static IServiceCollection AddCisHeartbeatCore(this IServiceCollection services,
        HeartbeatOptions? options, bool registerOwnerFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(_ => new HeartbeatRuntime(options));
        if (registerOwnerFactory)
        {
            ServiceDescriptor? runtime = services.LastOrDefault(item => !item.IsKeyedService
                && item.ServiceType == typeof(HeartbeatRuntime));
            if (runtime?.ImplementationInstance is HeartbeatRuntime borrowed)
            {
                services.TryAddSingleton<IHeartbeatOwnerFactory>(borrowed);
            }
            else
            {
                services.TryAddSingleton<IHeartbeatOwnerFactory>(root => root.GetRequiredService<HeartbeatRuntime>());
            }
        }
        services.TryAddSingleton(_ => new HeartbeatHostingOrder(
            services.LastOrDefault(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService))?.ImplementationType == typeof(HeartbeatHostedService)
            || StartupHostingOrder.FollowsHeartbeat(services)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HeartbeatHostedService>());
        return services;
    }
}

internal sealed class HeartbeatHostedService(HeartbeatRuntime runtime, IHostApplicationLifetime lifetime,
    HeartbeatHostingOrder order, IOptions<HostOptions> hostOptions, IServiceProvider root) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration started;
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration dependencyStopping;
    private readonly MetricsRuntime? metrics = root.GetService<IMetrics>() as MetricsRuntime;
    private readonly IRestClientRuntime? rest = root.GetService<IRestClientRuntime>();
    private readonly MessagingRuntime? messaging = root.GetService<MessagingRuntime>();
    private readonly CoordinationHostLifetime? coordination = root.GetService<CoordinationHostLifetime>();
    private readonly AuditRuntime? audit = root.GetService<AuditRuntime>();
    private readonly ManagementRegistrationLifetime? management = root.GetService<ManagementRegistrationLifetime>();

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStopConcurrently || !order.IsLast)
        {
            throw new InvalidOperationException("Install AddCisHeartbeat after dependent hosted services and disable concurrent hosted stopping so its barrier precedes dependency teardown.");
        }
        stopping = lifetime.ApplicationStopping.UnsafeRegister(static value => ((HeartbeatHostedService)value!).BeginStopping(), this);
        if (audit is not null || coordination is not null)
        {
            dependencyStopping = (audit?.DependencyStopping ?? coordination!.DependencyStopping).UnsafeRegister(static value => ((HeartbeatHostedService)value!).BeginStopping(), this);
        }
        started = lifetime.ApplicationStarted.UnsafeRegister(static value => ((HeartbeatRuntime)value!).Activate(), runtime);
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // A canceled host budget cannot authorize subsequent dependency teardown.
    // External callers may cancel their own StopAsync wait on the runtime instead.
    public Task StoppingAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StopAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void BeginStopping()
    {
        management?.BeginStopping();
        if (audit is not null && !audit.IsDrained)
        {
            audit.BeginStopping();
            return;
        }

        if (coordination is not null && !coordination.IsDrained)
        {
            coordination.BeginStopping();
            return;
        }

        messaging?.BeginStopping();
        rest?.BeginStopping();
        runtime.BeginStopping();
        if (messaging is null)
        {
            metrics?.BeginStopping();
        }
    }

    private async Task StopCoreAsync()
    {
        management?.BeginStopping();
        Exception? coordinationFailure = await AuditRuntime.CaptureStopAsync(audit, coordination).ConfigureAwait(false);
        BeginStopping();
        Task messagingDrain = messaging?.StopAsync() ?? Task.CompletedTask;
        try
        {
            await messagingDrain.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Rejoin the original fault only after all other module owners have begun actual drain.
        }
        Task restDrain = rest?.StopAsync() ?? Task.CompletedTask;
        try
        {
            await restDrain.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original REST fault remains part of the joint drain below.
        }
        await Task.WhenAll(messagingDrain, restDrain, management?.StopAsync() ?? Task.CompletedTask,
            runtime.StopAsync(), metrics?.StopAsync() ?? Task.CompletedTask,
            coordinationFailure is null ? Task.CompletedTask : Task.FromException(coordinationFailure)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            started.Dispose();
            stopping.Dispose();
            dependencyStopping.Dispose();
        }
    }
}

// Inspect descriptor order once at composition; never construct hosted services again to inspect order.
internal sealed record HeartbeatHostingOrder(bool IsLast);
