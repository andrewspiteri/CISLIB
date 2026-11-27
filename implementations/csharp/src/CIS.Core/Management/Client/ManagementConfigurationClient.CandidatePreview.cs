namespace CIS.Management;

public sealed partial class ManagementConfigurationClient : IManagementConfigurationCandidatePreviewClient
{
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationCandidatePreview>> PreviewCandidateAsync(
        ManagementConfigurationCandidatePreviewCommand command, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return CallAsync<ManagementConfigurationCandidatePreview>("POST", "configuration/candidate-preview",
            body: command, page: page ?? new(), options: options, token: cancellationToken);
    }
}

