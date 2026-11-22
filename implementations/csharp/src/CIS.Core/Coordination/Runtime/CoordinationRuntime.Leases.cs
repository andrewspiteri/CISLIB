namespace CIS.Coordination;

public sealed partial class CoordinationRuntime
{
    private int renewalWorkers;

    private void Pump()
    {
        List<Entry> lost = [];
        List<Entry> renew = [];
        lock (gate)
        {
            foreach (Entry entry in entries.Values)
            {
                if (CheckDeadlineLocked(entry))
                {
                    lost.Add(entry);
                    continue;
                }

                if (entry.State is CoordinationLeaseState.Owned or CoordinationLeaseState.Draining
                    && !entry.RenewalInFlight && Remaining(entry.NextRenewal.Started, entry.NextRenewal.Budget) <= TimeSpan.Zero
                    && renewalWorkers < options.MaximumConcurrentRenewals && renewalRequests < options.MaximumConcurrentRenewals)
                {
                    entry.RenewalInFlight = true;
                    entry.RenewalCoordinator = true;
                    entry.Coordinators++;
                    renewalWorkers++;
                    renew.Add(entry);
                }
            }
        }

        foreach (Entry entry in lost)
        {
            DispatchTerminal(entry);
        }

        foreach (Entry entry in renew)
        {
            _ = Task.Run(() => RenewAsync(entry), CancellationToken.None);
        }
    }

    private async Task RenewAsync(Entry entry)
    {
        try
        {
            CoordinationGrant grant;
            TimeSpan budget;
            bool lost;
            lock (gate)
            {
                lost = CheckDeadlineLocked(entry);
                if (entry.State is not (CoordinationLeaseState.Owned or CoordinationLeaseState.Draining))
                {
                    grant = entry.Grant!;
                    budget = TimeSpan.Zero;
                }
                else
                {
                    grant = entry.Grant!;
                    budget = Remaining(entry.Deadline.Started, entry.Deadline.Budget);
                    if (entry.DrainDeadline is { } drain)
                    {
                        budget = Min(budget, Remaining(drain.Started, drain.Budget));
                    }
                }
            }

            if (lost)
            {
                DispatchTerminal(entry);
            }

            if (budget <= TimeSpan.Zero)
            {
                return;
            }

            PhysicalReply<CoordinationMutationResult> reply = await RequestAsync(entry, CoordinationRuntimeOperation.Renew,
                token => provider.RenewAsync(grant, options.LeaseDuration, token), Min(options.RequestTimeout, budget),
                CancellationToken.None).ConfigureAwait(false);
            bool terminal;
            lock (gate)
            {
                // Recheck the OLD confirmed stop against the current clock before examining a late acknowledgement.
                terminal = CheckDeadlineLocked(entry);
                if (entry.State is CoordinationLeaseState.Owned or CoordinationLeaseState.Draining)
                {
                    if (reply.Result is { Outcome: CoordinationMutationOutcome.NotCurrent } refused)
                    {
                        terminal = MarkTerminalLocked(entry, CoordinationLeaseState.Lost,
                            refused.RejectionReason == CoordinationRejectionReason.Expired
                                ? CoordinationLossReason.Expired : CoordinationLossReason.Superseded);
                    }
                    else if (reply.Result is { Outcome: CoordinationMutationOutcome.Succeeded, Grant: not null } renewed
                        && IsExact(entry, renewed.Grant) && renewed.Grant.Generation == grant.Generation)
                    {
                        TimeSpan confirmed = Min(renewed.RemainingValidity, options.LeaseDuration) - options.SafetyMargin;
                        if (Remaining(reply.Started, confirmed) > TimeSpan.Zero)
                        {
                            entry.Grant = renewed.Grant;
                            entry.Deadline = new(reply.Started, confirmed);
                            entry.NextRenewal = new(time.GetTimestamp(), options.RenewalInterval);
                        }
                        else
                        {
                            entry.NextRenewal = new(time.GetTimestamp(), options.RenewalRetryDelay);
                        }
                    }
                    else
                    {
                        // Unconfirmed or malformed acknowledgements never extend the prior stop.
                        entry.NextRenewal = new(time.GetTimestamp(), options.RenewalRetryDelay);
                    }
                }
            }

            if (terminal)
            {
                DispatchTerminal(entry);
            }
        }
        finally
        {
            lock (gate)
            {
                entry.RenewalCoordinator = false;
                if (entry.RenewalRequests == 0)
                {
                    entry.RenewalInFlight = false;
                }

                renewalWorkers--;
                entry.Coordinators--;
                RemoveSettledLocked(entry);
            }
        }
    }

