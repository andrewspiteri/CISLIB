namespace CIS.Configuration;

// Full selected-setting validation produces local immutable state for later publication.
internal static class ConfigurationValidator
{
    public static IReadOnlyList<ConfigurationIssue> CollectIdentityAndOptionIssues(ConfigurationResolution resolution) => CollectIssues(resolution, null);

    private static IReadOnlyList<ConfigurationIssue> CollectIssues(ConfigurationResolution resolution, ConfigurationFilePathResolution? path)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var issues = new List<ConfigurationIssue>();
        ConfigurationIdentityValidator.AddSelected(issues, resolution.ApplicationName, 255, applicationName: true);
        ConfigurationIdentityValidator.AddSelected(issues, resolution.EnvironmentName, 255);
        ConfigurationIdentityValidator.AddSelected(issues, resolution.ServiceVersion, 512);
        IReadOnlyList<ConfigurationIssue> options = ConfigurationOptionValidator.Validate(resolution);
        foreach (IParsedConfigurationSetting setting in resolution.Settings.Skip(3))
        {
            if (setting.Key == "CORE_LOG_FILE_PATH" && path?.Issue is { } pathIssue)
            {
                issues.Add(pathIssue);
            }
            issues.AddRange(options.Where(issue => issue.Code != ConfigurationIssueCodes.InvalidEnvironmentKind && issue.Key == setting.SourceKey));
        }
        ConfigurationIdentityValidator.AddMetadata(issues, resolution.BuildVersion, "BuildVersion");
        ConfigurationIdentityValidator.AddMetadata(issues, resolution.CommitId, "CommitId", optional: true);
        ConfigurationIdentityValidator.AddMetadata(issues, resolution.BuildId, "BuildId", optional: true);
        issues.AddRange(options.Where(issue => issue.Code == ConfigurationIssueCodes.InvalidEnvironmentKind));
        return Array.AsReadOnly(issues.ToArray());
    }

    public static void ThrowIfIdentityOrOptionsInvalid(ConfigurationResolution resolution)
    {
        IReadOnlyList<ConfigurationIssue> issues = CollectIdentityAndOptionIssues(resolution);
        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }
    }

    public static ValidatedConfiguration Validate(ConfigurationResolution resolution, InitializationWorkingDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(directory);
        ConfigurationFilePathResolution path = ConfigurationFilePathResolution.Resolve(resolution.FilePath, directory);
        IReadOnlyList<ConfigurationIssue> issues = CollectIssues(resolution, path);
        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }
        return new ValidatedConfiguration(resolution, directory, path.AbsolutePath);
    }

}
