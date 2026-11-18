using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Metrics;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CIS.Configuration;

/// <summary>Owns initially populated retained objects and their actual refresh lifetime for one host.</summary>
/// <remarks>Prepare before Build, install through AddCisConfiguration before dependent IStartable services, and await host stop before disposing dependencies.</remarks>
public sealed class ConfigurationRuntime : IAsyncDisposable
{
    private readonly object gate = new(), diagnosticGate = new(), healthGate = new();
    private readonly ConfigurationCatalog catalog;
    private readonly ConfigurationRefreshCoordinator refresh;
    private readonly Queue<ConfigurationIssue> warnings = new();
    private IServiceCollection? registration;
    private IServiceProvider? root;
    private ILogger<ConfigurationRuntime>? logger;
    private IConditionMonitor? monitor;
    private HeartbeatOwner? pollingOwner;
    private PollingSubscriber? subscriber;
    private Task? activation, stopping, disposal;
    private int stopRequested, activationCompleted;

    private ConfigurationRuntime(ConfigurationCatalog catalog)
    {
        this.catalog = catalog;
        refresh = new(catalog, new ConfigurationValuePipeline(RecordWarning));
        refresh.Observer = _ => PublishHealth();
    }

    /// <summary>Discovers through the completed manager and loads all initial values before returning a usable runtime.</summary>
    /// <remarks>The adapter remains host-owned. Failure drains acquired work before propagating a safe initialization error.</remarks>
    public static async Task<ConfigurationRuntime> PrepareAsync(ITypeManager typeManager, ConfigurationOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = new ConfigurationRuntime(ConfigurationCatalog.Create(typeManager, options));
        try
        {
            await runtime.refresh.LoadAsync(cancellationToken).ConfigureAwait(false);
            return runtime;
        }
        catch
        {
            try { await runtime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception) { /* Preserve the original initialization outcome after the actual cleanup barrier. */ }
            throw;
        }
    }

    /// <summary>Captures detached current status without reading storage or configuration values.</summary>
    public ConfigurationRefreshStatus CaptureStatus()
        => refresh.CaptureStatus() with { ActivationCompleted = Volatile.Read(ref activationCompleted) != 0, IsStopping = IsStopping };

    internal void AttachMetrics(MetricsRuntime runtime) => refresh.AttachMetrics(runtime);

    /// <summary>Copies at most 32 recent bounded name-only mapping warnings, including warnings observed before DI logging existed.</summary>
    public IReadOnlyList<ConfigurationIssue> CaptureDiagnostics()
    {
        lock (diagnosticGate) { return Array.AsReadOnly(warnings.ToArray()); }
    }

    private bool IsStopping => Volatile.Read(ref stopRequested) != 0;

    internal void Register(IServiceCollection services, bool prependStartup = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (gate)
        {
            if (registration is not null || IsStopping)
            { throw Error("Configuration.Registration", "Install a fresh prepared Configuration runtime exactly once for one host."); }
            if (services.Any(item => item.ServiceType == typeof(ConfigurationRuntime)
                || !item.IsKeyedService && item.ServiceType == typeof(CIS.Startup.IStartable)
                    && (!prependStartup || item.ImplementationType == typeof(Hosting.ConfigurationStartable))))
            { throw Error("Configuration.Registration", "Install Configuration before all unkeyed IStartable registrations and without a conflicting runtime registration."); }
            // Catalog preflights every concrete/reader conflict before adding any descriptor.
            catalog.Register(services);
            registration = services;
            services.AddSingleton(provider => ClaimRoot(provider));
            var activation = ServiceDescriptor.Singleton<CIS.Startup.IStartable, Hosting.ConfigurationStartable>();
            int firstStartable = -1;
            if (prependStartup)
            {
                for (int index = 0; index < services.Count; index++)
                {
                    if (!services[index].IsKeyedService && services[index].ServiceType == typeof(CIS.Startup.IStartable))
                    { firstStartable = index; break; }
                }
            }
            if (prependStartup && firstStartable >= 0) { services.Insert(firstStartable, activation); }
            else { services.Add(activation); }
        }
    }

    private ConfigurationRuntime ClaimRoot(IServiceProvider provider)
    {
        lock (gate)
        {
            if (root is not null && !ReferenceEquals(root, provider))
            { throw Error("Configuration.Registration", "A prepared Configuration runtime cannot be shared by multiple root providers."); }
            root = provider;
            return this;
        }
    }

    internal void ValidateStartupOrder()
    {
        lock (gate)
        {
            ServiceDescriptor[] startables = registration?.Where(item => !item.IsKeyedService
                && item.ServiceType == typeof(CIS.Startup.IStartable)).ToArray() ?? [];
            if (startables.Length == 0 || startables[0].ImplementationType != typeof(Hosting.ConfigurationStartable)
                || startables.Count(item => item.ImplementationType == typeof(Hosting.ConfigurationStartable)) != 1)
            { throw Error("Configuration.Registration", "Configuration activation must be the first unkeyed IStartable registration."); }
        }
    }

