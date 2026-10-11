using CIS.Logging.Configuration;

namespace CIS.Configuration;

// Parsed selection is distinct from the later validated public snapshot.
internal sealed class ConfigurationResolution
{
    public ConfigurationResolution(
        string buildVersion,
        ParsedConfigurationSetting<string> applicationName,
        ParsedConfigurationSetting<string> environmentName,
        ParsedConfigurationSetting<string> serviceVersion,
        ParsedConfigurationSetting<ConfigurationSeverity> level,
        ParsedConfigurationSetting<ConfigurationOutputFormat> consoleFormat,
        ParsedConfigurationSetting<ConfigurationSeverity> consoleLevel,
        ParsedConfigurationSetting<ConfigurationConsoleStream> consoleStream,
        ParsedConfigurationSetting<ConfigurationHumanDetail> humanDetail,
        ParsedConfigurationSetting<string> filePath,
        ParsedConfigurationSetting<ConfigurationOutputFormat> fileFormat,
        ParsedConfigurationSetting<ConfigurationSeverity> fileLevel,
        ParsedConfigurationSetting<int> queueCapacity,
        ParsedConfigurationSetting<int> flushIntervalMilliseconds,
        ParsedConfigurationSetting<int> shutdownTimeoutMilliseconds,
        ParsedConfigurationSetting<long> fileMaxBytes,
        ParsedConfigurationSetting<int> fileRetainedFiles,
        ParsedConfigurationSetting<int> schemaVersion,
        ParsedConfigurationSetting<int> maxEntryBytes,
        ConfigurationEnvironmentKind environmentKind = ConfigurationEnvironmentKind.Unknown)
    {
        BuildVersion = buildVersion;
        EnvironmentKind = environmentKind;
        ApplicationName = applicationName;
        EnvironmentName = environmentName;
        ServiceVersion = serviceVersion;
        Level = level;
        ConsoleFormat = consoleFormat;
        ConsoleLevel = consoleLevel;
        ConsoleStream = consoleStream;
        HumanDetail = humanDetail;
        FilePath = filePath;
        FileFormat = fileFormat;
        FileLevel = fileLevel;
        QueueCapacity = queueCapacity;
        FlushIntervalMilliseconds = flushIntervalMilliseconds;
        ShutdownTimeoutMilliseconds = shutdownTimeoutMilliseconds;
        FileMaxBytes = fileMaxBytes;
        FileRetainedFiles = fileRetainedFiles;
        SchemaVersion = schemaVersion;
        MaxEntryBytes = maxEntryBytes;
        Settings = Array.AsReadOnly<IParsedConfigurationSetting>(
        [
            ApplicationName, EnvironmentName, ServiceVersion, Level, ConsoleFormat, ConsoleLevel, ConsoleStream, HumanDetail, FilePath, FileFormat, FileLevel, QueueCapacity, FlushIntervalMilliseconds, ShutdownTimeoutMilliseconds, FileMaxBytes, FileRetainedFiles, SchemaVersion, MaxEntryBytes
        ]);
    }

    public string BuildVersion { get; }
    public ConfigurationEnvironmentKind EnvironmentKind { get; }
    public ParsedConfigurationSetting<string> ApplicationName { get; }
    public ParsedConfigurationSetting<string> EnvironmentName { get; }
    public ParsedConfigurationSetting<string> ServiceVersion { get; }
    public ParsedConfigurationSetting<ConfigurationSeverity> Level { get; }
    public ParsedConfigurationSetting<ConfigurationOutputFormat> ConsoleFormat { get; }
    public ParsedConfigurationSetting<ConfigurationSeverity> ConsoleLevel { get; }
    public ParsedConfigurationSetting<ConfigurationConsoleStream> ConsoleStream { get; }
    public ParsedConfigurationSetting<ConfigurationHumanDetail> HumanDetail { get; }
    public ParsedConfigurationSetting<string> FilePath { get; }
    public ParsedConfigurationSetting<ConfigurationOutputFormat> FileFormat { get; }
    public ParsedConfigurationSetting<ConfigurationSeverity> FileLevel { get; }
    public ParsedConfigurationSetting<int> QueueCapacity { get; }
    public ParsedConfigurationSetting<int> FlushIntervalMilliseconds { get; }
    public ParsedConfigurationSetting<int> ShutdownTimeoutMilliseconds { get; }
    public ParsedConfigurationSetting<long> FileMaxBytes { get; }
    public ParsedConfigurationSetting<int> FileRetainedFiles { get; }
    public ParsedConfigurationSetting<int> SchemaVersion { get; }
    public ParsedConfigurationSetting<int> MaxEntryBytes { get; }
    public IReadOnlyList<IParsedConfigurationSetting> Settings { get; }
    public bool HasParseErrors => Settings.Any(setting => setting.State is not (ConfigurationParseState.Parsed or ConfigurationParseState.Absent));
    public bool? ConsoleEnabled => ConsoleLevel.HasValue ? ConsoleLevel.Value != ConfigurationSeverity.None : null;
    public bool? FileEnabled => FilePath.State == ConfigurationParseState.Absent || (FileLevel.HasValue && FileLevel.Value == ConfigurationSeverity.None)
        ? false
        : FilePath.HasValue && FileLevel.HasValue ? true : null;
    public TimeSpan? FlushInterval => FlushIntervalMilliseconds.HasValue ? TimeSpan.FromMilliseconds(FlushIntervalMilliseconds.Value) : null;
    public TimeSpan? ShutdownTimeout => ShutdownTimeoutMilliseconds.HasValue ? TimeSpan.FromMilliseconds(ShutdownTimeoutMilliseconds.Value) : null;
}
