namespace CIS.Management;

public sealed partial class ManagementConfigurationClient : IManagementConfigurationBindingReadClient
{
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationRelease>>> GetReleasesAsync(
        string applicationId, string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementConfigurationRelease>>("GET", Binding(applicationId, configurationId) + "/releases",
            schemaVersion, page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>>> GetBindingStatusAsync(
        string applicationId, string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementConfigurationAdoptionEntry>>("GET", Binding(applicationId, configurationId) + "/status",
            schemaVersion, page: page ?? new(), options: options, token: cancellationToken);
}

