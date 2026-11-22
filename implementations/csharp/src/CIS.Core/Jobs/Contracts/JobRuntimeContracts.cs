using CIS.Coordination;

namespace CIS.Jobs;

/// <summary>Local duty states; Idle, Waiting and Standby are normal operational states.</summary>
public enum JobLocalState
{
    /// <summary>Local effective enablement prevents new work.</summary>
    Disabled,
    /// <summary>The instance is eligible and awaiting ownership.</summary>
    Standby,
    /// <summary>An ownership acquisition is being resolved.</summary>
    Acquiring,
    /// <summary>A finite callback is actually unfinished.</summary>
    Running,
    /// <summary>A continuous owner is awaiting its configured idle delay without business capacity.</summary>
    Idle,
    /// <summary>A scheduled definition awaits a trusted admitted occurrence.</summary>
    Waiting,
    /// <summary>New admissions have stopped while actual callback work drains.</summary>
    Draining,
    /// <summary>Authority ended; cancellation does not prove the callback physically stopped.</summary>
    Lost,
    /// <summary>The exact grant was locally released after work drained.</summary>
    Released,
    /// <summary>The duty faulted locally; unrelated jobs remain independent.</summary>
    Faulted
}

/// <summary>A bounded detached observation, never authority for a later protected operation.</summary>
public sealed record JobRuntimeSnapshot
{
    /// <summary>Creates name-only local state with an optional explicitly observational owner grant.</summary>
    public JobRuntimeSnapshot(JobScopeRequest target, string categoryId, bool enabled, JobLocalState state,
        long configurationRevision, string? lastOutcome = null, CoordinationOwnerObservation? observedOwner = null,
        DateTimeOffset? lastOccurrenceUtc = null, DateTimeOffset? nextOccurrenceUtc = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        CategoryId = JobText.Text(categoryId, 128, nameof(categoryId));
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(configurationRevision);
        Target = target;
        Enabled = enabled;
        State = state;
        ConfigurationRevision = configurationRevision;
        LastOutcome = lastOutcome is null ? null : JobText.Text(lastOutcome, 128, nameof(lastOutcome));
        ObservedOwner = observedOwner;
        LastOccurrenceUtc = lastOccurrenceUtc?.ToUniversalTime();
        NextOccurrenceUtc = nextOccurrenceUtc?.ToUniversalTime();
    }

    /// <summary>Gets the registered target and mode.</summary>
    public JobScopeRequest Target
    {
        get;
    }
    /// <summary>Gets management grouping, excluded from ownership identity.</summary>
    public string CategoryId
    {
        get;
    }
    /// <summary>Gets last accepted effective local enablement, not instantaneous cluster-wide policy.</summary>
    public bool Enabled
    {
        get;
    }
    /// <summary>Gets the detached local runtime state.</summary>
    public JobLocalState State
    {
        get;
    }
    /// <summary>Gets the locally accepted configuration revision.</summary>
    public long ConfigurationRevision
    {
        get;
    }
    /// <summary>Gets the latest bounded safe outcome code.</summary>
    public string? LastOutcome
    {
        get;
    }
    /// <summary>Gets diagnostic owner/expiry data, never permission for subsequent effects.</summary>
    public CoordinationOwnerObservation? ObservedOwner
    {
        get;
    }
    /// <summary>Gets the scheduler-supplied last occurrence, or null when unavailable.</summary>
    public DateTimeOffset? LastOccurrenceUtc
    {
        get;
    }
    /// <summary>Gets a real scheduler-supplied next occurrence, or null; RM-05 does not calculate it.</summary>
    public DateTimeOffset? NextOccurrenceUtc
    {
        get;
    }
}

/// <summary>Local start admission facts, independent of acquiring distributed ownership.</summary>
public enum ContinuousJobStartOutcome
{
    /// <summary>The bounded runner accepted this duty lifetime; a healthy standby need not own a grant.</summary>
    Started,
    /// <summary>This exact duty already has a local lifetime or an unfinished previous callback.</summary>
    AlreadyRunning,
    /// <summary>Local disablement or capacity prevented admission.</summary>
    Deferred,
    /// <summary>The exact target or mode has no compatible registration.</summary>
    Unsupported,
    /// <summary>The established caller lacks permission.</summary>
    Unauthorized
}

/// <summary>Owns finite continuous iterations and their actual bounded local admission.</summary>
public interface IContinuousJobRunner
{
    /// <summary>Starts a managed duty without awaiting its entire operational lifetime.</summary>
    Task<ContinuousJobStartOutcome> StartAsync(string executionScope, string jobId, string resourceId,
        CancellationToken cancellationToken = default);
    /// <summary>Closes this duty and joins its actual callback; cancellation only ends this caller's wait.</summary>
    Task StopAsync(string executionScope, string jobId, string resourceId, CancellationToken cancellationToken = default);
    /// <summary>Copies a bounded page without provider I/O, business work or fabricated scheduler values.</summary>
    IReadOnlyList<JobRuntimeSnapshot> CaptureSnapshot(int offset = 0, int? pageSize = null);
}
