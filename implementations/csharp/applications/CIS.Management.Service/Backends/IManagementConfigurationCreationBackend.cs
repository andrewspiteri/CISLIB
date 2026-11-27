using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional application-scoped generic preparation and bounded assigned-origin metadata.</summary>
public interface IManagementConfigurationCreationBackend
{
    Task<ManagementStoreResult<ManagementConfigurationCreationContext>> ReadCreationContextAsync(string applicationId, CancellationToken token = default);
    Task<ManagementConfigurationResponse> CreateConfigurationAsync(ManagementApplication application, string key, string ifMatch,
        ManagementConfigurationCreationCommand command, CancellationToken token = default);
    Task<ManagementConfigurationResponse> LookupCreationAsync(ManagementApplication application, string key, CancellationToken token = default);
    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationSummaryCandidate>>> ReadSummaryCandidatesAsync(string applicationId,
        string? after, int take, CancellationToken token = default);
}

/// <summary>Native metadata and the exact referenced release identities to authorize before disclosing assigned origin.</summary>
public sealed record ManagementConfigurationSummaryCandidate(ManagementConfigurationSummary Summary, IReadOnlyList<string> ReleaseIds)
{
    /// <summary>Gets the actual source whose publication time requires independent metadata authorization.</summary>
    public string? ModifiedSourceId
    {
        get; init;
    }
}
