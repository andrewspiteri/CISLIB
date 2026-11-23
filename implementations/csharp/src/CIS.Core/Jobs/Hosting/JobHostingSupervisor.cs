namespace CIS.Jobs.Hosting;

// Only explicitly configured targets are retained. An epoch is latched BEFORE Start can complete synchronously.
internal sealed class JobHostingSupervisor : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly JobRuntime runtime;
    private readonly ContinuousJobRunner runner;
    private readonly JobScopeRequest[] requests;
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
        requests = options.ContinuousTargets.ToArray();
        targets = requests.Select(target => new Target(target)).ToArray();
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
        JobActivationSnapshot activation = runtime.CaptureActivationSnapshot(requests);
        for (int index = 0; index < targets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Target target = targets[index];
            JobActivationState state = activation.Targets[index];
            if (target.TransitionRevision != state.TransitionRevision)
            {
                target.TransitionRevision = state.TransitionRevision;
                target.Pending = state.Enabled;
            }

            if (!target.Pending)
            {
                continue;
            }
            ContinuousJobActivationResult result = await runner.StartAsync(target.Request, state.TransitionRevision,
                cancellationToken).ConfigureAwait(false);
            if (result.Consumed)
            {
                target.Pending = false;
            }

            if (result.Outcome is ContinuousJobStartOutcome.Unsupported or ContinuousJobStartOutcome.Unauthorized
                || result.Failed)
            {
                target.Pending = false;
                lock (gate)
                {
                    lastFailure = result.Outcome == ContinuousJobStartOutcome.Deferred
                        ? "jobs.host_activation_failed" : "jobs.host_target_refused";
                }
                if (initial)
                {
                    throw new InvalidOperationException("An enabled configured continuous target could not be authorized or activated.");
                }
            }
        }

        Interlocked.Exchange(ref reconciledRevision, activation.ConfigurationRevision);
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

    private sealed class Target(JobScopeRequest request)
    {
        internal JobScopeRequest Request => request;
        internal long? TransitionRevision;
        internal bool Pending;
    }
}
