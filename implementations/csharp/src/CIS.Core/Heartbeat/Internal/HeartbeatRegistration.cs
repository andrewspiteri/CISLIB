namespace CIS.Heartbeat;

internal sealed class HeartbeatRegistration(IHeartbeatSubscriber target, HeartbeatOwner owner,
    HeartbeatRegistrationOptions options, long intervalUnits, long delayUnits)
{
    internal readonly Guid Id = Guid.NewGuid();
    internal readonly WeakReference<IHeartbeatSubscriber> Target = new(target);
    internal readonly WeakReference<HeartbeatOwner> Owner = new(owner);
    internal readonly HeartbeatRegistrationOptions Options = options;
    internal readonly long IntervalUnits = intervalUnits;
    internal readonly long InitialDelayUnits = delayUnits;
    internal readonly TaskCompletionSource Initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Only the audited concrete Core adapter (or a controlled internal test observer)
    // is retained independently of the weak key, so collection can still retire it.
    internal IHeartbeatObserver? Observer;
    internal HeartbeatExecution? Current;
    internal long Origin, Delay, LastExecutionId;
    internal DateTimeOffset OriginUtc;
    internal TimeSpan EffectiveDelay;
    internal bool Initialized, Closing, Retiring, Unusable, ReportingSettlement;
}

// ConditionalWeakTable owns this value only while its actual subscriber key is alive.
internal sealed record HeartbeatBinding(HeartbeatRegistration Registration,
    Func<CancellationToken, Task<bool>>? Condition, Func<ValueTask>? Cleanup, CancellationToken ExecutionCancellation);

internal readonly record struct HeartbeatSchedulerCounts(int Registrations, int Accepted, int Dispatched, int Pending);
