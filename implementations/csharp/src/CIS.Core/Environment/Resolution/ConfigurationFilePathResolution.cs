namespace CIS.Configuration;

internal sealed class ConfigurationFilePathResolution
{
    private ConfigurationFilePathResolution(string? absolutePath, ConfigurationIssue? issue)
    {
        AbsolutePath = absolutePath;
        Issue = issue;
    }

    public string? AbsolutePath { get; }
    public ConfigurationIssue? Issue { get; }

    public static ConfigurationFilePathResolution Resolve(ParsedConfigurationSetting<string> selected, InitializationWorkingDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(directory);
        if (selected.State == ConfigurationParseState.Absent)
        {
            return new ConfigurationFilePathResolution(null, null);
        }
        if (selected.HasValue)
        {
            try
            {
                return new ConfigurationFilePathResolution(LexicalFilePathNormalizer.Resolve(selected.Value, directory.Value), null);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // These describe selected path syntax. Never copy platform exception text.
            }
        }
        return new ConfigurationFilePathResolution(null, new ConfigurationIssue(ConfigurationIssueCodes.InvalidPath, selected.SourceKey,
            "Use a path that can be resolved syntactically against the captured initialization directory."));
    }
}
