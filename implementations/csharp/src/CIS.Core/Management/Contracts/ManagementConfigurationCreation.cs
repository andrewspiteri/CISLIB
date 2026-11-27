namespace CIS.Management;

/// <summary>One authorized application's generic-JSON preparation context; it grants no publication or activation.</summary>
public sealed record ManagementConfigurationCreationContext(string ApplicationId, string Application, string Environment,
    string ETag, string SchemaVersion, string HostApplicationName);

/// <summary>Declares only the server's fixed generic object schema; payload publication is a separate original operation.</summary>
public sealed record ManagementConfigurationCreationCommand(int CommandVersion, string ConfigurationId, string Intent, string Reason);

/// <summary>One payload-free binding summary derived from actual assigned immutable references.</summary>
public sealed record ManagementConfigurationSummary(string ApplicationId, string Environment, string ConfigurationId,
    string SchemaVersion, string SourceId, string BindingId, string HostApplicationName, string Origin,
    bool HasCohortOverrides, string? Description = null)
{
    /// <summary>Gets the default-assigned selected layer's immutable publication time, when currently disclosed.</summary>
    public string? ModifiedAtUtc
    {
        get; init;
    }
}

/// <summary>Optional predeployment preparation and bounded assigned-origin discovery; existing clients remain valid.</summary>
public interface IManagementConfigurationCreationClient
{
    /// <summary>Reads the current native creation precondition for one actual application.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationCreationContext>> GetCreationContextAsync(string applicationId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Declares a generic definition/binding/source once, without publishing, assigning or enrolling a consumer.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> CreateConfigurationAsync(string applicationId,
        string operationKey, string ifMatch, ManagementConfigurationCreationCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Looks up the same native actor's exact original without application-read or definition prefetch.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationOperationResult>> GetCreationOperationAsync(string applicationId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads one bounded authorized metadata-only page; a protected origin is never guessed from history.</summary>
    Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationSummary>>> GetConfigurationSummariesAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}
