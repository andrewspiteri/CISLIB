using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend : IManagementConfigurationCreationBackend
{
    public Task<ManagementStoreResult<ManagementConfigurationCreationContext>> ReadCreationContextAsync(string applicationId, CancellationToken token = default)
        => store.ReadConfigurationCreationContextAsync(applicationId, token);
    public Task<ManagementConfigurationResponse> CreateConfigurationAsync(ManagementApplication application, string key, string ifMatch,
        ManagementConfigurationCreationCommand command, CancellationToken token = default)
        => configuration.CreateConfigurationAsync(application, key, ifMatch, command, token);
    public Task<ManagementConfigurationResponse> LookupCreationAsync(ManagementApplication application, string key, CancellationToken token = default)
        => configuration.LookupCreationAsync(application, key, token);
    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationSummaryCandidate>>> ReadSummaryCandidatesAsync(string applicationId,
        string? after, int take, CancellationToken token = default)
        => store.ReadConfigurationSummaryCandidatesAsync(applicationId, after, take, token);
}
