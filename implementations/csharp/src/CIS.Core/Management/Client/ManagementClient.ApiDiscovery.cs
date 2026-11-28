using CIS.Management.Wire;

namespace CIS.Management;

public sealed partial class ManagementClient
{
    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementServiceFeaturePage>> ListServiceFeaturesAsync(
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementServiceFeaturePage>("GET", "service-features", "service-features", null, null,
            null, ApiDiscoveryPage(page), null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementApplicationApiCapabilityPage>> ListApplicationApiCapabilitiesAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementApplicationApiCapabilityPage>("GET", "api-capabilities", "applications", applicationId,
            "api-capabilities", null, ApiDiscoveryPage(page), null, options, cancellationToken);

    private static ManagementPageRequest ApiDiscoveryPage(ManagementPageRequest? page)
    {
        ManagementPageRequest selected = page ?? new(32);
        if (!ManagementWire.IsPageRequest(selected) || selected.PageSize > 64)
        {
            throw new ArgumentException("Complete API discovery uses one through 64 rows, default 32.", nameof(page));
        }
        return selected;
    }
}
