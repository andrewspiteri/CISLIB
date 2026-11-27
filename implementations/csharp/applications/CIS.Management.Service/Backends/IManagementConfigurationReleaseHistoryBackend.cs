using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional bounded retained-release history for one exact binding, before per-release HTTP authorization.</summary>
public interface IManagementConfigurationReleaseHistoryBackend
{
    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationRelease>>> ReadReleasesAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default);
}

