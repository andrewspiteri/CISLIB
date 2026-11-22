using CIS.Coordination;

namespace CIS.Jobs;

public sealed partial class JobRuntime
{
    internal JobRuntimeHealth CaptureHealth()
    {
        lock (gate)
        {
            bool faulted = duties.Values.Any(duty => EnabledUnderGate(duty.Target) && duty.LocalState == JobLocalState.Faulted)
                || latest.Values.Any(item => item.State == JobLocalState.Faulted && EnabledUnderGate(item.Target)
                    && !duties.Values.Any(duty => duty.Target.ExecutionScope == item.Target.ExecutionScope
                        && duty.Target.JobId == item.Target.JobId));
            bool stuck = invocations.Any(invocation => invocation.Started && invocation.Duty.LeaseEndedWhileRunning);
            return new(faulted || stuck);
        }
    }

    private async Task ObserveLeaseAsync(JobDutyReservation duty, ICoordinationLease lease, TaskCompletionSource completion)
    {
        try
        {
            CoordinationLeaseTermination terminal = await lease.Termination.ConfigureAwait(false);
            JobRuntimeSnapshot? snapshot = null;
            IJobRuntimeObserver? observed;
            lock (gate)
            {
                observed = observer;
                if (IsCurrentUnderGate(duty) && ReferenceEquals(duty.CurrentLease, lease)
                    && invocations.Any(invocation => invocation.Started && ReferenceEquals(invocation.Duty, duty)))
                {
                    duty.LeaseEndedWhileRunning = true;
                    if (duty.LocalState != JobLocalState.Faulted)
                    {
                        duty.LocalState = JobLocalState.Lost;
                        if (duty.LastOutcome is not ("jobs.completed_after_ownership_loss" or "jobs.previously_completed_after_ownership_loss"))
                        {
                            duty.LastOutcome = terminal.State == CoordinationLeaseState.Lost
                                ? "jobs.ownership_lost_while_running" : "jobs.released_while_running";
                        }
                    }

                    snapshot = SnapshotUnderGate(duty);
                }
            }

            if (snapshot is not null)
            {
                Observe(() => observed?.Transition(snapshot));
            }
        }
        catch (Exception error) when (CIS.Metrics.MetricsRuntime.IsOrdinary(error))
        {
            RecordCleanupDiagnostic("jobs.lease_observation_failed");
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void NotifyConfigurationChanged()
    {
        IJobRuntimeObserver? observed;
        lock (gate)
        {
            observed = observer;
        }

        Observe(() => observed?.ConfigurationChanged());
    }
}
