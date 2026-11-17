namespace CIS.Configuration;

/// <summary>The current owned observation state, independent of stored values.</summary>
public enum ConfigurationWatchState
{
    /// <summary>No subscription is needed for this catalog/provider.</summary>
    NotRequired,
    /// <summary>Initial values are prepared; observation has not started.</summary>
    Inactive,
    /// <summary>Subscription acquisition is in progress.</summary>
    Activating,
    /// <summary>The acquired subscription is active.</summary>
    Active,
    /// <summary>Observation failed; inspect the safe failure code.</summary>
    Failed,
    /// <summary>Owned observation and refresh work have settled.</summary>
    Stopped
}

// Detached current observations only: no source tokens, payloads, checksums,
// exception objects, live POCOs or unbounded history.
/// <summary>Detached current facts for one retained configuration type, without values, tokens or checksums.</summary>
/// <param name="ConfigurationType">The actual CLR configuration type.</param>
/// <param name="IsValid">Whether the latest reconciliation succeeded.</param>
/// <param name="Disabled">Whether a broken live setter permanently disabled updates.</param>
/// <param name="Revision">The host-local accepted-change revision; initial population is zero.</param>
/// <param name="LastSuccessfulReconciliationUtc">The last successful reconciliation time, including unchanged values.</param>
/// <param name="LastChangeUtc">The last accepted runtime change time.</param>
/// <param name="FailureCode">A fixed safe reconciliation failure code.</param>
/// <param name="NotificationFailureCode">A separate safe subscriber-failure code, cleared by the next accepted publication.</param>
public sealed record ConfigurationEntryRefreshStatus(Type ConfigurationType, bool IsValid, bool Disabled,
    long Revision, DateTimeOffset? LastSuccessfulReconciliationUtc, DateTimeOffset? LastChangeUtc, string? FailureCode,
    string? NotificationFailureCode);

/// <summary>A detached bounded current observation of this host's Configuration lifetime.</summary>
/// <param name="ActivationCompleted">Whether the required activation operation completed.</param>
/// <param name="IsStopping">Whether trigger and commit admission is closed.</param>
/// <param name="WatchState">The current owned subscription state.</param>
/// <param name="WatchFailureCode">A fixed safe observation failure code.</param>
/// <param name="Entries">One detached observation per discovered type.</param>
public sealed record ConfigurationRefreshStatus(bool ActivationCompleted, bool IsStopping,
    ConfigurationWatchState WatchState, string? WatchFailureCode, IReadOnlyList<ConfigurationEntryRefreshStatus> Entries);
