using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional bounded discovery queries; existing backend implementations retain their original list API.</summary>
public interface IManagementDiscoveryBackend
{
    /// <summary>Filters exact environments and literal ordinal identity/display-name substrings before UUID paging.</summary>
    Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> QueryApplicationsAsync(ManagementApplicationQuery query,
        string? afterApplicationId, int take, CancellationToken cancellationToken = default);

    /// <summary>Reads application candidates ordered by UTF-8 environment then UUID. A null application position skips the completed environment.</summary>
    Task<ManagementStoreResult<IReadOnlyList<ManagementApplication>>> ReadEnvironmentCandidatesAsync(string? afterEnvironment,
        string? afterApplicationId, int take, CancellationToken cancellationToken = default);
}

/// <summary>Optional exact-application definition discovery, retaining independent source authorization in the HTTP boundary.</summary>
public interface IManagementApplicationConfigurationBackend
{
    /// <summary>Filters application sources by exact application/environment before source-ID paging; common sources are excluded.</summary>
    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadApplicationDefinitionsAsync(
        string applicationId, string environment, string? afterSourceId, int take, CancellationToken cancellationToken = default);
}
