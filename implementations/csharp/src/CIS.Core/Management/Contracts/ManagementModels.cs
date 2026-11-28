using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIS.Management;

/// <summary>An original response value and whether this client recognizes its meaning.</summary>
/// <param name="Value">The exact wire value.</param>
/// <param name="IsKnown">Whether the value has a documented v1 interpretation.</param>
public readonly record struct ManagementKnownValue(string Value, bool IsKnown);

/// <summary>The exact complete replacement of administrative display metadata.</summary>
/// <param name="DisplayName">One through 256 Unicode scalars without controls.</param>
/// <param name="Description">Optional zero through 2,048 Unicode scalars; null in this DTO means omitted on the wire.</param>
public sealed record ManagementApplicationMetadata(string DisplayName, string? Description = null);

/// <summary>A stored admitted application and its independent administrative revision.</summary>
/// <param name="ApplicationId">The stable canonical UUID.</param>
/// <param name="Application">The exact trusted application name.</param>
/// <param name="Environment">The exact trusted environment name.</param>
/// <param name="Metadata">Current display metadata.</param>
/// <param name="MetadataRevision">A positive Int64 decimal string.</param>
/// <param name="MetadataETag">The opaque strong metadata-only ETag, including quotes.</param>
public sealed record ManagementApplication(string ApplicationId, string Application, string Environment,
    ManagementApplicationMetadata Metadata, string MetadataRevision, string MetadataETag);

/// <summary>A bounded live-view page; a cursor is sent only to the same typed endpoint.</summary>
/// <param name="Items">The complete admitted items in stable order.</param>
/// <param name="NextCursor">An optional opaque continuation, not a URL or authority.</param>
public sealed record ManagementPage<T>(IReadOnlyList<T> Items, string? NextCursor = null);

/// <summary>Inputs for one independently authorized live-view page.</summary>
/// <param name="PageSize">One through 500, default 100.</param>
/// <param name="Cursor">A prior same-endpoint opaque cursor.</param>
public sealed record ManagementPageRequest(int PageSize = 100, string? Cursor = null);

/// <summary>Literal, case-sensitive application identity/display-name search and exact environment selection.</summary>
/// <param name="Query">Optional one through 128 Unicode scalars; no normalization or wildcard interpretation.</param>
/// <param name="Environment">Optional exact trusted environment, one through 255 Unicode scalars.</param>
public sealed record ManagementApplicationQuery(string? Query = null, string? Environment = null);

/// <summary>One environment disclosed through at least one currently authorized application.</summary>
/// <param name="Environment">The exact trusted environment name.</param>
public sealed record ManagementEnvironment(string Environment);

/// <summary>One actually installed typed API feature.</summary>
/// <param name="OperationId">The stable typed operation alias.</param>
/// <param name="Version">Its positive portable contract version.</param>
/// <param name="Method">The installed HTTP method.</param>
/// <param name="Path">Its descriptive API path, never a client-selected dispatch URL.</param>
/// <param name="RequiredCapabilities">The installed capability requirement, not a permission grant.</param>
public sealed record ManagementFeature(string OperationId, int Version, string Method, string Path,
    IReadOnlyList<string> RequiredCapabilities);

/// <summary>Authenticated service discovery without a policy or credential dump.</summary>
/// <param name="ServiceVersion">The reported service version.</param>
/// <param name="ProtocolMajors">Actually supported positive protocol majors.</param>
/// <param name="Features">Actually installed typed APIs.</param>
public sealed record ManagementServiceInfo(string ServiceVersion, IReadOnlyList<int> ProtocolMajors,
    IReadOnlyList<ManagementFeature> Features)
{
    /// <summary>Gets the optional complete API discovery version. Only version one selects complete-v1.</summary>
    public int? ApiDiscoveryVersion
    {
        get; init;
    }

    /// <summary>Gets the server's effective complete-inventory request byte bound, when advertised by this v1 server.</summary>
    public int? MaximumInventoryRequestBytes
    {
        get; init;
    }
}

/// <summary>An advisory permission hint that never replaces authorization at execution.</summary>
/// <param name="OperationId">The installed typed operation alias.</param>
/// <param name="Allowed">Whether current authorization supplied this hint.</param>
public sealed record ManagementOperationHint(string OperationId, bool Allowed);

