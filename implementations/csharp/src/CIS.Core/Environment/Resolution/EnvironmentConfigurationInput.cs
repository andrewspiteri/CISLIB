using CIS.Logging.Configuration;

namespace CIS.Configuration;

internal sealed class EnvironmentConfigurationInput
{
    public string? ApplicationName { get; init; }
    public string? EnvironmentName { get; init; }
    public string? ServiceVersion { get; init; }
    public LoggingConfigurationInput? Logging { get; init; }
}

internal sealed class LoggingConfigurationInput
{
    public ConfigurationSeverity? Level { get; init; }
    public ConfigurationOutputFormat? ConsoleFormat { get; init; }
    public ConfigurationSeverity? ConsoleLevel { get; init; }
    public ConfigurationConsoleStream? ConsoleStream { get; init; }
    public ConfigurationHumanDetail? HumanDetail { get; init; }
    public string? FilePath { get; init; }
    public ConfigurationOutputFormat? FileFormat { get; init; }
    public ConfigurationSeverity? FileLevel { get; init; }
    public int? QueueCapacity { get; init; }
    public int? FlushIntervalMilliseconds { get; init; }
    public int? ShutdownTimeoutMilliseconds { get; init; }
    public long? FileMaxBytes { get; init; }
    public int? FileRetainedFiles { get; init; }
    public int? SchemaVersion { get; init; }
    public int? MaxEntryBytes { get; init; }
}
