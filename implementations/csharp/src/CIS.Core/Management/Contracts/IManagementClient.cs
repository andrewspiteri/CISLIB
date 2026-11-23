using CIS.RestClient;

namespace CIS.Management;

/// <summary>Per-call budget and deliberately selected read retry choice; contains no credentials or caller identity.</summary>
/// <param name="Timeout">Optional finite whole-operation timeout.</param>
/// <param name="ReadRetryPolicy">Optional explicit native read policy; mutation and report methods do not inherit read replay.</param>
public sealed record ManagementCallOptions(TimeSpan? Timeout = null, RestRetryPolicy? ReadRetryPolicy = null);

/// <summary>The typed v1 HTTP APIs. Merely using this port never selects managed execution or starts periodic work.</summary>
public interface IManagementClient
{
    /// <summary>Reads authenticated supported protocols and actually installed APIs.</summary>
    Task<ManagementCallResult<ManagementServiceInfo>> GetServiceInfoAsync(ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates or recovers the same retained process registration under current trusted credentials.</summary>
    Task<ManagementCallResult<ManagementRegistrationReceipt>> RegisterAsync(string registrationId, ManagementRegistrationRequest request,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads fresh authoritative control with an exact per-attempt echo and no automatic transport retry.</summary>
    /// <remarks>Existing custom implementations remain usable for their original APIs. Unsupported control never supplies admission.</remarks>
    Task<ManagementCallResult<ManagementControlConfirmation>> GetControlAsync(string registrationId, string attemptId,
        CancellationToken cancellationToken = default)
        => Task.FromException<ManagementCallResult<ManagementControlConfirmation>>(
            new NotSupportedException("The selected management client does not implement authoritative control."));

    /// <summary>Reports one complete observation using its original stream sequence and content.</summary>
    Task<ManagementCallResult<ManagementReportReceipt>> ReportObservationAsync(string registrationId, ManagementObservation observation,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces one complete inventory using its independent original sequence.</summary>
    Task<ManagementCallResult<ManagementReportReceipt>> ReportInventoryAsync(string registrationId, ManagementInventorySnapshot inventory,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Idempotently closes a retained registration without claiming every business callback has stopped.</summary>
    Task<ManagementCallResult<ManagementCloseReceipt>> CloseRegistrationAsync(string registrationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads an authorized stable-order live-view application page.</summary>
    Task<ManagementCallResult<ManagementPage<ManagementApplication>>> ListApplicationsAsync(ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads one visible authoritative application resource and current metadata-only ETag.</summary>
    Task<ManagementCallResult<ManagementApplication>> GetApplicationAsync(string applicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded currently authorized registration/presence page.</summary>
    Task<ManagementCallResult<ManagementPage<ManagementInstance>>> ListInstancesAsync(string applicationId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads complete retained inventory snapshots with per-instance version and freshness provenance.</summary>
    Task<ManagementCallResult<ManagementPage<ManagementInventoryEntry>>> ListInventoryAsync(string applicationId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads actually installed operations and advisory current hints for one visible resource.</summary>
    Task<ManagementCallResult<ManagementCapabilities>> GetCapabilitiesAsync(string applicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Submits one explicit original-key metadata command; uncertainty never generates another key or automatic replay.</summary>
    Task<ManagementCallResult<ManagementMetadataResult>> ReplaceMetadataAsync(string applicationId, string operationKey,
        string ifMatch, ManagementMetadataCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Recovers the original result using the same target/key and current validated actor scope.</summary>
    Task<ManagementCallResult<ManagementMetadataResult>> GetMetadataOperationAsync(string applicationId, string operationKey,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}
