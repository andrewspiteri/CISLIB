using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

internal sealed partial class PostgreSqlManagementBackend
{
    public Task<ManagementStoreResult<ManagementReportReceipt>> ReportConfigurationStatusAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementConfigurationStatusReport report, CancellationToken token = default)
        => store.ReportConfigurationStatusAsync(owner, registrationId, report, token);

    public Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadConfigurationAdoptionAsync(
        string applicationId, string? after, int take, CancellationToken token = default)
        => store.ReadConfigurationAdoptionAsync(applicationId, after, take, token);
}
