namespace CIS.Heartbeat;

internal enum HeartbeatEvent { Registered, Eligible, Admitted, Dispatched, ConditionStarted, CallbackStarted, ErrorHandlingStarted, ErrorHandlingCompleted, CleanupCompleted, Outcome, Suppressed, Settled, Retired, IntegrationFault }
internal enum HeartbeatPhase { Condition, Callback }
internal enum HeartbeatResult { Succeeded, Failed, Skipped, ExpectedCancellation }
internal enum HeartbeatStep { NotStarted, Running, Succeeded, Failed, ExpectedCancellation, NotApplicable, NotInvoked }

// Transient owned handoff. The concrete adapter must detach exceptions before returning;
// neither entries nor monitoring history store this object after execution settlement.
internal sealed record HeartbeatNotification(HeartbeatEvent Kind, Guid RegistrationId, DateTimeOffset OccurredAtUtc)
{
    internal long? ExecutionId { get; init; }
    internal HeartbeatPhase? SourcePhase { get; init; }
    internal HeartbeatResult? Outcome { get; init; }
    internal DateTimeOffset? EligibleAtUtc { get; init; }
    internal DateTimeOffset? AdmittedAtUtc { get; init; }
    internal DateTimeOffset? DispatchedAtUtc { get; init; }
    internal DateTimeOffset? ConditionStartedAtUtc { get; init; }
    internal DateTimeOffset? CallbackStartedAtUtc { get; init; }
    internal DateTimeOffset? WorkCompletedAtUtc { get; init; }
    internal DateTimeOffset? ErrorHandlingStartedAtUtc { get; init; }
    internal DateTimeOffset? ErrorHandlingCompletedAtUtc { get; init; }
    internal DateTimeOffset? CleanupCompletedAtUtc { get; init; }
    internal DateTimeOffset? SuppressedAtUtc { get; init; }
    internal DateTimeOffset? SettledAtUtc { get; init; }
    internal HeartbeatPhase? SuppressedPhase { get; init; }
    internal bool TargetUnavailable { get; init; }
    internal bool? ConditionResult { get; init; }
    internal bool? CallbackResult { get; init; }
    internal HeartbeatPhase? ExpectedCancellationPhase { get; init; }
    internal double? QueueDelayMilliseconds { get; init; }
    internal double? DispatchDelayMilliseconds { get; init; }
    internal double? ConditionDurationMilliseconds { get; init; }
    internal double? CallbackDurationMilliseconds { get; init; }
    internal double? ErrorHandlingDurationMilliseconds { get; init; }
    internal double? CleanupDurationMilliseconds { get; init; }
    internal double? TotalDurationMilliseconds { get; init; }
    internal HeartbeatStep ErrorHandlingStatus { get; init; } = HeartbeatStep.NotApplicable;
    internal HeartbeatStep CleanupStatus { get; init; } = HeartbeatStep.NotApplicable;
    internal Exception? OriginalError { get; init; }
    internal Exception? ErrorHandlingError { get; init; }
    internal Exception? CleanupError { get; init; }
    internal Exception? IntegrationError { get; init; }
}

internal interface IHeartbeatObserver : IAsyncDisposable
{
    void Report(HeartbeatNotification notification);
    // Any owner-using handoff remains within the execution's slot and lifetime hold.
    ValueTask FlushAsync();
}

internal interface IHeartbeatExecutor
{
    // False guarantees no wrapper was posted. The production path never flows request context.
    bool TryPost(Action start);
}

internal sealed class HeartbeatThreadPoolExecutor : IHeartbeatExecutor
{
    public bool TryPost(Action start) => ThreadPool.UnsafeQueueUserWorkItem(static action => action(), start, preferLocal: false);
}
