namespace CIS.Configuration;

// Selected options/identity diagnostics; path syntax joins this pipeline in T03.
internal static class ConfigurationValidator
{
    public static IReadOnlyList<ConfigurationIssue> CollectIssues(ConfigurationResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var issues = new List<ConfigurationIssue>();
        ConfigurationIdentityValidator.AddSelected(issues, resolution.ApplicationName, 255, applicationName: true);
        ConfigurationIdentityValidator.AddSelected(issues, resolution.EnvironmentName, 255);
        ConfigurationIdentityValidator.AddSelected(issues, resolution.ServiceVersion, 512);
        IReadOnlyList<ConfigurationIssue> options = ConfigurationOptionValidator.Validate(resolution);
        issues.AddRange(options.Where(issue => issue.Code != ConfigurationIssueCodes.InvalidEnvironmentKind));
        ConfigurationIdentityValidator.AddMetadata(issues, resolution.BuildVersion, "BuildVersion");
        ConfigurationIdentityValidator.AddMetadata(issues, resolution.CommitId, "CommitId", optional: true);
        ConfigurationIdentityValidator.AddMetadata(issues, resolution.BuildId, "BuildId", optional: true);
        issues.AddRange(options.Where(issue => issue.Code == ConfigurationIssueCodes.InvalidEnvironmentKind));
        return Array.AsReadOnly(issues.ToArray());
    }

    public static void ThrowIfInvalid(ConfigurationResolution resolution)
    {
        IReadOnlyList<ConfigurationIssue> issues = CollectIssues(resolution);
        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }
    }
}
