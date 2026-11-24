using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>Independent finite settings for a configuration client borrowing one native REST owner.</summary>
public sealed class ManagementConfigurationClientOptions
{
    /// <summary>Selects a dedicated existing REST alias; construction performs no work.</summary>
    public ManagementConfigurationClientOptions(string restClientName)
    {
        if (!ManagementWire.IsScalarText(restClientName, 64))
        {
            throw new ArgumentException("Select one bounded native REST alias.", nameof(restClientName));
        }
        RestClientName = restClientName;
    }

    /// <summary>Gets the dedicated native and typed configuration alias.</summary>
    public string RestClientName
    {
        get;
    }
    /// <summary>Gets or sets the independent HTTPS endpoint; omission selects CIS_MANAGEMENT_URL.</summary>
    public string? BaseUrl
    {
        get; set;
    }
    /// <summary>Gets or sets the entry-through-cleanup budget, initially thirty seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets configuration-only finite bounds, without widening ordinary Management requests.</summary>
    public ManagementConfigurationWireLimits WireLimits { get; set; } = new();
    /// <summary>Gets or sets an explicitly registered conservative GET policy; omission means no retry.</summary>
    public RestRetryPolicy? ReadRetryPolicy
    {
        get; set;
    }
}

internal sealed record ManagementConfigurationClientSnapshot(string RestClientName, Uri BaseUri, TimeSpan Timeout,
    ManagementConfigurationWireLimits WireLimits, RestRetryPolicy? ReadRetryPolicy)
{
    internal static ManagementConfigurationClientSnapshot Capture(ManagementConfigurationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.WireLimits);
        // Reuse the established endpoint/prefix and finite read-policy rules.
        ManagementClientSnapshot endpoint = ManagementClientSnapshot.Capture(new(options.RestClientName)
        {
            BaseUrl = options.BaseUrl,
            Timeout = options.Timeout,
            ReadRetryPolicy = options.ReadRetryPolicy
        });
        return new(endpoint.RestClientName, endpoint.BaseUri, endpoint.Timeout, options.WireLimits, endpoint.ReadRetryPolicy);
    }
}
