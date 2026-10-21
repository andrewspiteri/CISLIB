using CIS.Healthcheck.Internal;

namespace CIS.Healthcheck;

/// <summary>Associates detached execution facts with an explicitly selected health policy.</summary>
/// <remarks>Binding is atomic and inert: it creates no schedule and executes no callback.</remarks>
public static class HealthExecutionBinder
{
    /// <summary>Binds an active Core-owned consecutive-failure or availability monitor without transferring its ownership.</summary>
    public static IHealthExecutionBinding Bind(IHealthMonitor monitor, HealthExecutionBindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(options);
        if (monitor is not IHealthStateAccess access)
        {
            throw new ArgumentException("Use an active monitor created by this Healthcheck implementation.", nameof(monitor));
        }
        try { return Create(access, BindingRequest.ForMonitor(monitor.MonitorId, options)); }
        finally { GC.KeepAlive(monitor); }
    }

    /// <summary>Atomically creates and binds a monitor whose lifetime is owned by the returned binding.</summary>
    public static IHealthExecutionBinding Bind(IHealthMonitorFactory factory,
        ConsecutiveFailureMonitorOptions policy, HealthExecutionBindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(options);
        return Create(GetAccess(factory), BindingRequest.ForConsecutiveFailures(policy, options));
    }

    /// <summary>Atomically creates and binds availability that requires an explicit usable-data publication declaration.</summary>
    public static IHealthExecutionBinding Bind(IHealthMonitorFactory factory,
        AvailabilityMonitorOptions policy, HealthExecutionBindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(options);
        return Create(GetAccess(factory), BindingRequest.ForAvailability(policy, options));
    }

    private static IHealthStateAccess GetAccess(IHealthMonitorFactory factory) => factory as IHealthStateAccess
        ?? throw new ArgumentException("Use the monitor factory supplied by this Healthcheck implementation.", nameof(factory));

    private static ExecutionBinding Create(IHealthStateAccess access, BindingRequest request)
    {
        IHealthExecutionState state = access.ExecutionState;
        IHealthCollectionState collectionState = access.CollectionState;
        BindingTarget target = state.Bind(request);
        return new ExecutionBinding(state, collectionState, target);
    }

    private sealed class ExecutionBinding(IHealthExecutionState state, IHealthCollectionState collectionState,
        BindingTarget target) : IHealthExecutionBinding
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource settlement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool retired;
        private bool releaseStarted;
        private Task? actualSettlement;
        private DateTimeOffset? registeredAt;
        private ExecutionFactRecord? current;
        private ExecutionFactRecord? previous;
        private long outcomeHighWater;

        public Guid RegistrationId => target.RegistrationId;
        public Guid MonitorId => target.MonitorId;

