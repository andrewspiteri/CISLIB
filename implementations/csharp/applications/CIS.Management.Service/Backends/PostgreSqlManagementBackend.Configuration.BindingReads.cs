using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementConfigurationReleaseHistoryBackend,
    IManagementConfigurationBindingAdoptionBackend
{
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationRelease>>> ReadReleasesAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default)
        => store.ReadConfigurationReleasesAsync(binding, after, take, token);

    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadBindingAdoptionAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default)
        => store.ReadBindingConfigurationAdoptionAsync(binding, after, take, token);
}

