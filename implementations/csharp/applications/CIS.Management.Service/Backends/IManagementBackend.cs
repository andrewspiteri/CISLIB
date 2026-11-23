using CIS.Management.Service.Metadata;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Backends;

/// <summary>The product's actual typed authority operations. This is not a generic persistence or command framework.</summary>
public interface IManagementBackend
{
    ManagementBackendCapabilities Capabilities
    {
        get;
    }
    ManagementWireLimits WireLimits
    {
        get;
    }
    int MaximumInventoryRequestBytes
    {
        get;
    }
    Task<ManagementAuthoritySnapshot> ProbeAsync(CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementRegistrationReceipt>> RegisterAsync(ManagementRegistrationOwner owner, string registrationId,
        ManagementRegistrationRequest request, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementControlConfirmation>> ConfirmControlAsync(ManagementRegistrationOwner owner,
        string registrationId, string attemptId, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementReportReceipt>> ReportObservationAsync(ManagementRegistrationOwner owner, string registrationId,
        ManagementObservation observation, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementReportReceipt>> ReportInventoryAsync(ManagementRegistrationOwner owner, string registrationId,
        ManagementInventorySnapshot inventory, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementCloseReceipt>> CloseAsync(ManagementRegistrationOwner owner, string registrationId,
        CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementApplication>> ReadApplicationAsync(string applicationId, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> ReadApplicationsAsync(string? afterApplicationId, int take,
        CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<IReadOnlyList<ManagementInstance>>> ReadInstancesAsync(string applicationId, string? afterRegistrationId,
        int take, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<IReadOnlyList<ManagementInventoryEntry>>> ReadInventoryAsync(string applicationId, string? afterRegistrationId,
        int take, int maximumEncodedBytes, CancellationToken cancellationToken = default);
    Task<ManagementMetadataResponse> ReplaceMetadataAsync(ManagementApplication application, string key, string ifMatch,
        ManagementMetadataCommand command, CancellationToken cancellationToken = default);
    Task<ManagementMetadataResponse> LookupMetadataAsync(ManagementApplication application, string key, CancellationToken cancellationToken = default);
    Task<ManagementStoreResult<ManagementCleanupResult>> CleanupAsync(int maximumRows, CancellationToken cancellationToken = default);
}
