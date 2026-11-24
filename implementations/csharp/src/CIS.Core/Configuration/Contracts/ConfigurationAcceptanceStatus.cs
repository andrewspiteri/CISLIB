namespace CIS.Configuration;

/// <summary>The local Configuration owner's actual candidate/acceptance disposition.</summary>
public enum ConfigurationAcceptanceOutcome
{
    /// <summary>No current matching acceptance is known.</summary>
    Pending,
    /// <summary>The desired release was actually accepted as the initial baseline or an effective runtime change.</summary>
    Applied,
    /// <summary>Desired provenance was accepted without changing effective values or local revision.</summary>
    AppliedUnchanged,
    /// <summary>An identified desired candidate failed before acceptance; prior intact accepted values remain.</summary>
    Rejected,
    /// <summary>A valid desired change is staged while the current process retains its active values.</summary>
    PendingRestart,
    /// <summary>The entry is disabled or its live-state guarantee failed; last acceptance is historical.</summary>
    Faulted
}

/// <summary>An immutable copy from the actual acceptance boundary, not an HTTP receipt or reusable work permit.</summary>
/// <remarks>No provider observer runs under the value gate. Captured last acceptance can be historical when
/// HasIntactAcceptedState is false; connectivity and subscriber diagnostics are separate facts.</remarks>
public sealed record ConfigurationAcceptanceStatus
{
    /// <summary>Captures the actual disposition and accepted revision without values or an effective checksum.</summary>
    public ConfigurationAcceptanceStatus(ConfigurationAcceptanceOutcome outcome, ConfigurationDesiredIdentity? desired,
        ConfigurationSnapshotProvenance? lastAccepted, long revision, bool hasIntactAcceptedState,
        string? failureCode = null, string? notificationFailureCode = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        if (hasIntactAcceptedState && lastAccepted is null
            || outcome == ConfigurationAcceptanceOutcome.Faulted && hasIntactAcceptedState
            || (outcome is ConfigurationAcceptanceOutcome.Applied or ConfigurationAcceptanceOutcome.AppliedUnchanged)
                && (!hasIntactAcceptedState || desired != lastAccepted?.Desired)
            || (outcome is ConfigurationAcceptanceOutcome.Rejected or ConfigurationAcceptanceOutcome.PendingRestart) && desired is null)
        {
            throw new ArgumentException("Disposition, desired identity and intact accepted provenance must agree.");
        }
        if (failureCode is not null)
        {
            ConfigurationSnapshotRules.Code(failureCode, nameof(failureCode));
        }
        if (notificationFailureCode is not null)
        {
            ConfigurationSnapshotRules.Code(notificationFailureCode, nameof(notificationFailureCode));
        }
        Outcome = outcome;
        Desired = desired;
        LastAccepted = lastAccepted;
        Revision = revision;
        HasIntactAcceptedState = hasIntactAcceptedState;
        FailureCode = failureCode;
        NotificationFailureCode = notificationFailureCode;
    }

    /// <summary>Gets the actual candidate/acceptance disposition.</summary>
    public ConfigurationAcceptanceOutcome Outcome
    {
        get;
    }
    /// <summary>Gets the last authenticated identified desired release, if one is known.</summary>
    public ConfigurationDesiredIdentity? Desired
    {
        get;
    }
    /// <summary>Gets last accepted provenance, which is historical when intact state is false.</summary>
    public ConfigurationSnapshotProvenance? LastAccepted
    {
        get;
    }
    /// <summary>Gets the Configuration owner's host-local revision; initialization is zero.</summary>
    public long Revision
    {
        get;
    }
    /// <summary>Gets whether the last accepted provenance still describes intact live values.</summary>
    public bool HasIntactAcceptedState
    {
        get;
    }
    /// <summary>Gets an optional bounded candidate or terminal-fault code.</summary>
    public string? FailureCode
    {
        get;
    }
    /// <summary>Gets an optional separate subscriber diagnostic that cannot undo acceptance.</summary>
    public string? NotificationFailureCode
    {
        get;
    }
}
