using CIS.Text;

namespace CIS.Configuration;

internal static class ConfigurationIdentityValidator
{
    public static bool IsValid(string value, int maximumScalars) =>
        !string.IsNullOrWhiteSpace(value) && UnicodeScalarText.IsValid(value, maximumScalars, allowControls: false);

    public static void AddSelected(List<ConfigurationIssue> issues, ParsedConfigurationSetting<string> setting, int maximumScalars, bool applicationName = false)
    {
        if (!setting.HasValue)
        {
            issues.Add(applicationName
                ? new(ConfigurationIssueCodes.MissingApplicationName, "CORE_SERVICE_NAME", "Supply an application name or an application assembly with a discoverable name.")
                : new(ConfigurationIssueCodes.InvalidIdentity, setting.SourceKey, "Supply a nonblank identity value."));
        }
        else if (!IsValid(setting.Value, maximumScalars))
        {
            issues.Add(Invalid(setting.SourceKey, maximumScalars));
        }
    }

    public static void AddMetadata(List<ConfigurationIssue> issues, string? value, string key, bool optional = false)
    {
        if (optional && value is null)
        {
            return;
        }
        if ((value is null || !IsValid(value, 512)) && !issues.Any(issue => issue.Key == key && issue.Code == ConfigurationIssueCodes.InvalidIdentity))
        {
            issues.Add(Invalid(key, 512));
        }
    }

    private static ConfigurationIssue Invalid(string key, int maximumScalars) =>
        new(ConfigurationIssueCodes.InvalidIdentity, key, maximumScalars == 255
            ? "Use a nonblank identity of at most 255 Unicode scalars, without control characters or malformed Unicode."
            : "Use a nonblank identity of at most 512 Unicode scalars, without control characters or malformed Unicode.");
}
