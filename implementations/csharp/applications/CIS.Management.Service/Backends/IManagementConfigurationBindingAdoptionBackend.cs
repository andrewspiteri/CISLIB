using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional adoption read restricted to one exact binding before recipient expansion and paging.</summary>
public interface IManagementConfigurationBindingAdoptionBackend
{
    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadBindingAdoptionAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken token = default);
}

