using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional bounded discovery of cohorts admissible for an exact Configuration binding.</summary>
public interface IManagementConfigurationCohortBackend
{
    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationCohortChoice>>> ReadCohortsAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default);
}
