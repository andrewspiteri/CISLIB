namespace CIS.Management;

public sealed partial class ManagementConfigurationClient : IManagementConfigurationCreationClient
{
    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationCreationContext>> GetCreationContextAsync(string applicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationCreationContext>("GET", "applications/" + Uuid(applicationId) + "/configuration-creations/context", options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> CreateConfigurationAsync(string applicationId,
        string operationKey, string ifMatch, ManagementConfigurationCreationCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("POST", "applications/" + Uuid(applicationId) + "/configuration-creations", body: command,
            headers: new(operationKey, ifMatch), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetCreationOperationAsync(string applicationId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementConfigurationOperationResult>("GET", "applications/" + Uuid(applicationId) + "/configuration-creation-operations/" + Identifier(operationKey),
            headers: new(operationKey, null, false), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationSummary>>> GetConfigurationSummariesAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementConfigurationSummary>>("GET", "applications/" + Uuid(applicationId) + "/configuration-summaries",
            page: page ?? new(), options: options, token: cancellationToken);
}