/// <summary>Actual installed features and current hints for one visible application resource.</summary>
/// <param name="ApplicationId">The resolved canonical application UUID.</param>
/// <param name="ImplementedApis">Installed APIs, distinct from reported instance features.</param>
/// <param name="CallerHints">Advisory current operation hints.</param>
public sealed record ManagementCapabilities(string ApplicationId, IReadOnlyList<ManagementFeature> ImplementedApis,
    IReadOnlyList<ManagementOperationHint> CallerHints)
{
    /// <summary>Gets the optional complete API discovery version; legacy arrays remain the legacy-v1 profile.</summary>
    public int? ApiDiscoveryVersion
    {
        get; init;
    }
}

/// <summary>Immutable reported registration facts; none replaces credential identity.</summary>
/// <param name="ProtocolMajor">The selected supported protocol major, initially one.</param>
/// <param name="ReportedInstanceId">Reported metadata of at most 1,024 Unicode scalars.</param>
/// <param name="ProcessStartedAtUtc">Original seven-fraction UTC timestamp text.</param>
/// <param name="ApplicationVersion">Bounded reported application version.</param>
public sealed record ManagementRegistrationRequest(int ProtocolMajor, string ReportedInstanceId,
    string ProcessStartedAtUtc, string ApplicationVersion);

/// <summary>The stable registration result returned unchanged for a matching replay.</summary>
/// <param name="RegistrationId">The caller's canonical process-incarnation UUID.</param>
/// <param name="ApplicationId">The server's stable admitted application UUID.</param>
/// <param name="RegisteredAtUtc">The original shared-database receipt time.</param>
public sealed record ManagementRegistrationReceipt(string RegistrationId, string ApplicationId, string RegisteredAtUtc);

/// <summary>Current authoritative control facts, distinct from presence, business permissions and RM-05 ownership.</summary>
/// <param name="AuthorityId">The persisted management namespace's canonical UUID.</param>
/// <param name="RegistrationId">The confirmed process registration UUID.</param>
/// <param name="ControlRevision">Exact opaque control-free text of one through 256 Unicode scalars.</param>
/// <param name="AdmissionAllowed">Whether current management-level prerequisites permit admission.</param>
/// <param name="ValidForMs">One through 15,000 milliseconds, measured from the request's local monotonic start.</param>
/// <param name="AttemptId">The exact canonical per-attempt UUID echoed from the request query.</param>
public sealed record ManagementControlConfirmation(string AuthorityId, string RegistrationId, string ControlRevision,
    bool AdmissionAllowed, int ValidForMs, string AttemptId);

/// <summary>One complete latest observation, not a history event or ownership assertion.</summary>
/// <param name="Sequence">The positive Int64 observation-stream sequence.</param>
/// <param name="ObservedAtUtc">Original source observation timestamp text.</param>
/// <param name="Lifecycle">Reported lifecycle value.</param>
/// <param name="Health">Reported application health, independent of presence.</param>
public sealed record ManagementObservation(string Sequence, string ObservedAtUtc, string Lifecycle, string Health)
{
    /// <summary>Gets the explicit known/unknown interpretation without replacing the original value.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownLifecycle => new(Lifecycle,
        Lifecycle is "starting" or "ready" or "stopping" or "stopped" or "unknown");

    /// <summary>Gets the explicit known/unknown interpretation without inferring ownership or connectivity.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownHealth => new(Health, Health is "healthy" or "degraded" or "unhealthy" or "unknown");
}

/// <summary>The actual retained stream receipt; a replay never refreshes ReceivedAtUtc.</summary>
/// <param name="RegistrationId">The bound registration UUID.</param>
/// <param name="Stream">Observation or inventory stream.</param>
/// <param name="SubmittedSequence">The request's original sequence.</param>
/// <param name="AcceptedSequence">The retained accepted sequence.</param>
/// <param name="Disposition">Accepted or replayed; future values remain explicit unknowns.</param>
/// <param name="ReceivedAtUtc">The retained accepted database receipt time.</param>
public sealed record ManagementReportReceipt(string RegistrationId, string Stream, string SubmittedSequence,
    string AcceptedSequence, string Disposition, string ReceivedAtUtc)
{
    /// <summary>Gets a recognized interpretation; obsolete reports currently fail with HTTP 409.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownDisposition => new(Disposition, Disposition is "accepted" or "replayed");
}

