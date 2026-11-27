using CIS.RestClient;

namespace CIS.Management;

public sealed partial class ManagementConfigurationClient
{
    /// <inheritdoc />
    public async Task<ManagementConfigurationCallResult<ManagementConfigurationEditorContext>> GetBindingEditorContextAsync(
        string applicationId, string configurationId, string schemaVersion, string source,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        long entered = clock.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        if (source is not ("common" or "application"))
        {
            throw new ArgumentException("Select the exact common or application source.", nameof(source));
        }
        string path = Binding(applicationId, configurationId) + "/editor-context";
        var mapper = new ConfigurationMapper(settings.WireLimits,
            responseBinding: new(path, schemaVersion, null, null, source));
        RestResult<ManagementConfigurationEditorContext> exchange = await SendAsync<ManagementConfigurationEditorContext>(
            entered, "GET", path, schemaVersion, null, null, null, options, mapper, null, cancellationToken,
            editorSource: source).ConfigureAwait(false);
        return new(exchange, FinalProblem(exchange, mapper), null);
    }
}

