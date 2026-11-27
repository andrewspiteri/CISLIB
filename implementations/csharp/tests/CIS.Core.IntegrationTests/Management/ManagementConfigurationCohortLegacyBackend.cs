using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Tests.IntegrationTests.Management;

// This named compatibility fixture deliberately implements only the original product interface.
// Its actual GET paths borrow the real native reads; any unexpected call fails explicitly.
internal class ManagementConfigurationCohortLegacyBackend(IManagementConfigurationBackend native) : IManagementConfigurationBackend
{
    public bool ConfigurationSelected => native.ConfigurationSelected;
    public ManagementConfigurationWireLimits ConfigurationWireLimits => native.ConfigurationWireLimits;
    public Task<ManagementStoreResult<ManagementConfigurationCatalog>> ReadCatalogAsync(CancellationToken token = default)
        => native.ReadCatalogAsync(token);
    public Task<ManagementStoreResult<ManagementConfigurationBindingResource>> ReadBindingAsync(string applicationId, string configurationId,
        string schemaVersion, CancellationToken token = default)
        => native.ReadBindingAsync(applicationId, configurationId, schemaVersion, token);

    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadDefinitionsAsync(string? after, int take,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementConfigurationDefinition>> ReadDefinitionAsync(string sourceId,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementConfigurationSource>> ReadSourceAsync(string sourceId, string? after, int take,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementConfigurationPublication>> ReadPublicationAsync(string publicationId,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<string>> ResolvePublicationSourceAsync(string publicationId,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> ApplyCatalogAsync(ManagementConfigurationCatalog catalog, string key, string ifMatch,
        ManagementConfigurationCatalogCommand command, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> PublishAsync(ManagementConfigurationDefinition source, string key, string ifMatch,
        ManagementConfigurationPublicationCommand command, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> LookupCatalogAsync(ManagementConfigurationCatalog catalog, string key,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> LookupPublicationAsync(ManagementConfigurationDefinition source, string key,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementConfigurationRelease>> ReadReleaseAsync(string releaseId,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementConfigurationAssignmentMap>> ReadAssignmentsAsync(ManagementConfigurationBindingResource binding,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> CreateReleaseAsync(ManagementConfigurationBindingResource binding, string key, string ifMatch,
        ManagementConfigurationReleaseCommand command, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> ReplaceAssignmentsAsync(ManagementConfigurationBindingResource binding, string key, string ifMatch,
        ManagementConfigurationAssignmentCommand command, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> LookupReleaseAsync(ManagementConfigurationBindingResource binding, string key,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementConfigurationResponse> LookupAssignmentAsync(ManagementConfigurationBindingResource binding, string key,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementConfigurationPreview>> PreviewAsync(ManagementConfigurationBindingResource binding,
        ManagementConfigurationPreviewCommand command, string? after, int take, bool includeValues, string? expectedMapETag,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementResolvedConfiguration>> ReadRegistrationConfigurationAsync(ManagementRegistrationOwner owner,
        string registrationId, string configurationId, string schemaVersion, string? ifNoneMatch,
        CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportConfigurationStatusAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementConfigurationStatusReport report, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadConfigurationAdoptionAsync(string applicationId,
        string? after, int take, CancellationToken token = default) => throw new NotSupportedException();
}
