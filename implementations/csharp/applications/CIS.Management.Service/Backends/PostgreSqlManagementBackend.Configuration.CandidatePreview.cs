using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementConfigurationCandidatePreviewBackend
{
    public Task<ManagementStoreResult<ManagementConfigurationCandidatePreview>> PreviewCandidateAsync(
        ManagementConfigurationBindingResource binding, ManagementConfigurationCandidatePreviewCommand command,
        string? after, int take, bool includeValues, CancellationToken token = default)
        => store.PreviewConfigurationCandidateAsync(binding, command, after, take, includeValues, token);
}

