using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CIS.Startup.Hosting;

/// <summary>Connects sequential root startup to the awaited Generic Host lifecycle.</summary>
public static class StartupHostingExtensions
{
    /// <summary>Installs startup after dependent hosted services and Heartbeat, before an optional framework web-host tail.</summary>
    /// <remarks>
    /// Enable standard DI ValidateScopes and ValidateOnBuild on the actual host builder.
    /// Disable concurrent hosted starting/stopping. HealthcheckService and HeartbeatRuntime
    /// are connected when registered; health routes must precede an application's business gate.
    /// Always await host stop before disposing its root, including failed startup.
    /// </remarks>
    public static IServiceCollection AddCisStartup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(provider => new StartupCoordinator(provider, services.ToArray(),
            provider.GetService<HealthcheckService>(), provider.GetService<HeartbeatRuntime>(),
            provider.GetService<ILogger<StartupCoordinator>>()));
        services.TryAddSingleton(new StartupHostingOrder(services));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupHostedService>());
        return services;
    }
}

internal sealed class StartupHostedService(StartupCoordinator coordinator, StartupHostingOrder order,
    IHostApplicationLifetime lifetime, IOptions<HostOptions> hostOptions) : IHostedLifecycleService, IAsyncDisposable
{
    private CancellationTokenRegistration stopping;
    private int subscribed;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStartConcurrently || hostOptions.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Install AddCisStartup after dependent hosted services and immediately after AddCisHeartbeat when used; disable concurrent hosted starting/stopping. Only the framework web host may follow Startup.");
        }
        if (Interlocked.Exchange(ref subscribed, 1) == 0)
        {
            stopping = lifetime.ApplicationStopping.UnsafeRegister(static value => ((StartupCoordinator)value!).BeginStopping(), coordinator);
        }
        return Task.CompletedTask;
    }

    // All ordinary IHostedService.StartAsync calls (including the listener) have
    // completed; ApplicationStarted is still held until this real task finishes.
    public Task StartedAsync(CancellationToken cancellationToken)
        => coordinator.StartAsync(cancellationToken, lifetime.ApplicationStopping);

    // A host wait budget cannot certify actual startup/Heartbeat settlement.
    public Task StoppingAsync(CancellationToken cancellationToken) => coordinator.StopAsync();
    public Task StopAsync(CancellationToken cancellationToken) => coordinator.StopAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await coordinator.StopAsync().ConfigureAwait(false);
        stopping.Dispose();
    }
}

// Inspect final unkeyed descriptors, never a second resolved hosted-service graph.
// WebApplicationBuilder appends this exact ordinary service during Build().
internal sealed class StartupHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal bool IsValid() => IsValid(Hosted(services), requireHeartbeat: false);

    internal static bool FollowsHeartbeat(IServiceCollection services) => IsValid(Hosted(services), requireHeartbeat: true);

    private static ServiceDescriptor[] Hosted(IServiceCollection collection)
        => collection.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)).ToArray();

    private static bool IsValid(ServiceDescriptor[] hosted, bool requireHeartbeat)
    {
        int startup = Array.FindIndex(hosted, item => item.ImplementationType == typeof(StartupHostedService));
        if (startup < 0 || hosted.Count(item => item.ImplementationType == typeof(StartupHostedService)) != 1) { return false; }
        bool validTail = startup == hosted.Length - 1
            || startup == hosted.Length - 2 && WebHost is not null && hosted[^1].ImplementationType == WebHost;
        if (!validTail) { return false; }
        int heartbeat = Array.FindIndex(hosted, item => item.ImplementationType == typeof(HeartbeatHostedService));
        if (heartbeat < 0) { return !requireHeartbeat; }
        return heartbeat == startup - 1
            && hosted.Count(item => item.ImplementationType == typeof(HeartbeatHostedService)) == 1;
    }
}
