using System.Text.Json.Serialization;

namespace CIS.Management;

/// <summary>A finite structural declaration; this is data, never a validator expression or CLR type.</summary>
/// <param name="Kind">object, array, dictionary, string, number, integer or boolean.</param>
/// <param name="Nullable">Whether an explicit JSON null is a valid value.</param>
/// <param name="Members">Object members, in declared order.</param>
/// <param name="Element">Array or dictionary element declaration.</param>
/// <param name="AllowedValues">Optional finite exact string values.</param>
public sealed record ManagementConfigurationShape(string Kind, bool Nullable,
    IReadOnlyList<ManagementConfigurationMember>? Members = null, ManagementConfigurationShape? Element = null,
    IReadOnlyList<string>? AllowedValues = null);

/// <summary>One explicitly declared structural member.</summary>
/// <param name="Name">Exact case-sensitive JSON member name.</param>
/// <param name="Required">Whether this member must be present in a complete value.</param>
/// <param name="Shape">Its finite structural declaration.</param>
public sealed record ManagementConfigurationMember(string Name, bool Required, ManagementConfigurationShape Shape);

/// <summary>One authorized source-discovery row carrying immutable portable definition data and its actual scope.</summary>
/// <param name="ConfigurationId">Exact URI-unreserved portable identity.</param>
/// <param name="SchemaVersion">Canonical positive Int64 string.</param>
/// <param name="SourceId">Stable discoverable source UUID; definition/schema alone is not a unique discovery row.</param>
/// <param name="Environment">Exact environment of this actual source resource.</param>
/// <param name="Kind">common or application.</param>
/// <param name="Shape">Immutable structural declaration.</param>
/// <param name="DefaultsJson">Optional exact deterministic defaults JSON, omitted when source-content authority is unavailable; omission is redaction.</param>
/// <param name="Description">Optional bounded description.</param>
/// <param name="ApplicationId">Required only for an application source row.</param>
/// <param name="BindingId">Stable binding UUID, required only for an application source row.</param>
/// <param name="HostApplicationName">Binding's fixed Configuration host scope, required only for an application source row.</param>
public sealed record ManagementConfigurationDefinition(string ConfigurationId, string SchemaVersion, string SourceId,
    string Environment, string Kind, ManagementConfigurationShape Shape, string? DefaultsJson = null, string? Description = null,
    string? ApplicationId = null, string? BindingId = null, string? HostApplicationName = null);

/// <summary>Payload-free catalog initialization and storage status.</summary>
/// <param name="CatalogId">Stable authoritative catalog UUID.</param>
/// <param name="ETag">Strong current catalog precondition.</param>
/// <param name="LoadedManifestDigest">Exact digest of the operator-controlled locally loaded manifest.</param>
/// <param name="Ready">Whether the applied binding is usable.</param>
/// <param name="DefinitionCount">Current declared definition count.</param>
/// <param name="SourceCount">Current provisioned source count.</param>
/// <param name="BindingCount">Current application binding count.</param>
/// <param name="QuotaBytes">Explicit configured logical quota, as a safe integer.</param>
/// <param name="UsedBytes">Counted logical bytes, as a safe integer.</param>
/// <param name="AppliedManifestDigest">Absent until a manifest is successfully applied.</param>
public sealed record ManagementConfigurationCatalog(string CatalogId, string ETag, string LoadedManifestDigest,
    bool Ready, int DefinitionCount, int SourceCount, int BindingCount, long QuotaBytes, long UsedBytes,
    string? AppliedManifestDigest = null);

/// <summary>Applies only the operator-controlled locally loaded manifest, never an HTTP-supplied manifest.</summary>
/// <param name="CommandVersion">Supported version, initially one.</param>
/// <param name="ManifestDigest">Exact selected loaded-manifest digest.</param>
/// <param name="Reason">Nonempty Unicode reason, at most 1,024 UTF-8 bytes.</param>
public sealed record ManagementConfigurationCatalogCommand(int CommandVersion, string ManifestDigest, string Reason);

