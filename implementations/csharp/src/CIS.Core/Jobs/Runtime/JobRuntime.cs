using CIS.Coordination;
using CIS.Security;
using CIS.Startup;

namespace CIS.Jobs;

/// <summary>Owns one bounded local jobs composition, accepted settings and actual work lifetime.</summary>
/// <remarks>Construction is passive. Start validates selected capabilities; applications explicitly start duties or dispatch admitted runs.
/// Stop waits for actual callbacks/scopes even when a finite graceful authority deadline has already elapsed.</remarks>
public sealed partial class JobRuntime : IStartable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IJobCatalog catalog;
    private readonly IJobExecutionScopeFactory scopes;
    private readonly IJobRunSource? source;
    private readonly IJobScheduleEvaluator? evaluator;
    private readonly TimeProvider clock;
    private readonly LocalApplicationIdentity application;
    private readonly IReadOnlyDictionary<(string Scope, string Id), JobHandlerBinding> registered;
    private readonly CancellationTokenSource admissionStopping = new();
    private readonly Dictionary<(string Scope, string Job, string Resource), JobDutyReservation> duties = [];
    private readonly HashSet<JobInvocationReservation> invocations = [];
    private readonly HashSet<JobPendingDispatch> pending = [];
    private readonly List<JobDutyReservation> iterationWaiters = [];
    private readonly Dictionary<(string Scope, string Job), JobRuntimeSnapshot> latest = [];
    private TaskCompletionSource changed = NewCompletion();
    private JobConfigurationSnapshot configuration;
    private JobConfigurationRejection? rejected;
    private Task? starting;
    private TaskCompletionSource? stopping;
    private TaskCompletionSource? stopSignal;
    private IJobRuntimeObserver? observer;
    private long changeVersion;
    private bool started;
    private bool closing;
    private string? cleanupDiagnostic;

    /// <summary>Captures explicit local services and passive complete settings without constructing handlers or accessing a provider.</summary>
    public JobRuntime(IJobCatalog catalog, IJobExecutionScopeFactory executionScopes,
        JobConfigurationSnapshot? configuration = null, IJobRunSource? runSource = null,
        IJobScheduleEvaluator? scheduleEvaluator = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(executionScopes);
        this.catalog = catalog;
        scopes = executionScopes;
        this.configuration = configuration ?? new(0, limits: catalog.Limits);
        catalog.ValidateConfiguration(this.configuration);
        registered = new Dictionary<(string Scope, string Id), JobHandlerBinding>(catalog.Bindings);
        foreach ((string Scope, string Id) key in registered.Keys)
        {
            activationRevisions.Add(key, this.configuration.Revision);
        }

        source = runSource;
        evaluator = scheduleEvaluator;
        clock = timeProvider ?? TimeProvider.System;
        application = LocalApplicationIdentity.Capture();
    }

    /// <summary>Gets the last completely validated immutable local settings revision.</summary>
    public JobConfigurationSnapshot Configuration
    {
        get
        {
            lock (gate)
            {
                return configuration;
            }
        }
    }
    /// <summary>Gets the latest bounded rejected revision, without replacing accepted settings.</summary>
    public JobConfigurationRejection? RejectedConfiguration
    {
        get
        {
            lock (gate)
            {
                return rejected;
            }
        }
    }
    internal IJobCatalog Catalog => catalog;
    internal TimeProvider Clock => clock;
    internal IJobRunSource? RunSource => source;
    internal LocalApplicationIdentity Application => application;
    internal CancellationToken AdmissionStopping => admissionStopping.Token;
    internal long ChangeVersion
    {
        get
        {
            lock (gate)
            {
                return changeVersion;
            }
        }
    }

    /// <summary>Validates the selected enabled scheduling capabilities once; does not await background duty lifetime.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (starting is not null)
            {
                return starting;
            }

            completion = NewCompletion();
            starting = completion.Task;
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JobConfigurationSnapshot candidate = Configuration;
                catalog.ValidateActivation(candidate, source, evaluator);
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(closing, this);
                    if (!ReferenceEquals(candidate, configuration))
                    {
                        continue;
                    }

                    started = true;
                    PulseUnderGate();
                    break;
                }
            }

            completion.TrySetResult();
        }
        catch (OperationCanceledException error)
        {
            completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }

        lock (gate)
        {
            CompleteStopUnderGate();
        }

        return completion.Task;
    }

    /// <summary>Validates the entire candidate outside runtime gates, then atomically adopts a strictly newer revision.</summary>
    public bool TryApplyConfiguration(JobConfigurationSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (gate)
        {
            if (closing || candidate.Revision <= configuration.Revision)
            {
                return false;
            }
        }

        try
        {
            catalog.ValidateConfiguration(candidate);
            bool activation;
            lock (gate)
            {
                activation = starting is not null;
            }
            if (activation)
            {
                catalog.ValidateActivation(candidate, source, evaluator);
            }

            while (true)
            {
                JobDutyReservation[] draining;
                bool retry;
                lock (gate)
                {
                    if (closing || candidate.Revision <= configuration.Revision)
                    {
                        return false;
                    }

                    retry = !activation && starting is not null;
                    if (!retry)
                    {
                        RecordActivationTransitionsUnderGate(candidate);
                        configuration = candidate;
                        foreach (JobDutyReservation duty in duties.Values)
                        {
                            if (!duty.Closing && duty.Target.Mode == JobExecutionMode.Continuous
                                && !invocations.Any(invocation => ReferenceEquals(invocation.Duty, duty))
                                && candidate.Jobs.TryGetValue((duty.Target.ExecutionScope, duty.Target.JobId), out JobSettings? settings)
                                && settings.Execution.Mode == JobExecutionMode.Continuous)
                            {
                                duty.CurrentDrainBudget = settings.Execution.MaximumDrain;
                            }
                        }

                        draining = duties.Values.Where(duty => !EnabledUnderGate(duty.Target) || BindingChangedUnderGate(duty)).ToArray();
                        PulseUnderGate();
                    }
                    else
                    {
                        draining = [];
                    }
                }

                if (retry)
                {
                    catalog.ValidateActivation(candidate, source, evaluator);
                    activation = true;
                    continue;
                }

                foreach (JobDutyReservation duty in draining)
                {
                    duty.RequestDrain();
                }

                NotifyConfigurationChanged();
                return true;
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            RejectConfiguration(candidate.Revision, "jobs.configuration.invalid");
            return false;
        }
    }

    internal bool InstallInitialConfiguration(JobConfigurationSnapshot candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Revision != 0)
        {
            throw new ArgumentException("Initial configuration uses revision zero.", nameof(candidate));
        }

        lock (gate)
        {
            if (starting is not null || configuration.Revision != 0 || closing)
            {
                return false;
            }
        }

        catalog.ValidateConfiguration(candidate);
        lock (gate)
        {
            if (starting is not null || configuration.Revision != 0 || closing)
            {
                return false;
            }

            configuration = candidate;
            PulseUnderGate();
            return true;
        }
    }

    internal void RejectConfiguration(long revision, string reasonCode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        string code = JobText.Text(reasonCode, 128, nameof(reasonCode));
        JobConfigurationRejection? change = null;
        IJobRuntimeObserver? observed;
        lock (gate)
        {
            observed = observer;
            if (rejected is null || revision > rejected.Revision || revision == rejected.Revision && code != rejected.ReasonCode)
            {
                rejected = new(revision, code);
                change = rejected;
            }
        }

        if (change is not null)
        {
            Observe(() => observed?.ConfigurationRejected(change));
        }
    }

    /// <summary>Captures reservation and actual callback facts without invoking providers, handlers or security.</summary>
    public JobRuntimeStatus CaptureStatus()
    {
        lock (gate)
        {
            return new(started, closing, configuration.Revision,
                duties.Values.Count(duty => duty.Target.Mode == JobExecutionMode.Continuous), duties.Count, invocations.Count,
                invocations.Count(invocation => invocation.Started && invocation.Duty.Target.Mode == JobExecutionMode.Continuous),
                invocations.Count(invocation => invocation.Started && invocation.Duty.Target.Mode == JobExecutionMode.Scheduled),
                pending.Count, cleanupDiagnostic);
        }
    }

    /// <summary>Copies a bounded page of active and recent local observations; no snapshot authorizes later work.</summary>
    public IReadOnlyList<JobRuntimeSnapshot> CaptureSnapshot(int offset = 0, int? pageSize = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        lock (gate)
        {
            int size = pageSize ?? configuration.Limits.MaximumSnapshotPageSize;
            if (size < 1 || size > configuration.Limits.MaximumSnapshotPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }

            JobRuntimeSnapshot[] failures = CaptureRetainedFailuresUnderGate().ToArray();
            var retainedKeys = duties.Keys.ToHashSet();
            foreach (JobRuntimeSnapshot failure in failures)
            {
                retainedKeys.Add(Key(failure.Target));
            }

            // Keep a prior fault visible while its exact resource is recovering; latest is only a fallback.
            return Array.AsReadOnly(duties.Values.Select(SnapshotUnderGate).Concat(failures.Where(item =>
                    !duties.TryGetValue(Key(item.Target), out JobDutyReservation? duty) || duty.LocalState != JobLocalState.Faulted))
                .Concat(latest.Values.Where(item => !retainedKeys.Contains(Key(item.Target))))
                .OrderBy(item => item.Target.ExecutionScope, StringComparer.Ordinal)
                .ThenBy(item => item.Target.JobId, StringComparer.Ordinal)
                .ThenBy(item => item.Target.ResourceId, StringComparer.Ordinal)
                .Skip(offset).Take(size).ToArray());
        }
    }

    /// <summary>Closes new duty, dispatch and iteration admission and starts finite graceful drain without waiting inline.</summary>
    public void BeginStopping()
    {
        JobDutyReservation[] draining;
        TaskCompletionSource? signal = null;
        lock (gate)
        {
            if (!closing)
            {
                signal = NewCompletion();
                stopSignal = signal;
            }

            closing = true;
            draining = duties.Values.ToArray();
            PulseUnderGate();
        }

        if (signal is not null)
        {
            _ = SignalStoppingAsync(signal);
        }

        foreach (JobDutyReservation duty in draining)
        {
            duty.RequestDrain();
        }
    }

    /// <summary>Joins actual owned callbacks, scopes and pending admission work; cancellation affects only this caller's wait.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        BeginStopping();
        Task operation;
        lock (gate)
        {
            stopping ??= NewCompletion();
            CompleteStopUnderGate();
            operation = stopping.Task;
        }

        return cancellationToken.CanBeCanceled ? operation.WaitAsync(cancellationToken) : operation;
    }

    /// <summary>Joins actual stopping before the application disposes shared providers and execution-scope dependencies.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        admissionStopping.Dispose();
    }

    internal Task WaitForChangeAsync(long observedVersion, CancellationToken cancellationToken)
    {
        Task wait;
        lock (gate)
        {
            wait = observedVersion == changeVersion ? changed.Task : Task.CompletedTask;
        }
        return cancellationToken.CanBeCanceled ? wait.WaitAsync(cancellationToken) : wait;
    }

    internal void AttachObserver(IJobRuntimeObserver value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (observer is not null && !ReferenceEquals(observer, value))
            {
                throw new InvalidOperationException("One exact library instrumentation owner may attach to this jobs runtime.");
            }

            observer = value;
        }
    }

    internal void RecordCleanupDiagnostic(string code)
    {
        lock (gate)
        {
            cleanupDiagnostic = JobText.Text(code, 128, nameof(code));
        }
    }

    private bool EnabledUnderGate(JobScopeRequest target)
    {
        if (!configuration.Enabled || !configuration.Jobs.TryGetValue((target.ExecutionScope, target.JobId), out JobSettings? settings)
            || !settings.Enabled || settings.Execution.Mode != target.Mode)
        {
            return false;
        }

        string category = settings.CategoryId ?? registered[(target.ExecutionScope, target.JobId)].Definition.CategoryId;
        return configuration.Categories.GetValueOrDefault((target.ExecutionScope, category))?.Enabled ?? true;
    }

    private string CategoryUnderGate(JobDutyReservation duty)
        => configuration.Jobs.GetValueOrDefault((duty.Target.ExecutionScope, duty.Target.JobId))?.CategoryId
            ?? duty.Binding.Definition.CategoryId;

    private bool BindingChangedUnderGate(JobDutyReservation duty)
        => duty.Target.Mode == JobExecutionMode.Scheduled
            && configuration.Jobs.GetValueOrDefault((duty.Target.ExecutionScope, duty.Target.JobId))?.Execution.ScheduledActivation != duty.Activation;

    private JobRuntimeSnapshot SnapshotUnderGate(JobDutyReservation duty)
        => new(duty.Target, CategoryUnderGate(duty), EnabledUnderGate(duty.Target), duty.LocalState,
            configuration.Revision, duty.LastOutcome, duty.OwnerObservation);

    private void PulseUnderGate()
    {
        TaskCompletionSource previous = changed;
        changed = NewCompletion();
        changeVersion++;
        previous.TrySetResult();
    }

    private void CompleteStopUnderGate()
    {
        if (closing && duties.Count == 0 && invocations.Count == 0 && pending.Count == 0
            && (starting is null || starting.IsCompleted) && stopSignal?.Task.IsCompleted == true)
        {
            stopping?.TrySetResult();
        }
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task SignalStoppingAsync(TaskCompletionSource completion)
    {
        try
        {
            await admissionStopping.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { RecordCleanupDiagnostic("jobs.stop_signal_failed"); }
        finally
        {
            completion.TrySetResult();
            lock (gate)
            {
                CompleteStopUnderGate();
            }
        }
    }

    private static void Observe(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { }
    }
}
