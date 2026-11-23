namespace CIS.Jobs;

public sealed partial class JobRuntime
{
    private readonly Dictionary<(string Scope, string Job), Dictionary<string, JobRuntimeSnapshot>> failedResources = [];

    private bool CanTrackResourceUnderGate(JobScopeRequest target)
    {
        failedResources.TryGetValue((target.ExecutionScope, target.JobId), out Dictionary<string, JobRuntimeSnapshot>? failures);
        if (failures?.ContainsKey(target.ResourceId) == true)
        {
            return true;
        }

        int retained = failures?.Count ?? 0;
        int active = duties.Values.Count(duty => duty.Target.ExecutionScope == target.ExecutionScope
            && duty.Target.JobId == target.JobId && failures?.ContainsKey(duty.Target.ResourceId) != true);
        int maximum = catalog.Limits.MaximumContinuousDuties + catalog.Limits.MaximumInvocations;
        return retained + active < maximum;
    }

    private void RetainFailureUnderGate(JobDutyReservation duty)
    {
        var definition = (duty.Target.ExecutionScope, duty.Target.JobId);
        if (!failedResources.TryGetValue(definition, out Dictionary<string, JobRuntimeSnapshot>? failures))
        {
            failures = new(StringComparer.Ordinal);
            failedResources.Add(definition, failures);
        }

        // Admission reserves this exact resource's slot before any callback can fail.
        failures[duty.Target.ResourceId] = SnapshotUnderGate(duty);
    }

    private void RecoverResourceUnderGate(JobDutyReservation duty)
    {
        var definition = (duty.Target.ExecutionScope, duty.Target.JobId);
        if (failedResources.TryGetValue(definition, out Dictionary<string, JobRuntimeSnapshot>? failures))
        {
            failures.Remove(duty.Target.ResourceId);
            if (failures.Count == 0)
            {
                failedResources.Remove(definition);
            }
        }
    }

    private IEnumerable<JobRuntimeSnapshot> CaptureRetainedFailuresUnderGate()
        => failedResources.Values.SelectMany(failures => failures.Values);
}
