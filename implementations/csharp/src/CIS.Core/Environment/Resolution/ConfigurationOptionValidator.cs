using System.Globalization;

namespace CIS.Configuration;

internal static class ConfigurationOptionValidator
{
    // This validates logging options and setup kind, not identity or path syntax.
    public static IReadOnlyList<ConfigurationIssue> Validate(ConfigurationResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var problems = new Dictionary<string, ConfigurationIssue>(StringComparer.Ordinal);
        foreach (IParsedConfigurationSetting setting in resolution.Settings)
        {
            if (!setting.Key.StartsWith("CORE_LOG_", StringComparison.Ordinal) || setting.Key == "CORE_LOG_FILE_PATH")
            {
                continue;
            }
            ConfigurationIssue? problem = ParseProblem(setting);
            if (problem is not null)
            {
                problems.Add(setting.Key, problem);
            }
        }
        AddRangeProblems(resolution, problems);
        AddFileSizeRelationshipProblem(resolution, problems);
        var ordered = resolution.Settings.Where(setting => problems.ContainsKey(setting.Key)).Select(setting => problems[setting.Key]).ToList();
        if (!Enum.IsDefined(resolution.EnvironmentKind))
        {
            ordered.Add(new ConfigurationIssue(ConfigurationIssueCodes.InvalidEnvironmentKind, "EnvironmentSetup.EnvironmentKind", "Use a defined EnvironmentKind value."));
        }
        return Array.AsReadOnly(ordered.ToArray());
    }

    private static ConfigurationIssue? ParseProblem(IParsedConfigurationSetting setting) => setting.State switch
    {
        ConfigurationParseState.InvalidToken => new(ConfigurationIssueCodes.InvalidToken, setting.SourceKey, "Use a supported token or an invariant decimal integer for this setting."),
        ConfigurationParseState.OutOfRange => new(ConfigurationIssueCodes.OutOfRange, setting.SourceKey, "The selected integer exceeds its declared numeric domain."),
        ConfigurationParseState.UnsupportedSchemaVersion => new(ConfigurationIssueCodes.UnsupportedSchemaVersion, setting.SourceKey, "Only schema version 1 is supported."),
        ConfigurationParseState.Absent => new(ConfigurationIssueCodes.InvalidToken, setting.SourceKey, "A required logging option has no selected value."),
        // An inherited failure is already reported on its selected parent.
        ConfigurationParseState.DependsOnInvalidSetting or ConfigurationParseState.Parsed => null,
        _ => throw new InvalidOperationException("Unknown configuration parse state.")
    };

    private static void AddRangeProblems(ConfigurationResolution resolution, Dictionary<string, ConfigurationIssue> problems)
    {
        foreach (ConfigurationIntegerRange range in ConfigurationIntegerRanges.All)
        {
            IParsedConfigurationSetting setting = resolution.Settings.Single(setting => setting.Key == range.Key);
            if (setting.State == ConfigurationParseState.Parsed)
            {
                long value = NumericValue(resolution, range.Key);
                if (value < range.Minimum || value > range.Maximum)
                {
                    string explanation = string.Create(CultureInfo.InvariantCulture, $"Use an integer between {range.Minimum} and {range.Maximum}, inclusive.");
                    problems.Add(range.Key, new ConfigurationIssue(ConfigurationIssueCodes.OutOfRange, setting.SourceKey, explanation));
                }
            }
        }
    }

    private static void AddFileSizeRelationshipProblem(ConfigurationResolution resolution, Dictionary<string, ConfigurationIssue> problems)
    {
        if (resolution.FileEnabled == true && resolution.FileMaxBytes.HasValue && resolution.MaxEntryBytes.HasValue
            && !problems.ContainsKey("CORE_LOG_FILE_MAX_BYTES") && !problems.ContainsKey("CORE_LOG_MAX_ENTRY_BYTES")
            && resolution.FileMaxBytes.Value < resolution.MaxEntryBytes.Value)
        {
            problems.Add("CORE_LOG_FILE_MAX_BYTES", new ConfigurationIssue(ConfigurationIssueCodes.OutOfRange, resolution.FileMaxBytes.SourceKey,
                "An enabled file output requires FileMaxBytes to be at least MaxEntryBytes."));
        }
    }

    private static long NumericValue(ConfigurationResolution resolution, string key) => key switch
    {
        "CORE_LOG_QUEUE_CAPACITY" => resolution.QueueCapacity.Value,
        "CORE_LOG_FLUSH_INTERVAL_MS" => resolution.FlushIntervalMilliseconds.Value,
        "CORE_LOG_SHUTDOWN_TIMEOUT_MS" => resolution.ShutdownTimeoutMilliseconds.Value,
        "CORE_LOG_FILE_MAX_BYTES" => resolution.FileMaxBytes.Value,
        "CORE_LOG_FILE_RETAINED_FILES" => resolution.FileRetainedFiles.Value,
        "CORE_LOG_SCHEMA_VERSION" => resolution.SchemaVersion.Value,
        "CORE_LOG_MAX_ENTRY_BYTES" => resolution.MaxEntryBytes.Value,
        _ => throw new InvalidOperationException("Unknown integer setting.")
    };
}
