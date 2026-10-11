using CIS.Logging.Configuration;

namespace CIS.Configuration;

internal static class ConfigurationResolver
{
    public static ConfigurationResolution Resolve(EnvironmentConfigurationInput setup, ApplicationIdentity application, CapturedEnvironment variables)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(variables);
        LoggingConfigurationInput logging = setup.Logging ?? new LoggingConfigurationInput();
        var name = ConfigurationSettingParser.Text("CORE_SERVICE_NAME", ConfigurationValueSelector.Select(variables, "CORE_SERVICE_NAME",
            new ConfigurationCandidate<string>(setup.ApplicationName), new ConfigurationCandidate<string>(application.ApplicationName)));
        var environment = ConfigurationSettingParser.Text("CORE_ENVIRONMENT", ConfigurationValueSelector.Select(variables, "CORE_ENVIRONMENT",
            new ConfigurationCandidate<string>(setup.EnvironmentName), fallback: new ConfigurationCandidate<string>("unknown")));
        var version = ConfigurationSettingParser.Text("CORE_SERVICE_VERSION", ConfigurationValueSelector.Select(variables, "CORE_SERVICE_VERSION",
            new ConfigurationCandidate<string>(setup.ServiceVersion), new ConfigurationCandidate<string>(application.BuildVersion)));
        var level = ConfigurationSettingParser.Severity("CORE_LOG_LEVEL", ConfigurationValueSelector.Select(variables, "CORE_LOG_LEVEL",
            Candidate(logging.Level), fallback: new ConfigurationCandidate<ConfigurationSeverity>(ConfigurationSeverity.Information)));
        var consoleLevel = OutputLevel(variables, "CORE_LOG_CONSOLE_LEVEL", logging.ConsoleLevel, level);
        var fileLevel = OutputLevel(variables, "CORE_LOG_FILE_LEVEL", logging.FileLevel, level);

        return new ConfigurationResolution(
            application.BuildVersion, name, environment, version, level,
            ConfigurationSettingParser.Format("CORE_LOG_CONSOLE_FORMAT", Select(variables, "CORE_LOG_CONSOLE_FORMAT", logging.ConsoleFormat, ConfigurationOutputFormat.Human)),
            consoleLevel,
            ConfigurationSettingParser.Stream("CORE_LOG_CONSOLE_STREAM", Select(variables, "CORE_LOG_CONSOLE_STREAM", logging.ConsoleStream, ConfigurationConsoleStream.StandardError)),
            ConfigurationSettingParser.Detail("CORE_LOG_HUMAN_DETAIL", Select(variables, "CORE_LOG_HUMAN_DETAIL", logging.HumanDetail, ConfigurationHumanDetail.Normal)),
            ConfigurationSettingParser.Text("CORE_LOG_FILE_PATH", ConfigurationValueSelector.Select(variables, "CORE_LOG_FILE_PATH", new ConfigurationCandidate<string>(logging.FilePath))),
            ConfigurationSettingParser.Format("CORE_LOG_FILE_FORMAT", Select(variables, "CORE_LOG_FILE_FORMAT", logging.FileFormat, ConfigurationOutputFormat.JsonLines)),
            fileLevel,
            ConfigurationSettingParser.Integer("CORE_LOG_QUEUE_CAPACITY", Select(variables, "CORE_LOG_QUEUE_CAPACITY", logging.QueueCapacity, 1024)),
            ConfigurationSettingParser.Integer("CORE_LOG_FLUSH_INTERVAL_MS", Select(variables, "CORE_LOG_FLUSH_INTERVAL_MS", logging.FlushIntervalMilliseconds, 1000)),
            ConfigurationSettingParser.Integer("CORE_LOG_SHUTDOWN_TIMEOUT_MS", Select(variables, "CORE_LOG_SHUTDOWN_TIMEOUT_MS", logging.ShutdownTimeoutMilliseconds, 5000)),
            ConfigurationSettingParser.LongInteger("CORE_LOG_FILE_MAX_BYTES", Select(variables, "CORE_LOG_FILE_MAX_BYTES", logging.FileMaxBytes, 10485760L)),
            ConfigurationSettingParser.Integer("CORE_LOG_FILE_RETAINED_FILES", Select(variables, "CORE_LOG_FILE_RETAINED_FILES", logging.FileRetainedFiles, 10)),
            ConfigurationSettingParser.Schema("CORE_LOG_SCHEMA_VERSION", Select(variables, "CORE_LOG_SCHEMA_VERSION", logging.SchemaVersion, 1)),
            ConfigurationSettingParser.Integer("CORE_LOG_MAX_ENTRY_BYTES", Select(variables, "CORE_LOG_MAX_ENTRY_BYTES", logging.MaxEntryBytes, 65536)),
            setup.EnvironmentKind ?? ConfigurationEnvironmentKind.Unknown, application.CommitId, application.BuildId);
    }

    private static ConfigurationCandidate<T> Candidate<T>(T? value) where T : struct =>
        value.HasValue ? new ConfigurationCandidate<T>(value.Value) : default;

    private static ConfigurationSelection<T> Select<T>(CapturedEnvironment variables, string key, T? setup, T fallback) where T : struct =>
        ConfigurationValueSelector.Select(variables, key, Candidate(setup), fallback: new ConfigurationCandidate<T>(fallback));

    private static ParsedConfigurationSetting<ConfigurationSeverity> OutputLevel(CapturedEnvironment variables, string key,
        ConfigurationSeverity? setup, ParsedConfigurationSetting<ConfigurationSeverity> fallback)
    {
        ConfigurationSelection<ConfigurationSeverity> selected = ConfigurationValueSelector.Select(variables, key, Candidate(setup));
        return selected.Source == ConfigurationSource.Absent
            ? ParsedConfigurationSetting<ConfigurationSeverity>.Inherit(key, fallback)
            : ConfigurationSettingParser.Severity(key, selected);
    }
}
