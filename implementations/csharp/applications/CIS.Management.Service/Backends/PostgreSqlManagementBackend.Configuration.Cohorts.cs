using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementConfigurationCohortBackend
{
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationCohortChoice>>> ReadCohortsAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default)
        => store.ReadConfigurationCohortsAsync(binding, after, take, token);
}
