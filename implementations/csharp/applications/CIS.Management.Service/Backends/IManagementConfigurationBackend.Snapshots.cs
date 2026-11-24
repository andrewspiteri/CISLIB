using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

public partial interface IManagementConfigurationBackend
{
    /// <summary>Reads the live native owner's frozen target and coherent pinned pair from the original authority.</summary>
    Task<ManagementStoreResult<ManagementResolvedConfiguration>> ReadRegistrationConfigurationAsync(ManagementRegistrationOwner owner,
        string registrationId, string configurationId, string schemaVersion, string? ifNoneMatch, CancellationToken token = default);
}

/// <summary>A completed authoritative read; conditional success never substitutes for a control confirmation.</summary>
public sealed record ManagementResolvedConfiguration(bool NotModified, ManagementConfigurationSnapshot? Snapshot, string ETag);
