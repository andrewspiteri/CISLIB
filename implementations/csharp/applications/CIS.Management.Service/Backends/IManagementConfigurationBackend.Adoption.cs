using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

public partial interface IManagementConfigurationBackend
{
    Task<ManagementStoreResult<ManagementReportReceipt>> ReportConfigurationStatusAsync(ManagementRegistrationOwner owner,
        string registrationId, ManagementConfigurationStatusReport report, CancellationToken token = default);

    Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>>> ReadConfigurationAdoptionAsync(
        string applicationId, string? after, int take, CancellationToken token = default);
}

/// <summary>A bounded internal-service page position and its payload-free current projection.</summary>
public sealed record ManagementConfigurationAdoptionRow(string Position, ManagementConfigurationAdoptionEntry Entry);
