namespace CIS.Management;

/// <summary>Substitutable configuration APIs. Selection alone starts no polling or managed execution.</summary>
public interface IManagementConfigurationClient
{
    /// <summary>Reads one authorized page of explicitly provisioned portable definitions.</summary>
    Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationDefinition>>> GetDefinitionsAsync(
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Validates a scoped draft without mutation or automatic POST replay.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationValidationResult>> ValidateAsync(
        ManagementConfigurationValidationCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Previews exact references or a complete map proposal and one authorized recipient page without mutation.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationPreview>> PreviewAsync(
        ManagementConfigurationPreviewCommand command, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads source metadata and a bounded publication-history page under current authority.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationSource>> GetSourceAsync(string sourceId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Publishes exact source text once under its original key and source-catalog ETag.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> PublishSourceAsync(string sourceId,
        string operationKey, string ifMatch, ManagementConfigurationPublicationCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads one immutable publication under its actual content authority.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationPublication>> GetPublicationAsync(string publicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates an immutable release against the current assignment-map ETag without changing or assigning that map.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> CreateReleaseAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationReleaseCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a coherent immutable release under current actual authority.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationRelease>> GetReleaseAsync(string releaseId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the complete assignment map, including initialized unassigned state.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationAssignmentMap>> GetAssignmentsAsync(string applicationId,
        string configurationId, string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces the complete map under original key and strong whole-map precondition.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ReplaceAssignmentsAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey, string ifMatch, ManagementConfigurationAssignmentCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a coherent authoritative snapshot, checking the independently trusted target inside the native decode deadline.</summary>
    /// <remarks>Derive If-None-Match only from a complete validated retained snapshot for this exact target/session/schema.
    /// A conditional 304 may reuse that snapshot only, and never renews Management authority.</remarks>
    Task<ManagementConfigurationCallResult<ManagementConfigurationSnapshot>> GetRegistrationConfigurationAsync(
        ManagementConfigurationSnapshotTarget expectedTarget, ManagementConfigurationSnapshot? retainedSnapshot = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reports one complete separately sequenced configuration-status capture without automatic replay.</summary>
    Task<ManagementConfigurationCallResult<ManagementReportReceipt>> ReportStatusAsync(string registrationId,
        ManagementConfigurationStatusReport report, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads current-target adoption separately from registration and observation freshness.</summary>
    Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>>> GetStatusAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads catalog bootstrap capability even before a deployment manifest has been applied.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationCatalog>> GetCatalogAsync(
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Applies only the locally loaded manifest under original key and catalog ETag.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> ApplyCatalogAsync(string operationKey,
        string ifMatch, ManagementConfigurationCatalogCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Recovers the original catalog decision using the unchanged key and current authorization.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetCatalogOperationAsync(string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Recovers an original publication decision, even if unreferenced content was later pruned.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetPublicationOperationAsync(string sourceId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Recovers the original release creation without creating or assigning another release.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetReleaseOperationAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Recovers the original complete assignment decision under the actual binding and actor scope.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetAssignmentOperationAsync(string applicationId,
        string configurationId, string schemaVersion, string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}