/// <summary>An immutable publication reference or explicit authorized absence.</summary>
/// <param name="State">present or absent; failures never become absence.</param>
/// <param name="PublicationId">Required only when present.</param>
public sealed record ManagementConfigurationSourceReference(string State, string? PublicationId = null);

/// <summary>Exact immutable source content and its independently checked scope.</summary>
/// <param name="State">present or absent.</param>
/// <param name="PublicationId">Required when present.</param>
/// <param name="Sha256">Lowercase hexadecimal SHA-256 of exact decoded payloadJson UTF-8.</param>
/// <param name="PayloadJson">Exact source JSON text, never reserialized for its digest.</param>
/// <param name="SourceId">Actual source UUID.</param>
/// <param name="ConfigurationId">Exact portable definition identity.</param>
/// <param name="SchemaVersion">Canonical positive schema string.</param>
/// <param name="Environment">Exact authorized environment.</param>
/// <param name="Kind">common or application.</param>
/// <param name="ApplicationId">Required only for application content.</param>
public sealed record ManagementConfigurationSourceContent(string State, string? PublicationId = null,
    string? Sha256 = null, string? PayloadJson = null, string? SourceId = null, string? ConfigurationId = null,
    string? SchemaVersion = null, string? Environment = null, string? Kind = null, string? ApplicationId = null);

/// <summary>Publication history metadata; it grants no content authority.</summary>
/// <param name="PublicationId">Immutable publication UUID.</param>
/// <param name="SourceId">Actual source UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="Environment">Exact environment.</param>
/// <param name="Kind">common or application.</param>
/// <param name="Sha256">Exact-content digest.</param>
/// <param name="Actor">Bounded safe original actor description, never credentials.</param>
/// <param name="CreatedAtUtc">Original precise UTC timestamp.</param>
/// <param name="Reason">Original bounded change reason.</param>
/// <param name="ApplicationId">Application UUID for application sources only.</param>
public record ManagementConfigurationPublicationMetadata(string PublicationId, string SourceId, string ConfigurationId,
    string SchemaVersion, string Environment, string Kind, string Sha256, string Actor, string CreatedAtUtc,
    string Reason, string? ApplicationId = null);

/// <summary>An immutable publication, including exact source text under content authority.</summary>
/// <param name="PublicationId">Immutable publication UUID.</param>
/// <param name="SourceId">Actual source UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="Environment">Exact environment.</param>
/// <param name="Kind">common or application.</param>
/// <param name="Sha256">Exact-content digest.</param>
/// <param name="Actor">Bounded safe actor description.</param>
/// <param name="CreatedAtUtc">Original precise UTC timestamp.</param>
/// <param name="Reason">Original bounded change reason.</param>
/// <param name="PayloadJson">Exact source JSON text.</param>
/// <param name="ApplicationId">Application UUID for application sources only.</param>
public sealed record ManagementConfigurationPublication(string PublicationId, string SourceId, string ConfigurationId,
    string SchemaVersion, string Environment, string Kind, string Sha256, string Actor, string CreatedAtUtc,
    string Reason, string PayloadJson, string? ApplicationId = null)
    : ManagementConfigurationPublicationMetadata(PublicationId, SourceId, ConfigurationId, SchemaVersion, Environment,
        Kind, Sha256, Actor, CreatedAtUtc, Reason, ApplicationId);

/// <summary>Current source catalog head and a bounded page of immutable history metadata.</summary>
/// <param name="SourceId">Stable source UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="Environment">Exact environment.</param>
/// <param name="Kind">common or application.</param>
/// <param name="ETag">Strong source-catalog precondition.</param>
/// <param name="Publications">A complete authorized page, never truncated content.</param>
/// <param name="ApplicationId">Application UUID for application sources only.</param>
public sealed record ManagementConfigurationSource(string SourceId, string ConfigurationId, string SchemaVersion,
    string Environment, string Kind, string ETag, ManagementPage<ManagementConfigurationPublicationMetadata> Publications,
    string? ApplicationId = null);

