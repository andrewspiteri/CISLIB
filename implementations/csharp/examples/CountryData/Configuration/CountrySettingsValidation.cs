using CIS.Heartbeat;
using CIS.RestClient;

namespace CIS.Examples.CountryData;

// One rule set for the catalog's captured input and centrally staged detached candidates.
internal static class CountrySettingsValidation
{
    internal static RestRetrySelection Validate(string source, TimeSpan interval, TimeSpan timeout, string? policyName,
        IEnumerable<RestRetryPolicy> retryPolicies)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Country SourceUrl must be an absolute HTTP(S) URL without user information.");
        }
        if (interval <= TimeSpan.Zero || interval > HeartbeatRegistrationOptions.MaximumDuration
            || timeout <= TimeSpan.Zero || timeout > HeartbeatRegistrationOptions.MaximumDuration)
        {
            throw new ArgumentException("Country refresh and request durations must be positive supported timer durations.");
        }
        if (policyName is not null && !retryPolicies.Any(policy => policy.Name == policyName))
        {
            throw new ArgumentException("The configured country retry policy is not registered.");
        }
        return policyName is null ? RestRetrySelection.None : RestRetrySelection.Named(policyName);
    }
}
