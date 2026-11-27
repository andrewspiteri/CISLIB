namespace CIS.Management;

/// <summary>Optional unpublished candidate read; no mutation or compulsory existing Configuration member is added.</summary>
public interface IManagementConfigurationCandidatePreviewClient
{
    /// <summary>Previews one exact raw draft over pinned baseline references under original source/map preconditions.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationCandidatePreview>> PreviewCandidateAsync(
        ManagementConfigurationCandidatePreviewCommand command, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