/// <summary>Publishes exact immutable source text under an original key and source ETag.</summary>
/// <param name="CommandVersion">Supported version, initially one.</param>
/// <param name="PayloadJson">Exact decoded source JSON text.</param>
/// <param name="Reason">Original bounded change reason.</param>
public sealed record ManagementConfigurationPublicationCommand(int CommandVersion, string PayloadJson, string Reason);

/// <summary>Creates a coherent immutable release without assigning it.</summary>
/// <param name="CommandVersion">Supported version, initially one.</param>
/// <param name="CommonSource">Pinned common publication or explicit absence.</param>
/// <param name="ApplicationSource">Pinned application publication or explicit absence.</param>
/// <param name="Reason">Original bounded change reason.</param>
public sealed record ManagementConfigurationReleaseCommand(int CommandVersion,
    ManagementConfigurationSourceReference CommonSource, ManagementConfigurationSourceReference ApplicationSource, string Reason);

/// <summary>An immutable application-specific pair of pinned source references.</summary>
/// <param name="ReleaseId">Immutable release UUID.</param>
/// <param name="ApplicationId">Actual application UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="Environment">Exact authorized environment.</param>
/// <param name="CommonSource">Pinned common reference.</param>
/// <param name="ApplicationSource">Pinned application reference.</param>
/// <param name="Actor">Bounded safe original actor description.</param>
/// <param name="CreatedAtUtc">Original precise UTC timestamp.</param>
/// <param name="Reason">Original bounded change reason.</param>
/// <param name="ETag">Strong immutable release identity.</param>
public sealed record ManagementConfigurationRelease(string ReleaseId, string ApplicationId, string ConfigurationId,
    string SchemaVersion, string Environment, ManagementConfigurationSourceReference CommonSource,
    ManagementConfigurationSourceReference ApplicationSource, string Actor, string CreatedAtUtc, string Reason, string ETag);

/// <summary>One trusted cohort's pinned override, without wildcard targeting.</summary>
/// <param name="CohortId">Exact URI-unreserved trusted cohort identity.</param>
/// <param name="ReleaseId">Pinned immutable release UUID.</param>
public sealed record ManagementConfigurationOverride(string CohortId, string ReleaseId);

/// <summary>One complete atomic map replacement, promotion or rollback.</summary>
/// <param name="CommandVersion">Supported version, initially one.</param>
/// <param name="Intent">replace, promote or rollback.</param>
/// <param name="DefaultReleaseId">Explicit default release; defaults require a real empty release.</param>
/// <param name="Overrides">The complete resulting cohort map, at most sixteen unique cohorts.</param>
/// <param name="Reason">Original bounded change reason.</param>
public sealed record ManagementConfigurationAssignmentCommand(int CommandVersion, string Intent, string DefaultReleaseId,
    IReadOnlyList<ManagementConfigurationOverride> Overrides, string Reason)
{
    /// <summary>Gets recognized complete-map intent without treating future response intent as replace.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownIntent => new(Intent, Intent is "replace" or "promote" or "rollback");
}

/// <summary>A complete authoritative assignment map, including initialized unassigned state.</summary>
/// <param name="BindingId">Stable application/definition/schema binding UUID.</param>
/// <param name="ApplicationId">Actual application UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="Environment">Exact authorized environment.</param>
/// <param name="ETag">Strong whole-map ETag.</param>
/// <param name="Overrides">The complete cohort map.</param>
/// <param name="DefaultReleaseId">Absent only before the first assignment.</param>
/// <param name="Generation">Absent only before the first assignment; otherwise positive Int64 text.</param>
public sealed record ManagementConfigurationAssignmentMap(string BindingId, string ApplicationId, string ConfigurationId,
    string SchemaVersion, string Environment, string ETag, IReadOnlyList<ManagementConfigurationOverride> Overrides,
    string? DefaultReleaseId = null, string? Generation = null);

