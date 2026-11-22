namespace CIS.Jobs;

/// <summary>The explicitly selected scheduling integration, independent of descriptor registration.</summary>
public enum JobScheduledActivation
{
    /// <summary>Consume trusted already-admitted occurrences without evaluating recurrence.</summary>
    ExternalOccurrences,
    /// <summary>Require a separately supplied compatible recurring evaluator and admitted-run source.</summary>
    AutomaticRecurrence
}

/// <summary>The initial distributed overlap contract.</summary>
public enum JobOverlapPolicy
{
    /// <summary>One current grant per stable job/resource; occurrences do not create separate lease keys.</summary>
    ExclusiveJobResource
}

/// <summary>An immutable per-invocation execution snapshot, not permission or scheduling admission.</summary>
public sealed class JobExecutionSettings
{
    /// <summary>Copies validated execution behavior. Unexpected business exceptions fault the duty; they are never automatically replayed.</summary>
    public JobExecutionSettings(JobExecutionMode mode, TimeSpan? idleDelay = null, TimeSpan? timeout = null,
        TimeSpan? maximumDrain = null, TimeSpan? acquisitionRetryDelay = null,
        JobScheduleDescriptor? schedule = null, JobScheduledActivation scheduledActivation = JobScheduledActivation.ExternalOccurrences,
        JobOverlapPolicy overlapPolicy = JobOverlapPolicy.ExclusiveJobResource)
    {
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(scheduledActivation) || !Enum.IsDefined(overlapPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Select supported mode, activation and exclusive overlap values.");
        }

        if (mode == JobExecutionMode.Continuous && (schedule is not null || scheduledActivation != JobScheduledActivation.ExternalOccurrences))
        {
            throw new ArgumentException("Continuous mode has no schedule or recurring activation.");
        }

        Mode = mode;
        IdleDelay = JobText.Duration(idleDelay ?? TimeSpan.FromSeconds(5), nameof(idleDelay));
        Timeout = timeout is { } selectedTimeout ? JobText.Duration(selectedTimeout, nameof(timeout)) : null;
        MaximumDrain = JobText.Duration(maximumDrain ?? TimeSpan.FromSeconds(30), nameof(maximumDrain));
        if (MaximumDrain > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDrain), "A job drain must fit the managed ownership maximum of one day.");
        }
        AcquisitionRetryDelay = JobText.Duration(acquisitionRetryDelay ?? TimeSpan.FromSeconds(5), nameof(acquisitionRetryDelay));
        Schedule = schedule;
        ScheduledActivation = scheduledActivation;
        OverlapPolicy = overlapPolicy;
    }

    /// <summary>Gets the selected mode, fixed for one activated lifetime.</summary>
    public JobExecutionMode Mode
    {
        get;
    }
    /// <summary>Gets the positive continuous idle delay; initially five seconds.</summary>
    public TimeSpan IdleDelay
    {
        get;
    }
    /// <summary>Gets optional cooperative callback timeout; expiry never proves callback settlement.</summary>
    public TimeSpan? Timeout
    {
        get;
    }
    /// <summary>Gets the finite graceful authority/drain budget; initially thirty seconds.</summary>
    public TimeSpan MaximumDrain
    {
        get;
    }
    /// <summary>Gets standby or known-not-committed acquisition retry delay, never a business callback replay policy.</summary>
    public TimeSpan AcquisitionRetryDelay
    {
        get;
    }
    /// <summary>Gets optional schedule metadata. Enabled scheduled activation requires a complete descriptor.</summary>
    public JobScheduleDescriptor? Schedule
    {
        get;
    }
    /// <summary>Gets the explicit external-occurrence or automatic-recurrence selection.</summary>
    public JobScheduledActivation ScheduledActivation
    {
        get;
    }
    /// <summary>Gets the exclusive stable job/resource policy.</summary>
    public JobOverlapPolicy OverlapPolicy
    {
        get;
    }
}

/// <summary>Finite per-instance bounds; none is a fleet-wide quota.</summary>
public sealed class JobRuntimeLimits
{
    /// <summary>Captures positive finite catalog, execution, pending-dispatch and snapshot limits.</summary>
    public JobRuntimeLimits(int maximumDefinitions = 1024, int maximumCategories = 64,
        int maximumContinuousDuties = 32, int maximumInvocations = 8, int maximumPendingDispatches = 128,
        int maximumSnapshotPageSize = 100)
    {
        MaximumDefinitions = Limit(maximumDefinitions, nameof(maximumDefinitions));
        MaximumCategories = Limit(maximumCategories, nameof(maximumCategories));
        MaximumContinuousDuties = Limit(maximumContinuousDuties, nameof(maximumContinuousDuties));
        MaximumInvocations = Limit(maximumInvocations, nameof(maximumInvocations));
        MaximumPendingDispatches = Limit(maximumPendingDispatches, nameof(maximumPendingDispatches));
        MaximumSnapshotPageSize = Limit(maximumSnapshotPageSize, nameof(maximumSnapshotPageSize));
    }

    /// <summary>Gets the catalog definition bound, initially 1,024.</summary>
    public int MaximumDefinitions
    {
        get;
    }
    /// <summary>Gets the category definition bound, initially 64.</summary>
    public int MaximumCategories
    {
        get;
    }
    /// <summary>Gets the owned continuous-duty bound, initially 32.</summary>
    public int MaximumContinuousDuties
    {
        get;
    }
    /// <summary>Gets actual unfinished callback capacity, initially eight.</summary>
    public int MaximumInvocations
    {
        get;
    }
    /// <summary>Gets locally retained scheduled admissions, initially 128.</summary>
    public int MaximumPendingDispatches
    {
        get;
    }
    /// <summary>Gets the maximum detached diagnostic page size, initially 100.</summary>
    public int MaximumSnapshotPageSize
    {
        get;
    }

    private static int Limit(int value, string parameterName)
    {
        if (value is < 1 or > 65536)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Select a finite limit from one through 65,536.");
        }

        return value;
    }
}
