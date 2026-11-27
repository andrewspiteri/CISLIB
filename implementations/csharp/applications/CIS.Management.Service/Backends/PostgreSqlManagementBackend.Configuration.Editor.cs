using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementConfigurationEditorBackend
{
    public Task<ManagementStoreResult<ManagementConfigurationEditorBindingResource>> ReadEditorBindingAsync(
        string applicationId, string configurationId, string schemaVersion, CancellationToken token = default)
        => store.ReadConfigurationEditorBindingAsync(applicationId, configurationId, schemaVersion, token);
}

