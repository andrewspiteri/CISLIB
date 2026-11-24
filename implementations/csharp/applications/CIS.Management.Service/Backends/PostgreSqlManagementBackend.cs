using CIS.Management.Service.Metadata;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Management.Service.Configuration;

namespace CIS.Management.Service.Backends;

// One complete product adapter. Its participants, journal and outbox all use the original management RM-02 capability.
internal sealed partial class PostgreSqlManagementBackend : IManagementBackend
{
    private readonly PostgreSqlManagementStore store;
    private readonly PostgreSqlManagementAuthority authority;
    private readonly ManagementMetadataService metadata;
    private readonly ManagementServiceOptions options;
    private readonly ManagementConfigurationService configuration;

    public PostgreSqlManagementBackend(PostgreSqlManagementStore store, PostgreSqlManagementAuthority authority,
        ManagementMetadataService metadata, ManagementServiceOptions options, ManagementConfigurationService configuration)
    {
        if (!store.UsesAuthority(authority))
        {
            throw new InvalidOperationException("The complete backend requires the same original PostgreSQL authority participant.");
        }
        Capabilities.Validate(options.Backend.Mode);
        this.store = store;
        this.authority = authority;
        this.metadata = metadata;
        this.options = options;
        this.configuration = configuration;
    }

    public ManagementBackendCapabilities Capabilities
    {
        get;
    } = new("PostgreSql", "postgresql-primary-durable-v1",
        true, true, true, true, 16777216);
    public ManagementWireLimits WireLimits => store.Options.WireLimits;
    public int MaximumInventoryRequestBytes => Math.Min(Capabilities.MaximumInventoryRequestBytes, WireLimits.MaximumInventoryRequestBytes);
    public Task<ManagementAuthoritySnapshot> ProbeAsync(CancellationToken cancellationToken = default) => authority.ProbeAsync(cancellationToken);
    public Task<ManagementStoreResult<ManagementRegistrationReceipt>> RegisterAsync(ManagementRegistrationOwner owner, string registrationId,
        ManagementRegistrationRequest request, CancellationToken cancellationToken = default) => store.RegisterAsync(owner, registrationId, request, cancellationToken);
    public Task<ManagementStoreResult<ManagementControlConfirmation>> ConfirmControlAsync(ManagementRegistrationOwner owner,
        string registrationId, string attemptId, CancellationToken cancellationToken = default)
        => store.ConfirmControlAsync(owner, registrationId, attemptId, options.Backend.ControlValidForMs, cancellationToken);
    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportObservationAsync(ManagementRegistrationOwner owner, string registrationId,
        ManagementObservation observation, CancellationToken cancellationToken = default) => store.ReportObservationAsync(owner, registrationId, observation, cancellationToken);
    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportInventoryAsync(ManagementRegistrationOwner owner, string registrationId,
        ManagementInventorySnapshot inventory, CancellationToken cancellationToken = default) => store.ReportInventoryAsync(owner, registrationId, inventory, cancellationToken);
    public Task<ManagementStoreResult<ManagementCloseReceipt>> CloseAsync(ManagementRegistrationOwner owner, string registrationId,
        CancellationToken cancellationToken = default) => store.CloseAsync(owner, registrationId, cancellationToken);
    public Task<ManagementStoreResult<ManagementApplication>> ReadApplicationAsync(string applicationId, CancellationToken cancellationToken = default)
        => store.ReadApplicationAsync(applicationId, cancellationToken);
    public Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> ReadApplicationsAsync(string? afterApplicationId, int take,
        CancellationToken cancellationToken = default) => store.ReadApplicationsAsync(afterApplicationId, take, cancellationToken);
    public Task<ManagementStoreResult<IReadOnlyList<ManagementInstance>>> ReadInstancesAsync(string applicationId, string? afterRegistrationId,
        int take, CancellationToken cancellationToken = default) => store.ReadInstancesAsync(applicationId, afterRegistrationId, take, cancellationToken);
    public Task<ManagementStoreResult<IReadOnlyList<ManagementInventoryEntry>>> ReadInventoryAsync(string applicationId, string? afterRegistrationId,
        int take, int maximumEncodedBytes, CancellationToken cancellationToken = default)
        => store.ReadInventoryAsync(applicationId, afterRegistrationId, take, maximumEncodedBytes, cancellationToken);
    public Task<ManagementMetadataResponse> ReplaceMetadataAsync(ManagementApplication application, string key, string ifMatch,
        ManagementMetadataCommand command, CancellationToken cancellationToken = default) => metadata.ReplaceAsync(application, key, ifMatch, command, cancellationToken);
    public Task<ManagementMetadataResponse> LookupMetadataAsync(ManagementApplication application, string key,
        CancellationToken cancellationToken = default) => metadata.LookupAsync(application, key, cancellationToken);
    public Task<ManagementStoreResult<ManagementCleanupResult>> CleanupAsync(int maximumRows, CancellationToken cancellationToken = default)
        => store.CleanupAsync(maximumRows, cancellationToken);
}
