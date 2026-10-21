namespace CIS.Healthcheck.Internal;

/// <summary>Atomic binding coordination implemented by the sole policy and monitor-state owner.</summary>
/// <remarks>The binder deduplicates facts before these operations. State code never calls binder or owner code under its gate.</remarks>
internal interface IHealthExecutionState
{
    /// <summary>Atomically associates a suitable active monitor, or creates and associates exactly one requested policy monitor.</summary>
    /// <remarks>Stopping, invalid policy, or liveness failure leaves no partial binding or newly created monitor.</remarks>
    BindingTarget Bind(BindingRequest request);

    /// <summary>Checks target liveness without changing revisions or lifecycle state.</summary>
    /// <remarks>Stopping closes new admission but existing bindings remain active for final handoffs until withdrawal.</remarks>
    bool IsActive(BindingTarget target);

    /// <summary>Commits one already-deduplicated execution outcome and its policy update atomically, or returns Retired.</summary>
    HealthFactDisposition ApplyOutcome(BindingTarget target, HealthExecutionFact fact);

    /// <summary>Records detached lifecycle/timing facts, including enriched deduplicated outcome context, without incrementing any outcome.</summary>
    HealthFactDisposition UpdateLifecycle(BindingTarget target, HealthExecutionFact fact);

    /// <summary>Releases the association, withdraws an owned monitor, and returns the shared actual-settlement barrier.</summary>
    /// <remarks>An external monitor is never withdrawn by unbinding. State-owned cancellation is signaled outside the state gate.</remarks>
    Task ReleaseBinding(BindingTarget target);
}
