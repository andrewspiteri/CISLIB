using CIS.Healthcheck;
using CIS.Configuration;
using CIS.Heartbeat;
using CIS.Heartbeat.Hosting;
using CIS.Metrics;
using CIS.Messaging;
using CIS.RestClient;
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
        services.TryAddSingleton(new StartupHostingOrder(services));
        services.TryAddSingleton(new StartupOperationReference());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, StartupHostedService>());
        return services;
    }
}

internal sealed class StartupHostedService : IHostedLifecycleService, IAsyncDisposable
{
    private readonly StartupCoordinator coordinator;
    private readonly StartupOperationReference operationReference;
    private readonly HeartbeatRuntime? heartbeat;
    private readonly ConfigurationRuntime? configuration;
    private readonly MetricsRuntime? metrics;
    private readonly MetricsRuntime? moduleMetrics;
    private readonly MessagingRuntime? messaging;
    private readonly IRestClientRuntime? rest;
    private readonly StartupHostingOrder order;
    private readonly IHostApplicationLifetime lifetime;
    private readonly IOptions<HostOptions> hostOptions;
    private CancellationTokenRegistration stopping;
    private CancellationTokenRegistration startupStopping;
    private CancellationTokenRegistration completionStopping;
    private int subscribed;

    public StartupHostedService(IServiceProvider root, StartupHostingOrder order,
        IHostApplicationLifetime lifetime, IOptions<HostOptions> hostOptions)
    {
        this.order = order;
        this.lifetime = lifetime;
        this.hostOptions = hostOptions;
        operationReference = root.GetRequiredService<StartupOperationReference>();
        heartbeat = root.GetService<HeartbeatRuntime>();
        configuration = root.GetService<ConfigurationRuntime>();
        metrics = root.GetService<IMetrics>() as MetricsRuntime;
        moduleMetrics = root.GetService<MetricsRuntime>();
        rest = root.GetService<IRestClientRuntime>();
        messaging = root.GetService<MessagingRuntime>();
        coordinator = new StartupCoordinator(root, order.Descriptors,
            root.GetService<HealthcheckService>(), root.GetService<ILogger<StartupCoordinator>>(),
            ConfirmStartupCompletion);
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (hostOptions.Value.ServicesStartConcurrently || hostOptions.Value.ServicesStopConcurrently || !order.IsValid())
        {
            throw new InvalidOperationException("Install AddCisStartup after dependent hosted services and immediately after AddCisHeartbeat when used; disable concurrent hosted starting/stopping. Only the framework web host may follow Startup.");
        }
        if (Interlocked.Exchange(ref subscribed, 1) == 0)
        {
            stopping = lifetime.ApplicationStopping.UnsafeRegister(static value => ((StartupHostedService)value!).BeginStopping(), this);
            // Close admission even while an initializer ignores startup cancellation.
            startupStopping = heartbeat is null && configuration is null && metrics is null && rest is null && messaging is null ? default : cancellationToken.UnsafeRegister(
                static value => ((StartupHostedService)value!).CloseModuleAdmission(), this);
        }
        return Task.CompletedTask;
    }

