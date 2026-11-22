namespace CIS.Jobs.Hosting;

// Only explicitly configured targets are retained. An epoch is latched BEFORE Start can complete synchronously.
internal sealed class JobHostingSupervisor : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly JobRuntime runtime;
    private readonly ContinuousJobRunner runner;
    private readonly Target[] targets;
    private readonly CancellationTokenSource stopping = new();
    private Task? startup;
    private Task? loop;
    private Task? disposal;
    private string? lastFailure;
    private long reconciledRevision = -1;

    internal JobHostingSupervisor(JobRuntime runtime, JobHostingOptions options)
    {
        this.runtime = runtime;
        runner = new(runtime);
        targets = options.ContinuousTargets.Select(target => new Target(target)).ToArray();
    }

    internal string? LastFailure
    {
        get
        {
            lock (gate)
            {
                return lastFailure;
            }
        }
    }
    internal long ReconciledRevision => Interlocked.Read(ref reconciledRevision);

    internal Task StartAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? completion = null;
        Task operation;
        lock (gate)
        {
            if (startup is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                startup = completion.Task;
            }

            operation = startup;
        }

        if (completion is not null)
        {
            _ = StartCoreAsync(completion, cancellationToken);
        }
        return operation;
    }

    private async Task StartCoreAsync(TaskCompletionSource completion, CancellationToken caller)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
            await ReconcileAsync(initial: true, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            Task running = RunAsync();
            lock (gate)
            {
                loop = running;
            }
            completion.TrySetResult();
        }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private async Task RunAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            long version = runtime.ChangeVersion;
            try
            {
                await ReconcileAsync(initial: false, stopping.Token).ConfigureAwait(false);
                await runtime.WaitForChangeAsync(version, stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error))
            {
                lock (gate)
                {
                    lastFailure = "jobs.host_activation_failed";
                }
                return;
            }
        }
    }

    private async Task ReconcileAsync(bool initial, CancellationToken cancellationToken)
    {
        JobConfigurationSnapshot configuration = runtime.Configuration;
        foreach (Target target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JobSettings? settings = configuration.Jobs.GetValueOrDefault((target.Request.ExecutionScope, target.Request.JobId));
            bool enabled = settings?.Execution.Mode == JobExecutionMode.Continuous && runtime.IsEnabled(target.Request);
            var epoch = new Epoch(enabled, settings?.Execution.Mode, settings?.Execution.ScheduledActivation);
            bool changed = target.Epoch != epoch;
            if (changed)
            {
                target.Epoch = epoch;
                target.Pending = enabled;
                target.WaitForPrior = !initial;
            }

            if (!target.Pending)
            {
                continue;
            }
            ContinuousJobStartOutcome outcome = await runner.StartAsync(target.Request.ExecutionScope, target.Request.JobId,
                target.Request.ResourceId, cancellationToken).ConfigureAwait(false);
            if (outcome == ContinuousJobStartOutcome.Started
                || outcome == ContinuousJobStartOutcome.AlreadyRunning && !target.WaitForPrior)
            {
                target.Pending = false;
            }
            else if (outcome is ContinuousJobStartOutcome.Unsupported or ContinuousJobStartOutcome.Unauthorized)
            {
                target.Pending = false;
                lock (gate)
                {
                    lastFailure = "jobs.host_target_refused";
                }
                if (initial)
                {
                    throw new InvalidOperationException("An enabled configured continuous target could not be authorized or activated.");
                }
            }
        }

        Interlocked.Exchange(ref reconciledRevision, configuration.Revision);
    }

    internal void BeginStopping()
    {
        lock (gate)
        {
            disposal ??= stopping.CancelAsync();
        }
    }

    internal async Task StopAsync()
    {
        BeginStopping();
        Task? initializing;
        Task? running;
        Task? cancellation;
        lock (gate)
        {
            initializing = startup;
            running = loop;
            cancellation = disposal;
        }
        if (cancellation is not null)
        {
            await cancellation.ConfigureAwait(false);
        }
        if (initializing is not null)
        {
            try
            {
                await initializing.ConfigureAwait(false);
            }
            catch (Exception) { /* The sequential startup caller retains its actual original failure. */ }
        }

        lock (gate)
        {
            running = loop;
        }
        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally { stopping.Dispose(); }
    }

    private sealed record Epoch(bool Enabled, JobExecutionMode? Mode, JobScheduledActivation? Activation);
    private sealed class Target(JobScopeRequest request)
    {
        internal JobScopeRequest Request => request;
        internal Epoch? Epoch;
        internal bool Pending;
        internal bool WaitForPrior;
    }
}
