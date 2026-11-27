namespace CIS.Management;

/// <summary>Optional exact binding release-history and adoption reads; existing compulsory ports remain unchanged.</summary>
public interface IManagementConfigurationBindingReadClient
{
    /// <summary>Reads retained immutable releases in bounded creation-time/UUID descending pages.</summary>
    Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationRelease>>> GetReleasesAsync(
        string applicationId, string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads adoption restricted to this exact application/configuration/schema before paging.</summary>
    Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationAdoptionEntry>>> GetBindingStatusAsync(
        string applicationId, string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