    // All ordinary IHostedService.StartAsync calls (including the listener) have
    // completed; ApplicationStarted is still held until this real task finishes.
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        // Metrics' Starting phase has already activated the existing provider.
        // The optional alias enables automatic modules; ordinary callbacks use
        // the separate facade binding even when that collection is disabled.
        if (moduleMetrics is not null)
        {
            coordinator.AttachMetrics(moduleMetrics);
        }
        Task operation = coordinator.StartAsync(cancellationToken, lifetime.ApplicationStopping);
        operationReference.Bind(operation);
        return operation;
    }

    // A host wait budget cannot certify actual startup/Heartbeat settlement.
    public Task StoppingAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StopAsync(CancellationToken cancellationToken) => StopCoreAsync();
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool ConfirmStartupCompletion(CancellationToken cancellationToken)
    {
        if (heartbeat is null && configuration is null && metrics is null && rest is null && messaging is null)
        {
            return true;
        }
        // This callback closes dispatch independently of the coordinator gate if
        // the first host startup token is canceled during actual activation.
        completionStopping = cancellationToken.UnsafeRegister(
            static value => ((StartupHostedService)value!).CloseModuleAdmission(), this);
        return heartbeat?.ActivateForStartup() ?? true;
    }

    private void BeginStopping()
    {
        coordinator.RequestStopping();
        CloseModuleAdmission();
        coordinator.BeginStopping();
    }

    private void CloseModuleAdmission()
    {
        messaging?.BeginStopping();
        // A callback may be awaiting REST while holding another module's drain.
        rest?.BeginStopping();
        // Configuration owns cancellation for provider work inside HB callbacks.
        // Publish its cutoff before either module's actual drain is awaited.
        configuration?.BeginStopping();
        // Close dispatch before waiting for the final startup publication gate.
        heartbeat?.BeginStopping();
        if (messaging is null)
        {
            metrics?.BeginStopping();
        }
    }

    private async Task StopCoreAsync()
    {
        BeginStopping();
        Task messagingDrain = messaging?.StopAsync() ?? Task.CompletedTask;
        try
        {
            await messagingDrain.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Preserve this settled fault in the joint task below while still draining every dependency owner.
        }
        Task configurationDrain = configuration?.StopAsync() ?? Task.CompletedTask;
        Task heartbeatDrain = heartbeat?.StopAsync() ?? Task.CompletedTask;
        Task metricsDrain = metrics?.StopAsync() ?? Task.CompletedTask;
        Task restDrain = rest?.StopAsync() ?? Task.CompletedTask;
        await Task.WhenAll(messagingDrain, coordinator.StopAsync(), configurationDrain, heartbeatDrain, metricsDrain, restDrain).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await coordinator.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                completionStopping.Dispose();
                startupStopping.Dispose();
                stopping.Dispose();
            }
        }
    }
}

// A passive reference to the original coordinator task; the coordinator still owns its lifecycle.
internal sealed class StartupOperationReference
{
    private Task? operation;
    internal Task? Operation => Volatile.Read(ref operation);
    internal void Bind(Task value) => Interlocked.CompareExchange(ref operation, value, null);
}

// Inspect final unkeyed descriptors, never a second resolved hosted-service graph.
// WebApplicationBuilder appends this exact ordinary service during Build().
internal sealed class StartupHostingOrder(IServiceCollection services)
{
    private static readonly Type? WebHost = typeof(GenericHostWebHostBuilderExtensions).Assembly
        .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService");

    internal ServiceDescriptor[] Descriptors => services.ToArray();

    internal bool IsValid() => IsValid(Hosted(services), requireHeartbeat: false);

    internal static bool FollowsHeartbeat(IServiceCollection services) => IsValid(Hosted(services), requireHeartbeat: true);

    private static ServiceDescriptor[] Hosted(IServiceCollection collection)
        => collection.Where(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)).ToArray();

    private static bool IsValid(ServiceDescriptor[] hosted, bool requireHeartbeat)
    {
        int startup = Array.FindIndex(hosted, item => item.ImplementationType == typeof(StartupHostedService));
        if (startup < 0 || hosted.Count(item => item.ImplementationType == typeof(StartupHostedService)) != 1)
        {
            return false;
        }
        bool validTail = startup == hosted.Length - 1
            || startup == hosted.Length - 2 && WebHost is not null && hosted[^1].ImplementationType == WebHost;
        if (!validTail)
        {
            return false;
        }
        int heartbeat = Array.FindIndex(hosted, item => item.ImplementationType == typeof(HeartbeatHostedService));
        if (heartbeat < 0)
        {
            return !requireHeartbeat;
        }
        return heartbeat == startup - 1
            && hosted.Count(item => item.ImplementationType == typeof(HeartbeatHostedService)) == 1;
    }
}
