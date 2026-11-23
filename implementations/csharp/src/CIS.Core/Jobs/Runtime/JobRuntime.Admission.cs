using CIS.Coordination;

namespace CIS.Jobs;

public sealed partial class JobRuntime
{
    internal bool IsEnabled(JobScopeRequest target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (gate)
        {
            return started && !closing && EnabledUnderGate(target);
        }
    }

    internal Task StopDutyAsync(JobScopeRequest target, CancellationToken cancellationToken)
    {
        JobDutyReservation? duty;
        lock (gate)
        {
            duties.TryGetValue(Key(target), out duty);
        }
        if (duty is null || duty.Target.Mode != target.Mode)
        {
            return Task.CompletedTask;
        }

        duty.RequestDrain();
        return cancellationToken.CanBeCanceled ? duty.Completion.WaitAsync(cancellationToken) : duty.Completion;
    }

    internal JobPendingDispatch? TryReservePending()
    {
        lock (gate)
        {
            if (!started || closing || !CanAdmitManagedWork() || pending.Count >= configuration.Limits.MaximumPendingDispatches)
            {
                return null;
            }

            var reservation = new JobPendingDispatch(this);
            pending.Add(reservation);
            return reservation;
        }
    }

    internal JobAdmission TryReserveContinuous(JobScopeRequest target, long? expectedActivationRevision = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (gate)
        {
            JobHandlerBinding? binding = registered.GetValueOrDefault((target.ExecutionScope, target.JobId));
            if (target.Mode != JobExecutionMode.Continuous || binding is null
                || !binding.Definition.SupportedModes.Contains(JobExecutionMode.Continuous))
            {
                return new(JobAdmissionOutcome.Unsupported);
            }

            long activationRevision = activationRevisions[(target.ExecutionScope, target.JobId)];
            if (expectedActivationRevision is { } expected && expected != activationRevision)
            {
                return new(JobAdmissionOutcome.Deferred);
            }

            if (duties.TryGetValue(Key(target), out JobDutyReservation? existing))
            {
                bool sameActivation = expectedActivationRevision is null
                    || existing.Target.Mode == JobExecutionMode.Continuous && existing.ActivationRevision == activationRevision;
                return new(sameActivation ? JobAdmissionOutcome.Busy : JobAdmissionOutcome.Deferred);
            }

            if (!started || closing || !CanAdmitManagedWork() || !EnabledUnderGate(target) || !CanTrackResourceUnderGate(target)
                || duties.Values.Count(duty => duty.Target.Mode == JobExecutionMode.Continuous) >= configuration.Limits.MaximumContinuousDuties)
            {
                return new(JobAdmissionOutcome.Deferred);
            }

            JobExecutionSettings settings = configuration.Jobs[(target.ExecutionScope, target.JobId)].Execution;
            var duty = new JobDutyReservation(this, target, binding, settings.MaximumDrain, activationRevision: activationRevision);
            duties.Add(Key(target), duty);
            PulseUnderGate();
            return new(JobAdmissionOutcome.Accepted, duty);
        }
    }