/// <summary>Independently validated desired identity; content failure cannot invent this context.</summary>
/// <param name="AuthorityId">Expected authoritative namespace UUID.</param>
/// <param name="ApplicationId">Expected admitted application UUID.</param>
/// <param name="Environment">Expected exact environment.</param>
/// <param name="RegistrationId">Expected current registration UUID.</param>
/// <param name="ConfigurationId">Expected enrolled portable identity.</param>
/// <param name="SchemaVersion">Expected exact enrolled schema.</param>
/// <param name="HostApplicationName">Expected fixed Configuration host scope.</param>
/// <param name="ReleaseId">Identified immutable desired release UUID.</param>
/// <param name="Generation">Identified positive assignment generation.</param>
/// <param name="CohortId">Expected trusted admitted cohort, if any.</param>
public sealed record ManagementConfigurationDesiredContext(string AuthorityId, string ApplicationId, string Environment,
    string RegistrationId, string ConfigurationId, string SchemaVersion, string HostApplicationName, string ReleaseId,
    string Generation, string? CohortId = null);

/// <summary>Trusted expected session/enrollment identity, supplied independently from response data.</summary>
/// <param name="AuthorityId">Current authoritative namespace UUID.</param>
/// <param name="ApplicationId">Current admitted application UUID.</param>
/// <param name="Environment">Exact expected environment.</param>
/// <param name="RegistrationId">Current registration UUID.</param>
/// <param name="ConfigurationId">Explicitly enrolled portable identity.</param>
/// <param name="SchemaVersion">Exact locally enrolled schema.</param>
/// <param name="HostApplicationName">Fixed local Configuration host scope.</param>
/// <param name="CohortId">Trusted admitted cohort, if any.</param>
public sealed record ManagementConfigurationSnapshotTarget(string AuthorityId, string ApplicationId, string Environment,
    string RegistrationId, string ConfigurationId, string SchemaVersion, string HostApplicationName, string? CohortId = null);

/// <summary>One coherent authoritative observation, never two independent source reads.</summary>
/// <param name="Desired">Target/session binding and identified desired state.</param>
/// <param name="ETag">Conditional token bound to this exact session and snapshot.</param>
/// <param name="CommonSource">Explicit common source state and exact content.</param>
/// <param name="ApplicationSource">Explicit application source state and exact content.</param>
public sealed record ManagementConfigurationSnapshot(ManagementConfigurationDesiredContext Desired, string ETag,
    ManagementConfigurationSourceContent CommonSource, ManagementConfigurationSourceContent ApplicationSource);

/// <summary>A payload-free bounded structural issue, not native exception text.</summary>
/// <param name="Code">Safe stable machine-readable code.</param>
/// <param name="Path">Optional bounded structural member path.</param>
public sealed record ManagementConfigurationIssue(string Code, string? Path = null);

/// <summary>Side-effect-free validation scoped to an actual provisioned source.</summary>
/// <param name="CommandVersion">Supported version, initially one.</param>
/// <param name="SourceId">Actual authorized source UUID.</param>
/// <param name="PayloadJson">Exact draft source JSON.</param>
public sealed record ManagementConfigurationValidationCommand(int CommandVersion, string SourceId, string PayloadJson);

/// <summary>Structural eligibility only; it does not prove deployed typed acceptance.</summary>
/// <param name="Valid">Whether declared structural validation succeeded.</param>
/// <param name="Issues">Complete bounded payload-free issues.</param>
public sealed record ManagementConfigurationValidationResult(bool Valid, IReadOnlyList<ManagementConfigurationIssue> Issues);

/// <summary>Side-effect-free preview of an exact source pair or a complete assignment proposal.</summary>
/// <param name="CommandVersion">Supported version, initially one.</param>
/// <param name="ApplicationId">Actual application UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="CommonSource">Pinned common reference or explicit absence; paired with ApplicationSource only when Assignment is absent.</param>
/// <param name="ApplicationSource">Pinned application reference or explicit absence; paired with CommonSource only when Assignment is absent.</param>
/// <param name="Assignment">Complete proposed map and intent, mutually exclusive with the source pair.</param>
public sealed record ManagementConfigurationPreviewCommand(int CommandVersion, string ApplicationId, string ConfigurationId,
    string SchemaVersion, ManagementConfigurationSourceReference? CommonSource = null,
    ManagementConfigurationSourceReference? ApplicationSource = null, ManagementConfigurationAssignmentCommand? Assignment = null);

