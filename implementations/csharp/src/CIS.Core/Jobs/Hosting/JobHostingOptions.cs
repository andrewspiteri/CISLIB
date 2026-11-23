namespace CIS.Jobs.Hosting;

/// <summary>Explicit bounded local targets, not discovery activation or a recurring scheduler.</summary>
public sealed class JobHostingOptions
{
    /// <summary>Gets or sets optional required-jobs health criticality; initially true.</summary>
    public bool IsHealthCritical { get; set; } = true;
    /// <summary>Gets or sets explicit continuous job/resources. No targets are selected by default.</summary>
    public IReadOnlyList<JobScopeRequest> ContinuousTargets { get; set; } = Array.Empty<JobScopeRequest>();

    internal JobHostingOptions Copy(IJobCatalog catalog, JobRuntimeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(ContinuousTargets);
        if (ContinuousTargets.Count > limits.MaximumContinuousDuties)
        {
            throw new ArgumentException("Explicit continuous targets must fit the local continuous-duty bound.");
        }

        var copied = new List<JobScopeRequest>();
        foreach (JobScopeRequest target in ContinuousTargets)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.Mode != JobExecutionMode.Continuous
                || catalog.Find(target.ExecutionScope, target.JobId)?.Definition.SupportedModes.Contains(JobExecutionMode.Continuous) != true
                || copied.Any(item => item.ExecutionScope == target.ExecutionScope && item.JobId == target.JobId && item.ResourceId == target.ResourceId))
            {
                throw new ArgumentException("Select distinct explicit registered continuous job/resources.");
            }

            copied.Add(target);
        }

        return new()
        {
            IsHealthCritical = IsHealthCritical,
            ContinuousTargets = copied.AsReadOnly()
        };
    }
}
