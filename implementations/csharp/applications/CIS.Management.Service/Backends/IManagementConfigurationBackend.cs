using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Backends;

/// <summary>Additive product capability; existing management backends acquire no compulsory member.</summary>
public partial interface IManagementConfigurationBackend
{
    bool ConfigurationSelected
    {
        get;
    }
    ManagementConfigurationWireLimits ConfigurationWireLimits
    {
        get;
    }
    Task<ManagementStoreResult<ManagementConfigurationCatalog>> ReadCatalogAsync(CancellationToken token = default);
    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadDefinitionsAsync(string? after, int take,
        CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationDefinition>> ReadDefinitionAsync(string sourceId, CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationSource>> ReadSourceAsync(string sourceId, string? after, int take,
        CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationPublication>> ReadPublicationAsync(string publicationId, CancellationToken token = default);
    Task<ManagementStoreResult<string>> ResolvePublicationSourceAsync(string publicationId, CancellationToken token = default);
    Task<ManagementConfigurationResponse> ApplyCatalogAsync(ManagementConfigurationCatalog catalog, string key, string ifMatch,
        ManagementConfigurationCatalogCommand command, CancellationToken token = default);
    Task<ManagementConfigurationResponse> PublishAsync(ManagementConfigurationDefinition source, string key, string ifMatch,
        ManagementConfigurationPublicationCommand command, CancellationToken token = default);
    Task<ManagementConfigurationResponse> LookupCatalogAsync(ManagementConfigurationCatalog catalog, string key, CancellationToken token = default);
    Task<ManagementConfigurationResponse> LookupPublicationAsync(ManagementConfigurationDefinition source, string key, CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationBindingResource>> ReadBindingAsync(string applicationId, string configurationId,
        string schemaVersion, CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationRelease>> ReadReleaseAsync(string releaseId, CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationAssignmentMap>> ReadAssignmentsAsync(ManagementConfigurationBindingResource binding,
        CancellationToken token = default);
    Task<ManagementConfigurationResponse> CreateReleaseAsync(ManagementConfigurationBindingResource binding, string key, string ifMatch,
        ManagementConfigurationReleaseCommand command, CancellationToken token = default);
    Task<ManagementConfigurationResponse> ReplaceAssignmentsAsync(ManagementConfigurationBindingResource binding, string key, string ifMatch,
        ManagementConfigurationAssignmentCommand command, CancellationToken token = default);
    Task<ManagementConfigurationResponse> LookupReleaseAsync(ManagementConfigurationBindingResource binding, string key, CancellationToken token = default);
    Task<ManagementConfigurationResponse> LookupAssignmentAsync(ManagementConfigurationBindingResource binding, string key, CancellationToken token = default);
    Task<ManagementStoreResult<ManagementConfigurationPreview>> PreviewAsync(ManagementConfigurationBindingResource binding,
        ManagementConfigurationPreviewCommand command, string? after, int take, bool includeValues, string? expectedMapETag,
        CancellationToken token = default);
}