/// <summary>One payload-free preview change.</summary>
/// <param name="Path">Bounded structural path.</param>
/// <param name="Kind">Recognized or future change kind, without inferring success.</param>
public sealed record ManagementConfigurationChange(string Path, string Kind);

/// <summary>A bounded scoped preview that never promises consumer acceptance.</summary>
/// <param name="ApplicationId">Actual application UUID.</param>
/// <param name="ConfigurationId">Portable definition identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="StructurallyValid">Declared structural eligibility.</param>
/// <param name="Redacted">Whether value authority was unavailable; changes contain no values in either case.</param>
/// <param name="Changes">Complete bounded payload-free changes.</param>
/// <param name="Issues">Complete bounded issues.</param>
/// <param name="CurrentAssignment">Complete current map, including the exact precondition and retained overrides.</param>
/// <param name="Recipients">A bounded authorized page of registration compatibility observations.</param>
/// <param name="CommonSource">Exact previewed common reference, paired with ApplicationSource when ProposedAssignment is absent.</param>
/// <param name="ApplicationSource">Exact previewed application reference, paired with CommonSource when ProposedAssignment is absent.</param>
/// <param name="ProposedAssignment">Complete previewed assignment intent, including removed or retained cohort pins.</param>
public sealed record ManagementConfigurationPreview(string ApplicationId, string ConfigurationId, string SchemaVersion,
    bool StructurallyValid, bool Redacted, IReadOnlyList<ManagementConfigurationChange> Changes,
    IReadOnlyList<ManagementConfigurationIssue> Issues, ManagementConfigurationAssignmentMap CurrentAssignment,
    ManagementPage<ManagementConfigurationPreviewRecipient> Recipients,
    ManagementConfigurationSourceReference? CommonSource = null, ManagementConfigurationSourceReference? ApplicationSource = null,
    ManagementConfigurationAssignmentCommand? ProposedAssignment = null);

/// <summary>One reported registration compatibility observation, never proof of deployed typed acceptance.</summary>
/// <param name="RegistrationId">Actual authorized retained registration UUID.</param>
/// <param name="Compatibility">reportedCompatible or unknown, preserving future observations explicitly.</param>
/// <param name="CohortId">Trusted admitted cohort, if any.</param>
public sealed record ManagementConfigurationPreviewRecipient(string RegistrationId, string Compatibility, string? CohortId = null)
{
    /// <summary>Gets recognized advisory compatibility without converting unknown into compatible.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownCompatibility => new(Compatibility, Compatibility is "reportedCompatible" or "unknown");
}

/// <summary>An identified release and positive assignment generation.</summary>
/// <param name="ReleaseId">Immutable release UUID.</param>
/// <param name="Generation">Positive Int64 assignment generation.</param>
public record ManagementConfigurationVersion(string ReleaseId, string Generation);

/// <summary>Actual local acceptance, distinct from desired state or notification success.</summary>
/// <param name="ReleaseId">Actually accepted release UUID.</param>
/// <param name="Generation">Actually accepted assignment generation.</param>
/// <param name="LocalRevision">Local accepted revision; zero is permitted.</param>
public sealed record ManagementConfigurationAppliedVersion(string ReleaseId, string Generation, string LocalRevision)
    : ManagementConfigurationVersion(ReleaseId, Generation);

