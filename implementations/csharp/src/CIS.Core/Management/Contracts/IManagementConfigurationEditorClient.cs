namespace CIS.Management;

/// <summary>Optional exact binding-editor read; existing Configuration clients acquire no compulsory member.</summary>
public interface IManagementConfigurationEditorClient
{
    /// <summary>Reads one exact visible binding and one selected source's independently authorized declared defaults.</summary>
    Task<ManagementConfigurationCallResult<ManagementConfigurationEditorContext>> GetBindingEditorContextAsync(
        string applicationId, string configurationId, string schemaVersion, string source,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

