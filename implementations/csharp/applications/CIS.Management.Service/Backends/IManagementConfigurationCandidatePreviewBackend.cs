using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional side-effect-free preview of one unpublished layer over an explicit immutable source baseline.</summary>
public interface IManagementConfigurationCandidatePreviewBackend
{
    Task<ManagementStoreResult<ManagementConfigurationCandidatePreview>> PreviewCandidateAsync(
        ManagementConfigurationBindingResource binding, ManagementConfigurationCandidatePreviewCommand command,
        string? after, int take, bool includeValues, CancellationToken token = default);
}