    internal JobAdmission TryReserveScheduled(AdmittedJobRun run, JobPendingDispatch pendingDispatch)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(pendingDispatch);
        var target = new JobScopeRequest(run.ExecutionScope, run.JobId, run.ResourceId, JobExecutionMode.Scheduled);
        lock (gate)
        {
            if (!pending.Contains(pendingDispatch) || pendingDispatch.Released || pendingDispatch.Claimed)
            {
                throw new InvalidOperationException("Use one live pending reservation from this runtime for one scheduled admission.");
            }

            JobHandlerBinding? binding = registered.GetValueOrDefault((target.ExecutionScope, target.JobId));
            if (binding is null || !binding.Definition.SupportedModes.Contains(JobExecutionMode.Scheduled))
            {
                return new(JobAdmissionOutcome.Unsupported);
            }

            if (duties.ContainsKey(Key(target)))
            {
                return new(JobAdmissionOutcome.Busy);
            }

            if (!started || closing || !CanAdmitManagedWork() || !EnabledUnderGate(target) || !HasInvocationCapacityUnderGate(target)
                || !CanTrackResourceUnderGate(target))
            {
                return new(JobAdmissionOutcome.Deferred);
            }

            var duty = new JobDutyReservation(this, target, binding, run.Settings.MaximumDrain,
                configuration.Jobs[(target.ExecutionScope, target.JobId)].Execution.ScheduledActivation);
            var invocation = NewInvocationUnderGate(duty, run.Settings, pendingDispatch);
            pendingDispatch.Claimed = true;
            duties.Add(Key(target), duty);
            invocations.Add(invocation);
            PulseUnderGate();
            return new(JobAdmissionOutcome.Accepted, duty, invocation);
        }
    }

    internal JobInvocationReservation? TryReserveIteration(JobDutyReservation duty)
    {
        ArgumentNullException.ThrowIfNull(duty);
        lock (gate)
        {
            if (!IsCurrentUnderGate(duty) || duty.Closing || closing || !CanAdmitManagedWork() || !EnabledUnderGate(duty.Target)
                || duty.Target.Mode != JobExecutionMode.Continuous
                || invocations.Any(item => ReferenceEquals(item.Duty, duty)))
            {
                return null;
            }

            iterationWaiters.RemoveAll(item => item.Released || item.Closing || !EnabledUnderGate(item.Target));
            if (!iterationWaiters.Contains(duty))
            {
                iterationWaiters.Add(duty);
            }

            JobDutyReservation? eligible = iterationWaiters.FirstOrDefault(item => HasInvocationCapacityUnderGate(item.Target));
            if (!ReferenceEquals(eligible, duty))
            {
                return null;
            }

            iterationWaiters.Remove(duty);
            JobExecutionSettings settings = configuration.Jobs[(duty.Target.ExecutionScope, duty.Target.JobId)].Execution;
            JobInvocationReservation invocation = NewInvocationUnderGate(duty, settings);
            invocations.Add(invocation);
            return invocation;
        }
    }

    internal bool TryEnterInvocation(JobInvocationReservation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ICoordinationLease? lease = invocation.Duty.Lease;
        bool owned = lease?.State == CoordinationLeaseState.Owned;
        long? timestamp = Timestamp();
        IJobRuntimeObserver? observed;
        lock (gate)
        {
            if (!owned || !ReferenceEquals(lease, invocation.Duty.CurrentLease)
                || !invocations.Contains(invocation) || invocation.Released || invocation.Started
                || !IsCurrentUnderGate(invocation.Duty) || invocation.Duty.Closing || closing || !CanAdmitManagedWork()
                || !EnabledUnderGate(invocation.Duty.Target) || BindingChangedUnderGate(invocation.Duty)
                || !HasInvocationCapacityUnderGate(invocation.Duty.Target, invocation))
            {
                return false;
            }

            invocation.Started = true;
            invocation.StartedAt = timestamp;
            invocation.Duty.LocalState = JobLocalState.Running;
            observed = observer;
        }

        Observe(() => invocation.Observation = observed?.CaptureInvocationObserver());
        invocation.Pending?.Dispose();
        Observe(() => invocation.Observation?.InvocationStarted(invocation.Duty.Target.Mode));
        return true;
    }

    internal bool AttachLease(JobDutyReservation duty, ICoordinationLease lease)
    {
        ArgumentNullException.ThrowIfNull(duty);
        ArgumentNullException.ThrowIfNull(lease);
        CoordinationGrant grant = lease.Grant;
        DateTimeOffset? observedAt = null;
        Observe(() => observedAt = clock.GetUtcNow());
        if (grant.Key != new CoordinationKey(application.Environment, duty.Target.ExecutionScope, duty.Target.JobId, duty.Target.ResourceId)
            || grant.Acquisition.OwnerApplication != application.Application || grant.Acquisition.InstanceId != application.InstanceId)
        {
            return false;
        }

        bool drain;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (!IsCurrentUnderGate(duty))
            {
                return false;
            }

            if (duty.CurrentLease is not null)
            {
                return ReferenceEquals(duty.CurrentLease, lease);
            }

            duty.CurrentLease = lease;
            duty.LeaseObservation = observed.Task;
            duty.OwnerObservation = observedAt is { } time ? new(grant.Acquisition.OwnerApplication, grant.Acquisition.InstanceId,
                grant.Generation, grant.ExpiresAtUtc, time) : null;
            drain = duty.Closing || closing;
        }

        _ = Task.Run(() => ObserveLeaseAsync(duty, lease, observed), CancellationToken.None);

        if (drain)
        {
            lease.BeginDraining(duty.DrainBudget);
        }

        return true;
    }

    internal void ObserveStandby(JobDutyReservation duty, CoordinationOwnerObservation? owner)
    {
        lock (gate)
        {
            if (IsCurrentUnderGate(duty))
            {
                duty.OwnerObservation = owner;
                if (!duty.Closing)
                {
                    duty.LocalState = JobLocalState.Standby;
                }
            }
        }
    }

    internal void SetState(JobDutyReservation duty, JobLocalState state, string? reasonCode)
    {
        string? code = reasonCode is null ? null : JobText.Text(reasonCode, 128, nameof(reasonCode));
        JobRuntimeSnapshot? observation = null;
        IJobRuntimeObserver? observed;
        lock (gate)
        {
            observed = observer;
            if (IsCurrentUnderGate(duty))
            {
                if (duty.Closing && state is (JobLocalState.Standby or JobLocalState.Acquiring or JobLocalState.Running or JobLocalState.Idle))
                {
                    return;
                }

                bool transition = duty.LocalState != state || duty.LastOutcome != code;
                duty.LocalState = state;
                duty.LastOutcome = code;
                if (state == JobLocalState.Faulted)
                {
                    RetainFailureUnderGate(duty);
                }

                if (transition)
                {
                    observation = SnapshotUnderGate(duty);
                }
            }
        }

        if (observation is not null)
        {
            Observe(() => observed?.Transition(observation));
        }
    }

    internal bool IsDraining(JobDutyReservation duty)
    {
        lock (gate)
        {
            return duty.Closing || closing || duty.Released;
        }
    }

    internal bool IsFaulted(JobDutyReservation duty)
    {
        lock (gate)
        {
            return duty.LocalState == JobLocalState.Faulted;
        }
    }

    internal ICoordinationLease? GetLease(JobDutyReservation duty)
    {
        lock (gate)
        {
            return duty.CurrentLease;
        }
    }

    internal TimeSpan GetDrainBudget(JobDutyReservation duty)
    {
        lock (gate)
        {
            return duty.CurrentDrainBudget;
        }
    }

    internal void DrainDuty(JobDutyReservation duty)
    {
        ICoordinationLease? lease;
        TaskCompletionSource cancellation;
        TimeSpan budget;
        lock (gate)
        {
            if (!IsCurrentUnderGate(duty) || duty.Closing)
            {
                return;
            }

            duty.Closing = true;
            duty.LocalState = JobLocalState.Draining;
            lease = duty.CurrentLease;
            cancellation = duty.PrepareCancellation();
            budget = duty.CurrentDrainBudget;
            PulseUnderGate();
        }

        _ = SignalDutyDrainAsync(duty, lease, budget, cancellation);
    }

    internal void ReleasePending(JobPendingDispatch reservation)
    {
        lock (gate)
        {
            if (reservation.Released)
            {
                return;
            }

            reservation.Released = true;
            pending.Remove(reservation);
            reservation.Completion.TrySetResult();
            PulseUnderGate();
            CompleteStopUnderGate();
        }
    }

    internal void ReleaseInvocation(JobInvocationReservation reservation, string outcome, bool scopeCleanupSucceeded = true)
    {
        if (outcome is not ("success" or "failed" or "cancelled" or "ownership_lost" or "indeterminate"))
        {
            throw new ArgumentException("Use the finite jobs observation outcomes.", nameof(outcome));
        }

        long? now = Timestamp();
        bool entered;
        IJobRuntimeObserver? observed;
        lock (gate)
        {
            if (reservation.Released)
            {
                return;
            }

            reservation.Released = true;
            entered = reservation.Started;
            observed = reservation.Observation;
            invocations.Remove(reservation);
            if (entered && outcome == "success" && scopeCleanupSucceeded
                && reservation.Duty.LocalState is not (JobLocalState.Faulted or JobLocalState.Lost))
            {
                RecoverResourceUnderGate(reservation.Duty);
            }

            PulseUnderGate();
            CompleteStopUnderGate();
        }

        if (entered)
        {
            double? seconds = null;
            if (reservation.StartedAt is { } begin && now is { } end)
            {
                Observe(() => seconds = clock.GetElapsedTime(begin, end).TotalSeconds);
            }

            Observe(() => observed?.InvocationCompleted(reservation.Duty.Target.Mode, outcome, seconds));
        }
    }

    internal async ValueTask ReleaseDutyAsync(JobDutyReservation duty)
    {
        await duty.LeaseObservation.ConfigureAwait(false);
        while (true)
        {
            Task cancellation;
            bool removed = false;
            lock (gate)
            {
                if (duty.Released)
                {
                    return;
                }

                cancellation = duty.Cancellation;
                if (cancellation.IsCompletedSuccessfully)
                {
                    if (invocations.Any(item => ReferenceEquals(item.Duty, duty)))
                    {
                        throw new InvalidOperationException("Release the actual invocation and scope before its stable duty occupancy.");
                    }

                    duty.Released = true;
                    duties.Remove(Key(duty.Target));
                    iterationWaiters.Remove(duty);
                    if (duty.LocalState is not (JobLocalState.Faulted or JobLocalState.Lost))
                    {
                        duty.LocalState = JobLocalState.Released;
                    }

                    latest[(duty.Target.ExecutionScope, duty.Target.JobId)] = SnapshotUnderGate(duty);
                    if (duty.LocalState == JobLocalState.Faulted)
                    {
                        RetainFailureUnderGate(duty);
                    }

                    duty.Finished.TrySetResult();
                    PulseUnderGate();
                    CompleteStopUnderGate();
                    removed = true;
                }
            }

            if (removed)
            {
                duty.Stop.Dispose();
                return;
            }

            await cancellation.ConfigureAwait(false);
        }
    }

    private bool HasInvocationCapacityUnderGate(JobScopeRequest target, JobInvocationReservation? excluding = null)
    {
        if (invocations.Count(item => !ReferenceEquals(item, excluding)) >= configuration.Limits.MaximumInvocations)
        {
            return false;
        }

        JobSettings settings = configuration.Jobs[(target.ExecutionScope, target.JobId)];
        string category = settings.CategoryId ?? registered[(target.ExecutionScope, target.JobId)].Definition.CategoryId;
        int ceiling = Math.Min(configuration.Limits.MaximumInvocations,
            configuration.Categories.GetValueOrDefault((target.ExecutionScope, category))?.MaximumInvocations
                ?? configuration.Limits.MaximumInvocations);
        return invocations.Count(item => !ReferenceEquals(item, excluding) && item.Duty.Target.ExecutionScope == target.ExecutionScope
            && CategoryUnderGate(item.Duty) == category) < ceiling;
    }

    private JobInvocationReservation NewInvocationUnderGate(JobDutyReservation duty, JobExecutionSettings settings,
        JobPendingDispatch? pendingDispatch = null)
    {
        string category = CategoryUnderGate(duty);
        if (settings.Mode == JobExecutionMode.Continuous)
        {
            duty.CurrentDrainBudget = settings.MaximumDrain;
        }

        TimeSpan? timeout = settings.Mode == JobExecutionMode.Scheduled ? settings.Timeout : settings.Timeout
            ?? configuration.Categories.GetValueOrDefault((duty.Target.ExecutionScope, category))?.DefaultTimeout;
        return new(this, duty, settings, timeout, configuration.Revision, pendingDispatch);
    }

    private bool IsCurrentUnderGate(JobDutyReservation duty)
        => !duty.Released && duties.TryGetValue(Key(duty.Target), out JobDutyReservation? current) && ReferenceEquals(current, duty);

    private static (string Scope, string Job, string Resource) Key(JobScopeRequest target)
        => (target.ExecutionScope, target.JobId, target.ResourceId);

    private long? Timestamp()
    {
        long? timestamp = null;
        Observe(() => timestamp = clock.GetTimestamp());
        return timestamp;
    }

    private async Task SignalDutyDrainAsync(JobDutyReservation duty, ICoordinationLease? lease, TimeSpan budget, TaskCompletionSource completion)
    {
        try
        {
            if (lease is not null)
            {
                try
                {
                    lease.BeginDraining(budget);
                }
                catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { RecordCleanupDiagnostic("jobs.lease_drain_failed"); }
            }

            try
            {
                await duty.Stop.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error)) { RecordCleanupDiagnostic("jobs.stop_callback_failed"); }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
