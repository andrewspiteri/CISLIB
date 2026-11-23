using CIS.Coordination;

namespace CIS.Jobs;

internal sealed class JobPendingDispatch(JobRuntime runtime) : IDisposable
{
    internal bool Released;
    internal bool Claimed;
    internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() => runtime.ReleasePending(this);
}

internal sealed class JobDutyReservation(JobRuntime runtime, JobScopeRequest target, JobHandlerBinding binding,
    TimeSpan drainBudget, JobScheduledActivation activation = JobScheduledActivation.ExternalOccurrences,
    long activationRevision = 0) : IAsyncDisposable
{
    internal readonly CancellationTokenSource Stop = new();
    internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Cancellation = Task.CompletedTask;
    internal Task LeaseObservation = Task.CompletedTask;
    internal bool LeaseEndedWhileRunning;
    internal bool Closing;
    internal bool Released;
    internal JobLocalState LocalState = JobLocalState.Standby;
    internal string? LastOutcome;
    internal CoordinationOwnerObservation? OwnerObservation;
    internal ICoordinationLease? CurrentLease;
    internal TimeSpan CurrentDrainBudget = drainBudget;

    internal JobScopeRequest Target => target;
    internal JobHandlerBinding Binding => binding;
    internal TimeSpan DrainBudget => runtime.GetDrainBudget(this);
    internal JobScheduledActivation Activation => activation;
    internal long ActivationRevision => activationRevision;
    internal CancellationToken StopRequested => Stop.Token;
    internal bool IsDraining => runtime.IsDraining(this);
    internal ICoordinationLease? Lease => runtime.GetLease(this);
    internal Task Completion => Finished.Task;
    internal void RequestDrain() => runtime.DrainDuty(this);
    internal void SetState(JobLocalState state, string? reasonCode = null) => runtime.SetState(this, state, reasonCode);
    public ValueTask DisposeAsync() => runtime.ReleaseDutyAsync(this);

    internal TaskCompletionSource PrepareCancellation()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Cancellation = completion.Task;
        return completion;
    }
}

internal sealed class JobInvocationReservation(JobRuntime runtime, JobDutyReservation duty, JobExecutionSettings settings,
    TimeSpan? effectiveTimeout, long revision, JobPendingDispatch? pending = null) : IDisposable
{
    internal bool Released;
    internal bool Started;
    internal long? StartedAt;
    internal IJobRuntimeObserver? Observation;
    internal JobDutyReservation Duty => duty;
    internal JobExecutionSettings Settings => settings;
    internal TimeSpan? EffectiveTimeout => effectiveTimeout;
    internal long ConfigurationRevision => revision;
    internal JobPendingDispatch? Pending => pending;
    internal bool Entered => Started;
    internal void Complete(string outcome, bool scopeCleanupSucceeded = true)
        => runtime.ReleaseInvocation(this, outcome, scopeCleanupSucceeded);
    public void Dispose() => Complete("failed");
}
