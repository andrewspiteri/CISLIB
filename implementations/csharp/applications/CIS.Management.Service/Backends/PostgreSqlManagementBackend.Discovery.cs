using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementDiscoveryBackend, IManagementApplicationConfigurationBackend
{
    public Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> QueryApplicationsAsync(ManagementApplicationQuery query,
        string? afterApplicationId, int take, CancellationToken cancellationToken = default)
        => store.QueryApplicationsAsync(query, afterApplicationId, take, cancellationToken);

    public Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> ReadEnvironmentCandidatesAsync(string? afterEnvironment,
        string? afterApplicationId, int take, CancellationToken cancellationToken = default)
        => store.ReadEnvironmentCandidatesAsync(afterEnvironment, afterApplicationId, take, cancellationToken);

    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadApplicationDefinitionsAsync(
        string applicationId, string environment, string? afterSourceId, int take, CancellationToken cancellationToken = default)
        => store.ReadApplicationConfigurationDefinitionsAsync(applicationId, environment, afterSourceId, take, cancellationToken);
}
