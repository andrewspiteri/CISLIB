using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend
{
    public Task<ManagementStoreResult<ManagementConfigurationBindingResource>> ReadBindingAsync(string applicationId, string configurationId,
        string schemaVersion, CancellationToken token = default) => store.ReadConfigurationBindingAsync(applicationId, configurationId, schemaVersion, token);
    public Task<ManagementStoreResult<ManagementConfigurationRelease>> ReadReleaseAsync(string releaseId, CancellationToken token = default)
        => store.ReadConfigurationReleaseAsync(releaseId, token);
    public Task<ManagementStoreResult<ManagementConfigurationAssignmentMap>> ReadAssignmentsAsync(ManagementConfigurationBindingResource binding,
        CancellationToken token = default) => store.ReadConfigurationAssignmentsAsync(binding, token);
    public Task<ManagementConfigurationResponse> CreateReleaseAsync(ManagementConfigurationBindingResource binding, string key, string ifMatch,
        ManagementConfigurationReleaseCommand command, CancellationToken token = default)
        => configuration.CreateReleaseAsync(binding, key, ifMatch, command, token);
    public Task<ManagementConfigurationResponse> ReplaceAssignmentsAsync(ManagementConfigurationBindingResource binding, string key, string ifMatch,
        ManagementConfigurationAssignmentCommand command, CancellationToken token = default)
        => configuration.ReplaceAssignmentsAsync(binding, key, ifMatch, command, token);
    public Task<ManagementConfigurationResponse> LookupReleaseAsync(ManagementConfigurationBindingResource binding, string key, CancellationToken token = default)
        => configuration.LookupReleaseAsync(binding, key, token);
    public Task<ManagementConfigurationResponse> LookupAssignmentAsync(ManagementConfigurationBindingResource binding, string key, CancellationToken token = default)
        => configuration.LookupAssignmentAsync(binding, key, token);
    public Task<ManagementStoreResult<ManagementConfigurationPreview>> PreviewAsync(ManagementConfigurationBindingResource binding,
        ManagementConfigurationPreviewCommand command, string? after, int take, bool includeValues, string? expectedMapETag,
        CancellationToken token = default) => store.PreviewConfigurationAsync(binding, command, after, take, includeValues, expectedMapETag, token);
}