/// <summary>One bounded local adoption observation; historical success never proves intact live state.</summary>
/// <param name="ConfigurationId">Explicitly enrolled portable identity.</param>
/// <param name="SchemaVersion">Exact enrolled schema.</param>
/// <param name="UpdateMode">Locally selected HotReload or RestartRequired.</param>
/// <param name="State">Raw adoption state with explicit known/unknown interpretation.</param>
/// <param name="LiveStateValid">Whether accepted live-state guarantees remain intact.</param>
/// <param name="Desired">Independently identified latest desired state.</param>
/// <param name="Applied">Current accepted state, present only when live state is valid.</param>
/// <param name="LastSuccessful">Historical last acceptance, including after a terminal fault.</param>
/// <param name="CandidateFailureCode">Separate bounded content or validation failure.</param>
/// <param name="NotificationFailureCode">Separate bounded notification failure.</param>
public sealed record ManagementConfigurationStatusEntry(string ConfigurationId, string SchemaVersion, string UpdateMode,
    string State, bool LiveStateValid, ManagementConfigurationVersion? Desired = null,
    ManagementConfigurationAppliedVersion? Applied = null, ManagementConfigurationAppliedVersion? LastSuccessful = null,
    string? CandidateFailureCode = null, string? NotificationFailureCode = null)
{
    /// <summary>Gets the local update-mode interpretation without accepting a future mode by default.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownUpdateMode => new(UpdateMode, UpdateMode is "HotReload" or "RestartRequired");

    /// <summary>Gets an interpretation that never converts a future state to accepted.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownState => new(State,
        State is "Pending" or "Applied" or "AppliedUnchanged" or "Rejected" or "PendingRestart" or "Faulted");
}

/// <summary>One complete independently sequenced configuration-status capture.</summary>
/// <param name="Sequence">Positive Int64 stream sequence.</param>
/// <param name="ObservedAtUtc">Original precise source observation time.</param>
/// <param name="Entries">Complete unique definition/schema observations, at most 256.</param>
public sealed record ManagementConfigurationStatusReport(string Sequence, string ObservedAtUtc,
    IReadOnlyList<ManagementConfigurationStatusEntry> Entries);

/// <summary>Current-target adoption projection; no-recipient, offline and unknown are explicit facts.</summary>
/// <param name="ApplicationId">Actual application UUID.</param>
/// <param name="Environment">Actual authorized environment.</param>
/// <param name="ConfigurationId">Portable identity.</param>
/// <param name="SchemaVersion">Exact schema string.</param>
/// <param name="RegistrationFreshness">Raw registration freshness, independent of adoption.</param>
/// <param name="AdoptionFreshness">Raw adoption receipt freshness.</param>
/// <param name="Convergence">Raw current-target interpretation, never inferred from historical Applied.</param>
/// <param name="CohortId">Trusted target cohort, if any.</param>
/// <param name="RegistrationId">Admitted registration, absent for no observed recipients.</param>
/// <param name="ReportedInstanceId">Reported metadata, never proof of a distinct physical process.</param>
/// <param name="ApplicationVersion">Retained registration version.</param>
/// <param name="Desired">Currently resolved server desired state.</param>
/// <param name="Reported">Last complete matching definition observation.</param>
/// <param name="ObservedAtUtc">Original source observation time.</param>
/// <param name="ReceivedAtUtc">Authoritative server receipt time.</param>
public sealed record ManagementConfigurationAdoptionEntry(string ApplicationId, string Environment,
    string ConfigurationId, string SchemaVersion, string RegistrationFreshness, string AdoptionFreshness,
    string Convergence, string? CohortId = null, string? RegistrationId = null, string? ReportedInstanceId = null,
    string? ApplicationVersion = null, ManagementConfigurationVersion? Desired = null,
    ManagementConfigurationStatusEntry? Reported = null, string? ObservedAtUtc = null, string? ReceivedAtUtc = null)
{
    /// <summary>Gets recognized registration freshness without interpreting unknown as fresh.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownRegistrationFreshness => Freshness(RegistrationFreshness);

    /// <summary>Gets recognized adoption freshness, separately from registration presence.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownAdoptionFreshness => Freshness(AdoptionFreshness);

    /// <summary>Gets recognized current-target convergence, never historical acceptance by default.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownConvergence => new(Convergence,
        Convergence is "pending" or "applied" or "rejected" or "pendingRestart" or "faulted" or "unknown" or "noInstances");

    private static ManagementKnownValue Freshness(string value)
        => new(value, value is "fresh" or "stale" or "offline" or "unknown" or "noInstances");
}
