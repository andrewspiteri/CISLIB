namespace CIS.Jobs;

/// <summary>The latest bounded settings refusal, separate from the last accepted configuration.</summary>
public sealed record JobConfigurationRejection(long Revision, string ReasonCode);

/// <summary>Passive local resource facts. Reservations and actual entered callbacks are deliberately distinct.</summary>
public sealed record JobRuntimeStatus(bool Started, bool Stopping, long ConfigurationRevision,
    int ContinuousDuties, int DutyReservations, int InvocationReservations,
    int ActiveContinuousInvocations, int ActiveScheduledInvocations, int PendingDispatches,
    string? LastCleanupDiagnostic);

internal interface IJobRuntimeObserver
{
    IJobRuntimeObserver CaptureInvocationObserver() => this;
    void InvocationStarted(JobExecutionMode mode);
    void InvocationCompleted(JobExecutionMode mode, string outcome, double? durationSeconds);
    void Transition(JobRuntimeSnapshot observation);
    void ConfigurationRejected(JobConfigurationRejection rejection)
    {
    }
    void ConfigurationChanged()
    {
    }
}

internal sealed record JobRuntimeHealth(bool Unhealthy);

internal enum JobAdmissionOutcome
{
    Accepted, Busy, Deferred, Unsupported, Unauthorized
}

internal sealed record JobAdmission(JobAdmissionOutcome Outcome, JobDutyReservation? Duty = null,
    JobInvocationReservation? Invocation = null);

internal sealed record JobAuthorizedScopeResult(JobAdmissionOutcome Outcome, JobAuthorizedScope? Scope = null);

internal sealed class JobAuthorizedScope(IJobExecutionScope owned, CIS.Coordination.CoordinationTarget target,
    Action<string> recordCleanup) : IAsyncDisposable
{
    private readonly object gate = new();
    private Task? disposal;
    internal bool CleanupFailed
    {
        get; private set;
    }
    internal IServiceProvider Services => owned.Services;
    internal CIS.Coordination.CoordinationTarget Target => target;
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task pending;
        lock (gate)
        {
            if (disposal is null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = completion.Task;
            }

            pending = disposal;
        }

        if (completion is not null)
        {
            _ = DisposeCoreAsync(completion);
        }

        return new(pending);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error))
            {
                CleanupFailed = true;
                recordCleanup("jobs.scope_cleanup_failed");
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