    internal Task ActivateAsync(HeartbeatRuntime? heartbeat, HealthcheckService? health,
        ILogger<ConfigurationRuntime>? configuredLogger, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (activation is not null) { return activation; }
            if (IsStopping) { return Task.FromCanceled(new CancellationToken(canceled: true)); }
            completion = NewCompletion();
            activation = completion.Task;
        }
        _ = ActivateCoreAsync(completion, heartbeat, health, configuredLogger, cancellationToken);
        return completion.Task;
    }

    private async Task ActivateCoreAsync(TaskCompletionSource completion, HeartbeatRuntime? heartbeat,
        HealthcheckService? health, ILogger<ConfigurationRuntime>? configuredLogger, CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenRegistration cutoff = cancellationToken.UnsafeRegister(
                static value => ((ConfigurationRuntime)value!).BeginStopping(), this);
            ThrowIfStopping();
            if (health is not null && catalog.Settings.IsHealthCritical is null)
            { throw Error("Configuration.HealthPolicy", "Select ConfigurationOptions.IsHealthCritical explicitly when Healthcheck is connected."); }
            if (catalog.Settings.PollingInterval is not null && heartbeat is null)
            { throw Error("Configuration.HeartbeatRequired", "An explicit polling interval requires the actual shared Heartbeat runtime."); }
            AttachLogger(configuredLogger);
            if (health is not null)
            {
                IConditionMonitor created = health.CreateCondition(new ConditionMonitorOptions
                { Name = "Configuration refresh", IsCritical = catalog.Settings.IsHealthCritical!.Value, InitiallyFlagged = true });
                Volatile.Write(ref monitor, created);
            }
            ThrowIfStopping();
            await refresh.ActivateAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfStopping();
            if (catalog.Settings.PollingInterval is { } interval && catalog.Entries.Count != 0)
            {
                HeartbeatOwner owner = heartbeat!.CreateOwner();
                lock (gate) { pollingOwner = owner; }
                ThrowIfStopping();
                // Heartbeat has weak targets. This runtime strongly retains both
                // the real owner and subscriber through registration and closure.
                var target = new PollingSubscriber(this);
                lock (gate) { subscriber = target; }
                owner.Service.Register(target, new HeartbeatRegistrationOptions
                { Name = "Configuration reconciliation", Interval = interval, InitialDelay = interval });
            }
            lock (gate) { ThrowIfStopping(); Volatile.Write(ref activationCompleted, 1); }
            PublishHealth();
            lock (gate) { ThrowIfStopping(); completion.TrySetResult(); }
        }
        catch (OperationCanceledException) when (IsStopping || cancellationToken.IsCancellationRequested)
        {
            BeginStopping();
            Volatile.Write(ref activationCompleted, 0);
            completion.TrySetCanceled(new CancellationToken(canceled: true));
        }
        catch (InvalidOperationException) when (IsStopping || cancellationToken.IsCancellationRequested)
        {
            // Existing Healthcheck/Heartbeat admission may close immediately
            // after our last check; that known host cutoff is cancellation.
            BeginStopping();
            Volatile.Write(ref activationCompleted, 0);
            completion.TrySetCanceled(new CancellationToken(canceled: true));
        }
        catch (CoreConfigurationException error)
        {
            BeginStopping();
            Volatile.Write(ref activationCompleted, 0);
            completion.TrySetException(error);
        }
        catch (Exception)
        {
            BeginStopping();
            Volatile.Write(ref activationCompleted, 0);
            completion.TrySetException(Error("Configuration.Activation", "Connecting Configuration observation, health or Heartbeat failed."));
        }
    }

    internal Task<bool> ReconcileAsync() => refresh.ReconcileAsync();

    internal void BeginStopping()
    {
        Interlocked.Exchange(ref stopRequested, 1);
        refresh.BeginStopping();
        HeartbeatOwner? owner;
        lock (gate) { owner = pollingOwner; }
        // Close dispatch without synchronously waiting for its callback. Even a
        // self-wait rejection closes the owner first; StopAsync joins from outside.
        try { if (owner is not null) { _ = owner.CloseAsync(); } }
        catch (InvalidOperationException) { }
    }

    /// <summary>Closes refresh/dispatch and joins actual startup, provider, notification and polling work without disposing the host-owned store or POCOs.</summary>
    public Task StopAsync()
    {
        BeginStopping();
        TaskCompletionSource completion;
        lock (gate)
        {
            if (stopping is not null) { return stopping; }
            completion = NewCompletion();
            stopping = completion.Task;
        }
        _ = StopCoreAsync(completion);
        return completion.Task;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        bool failed = false;
        Task refreshDrain = refresh.StopAsync();
        Task? starting;
        lock (gate) { starting = activation; }
        await ObserveAsync(starting).ConfigureAwait(false);
        HeartbeatOwner? owner;
        lock (gate) { owner = pollingOwner; }
        Task ownerDrain;
        try { ownerDrain = owner?.CloseAsync() ?? Task.CompletedTask; }
        catch (InvalidOperationException)
        {
            // CloseAsync already publishes the cutoff before its self-wait guard.
            // Never replace the real owner barrier with a completed task.
            ownerDrain = owner!.Completion.Task;
        }
        try { await Task.WhenAll(refreshDrain, ownerDrain).ConfigureAwait(false); }
        catch (Exception) { failed = true; }
        GC.KeepAlive(subscriber);
        IConditionMonitor? ownedMonitor = Volatile.Read(ref monitor);
        if (ownedMonitor is not null)
        {
            try { await ownedMonitor.DisposeAsync().ConfigureAwait(false); }
            catch (Exception) { failed = true; }
        }
        if (failed) { completion.TrySetException(Error("Configuration.Stop", "Configuration cleanup failed after actual refresh and polling work settled.")); }
        else { completion.TrySetResult(); }
    }

    /// <summary>Joins the shared stop barrier and disposes only resources acquired by this runtime.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task shared;
        lock (gate)
        {
            if (disposal is null) { completion = NewCompletion(); disposal = completion.Task; }
            shared = disposal;
        }
        if (completion is not null) { _ = DisposeCoreAsync(completion); }
        return new(shared);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        bool failed = false;
        try { await StopAsync().ConfigureAwait(false); }
        catch (Exception) { failed = true; }
        try { await refresh.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { failed = true; }
        HeartbeatOwner? owner;
        lock (gate) { owner = pollingOwner; }
        if (owner is not null)
        {
            try { await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception) { failed = true; }
        }
        if (failed) { completion.TrySetException(Error("Configuration.Stop", "Owned Configuration resources failed cleanup after their actual work settled.")); }
        else { completion.TrySetResult(); }
    }

    private void RecordWarning(Type type, string explanation)
    {
        var issue = new ConfigurationIssue("Configuration.UnknownProperty", type.FullName ?? type.Name, explanation);
        ILogger<ConfigurationRuntime>? observer;
        lock (diagnosticGate)
        {
            if (warnings.Count == 32) { warnings.Dequeue(); }
            warnings.Enqueue(issue);
            observer = logger;
        }
        LogWarning(observer, issue);
    }

    private void AttachLogger(ILogger<ConfigurationRuntime>? observer)
    {
        ConfigurationIssue[] bootstrap;
        lock (diagnosticGate) { logger = observer; bootstrap = warnings.ToArray(); }
        foreach (ConfigurationIssue issue in bootstrap) { LogWarning(observer, issue); }
    }

    private static void LogWarning(ILogger<ConfigurationRuntime>? observer, ConfigurationIssue issue)
    {
        try { observer?.LogWarning("Configuration warning {Code} for {ConfigurationType}: {Explanation}", issue.Code, issue.Key, issue.Explanation); }
        catch (Exception) { /* Diagnostics cannot change accepted data or lifetime settlement. */ }
    }

    private void PublishHealth()
    {
        IConditionMonitor? target = Volatile.Read(ref monitor);
        if (target is null || IsStopping) { return; }
        // Serialize only the existing in-process condition report and its current
        // snapshot. No provider, event, logger or lifecycle gate runs here.
        lock (healthGate)
        {
            if (IsStopping) { return; }
            ConfigurationRefreshStatus status = CaptureStatus();
            bool valid = status.ActivationCompleted && status.Entries.All(entry => entry.IsValid && !entry.Disabled)
                && status.WatchState is ConfigurationWatchState.Active or ConfigurationWatchState.NotRequired;
            try
            {
                if (valid) { target.Clear(); }
                else { target.Flag(new HealthDiagnostic("Configuration.Refresh", "Configuration activation or latest reconciliation is not valid; inspect its bounded status.")); }
            }
            catch (Exception) { /* Reporting remains secondary to real module outcomes. */ }
        }
    }

    private void ThrowIfStopping()
    {
        if (IsStopping) { throw new OperationCanceledException(new CancellationToken(canceled: true)); }
    }
    private static async Task ObserveAsync(Task? operation)
    {
        if (operation is null) { return; }
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { /* Startup retains its original outcome; cleanup still joins every other barrier. */ }
    }
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static CoreConfigurationException Error(string code, string explanation)
        => ConfigurationDiagnostics.Error(typeof(ConfigurationRuntime), code, explanation);

    private sealed class PollingSubscriber(ConfigurationRuntime runtime) : IHeartbeatSubscriber
    {
        public Task<bool> OnElapsedAsync(CancellationToken cancellationToken) => runtime.ReconcileAsync();
        public Task OnErrorAsync(Exception exception) => Task.CompletedTask;
    }
}