    private bool CheckDeadlineLocked(Entry entry)
    {
        if (entry.State is not (CoordinationLeaseState.Owned or CoordinationLeaseState.Draining))
        {
            return false;
        }

        if (entry.DrainDeadline is { } drain && Remaining(drain.Started, drain.Budget) <= TimeSpan.Zero)
        {
            return MarkTerminalLocked(entry, CoordinationLeaseState.Lost, CoordinationLossReason.ShutdownDeadlineExceeded);
        }

        if (Remaining(entry.Deadline.Started, entry.Deadline.Budget) <= TimeSpan.Zero)
        {
            return MarkTerminalLocked(entry, CoordinationLeaseState.Lost, CoordinationLossReason.RenewalDeadlineExceeded);
        }

        return false;
    }

    private bool MarkTerminalLocked(Entry entry, CoordinationLeaseState terminal, CoordinationLossReason? reason = null)
    {
        if (entry.State is CoordinationLeaseState.Lost or CoordinationLeaseState.Released)
        {
            return false;
        }

        entry.State = terminal;
        entry.LossReason = reason;
        entry.Active = false;
        entry.Notifications++;
        notifications++;
        return true;
    }

    private bool BeginDrainingLocked(Entry entry, TimeSpan budget)
    {
        if (entry.State is not (CoordinationLeaseState.Owned or CoordinationLeaseState.Draining))
        {
            return false;
        }

        TimeSpan bounded = Min(budget, options.MaximumGracefulDrain);
        if (entry.DrainDeadline is { } previous && Remaining(previous.Started, previous.Budget) <= bounded)
        {
            return false;
        }

        entry.DrainDeadline = new(time.GetTimestamp(), bounded);
        bool changed = entry.State != CoordinationLeaseState.Draining;
        entry.State = CoordinationLeaseState.Draining;
        return changed;
    }

    private void DispatchTerminal(Entry entry)
    {
        CoordinationLeaseTermination termination;
        CoordinationLeaseSnapshot snapshot;
        ICoordinationRuntimeObserver? target;
        lock (gate)
        {
            termination = new(entry.State, entry.LossReason);
            snapshot = SnapshotLocked(entry);
            target = observer;
        }

        entry.Termination.TrySetResult(termination);
        // CancelAsync signals the token now while executing arbitrary registrations outside this scheduler and all gates.
        Task cancellation;
        try
        {
            cancellation = entry.OwnershipLost.CancelAsync();
        }
        catch (Exception exception)
        {
            cancellation = Task.FromException(exception);
        }

        _ = FinishTerminalAsync(entry, cancellation, target, snapshot);
    }

    private async Task FinishTerminalAsync(Entry entry, Task cancellation, ICoordinationRuntimeObserver? target,
        CoordinationLeaseSnapshot snapshot)
    {
        Task observation = Task.Run(() => ObserveTransition(target, snapshot), CancellationToken.None);
        try
        {
            await cancellation.ConfigureAwait(false);
        }
        catch
        {
            // A cancellation callback fault cannot replace the stable terminal fact.
        }

        await observation.ConfigureAwait(false);
        EndNotification(entry);
    }

    private TransitionNotice? CaptureTransitionLocked(Entry entry)
    {
        if (observer is null)
        {
            return null;
        }

        entry.Notifications++;
        notifications++;
        return new(observer, SnapshotLocked(entry));
    }