/// <summary>The terminal close receipt; repetition retains the first close time.</summary>
/// <param name="RegistrationId">The bound registration UUID.</param>
/// <param name="ClosedAtUtc">The original terminal close time.</param>
public sealed record ManagementCloseReceipt(string RegistrationId, string ClosedAtUtc);

/// <summary>Native evidence separate from the registration body's reported label.</summary>
/// <param name="Kind">notProvided, reported or verified, or an explicitly unknown future value.</param>
/// <param name="ReportedIdentifier">Optional native reported evidence.</param>
/// <param name="VerifiedIdentifier">Optional independently validated native evidence.</param>
public sealed record ManagementInstanceEvidence(string Kind, string? ReportedIdentifier = null, string? VerifiedIdentifier = null)
{
    /// <summary>Gets the recognized evidence kind without promoting reported registration metadata.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownKind => new(Kind, Kind is "notProvided" or "reported" or "verified");
}

/// <summary>A stored registration, current derived presence and separate reported observations.</summary>
/// <param name="RegistrationId">The process-incarnation UUID.</param>
/// <param name="ApplicationId">The stable admitted application UUID.</param>
/// <param name="ReportedInstanceId">The registration body's unverified label.</param>
/// <param name="InstanceEvidence">Actual selected native evidence.</param>
/// <param name="ProcessStartedAtUtc">Original reported timestamp text.</param>
/// <param name="ApplicationVersion">Reported application version.</param>
/// <param name="ProtocolMajor">Registered protocol major.</param>
/// <param name="RegisteredAtUtc">Shared-database initial receipt.</param>
/// <param name="Presence">Derived active, stale or closed state.</param>
/// <param name="LastSeenAtUtc">Latest accepted receipt, if supplied.</param>
/// <param name="ClosedAtUtc">Original terminal close, if supplied.</param>
/// <param name="Observation">Latest complete reported observation, if available.</param>
/// <param name="ObservationReceivedAtUtc">Its unchanged accepted database receipt.</param>
/// <param name="InventorySequence">Latest accepted independent inventory sequence.</param>
/// <param name="InventoryReceivedAtUtc">Its unchanged accepted database receipt.</param>
public sealed record ManagementInstance(string RegistrationId, string ApplicationId, string ReportedInstanceId,
    ManagementInstanceEvidence InstanceEvidence, string ProcessStartedAtUtc, string ApplicationVersion, int ProtocolMajor,
    string RegisteredAtUtc, string Presence, string? LastSeenAtUtc = null, string? ClosedAtUtc = null,
    ManagementObservation? Observation = null, string? ObservationReceivedAtUtc = null,
    string? InventorySequence = null, string? InventoryReceivedAtUtc = null)
{
    /// <summary>Gets the known presence interpretation without equating staleness to process death.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownPresence => new(Presence, Presence is "active" or "stale" or "closed");
}

/// <summary>One bounded reported feature; unknown kinds remain data rather than executable code.</summary>
/// <param name="Kind">Stable reported module kind.</param>
/// <param name="Version">Reported version text.</param>
/// <param name="Details">Optional bounded opaque JSON object.</param>
public sealed record ManagementModuleInventory(string Kind, string Version, JsonElement? Details = null);

/// <summary>One exact native category tuple, counted once in the global category bound.</summary>
/// <param name="ExecutionScope">At most 256 UTF-8 bytes.</param>
/// <param name="CategoryId">At most 128 UTF-8 bytes within its scope.</param>
/// <param name="DisplayName">At most 256 UTF-8 bytes.</param>
/// <param name="Description">Optional zero through 2,048 UTF-8 bytes.</param>
public sealed record ManagementCategoryInventory(string ExecutionScope, string CategoryId, string DisplayName,
    string? Description = null);

/// <summary>Actually available effective local configuration, not central desired state.</summary>
/// <param name="Revision">A nonnegative Int64 decimal string.</param>
/// <param name="Enabled">Actually adopted local enablement.</param>
public sealed record ManagementJobConfiguration(string Revision, bool Enabled);

