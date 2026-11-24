using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend
{
    public Task<ManagementStoreResult<ManagementResolvedConfiguration>> ReadRegistrationConfigurationAsync(ManagementRegistrationOwner owner,
        string registrationId, string configurationId, string schemaVersion, string? ifNoneMatch, CancellationToken token = default)
        => store.ReadRegistrationConfigurationAsync(owner, registrationId, configurationId, schemaVersion, ifNoneMatch, token);
}