    private void DispatchTransition(Entry entry, TransitionNotice? notice)
    {
        if (notice is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                ObserveTransition(notice.Value.Target, notice.Value.Snapshot);
            }
            finally
            {
                EndNotification(entry);
            }
        }, CancellationToken.None);
    }

    private static void ObserveTransition(ICoordinationRuntimeObserver? target, CoordinationLeaseSnapshot snapshot)
    {
        try
        {
            target?.Transition(snapshot);
        }
        catch
        {
            // Optional trusted telemetry never changes the terminal result or scheduler progress.
        }
    }

    private void EndNotification(Entry entry)
    {
        lock (gate)
        {
            entry.Notifications--;
            notifications--;
            RemoveSettledLocked(entry);
        }
    }

    private CoordinationLeaseState ReadState(Entry entry)
    {
        bool terminal;
        CoordinationLeaseState current;
        lock (gate)
        {
            terminal = CheckDeadlineLocked(entry);
            current = entry.State;
        }

        if (terminal)
        {
            DispatchTerminal(entry);
        }

        return current;
    }

    private void BeginDraining(Entry entry, TimeSpan? requested)
    {
        TimeSpan budget = requested ?? options.MaximumGracefulDrain;
        CoordinationText.Duration(budget, nameof(requested));
        bool terminal;
        bool changed;
        TransitionNotice? notice = null;
        lock (gate)
        {
            terminal = CheckDeadlineLocked(entry);
            changed = BeginDrainingLocked(entry, budget);
            if (changed)
            {
                notice = CaptureTransitionLocked(entry);
            }
        }

        if (terminal)
        {
            DispatchTerminal(entry);
        }
        else if (changed)
        {
            DispatchTransition(entry, notice);
        }
    }

    private Task<CoordinationMutationResult> Release(Entry entry, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        Task<CoordinationMutationResult> release;
        bool terminal;
        lock (gate)
        {
            if (entry.ReleaseTask is not null)
            {
                return entry.ReleaseTask;
            }

            if (entry.State == CoordinationLeaseState.Lost)
            {
                return Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Unavailable,
                    reasonCode: "coordination.lease.lost"));
            }

            entry.Coordinators++;
            terminal = MarkTerminalLocked(entry, CoordinationLeaseState.Released);
            release = Task.Run(() => ReleaseCoreAsync(entry, caller), CancellationToken.None);
            entry.ReleaseTask = release;
        }

        if (terminal)
        {
            DispatchTerminal(entry);
        }

        return release;
    }

    private async Task<CoordinationMutationResult> ReleaseCoreAsync(Entry entry, CancellationToken caller)
    {
        try
        {
            PhysicalReply<CoordinationMutationResult> reply = await RequestAsync(entry, CoordinationRuntimeOperation.Release,
                token => provider.ReleaseAsync(entry.Grant!, token), options.RequestTimeout, caller).ConfigureAwait(false);
            if (reply.Result is { } result)
            {
                return result;
            }

            caller.ThrowIfCancellationRequested();
            return new(reply.CapacityExceeded ? CoordinationMutationOutcome.Unavailable : CoordinationMutationOutcome.Indeterminate,
                reasonCode: reply.CapacityExceeded ? "coordination.capacity.requests" : "coordination.release.unconfirmed");
        }
        finally
        {
            EndCoordinator(entry);
        }
    }

    private sealed class ManagedLease(CoordinationRuntime owner, Entry entry) : ICoordinationLease
    {
        public CoordinationGrant Grant
        {
            get
            {
                lock (owner.gate)
                {
                    return entry.Grant!;
                }
            }
        }

        public CoordinationLeaseState State => owner.ReadState(entry);

        public CoordinationLossReason? LossReason
        {
            get
            {
                owner.ReadState(entry);
                lock (owner.gate)
                {
                    return entry.LossReason;
                }
            }
        }

        public CancellationToken OwnershipLost => entry.OwnershipToken;

        public Task<CoordinationLeaseTermination> Termination => entry.Termination.Task;

        public void BeginDraining(TimeSpan? drainBudget = null) => owner.BeginDraining(entry, drainBudget);

        public Task<CoordinationMutationResult> ReleaseAsync(CancellationToken cancellationToken = default) => owner.Release(entry, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync().ConfigureAwait(false);
            await entry.Settled.Task.ConfigureAwait(false);
        }
    }

    private readonly record struct TransitionNotice(ICoordinationRuntimeObserver Target, CoordinationLeaseSnapshot Snapshot);
}