/// <summary>Diagnostic owner observation without a lease token or authority.</summary>
/// <param name="OwnerApplication">The actual observed owner application, at most 1,024 UTF-8 bytes.</param>
/// <param name="InstanceId">The actual observed owner process, at most 128 UTF-8 bytes.</param>
/// <param name="Generation">A positive Int64 decimal string.</param>
/// <param name="ExpiresAtUtc">Actually observed expiry timestamp text.</param>
/// <param name="ObservedAtUtc">The authority's actual observation timestamp text.</param>
public sealed record ManagementOwnerObservation(string OwnerApplication, string InstanceId, string Generation,
    string ExpiresAtUtc, string ObservedAtUtc);

/// <summary>One available resource-specific local runtime observation.</summary>
/// <param name="ResourceId">The exact existing bounded resource identifier.</param>
/// <param name="Mode">The actual observed execution mode.</param>
/// <param name="State">The raw local runtime state.</param>
/// <param name="LastOutcome">Optional safe native outcome, at most 128 UTF-8 bytes.</param>
/// <param name="ObservedOwner">Optional real diagnostic ownership observation.</param>
/// <param name="LastOccurrenceUtc">Only an existing authoritative source's last occurrence.</param>
/// <param name="NextOccurrenceUtc">Only an existing authoritative source's next occurrence.</param>
public sealed record ManagementJobObservation(string ResourceId, string Mode, string State, string? LastOutcome = null,
    ManagementOwnerObservation? ObservedOwner = null, string? LastOccurrenceUtc = null, string? NextOccurrenceUtc = null)
{
    /// <summary>Gets a known local state; unknown values never become a successful default.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownState => new(State, State is "disabled" or "standby" or "acquiring"
        or "running" or "idle" or "waiting" or "draining" or "lost" or "released" or "faulted");
}

/// <summary>One passive job definition and its available per-resource observations.</summary>
/// <param name="ExecutionScope">Exact native scope, at most 256 UTF-8 bytes.</param>
/// <param name="JobId">Exact native ID, at most 128 UTF-8 bytes.</param>
/// <param name="CategoryId">Exact native category, at most 128 UTF-8 bytes.</param>
/// <param name="DisplayName">Native display text, at most 256 UTF-8 bytes.</param>
/// <param name="SupportedModes">Explicit reported supported modes.</param>
/// <param name="Description">Optional native description, at most 2,048 UTF-8 bytes.</param>
/// <param name="EffectiveConfiguration">Only actually supplied effective configuration.</param>
/// <param name="Observations">Available per-resource observations, globally bounded across the snapshot.</param>
/// <param name="ResourceScopeDescription">Optional existing safe description of resource grouping, at most 1,024 UTF-8 bytes.</param>
public sealed record ManagementJobInventory(string ExecutionScope, string JobId, string CategoryId, string DisplayName,
    IReadOnlyList<string> SupportedModes, string? Description = null, ManagementJobConfiguration? EffectiveConfiguration = null,
    IReadOnlyList<ManagementJobObservation>? Observations = null, string? ResourceScopeDescription = null);

/// <summary>A complete independently sequenced replacement, never a partial catalog patch.</summary>
/// <param name="Sequence">Positive Int64 inventory-stream sequence.</param>
/// <param name="ObservedAtUtc">Original source observation timestamp text.</param>
/// <param name="Modules">Complete bounded reported module list.</param>
/// <param name="Categories">Complete bounded unique native scope/category tuples.</param>
/// <param name="Jobs">Complete bounded unique native scope/job definitions.</param>
public sealed record ManagementInventorySnapshot(string Sequence, string ObservedAtUtc,
    IReadOnlyList<ManagementModuleInventory> Modules, IReadOnlyList<ManagementCategoryInventory> Categories,
    IReadOnlyList<ManagementJobInventory> Jobs);

/// <summary>One complete retained inventory with registration/version/freshness provenance.</summary>
/// <param name="RegistrationId">The canonical registration UUID.</param>
/// <param name="ApplicationVersion">The instance's reported application version.</param>
/// <param name="ReceivedAtUtc">Actual retained accepted database receipt.</param>
/// <param name="Inventory">The original complete logical replacement.</param>
public sealed record ManagementInventoryEntry(string RegistrationId, string ApplicationVersion, string ReceivedAtUtc,
    ManagementInventorySnapshot Inventory);
