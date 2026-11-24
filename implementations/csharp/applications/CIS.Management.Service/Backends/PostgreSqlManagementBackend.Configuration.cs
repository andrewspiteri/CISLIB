using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementConfigurationBackend
{
    public bool ConfigurationSelected => options.Configuration is not null;
    public ManagementConfigurationWireLimits ConfigurationWireLimits { get; } = new();

    public Task<ManagementStoreResult<ManagementConfigurationCatalog>> ReadCatalogAsync(CancellationToken token = default)
        => store.ReadConfigurationCatalogAsync(options.Configuration ?? throw new InvalidOperationException("Configuration is not selected."), token);
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadDefinitionsAsync(string? after, int take,
        CancellationToken token = default) => store.ReadConfigurationDefinitionsAsync(after, take, token);
    public Task<ManagementStoreResult<ManagementConfigurationDefinition>> ReadDefinitionAsync(string sourceId, CancellationToken token = default)
        => store.ReadConfigurationDefinitionAsync(sourceId, token);
    public Task<ManagementStoreResult<ManagementConfigurationSource>> ReadSourceAsync(string sourceId, string? after, int take,
        CancellationToken token = default) => store.ReadConfigurationSourceAsync(sourceId, after, take, token);
    public Task<ManagementStoreResult<ManagementConfigurationPublication>> ReadPublicationAsync(string publicationId, CancellationToken token = default)
        => store.ReadConfigurationPublicationAsync(publicationId, token);
    public Task<ManagementStoreResult<string>> ResolvePublicationSourceAsync(string publicationId, CancellationToken token = default)
        => store.ResolveConfigurationPublicationSourceAsync(publicationId, token);
    public Task<ManagementConfigurationResponse> ApplyCatalogAsync(ManagementConfigurationCatalog catalog, string key, string ifMatch,
        ManagementConfigurationCatalogCommand command, CancellationToken token = default)
        => configuration.ApplyCatalogAsync(catalog, key, ifMatch, command, token);
    public Task<ManagementConfigurationResponse> PublishAsync(ManagementConfigurationDefinition source, string key, string ifMatch,
        ManagementConfigurationPublicationCommand command, CancellationToken token = default)
        => configuration.PublishAsync(source, key, ifMatch, command, token);
    public Task<ManagementConfigurationResponse> LookupCatalogAsync(ManagementConfigurationCatalog catalog, string key, CancellationToken token = default)
        => configuration.LookupCatalogAsync(catalog, key, token);
    public Task<ManagementConfigurationResponse> LookupPublicationAsync(ManagementConfigurationDefinition source, string key, CancellationToken token = default)
        => configuration.LookupPublicationAsync(source, key, token);
}