        public HealthFactDisposition Report(HealthExecutionFact fact)
        {
            // Retirement is terminal even for detached late notifications that are no longer useful to validate.
            if (ObserveExternalRetirement()) { return HealthFactDisposition.Retired; }
            HealthExecutionFact normalized = ExecutionFactValidation.Normalize(fact, RegistrationId);
            if (normalized.Kind == HealthExecutionFactKind.Retired) { return Retire(normalized); }
            bool release = false;
            try
            {
                lock (gate)
                {
                    if (retired) { return HealthFactDisposition.Retired; }
                    if (!state.IsActive(target))
                    {
                        return ObserveRetirement(HealthFactDisposition.Retired, ref release);
                    }
                    if (normalized.Kind == HealthExecutionFactKind.Registered)
                    {
                        if (registeredAt.HasValue)
                        {
                            if (registeredAt.Value != normalized.OccurredAtUtc)
                            {
                                throw ExecutionFactValidation.Contradiction();
                            }
                            return HealthFactDisposition.Duplicate;
                        }
                        HealthFactDisposition disposition = state.UpdateLifecycle(target, normalized);
                        if (disposition == HealthFactDisposition.Recorded) { registeredAt = normalized.OccurredAtUtc; }
                        return ObserveRetirement(disposition, ref release);
                    }
                    if (normalized.ExecutionId is not long executionId)
                    {
                        return ObserveRetirement(state.UpdateLifecycle(target, normalized), ref release);
                    }

                    ExecutionFactRecord? retained = current?.ExecutionId == executionId ? current
                        : previous?.ExecutionId == executionId ? previous : null;
                    if (retained is null && current is not null && executionId < current.ExecutionId)
                    {
                        return HealthFactDisposition.Obsolete;
                    }

                    ExecutionFactRecord next = (retained ?? new ExecutionFactRecord(executionId)).Merge(normalized);
                    bool repeatedOutcome = normalized.Kind == HealthExecutionFactKind.Outcome && retained?.HasOutcome == true;
                    if (normalized.Kind == HealthExecutionFactKind.Outcome && !repeatedOutcome && executionId <= outcomeHighWater)
                    {
                        return HealthFactDisposition.Obsolete;
                    }
                    if (repeatedOutcome && next.SameContext(retained!)) { return HealthFactDisposition.Duplicate; }

                    HealthFactDisposition accepted = normalized.Kind == HealthExecutionFactKind.Outcome && !repeatedOutcome
                        ? state.ApplyOutcome(target, normalized) : state.UpdateLifecycle(target, normalized);
                    if (accepted != HealthFactDisposition.Recorded) { return ObserveRetirement(accepted, ref release); }
                    if (retained is null)
                    {
                        previous = current;
                        current = next;
                    }
                    else if (ReferenceEquals(current, retained)) { current = next; }
                    else { previous = next; }
                    if (normalized.Kind == HealthExecutionFactKind.Outcome && !repeatedOutcome) { outcomeHighWater = executionId; }
                    return repeatedOutcome ? HealthFactDisposition.Duplicate : HealthFactDisposition.Recorded;
                }
            }
            finally
            {
                if (release) { Release(); }
                GC.KeepAlive(this);
            }
        }

        private HealthFactDisposition ObserveRetirement(HealthFactDisposition disposition, ref bool release)
        {
            if (disposition == HealthFactDisposition.Retired)
            {
                retired = true;
                release = ClaimRelease();
            }
            return disposition;
        }

        private bool ObserveExternalRetirement()
        {
            bool release;
            lock (gate)
            {
                if (retired) { return true; }
                if (state.IsActive(target)) { return false; }
                retired = true;
                release = ClaimRelease();
            }
            if (release) { Release(); }
            return true;
        }

        private HealthFactDisposition Retire(HealthExecutionFact fact)
        {
            bool release;
            lock (gate)
            {
                if (retired) { return HealthFactDisposition.Retired; }
                retired = true;
                release = ClaimRelease();
            }
            try { return state.UpdateLifecycle(target, fact); }
            finally
            {
                // Withdrawal can synchronously notify user lifetime callbacks. Never hold the ordering gate here.
                if (release) { Release(); }
                GC.KeepAlive(this);
            }
        }

        private bool ClaimRelease()
        {
            if (releaseStarted) { return false; }
            releaseStarted = true;
            return true;
        }

        private void Release()
        {
            try
            {
                actualSettlement = state.ReleaseBinding(target);
                if (actualSettlement.IsCompletedSuccessfully) { settlement.TrySetResult(); }
                else { _ = CompleteSettlementAsync(actualSettlement, settlement); }
            }
            catch (Exception error)
            {
                settlement.TrySetException(error);
                throw;
            }
        }

        private static async Task CompleteSettlementAsync(Task actual, TaskCompletionSource completion)
        {
            try
            {
                await actual.ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (OperationCanceledException canceled) { completion.TrySetCanceled(canceled.CancellationToken); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        public ValueTask DisposeAsync()
        {
            bool release;
            lock (gate)
            {
                retired = true;
                release = ClaimRelease();
            }
            if (release) { Release(); }
            if (target.Ownership == HealthBindingOwnership.CreatedMonitor)
            {
                HealthCollectionExecution.ThrowIfSelfWait(collectionState, monitorId: MonitorId);
            }
            return new ValueTask(settlement.Task);
        }
    }
}
