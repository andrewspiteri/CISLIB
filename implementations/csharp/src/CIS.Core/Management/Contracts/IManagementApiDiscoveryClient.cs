namespace CIS.Management;

/// <summary>Optional complete installed-API discovery. Legacy client implementations need not implement this port.</summary>
public interface IManagementApiDiscoveryClient
{
    /// <summary>Reads a complete-v1 page after checking the service's advertised API discovery version.</summary>
    Task<ManagementCallResult<ManagementServiceFeaturePage>> ListServiceFeaturesAsync(
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads advisory current eligibility for one application; each operation still authorizes its actual resource.</summary>
    Task<ManagementCallResult<ManagementApplicationApiCapabilityPage>> ListApplicationApiCapabilitiesAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>A bounded page of the complete installed API inventory, distinct from the closed legacy-v1 projection.</summary>
public sealed record ManagementServiceFeaturePage(string Profile, IReadOnlyList<ManagementFeature> Items, string? NextCursor = null);

/// <summary>One installed operation and an advisory native authorization observation, never an execution permit.</summary>
public sealed record ManagementApiCapability(ManagementFeature Feature, bool Allowed);

/// <summary>A complete-v1 API page bound to the exact visible application.</summary>
public sealed record ManagementApplicationApiCapabilityPage(string ApplicationId, string Profile,
    IReadOnlyList<ManagementApiCapability> Items, string? NextCursor = null);
