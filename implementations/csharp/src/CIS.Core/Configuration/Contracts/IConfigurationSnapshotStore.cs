namespace CIS.Configuration;

/// <summary>Optional coherent paired reads alongside the existing independent-record store contract.</summary>
/// <remarks>Central entries require this capability. Legacy stores acquire no compulsory new member.
/// Source integrity and authority are established by the selected adapter; this port does not grant work admission.</remarks>
public interface IConfigurationSnapshotStore
{
    /// <summary>Reads one pinned pair and its provenance, or confirms the exact retained same-session pair.</summary>
    ValueTask<ConfigurationSnapshotReadResult> ReadSnapshotAsync(ConfigurationSnapshotRequest request,
        CancellationToken cancellationToken);
}

/// <summary>One exact read cycle, carrying independently established central context when enrollment is selected.</summary>
public sealed class ConfigurationSnapshotRequest
{
    // Shared immutable retained data, not a second adapter cache or callback.
    internal ConfigurationSnapshotReadResult? RetainedPair
    {
        get; init;
    }
    /// <summary>Captures the paired keys, optional retained pair token and explicit central selection.</summary>
    /// <remarks>A token is usable only while the caller retains the complete matching pair.
    /// Enrollment and expected target are either both supplied or both absent.</remarks>
    public ConfigurationSnapshotRequest(ConfigurationRecordKey commonKey, ConfigurationRecordKey applicationKey,
        string? snapshotToken = null, ConfigurationEnrollment? enrollment = null,
        ConfigurationSnapshotTarget? expectedTarget = null)
    {
        ArgumentNullException.ThrowIfNull(commonKey);
        ArgumentNullException.ThrowIfNull(applicationKey);
        if (commonKey.Scope != "CommonConfiguration" || applicationKey.Scope == "CommonConfiguration"
            || commonKey.Definition != applicationKey.Definition)
        {
            throw new ArgumentException("Select the common and fixed-host keys for one exact persisted definition.");
        }
        if ((enrollment is null) != (expectedTarget is null))
        {
            throw new ArgumentException("Central enrollment requires an independently established target/session.");
        }
        if (enrollment is not null && (commonKey.Definition != enrollment.PersistedDefinition
            || applicationKey.Scope != expectedTarget!.HostApplicationName))
        {
            throw new ArgumentException("Enrollment and trusted host scope must match the exact source keys.");
        }
        if (snapshotToken is not null)
        {
            ConfigurationSnapshotRules.Text(snapshotToken, 4096, nameof(snapshotToken));
        }
        CommonKey = commonKey;
        ApplicationKey = applicationKey;
        SnapshotToken = snapshotToken;
        Enrollment = enrollment;
        ExpectedTarget = expectedTarget;
    }

    /// <summary>Gets the exact common source address.</summary>
    public ConfigurationRecordKey CommonKey
    {
        get;
    }
    /// <summary>Gets the exact fixed-host source address.</summary>
    public ConfigurationRecordKey ApplicationKey
    {
        get;
    }
    /// <summary>Gets the opaque pair equality token, never an ordered generation or authority renewal.</summary>
    public string? SnapshotToken
    {
        get;
    }
    /// <summary>Gets explicit central enrollment, otherwise this is an optional noncentral paired read.</summary>
    public ConfigurationEnrollment? Enrollment
    {
        get;
    }
    /// <summary>Gets the independently captured target/session, never a value inferred from a response.</summary>
    public ConfigurationSnapshotTarget? ExpectedTarget
    {
        get;
    }

    /// <summary>Checks exact target and schema facts only; the owner must separately fence current session and generation at commit.</summary>
    public bool MatchesTarget(ConfigurationDesiredIdentity desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        return Enrollment is not null && ExpectedTarget == desired.Target
            && Enrollment.ConfigurationId == desired.ConfigurationId && Enrollment.SchemaVersion == desired.SchemaVersion;
    }
}

/// <summary>A whole-pair result; no member can independently claim conditional equality.</summary>
public enum ConfigurationSnapshotReadKind
{
    /// <summary>A complete pinned pair, including explicit source absence where selected.</summary>
    Complete,
    /// <summary>The exact already-retained target/session pair remains current.</summary>
    Unchanged,
    /// <summary>No usable complete pair was retrieved; it is never source absence.</summary>
    Failure
}

