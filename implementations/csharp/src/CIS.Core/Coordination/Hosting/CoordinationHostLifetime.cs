using CIS.Healthcheck;
using CIS.Jobs;
using CIS.Jobs.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Coordination.Hosting;

// One known-module barrier. It never resolves an owner or creates a provider in its constructor.
internal sealed class CoordinationHostLifetime : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource dependencyStopping = new();
    private readonly CancellationToken dependencyToken;
    private readonly TaskCompletionSource dependencies = Completion();
    private readonly List<Exception> cutoffFailures = [];
    private CoordinationRuntime? coordination;
    private JobRuntime? jobs;
    private IJobConfigurationBinding? configuration;
    private JobHostingSupervisor? supervisor;
    private CoordinationInstrumentation? coordinationObservations;
    private JobInstrumentation? jobObservations;
    private IConditionMonitor? stoppingMonitor;
    private IHostApplicationLifetime? host;
    private CancellationTokenRegistration hostStopping;
    private CancellationTokenRegistration healthStopping;
    private TaskCompletionSource bindingDrain = Completed();
    private Task? stop;
    private int bindingCount;
    private bool closing;

    internal CoordinationHostLifetime() => dependencyToken = dependencyStopping.Token;
    internal CancellationToken DependencyStopping => dependencyToken;
    internal bool IsDrained => dependencies.Task.IsCompletedSuccessfully;

    internal static CancellationToken SelectStoppingToken(IServiceProvider services, IHostApplicationLifetime lifetime)
        => DependencyToken(services, lifetime.ApplicationStopping);

    internal static CancellationToken DependencyToken(IServiceProvider services, CancellationToken rawStopping)
        => services.GetService<CoordinationHostLifetime>()?.DependencyStopping ?? rawStopping;

    internal void ObserveHost(IHostApplicationLifetime lifetime)
    {
        bool subscribe;
        lock (gate)
        {
            if (host is not null && !ReferenceEquals(host, lifetime))
            {
                throw new InvalidOperationException("A coordination composition belongs to one actual host/provider.");
            }

            subscribe = host is null;
            host = lifetime;
        }

        if (subscribe)
        {
            hostStopping = lifetime.ApplicationStopping.UnsafeRegister(static state => ((CoordinationHostLifetime)state!).BeginStopping(), this);
        }
    }

    internal IDisposable EnterBinding()
    {
        lock (gate)
        {
            if (closing)
            {
                throw new InvalidOperationException("Coordination host admission is closed before runtime construction.");
            }

            if (bindingCount++ == 0)
            {
                bindingDrain = Completion();
            }
        }

        return new Binding(this);
    }

    internal void Bind(CoordinationRuntime value)
    {
        bool close;
        lock (gate)
        {
            if (coordination is not null && !ReferenceEquals(coordination, value))
            {
                throw new InvalidOperationException("One coordination runtime belongs to this hosting composition.");
            }

            coordination = value;
            close = closing;
        }

        if (close)
        {
            value.BeginStopping();
        }
    }

    internal void Bind(JobRuntime value)
    {
        bool close;
        lock (gate)
        {
            if (jobs is not null && !ReferenceEquals(jobs, value))
            {
                throw new InvalidOperationException("One jobs runtime belongs to this hosting composition.");
            }

            jobs = value;
            close = closing;
        }

        if (close)
        {
            value.BeginStopping();
        }
    }

    internal void Bind(IJobConfigurationBinding value)
    {
        lock (gate)
        {
            if (configuration is not null && !ReferenceEquals(configuration, value))
            {
                throw new InvalidOperationException("Select one complete jobs configuration binding.");
            }

            configuration = value;
        }
    }

    internal void Bind(JobHostingSupervisor value)
    {
        bool close;
        lock (gate)
        {
            if (supervisor is not null && !ReferenceEquals(supervisor, value))
            {
                throw new InvalidOperationException("One bounded explicit-target jobs supervisor belongs to this host.");
            }

            supervisor = value;
            close = closing;
        }

        if (close)
        {
            value.BeginStopping();
        }
    }

    internal void Bind(CoordinationInstrumentation value)
    {
        lock (gate)
        {
            coordinationObservations = value;
        }
    }
    internal void Bind(JobInstrumentation value)
    {
        lock (gate)
        {
            jobObservations = value;
        }
    }

    internal void BindStoppingMonitor(IConditionMonitor value)
    {
        bool close;
        lock (gate)
        {
            if (stoppingMonitor is not null && !ReferenceEquals(stoppingMonitor, value))
            {
                throw new InvalidOperationException("Create one coordination stopping monitor during sequential startup.");
            }

            stoppingMonitor = value;
            close = closing;
        }

        if (close)
        {
            FlagStopping(value);
        }
    }

    internal void BindHealth(HealthcheckService value)
        => healthStopping = dependencyToken.UnsafeRegister(static state => ((HealthcheckService)state!).BeginStopping(), value);

    internal void BeginStopping()
    {
        CoordinationRuntime? coordinationOwner;
        JobRuntime? jobOwner;
        JobHostingSupervisor? supervisorOwner;
        IConditionMonitor? monitor;
        lock (gate)
        {
            if (closing)
            {
                return;
            }

            closing = true;
            coordinationOwner = coordination;
            jobOwner = jobs;
            supervisorOwner = supervisor;
            monitor = stoppingMonitor;
        }

        if (monitor is not null)
        {
            Cutoff(() => FlagStopping(monitor));
        }
        Cutoff(() => supervisorOwner?.BeginStopping());
        Cutoff(() => jobOwner?.BeginStopping());
        Cutoff(() => coordinationOwner?.BeginStopping());
        _ = StopAsync();
    }

    internal Task StopAsync()
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (stop is null)
            {
                completion = Completion();
                stop = completion.Task;
            }

            operation = stop;
        }

        if (completion is not null)
        {
            BeginStopping();
            _ = StopCoreAsync(completion);
        }

        return operation;
    }

    // Always succeeds after actual owned settlement, BEFORE deferred callbacks are dispatched.
    internal Task WaitForDependenciesAsync()
    {
        BeginStopping();
        return dependencies.Task;
    }

    internal static async Task<Exception?> CaptureStopAsync(CoordinationHostLifetime? lifetime)
    {
        if (lifetime is null)
        {
            return null;
        }
        try
        {
            await lifetime.StopAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception error) { return error; }
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        try
        {
            Task factories;
            lock (gate)
            {
                factories = bindingDrain.Task;
            }
            await factories.ConfigureAwait(false);
            CoordinationRuntime? coordinationOwner;
            JobRuntime? jobOwner;
            IJobConfigurationBinding? configurationOwner;
            JobHostingSupervisor? supervisorOwner;
            CoordinationInstrumentation? coordinationTelemetry;
            JobInstrumentation? jobTelemetry;
            lock (gate)
            {
                coordinationOwner = coordination;
                jobOwner = jobs;
                configurationOwner = configuration;
                supervisorOwner = supervisor;
                coordinationTelemetry = coordinationObservations;
                jobTelemetry = jobObservations;
            }

            await Task.WhenAll(
                Capture(() => supervisorOwner?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures),
                Capture(() => configurationOwner?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures),
                Capture(() => jobOwner?.StopAsync() ?? Task.CompletedTask, failures),
                Capture(() => coordinationOwner?.StopAsync() ?? Task.CompletedTask, failures)).ConfigureAwait(false);
            await Task.WhenAll(
                Capture(() => coordinationTelemetry?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures),
                Capture(() => jobTelemetry?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures)).ConfigureAwait(false);
            dependencies.TrySetResult();
            await Capture(() => dependencyStopping.CancelAsync(), failures).ConfigureAwait(false);
            IConditionMonitor? monitor;
            lock (gate)
            {
                monitor = stoppingMonitor;
                failures.AddRange(cutoffFailures);
            }
            if (monitor is not null)
            {
                await Capture(() => monitor.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            }

            if (failures.Count == 0)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(failures.Count == 1 ? failures[0] : new AggregateException(failures));
            }
        }
        catch (Exception error)
        {
            dependencies.TrySetException(error);
            completion.TrySetException(error);
        }
    }

    private void Cutoff(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) { lock (gate) { if (cutoffFailures.Count < 8) { cutoffFailures.Add(error); } } }
    }

    private static void FlagStopping(IConditionMonitor monitor)
        => monitor.Flag(new("coordination.stopping", "The application is draining shared duties."));

    private static async Task Capture(Func<Task> action, List<Exception> failures)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception error) { lock (failures) { failures.Add(error); } }
    }

    private void ExitBinding()
    {
        lock (gate)
        {
            if (--bindingCount == 0)
            {
                bindingDrain.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally { healthStopping.Dispose(); hostStopping.Dispose(); dependencyStopping.Dispose(); }
    }

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource Completed()
    {
        TaskCompletionSource value = Completion();
        value.SetResult();
        return value;
    }
    private sealed class Binding(CoordinationHostLifetime owner) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.ExitBinding();
            }
        }
    }
}
