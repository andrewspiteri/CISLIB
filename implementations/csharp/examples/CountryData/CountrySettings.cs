using CIS.Configuration;

namespace CIS.Examples.CountryData;

// Captured consistently for the catalog lifetime; changes require application restart.
public sealed class CountrySettings : ConfigurationObject
{
    public string SourceUrl { get; set; } = "https://countriesnow.space/api/v0.1/countries/iso";
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public string? RetryPolicyName { get; set; } = "transient-http";
}
