using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>Passive options for a typed client borrowing one explicitly selected native REST owner.</summary>
public sealed class ManagementClientOptions
{
    /// <summary>Creates configuration for the existing keyed REST alias.</summary>
    public ManagementClientOptions(string restClientName)
    {
        if (!ManagementWire.IsScalarText(restClientName, 64))
        {
            throw new ArgumentException("Supply a bounded existing REST client alias.", nameof(restClientName));
        }
        RestClientName = restClientName;
    }

    /// <summary>Gets the exact existing keyed REST alias and keyed management alias.</summary>
    public string RestClientName
    {
        get;
    }

    /// <summary>Gets or sets the HTTPS bootstrap endpoint; an explicit value precedes CIS_MANAGEMENT_URL.</summary>
    public string? BaseUrl
    {
        get; set;
    }

    /// <summary>Gets or sets the finite entry-to-settlement request budget, initially thirty seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets immutable finite wire and catalog limits.</summary>
    public ManagementWireLimits WireLimits { get; set; } = new();

    /// <summary>Gets or sets an explicitly registered native read policy; omission means no retry.</summary>
    public RestRetryPolicy? ReadRetryPolicy
    {
        get; set;
    }
}

internal sealed record ManagementClientSnapshot(string RestClientName, Uri BaseUri, TimeSpan Timeout,
    ManagementWireLimits WireLimits, RestRetryPolicy? ReadRetryPolicy)
{
    internal static ManagementClientSnapshot Capture(ManagementClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.WireLimits);
        RestContractRules.Timeout(options.Timeout, nameof(options.Timeout));
        string? configured = options.BaseUrl ?? Environment.GetEnvironmentVariable("CIS_MANAGEMENT_URL");
        if (configured is null || configured.Length > 4096 || configured.Any(char.IsControl)
            || !Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || configured.Contains('\\'))
        {
            throw new ArgumentException("Configure an absolute HTTPS management endpoint without credentials, query or fragment.", nameof(options));
        }
        // Reject raw or escaped dot/separator segments before native URI canonicalization
        // could silently change the configured path prefix.
        int authority = configured.IndexOf("://", StringComparison.Ordinal);
        int pathStart = configured.IndexOf('/', authority + 3);
        if (pathStart >= 0)
        {
            foreach (string segment in configured[pathStart..].Split('/'))
            {
                string decoded = Uri.UnescapeDataString(segment);
                if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\'))
                {
                    throw new ArgumentException("The configured management base path contains an ambiguous segment.", nameof(options));
                }
            }
        }
        ManagementRetryRules.Validate(options.ReadRetryPolicy);
        string baseUri = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new(options.RestClientName, new Uri(baseUri, UriKind.Absolute),
            options.Timeout, options.WireLimits, options.ReadRetryPolicy);
    }
}

internal static class ManagementRetryRules
{
    internal static void Validate(RestRetryPolicy? policy)
    {
        if (policy is null)
        {
            return;
        }
        if (policy.MaxRetries > 2 || policy.Methods.Any(method => method is not ("GET" or "HEAD" or "OPTIONS"))
            || policy.StatusCodes.Any(status => status is not (408 or 429 or 502 or 503 or 504))
            || policy.FailureKinds.Count != 0 || policy.ErrorCodes.Count != 0 || policy.TransportFailures.Count != 0
            || policy.RetryBodyFailures || policy.MaxLocalDelay > TimeSpan.FromSeconds(5) || !policy.HonorRetryAfter)
        {
            throw new ArgumentException("Management reads support at most two explicit conservative transient HTTP retries.", nameof(policy));
        }
    }
}