/// <summary>An immutable paired observation without callbacks, live roots or inferred acceptance.</summary>
public sealed class ConfigurationSnapshotReadResult
{
    private ConfigurationSnapshotReadResult(ConfigurationSnapshotReadKind kind,
        ConfigurationReadResult? common, ConfigurationReadResult? application, string? snapshotToken,
        ConfigurationSnapshotProvenance? provenance, ConfigurationDesiredIdentity? desired, string? failureCode)
    {
        Kind = kind;
        Common = common;
        Application = application;
        SnapshotToken = snapshotToken;
        Provenance = provenance;
        Desired = desired;
        FailureCode = failureCode;
    }

    /// <summary>Gets the whole-pair retrieval outcome.</summary>
    public ConfigurationSnapshotReadKind Kind
    {
        get;
    }
    /// <summary>Gets a complete common observation; null on Failure/Unchanged is not source absence.</summary>
    public ConfigurationReadResult? Common
    {
        get;
    }
    /// <summary>Gets a complete application observation; null on Failure/Unchanged is not source absence.</summary>
    public ConfigurationReadResult? Application
    {
        get;
    }
    /// <summary>Gets the complete pair's bounded opaque equality stamp.</summary>
    public string? SnapshotToken
    {
        get;
    }
    /// <summary>Gets exact complete central provenance; noncentral paired stores may omit it.</summary>
    public ConfigurationSnapshotProvenance? Provenance
    {
        get;
    }
    /// <summary>Gets independently validated desired identity, if known even on content failure.</summary>
    public ConfigurationDesiredIdentity? Desired
    {
        get;
    }
    /// <summary>Gets a bounded safe failure code, never provider exception text or a payload.</summary>
    public string? FailureCode
    {
        get;
    }

    /// <summary>Captures one complete pair; individual members must be Found or confirmed Missing.</summary>
    /// <remarks>ConfigurationReadResult already owns detached JSON. A central caller must require provenance
    /// and match its target; this factory cannot authenticate a custom provider.</remarks>
    public static ConfigurationSnapshotReadResult Complete(ConfigurationReadResult common,
        ConfigurationReadResult application, string snapshotToken, ConfigurationSnapshotProvenance? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(common);
        ArgumentNullException.ThrowIfNull(application);
        ConfigurationSnapshotRules.Text(snapshotToken, 4096, nameof(snapshotToken));
        if (common.Kind is not (ConfigurationReadKind.Found or ConfigurationReadKind.Missing)
            || application.Kind is not (ConfigurationReadKind.Found or ConfigurationReadKind.Missing))
        {
            throw new ArgumentException("A coherent complete pair cannot contain failure or member-level unchanged results.");
        }
        if (provenance is not null && ((common.Kind == ConfigurationReadKind.Found) != (provenance.CommonSource is not null)
            || (application.Kind == ConfigurationReadKind.Found) != (provenance.ApplicationSource is not null)))
        {
            throw new ArgumentException("Complete source references must agree with present/explicitly absent source observations.");
        }
        return new(ConfigurationSnapshotReadKind.Complete, common, application, snapshotToken, provenance, provenance?.Desired, null);
    }

    /// <summary>Confirms a complete retained same-target/session pair; unusable without that retained state.</summary>
    public static ConfigurationSnapshotReadResult Unchanged()
        => new(ConfigurationSnapshotReadKind.Unchanged, null, null, null, null, null, null);

    /// <summary>Reports failure while optionally retaining a trustworthy desired identity for a visible rejection.</summary>
    /// <remarks>Omit desired identity for an unidentified poll failure. Neither case means source absence.</remarks>
    public static ConfigurationSnapshotReadResult Failure(string failureCode = "Configuration.SnapshotRead",
        ConfigurationDesiredIdentity? desired = null)
    {
        ConfigurationSnapshotRules.Code(failureCode, nameof(failureCode));
        return new(ConfigurationSnapshotReadKind.Failure, null, null, null, null, desired, failureCode);
    }
}
