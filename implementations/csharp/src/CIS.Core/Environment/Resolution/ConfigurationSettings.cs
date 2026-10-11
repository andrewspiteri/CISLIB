namespace CIS.Configuration;

internal static class ConfigurationSettings
{
    public static IReadOnlyList<ConfigurationSetting> All { get; } = Array.AsReadOnly<ConfigurationSetting>(
    [
        new("CORE_SERVICE_NAME", "DD_SERVICE", "EnvironmentSetup.ApplicationName", "ApplicationName"),
        new("CORE_ENVIRONMENT", "DD_ENV", "EnvironmentSetup.EnvironmentName", "EnvironmentName"),
        new("CORE_SERVICE_VERSION", "DD_VERSION", "EnvironmentSetup.ServiceVersion", "BuildVersion"),
        new("CORE_LOG_LEVEL", null, "LoggingSetup.Level", "Logging.Level"),
        new("CORE_LOG_CONSOLE_FORMAT", null, "LoggingSetup.ConsoleFormat", "Logging.ConsoleFormat"),
        new("CORE_LOG_CONSOLE_LEVEL", null, "LoggingSetup.ConsoleLevel", "Logging.ConsoleLevel"),
        new("CORE_LOG_CONSOLE_STREAM", null, "LoggingSetup.ConsoleStream", "Logging.ConsoleStream"),
        new("CORE_LOG_HUMAN_DETAIL", null, "LoggingSetup.HumanDetail", "Logging.HumanDetail"),
        new("CORE_LOG_FILE_PATH", null, "LoggingSetup.FilePath", "Logging.FilePath"),
        new("CORE_LOG_FILE_FORMAT", null, "LoggingSetup.FileFormat", "Logging.FileFormat"),
        new("CORE_LOG_FILE_LEVEL", null, "LoggingSetup.FileLevel", "Logging.FileLevel"),
        new("CORE_LOG_QUEUE_CAPACITY", null, "LoggingSetup.QueueCapacity", "Logging.QueueCapacity"),
        new("CORE_LOG_FLUSH_INTERVAL_MS", null, "LoggingSetup.FlushIntervalMilliseconds", "Logging.FlushIntervalMilliseconds"),
        new("CORE_LOG_SHUTDOWN_TIMEOUT_MS", null, "LoggingSetup.ShutdownTimeoutMilliseconds", "Logging.ShutdownTimeoutMilliseconds"),
        new("CORE_LOG_FILE_MAX_BYTES", null, "LoggingSetup.FileMaxBytes", "Logging.FileMaxBytes"),
        new("CORE_LOG_FILE_RETAINED_FILES", null, "LoggingSetup.FileRetainedFiles", "Logging.FileRetainedFiles"),
        new("CORE_LOG_SCHEMA_VERSION", null, "LoggingSetup.SchemaVersion", "Logging.SchemaVersion"),
        new("CORE_LOG_MAX_ENTRY_BYTES", null, "LoggingSetup.MaxEntryBytes", "Logging.MaxEntryBytes")
    ]);

    public static ConfigurationSetting Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return All.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.Ordinal))
            ?? throw new ArgumentException("The configuration setting is not recognized.", nameof(key));
    }
}
