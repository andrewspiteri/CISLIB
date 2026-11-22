namespace CIS.Jobs;

public sealed partial class JobRuntime
{
    // One stamp per frozen catalog definition, independent of completed duty/resource lifetimes.
    private readonly Dictionary<(string Scope, string Job), long> activationRevisions = [];

    internal JobActivationSnapshot CaptureActivationSnapshot(IReadOnlyList<JobScopeRequest> targets)
    {
        lock (gate)
        {
            var captured = new JobActivationState[targets.Count];
            for (int index = 0; index < targets.Count; index++)
            {
                JobScopeRequest target = targets[index];
                var key = (target.ExecutionScope, target.JobId);
                captured[index] = new(activationRevisions.GetValueOrDefault(key),
                    started && !closing && ActivationUnderGate(configuration, key).Enabled);
            }

            return new(configuration.Revision, captured);
        }
    }

    private void RecordActivationTransitionsUnderGate(JobConfigurationSnapshot candidate)
    {
        foreach ((string Scope, string Id) key in registered.Keys)
        {
            if (ActivationUnderGate(configuration, key) != ActivationUnderGate(candidate, key))
            {
                // Accepted revisions strictly increase, so this also preserves coalesced transitions without another counter.
                activationRevisions[key] = candidate.Revision;
            }
        }
    }

    private JobActivationFingerprint ActivationUnderGate(JobConfigurationSnapshot snapshot, (string Scope, string Job) key)
    {
        JobSettings? settings = snapshot.Jobs.GetValueOrDefault(key);
        if (settings is null || !registered.TryGetValue(key, out JobHandlerBinding? binding))
        {
            return new(false, settings?.Execution.Mode, settings?.Execution.ScheduledActivation);
        }

        string category = settings.CategoryId ?? binding.Definition.CategoryId;
        bool enabled = snapshot.Enabled && settings.Enabled && settings.Execution.Mode == JobExecutionMode.Continuous
            && (snapshot.Categories.GetValueOrDefault((key.Scope, category))?.Enabled ?? true);
        return new(enabled, settings.Execution.Mode, settings.Execution.ScheduledActivation);
    }

    private readonly record struct JobActivationFingerprint(bool Enabled, JobExecutionMode? Mode, JobScheduledActivation? Activation);
}

internal readonly record struct JobActivationState(long TransitionRevision, bool Enabled);
internal sealed record JobActivationSnapshot(long ConfigurationRevision, JobActivationState[] Targets);
