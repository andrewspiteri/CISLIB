using CIS.Configuration;
using CIS.Logging.Configuration;

namespace CIS.Logging;

/// <summary>Caller-owned logging values and defaults. Setters do not validate or start outputs.</summary>
public sealed class LoggingOptions
{
    internal const int MinimumQueueCapacity = 1;
    internal const int MaximumQueueCapacity = 65536;
    internal const int MinimumFlushIntervalMilliseconds = 10;
    internal const int MaximumFlushIntervalMilliseconds = 60000;
    internal const int MinimumShutdownTimeoutMilliseconds = 1;
    internal const int MaximumShutdownTimeoutMilliseconds = 300000;
    internal const int MinimumFileMaxBytes = 65536;
    internal const int MaximumFileMaxBytes = 1073741824;
    internal const int MinimumFileRetainedFiles = 1;
    internal const int MaximumFileRetainedFiles = 1000;
    internal const int MinimumMaxEntryBytes = 16384;
    internal const int MaximumMaxEntryBytes = 16777216;

    /// <summary>The Level setting.</summary>
    public CoreLogLevel Level { get; set; } = CoreLogLevel.Information;

    /// <summary>The ConsoleFormat setting.</summary>
    public LogOutputFormat ConsoleFormat { get; set; } = LogOutputFormat.Human;

    /// <summary>The ConsoleLevel setting (null inherits Level).</summary>
    public CoreLogLevel? ConsoleLevel { get; set; }

    /// <summary>The ConsoleStream setting.</summary>
    public ConsoleOutputStream ConsoleStream { get; set; } = ConsoleOutputStream.StandardError;

    /// <summary>The HumanDetail setting.</summary>
    public HumanOutputDetail HumanDetail { get; set; } = HumanOutputDetail.Normal;

    /// <summary>The FilePath setting.</summary>
    public string? FilePath { get; set; }

    /// <summary>The FileFormat setting.</summary>
    public LogOutputFormat FileFormat { get; set; } = LogOutputFormat.JsonLines;

    /// <summary>The FileLevel setting (null inherits Level).</summary>
    public CoreLogLevel? FileLevel { get; set; }

    /// <summary>The QueueCapacity setting.</summary>
    public int QueueCapacity { get; set; } = 1024;

    /// <summary>Target interval in milliseconds for scheduling file-buffer flushes, including idle periods.</summary>
    /// <remarks>
    /// Defaults to 1,000 ms; Prepare requires 10–60,000 ms.
    /// CORE_LOG_FLUSH_INTERVAL_MS populates this value in EnvironmentOptions.Logging.
    /// Configured runtime creation checks this range only when file output is enabled.
    /// Used by enabled file output as a scheduling target, not a completion or durability guarantee.
    /// </remarks>
    public int FlushIntervalMilliseconds { get; set; } = 1000;

    /// <summary>The ShutdownTimeoutMilliseconds setting.</summary>
    public int ShutdownTimeoutMilliseconds { get; set; } = 5000;

    /// <summary>The FileMaxBytes setting.</summary>
    public long FileMaxBytes { get; set; } = 10485760;

    /// <summary>The FileRetainedFiles setting.</summary>
    public int FileRetainedFiles { get; set; } = 10;

    /// <summary>The SchemaVersion setting.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The MaxEntryBytes setting.</summary>
    public int MaxEntryBytes { get; set; } = 65536;

    /// <summary>The console threshold after inheritance.</summary>
    public CoreLogLevel EffectiveConsoleLevel => ConsoleLevel ?? Level;

    /// <summary>The file threshold after inheritance.</summary>
    public CoreLogLevel EffectiveFileLevel => FileLevel ?? Level;

    /// <summary>Whether the console threshold enables output.</summary>
    public bool ConsoleEnabled => EffectiveConsoleLevel != CoreLogLevel.None;

    /// <summary>Whether a path and threshold enable file output.</summary>
    public bool FileEnabled => !string.IsNullOrWhiteSpace(FilePath) && EffectiveFileLevel != CoreLogLevel.None;

    /// <summary>The selected flush interval.</summary>
    public TimeSpan FlushInterval => TimeSpan.FromMilliseconds(FlushIntervalMilliseconds);

    /// <summary>The selected shutdown timeout.</summary>
    public TimeSpan ShutdownTimeout => TimeSpan.FromMilliseconds(ShutdownTimeoutMilliseconds);

    /// <summary>Checks logging settings and normalizes the file path against the application's supplied absolute directory.</summary>
    /// <remarks>Call after assignments and overrides, before starting logging. Performs no output creation or probing.</remarks>
    public void Prepare(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Path.IsPathFullyQualified(baseDirectory))
        {
            throw new ArgumentException("Supply an absolute application startup directory.", nameof(baseDirectory));
        }
        var issues = new List<ConfigurationIssue>();
        CheckEnum(Level, nameof(Level), issues);
        CheckEnum(ConsoleFormat, nameof(ConsoleFormat), issues);
        if (ConsoleLevel is { } consoleLevel)
        {
            CheckEnum(consoleLevel, nameof(ConsoleLevel), issues);
        }
        CheckEnum(ConsoleStream, nameof(ConsoleStream), issues);
        CheckEnum(HumanDetail, nameof(HumanDetail), issues);
        CheckEnum(FileFormat, nameof(FileFormat), issues);
        if (FileLevel is { } fileLevel)
        {
            CheckEnum(fileLevel, nameof(FileLevel), issues);
        }
        CheckRange(QueueCapacity, MinimumQueueCapacity, MaximumQueueCapacity, nameof(QueueCapacity), issues);
        CheckRange(FlushIntervalMilliseconds, MinimumFlushIntervalMilliseconds, MaximumFlushIntervalMilliseconds, nameof(FlushIntervalMilliseconds), issues);
        CheckRange(ShutdownTimeoutMilliseconds, MinimumShutdownTimeoutMilliseconds, MaximumShutdownTimeoutMilliseconds, nameof(ShutdownTimeoutMilliseconds), issues);
        CheckRange(FileMaxBytes, MinimumFileMaxBytes, MaximumFileMaxBytes, nameof(FileMaxBytes), issues);
        CheckRange(FileRetainedFiles, MinimumFileRetainedFiles, MaximumFileRetainedFiles, nameof(FileRetainedFiles), issues);
        CheckRange(MaxEntryBytes, MinimumMaxEntryBytes, MaximumMaxEntryBytes, nameof(MaxEntryBytes), issues);
        if (SchemaVersion != 1)
        {
            issues.Add(new(ConfigurationIssueCodes.UnsupportedSchemaVersion, "LoggingOptions.SchemaVersion", "Only schema version 1 is supported."));
        }
        bool entryAndFileSizesInRange = FileMaxBytes is >= MinimumFileMaxBytes and <= MaximumFileMaxBytes
            && MaxEntryBytes is >= MinimumMaxEntryBytes and <= MaximumMaxEntryBytes;
        if (FileEnabled && entryAndFileSizesInRange && FileMaxBytes < MaxEntryBytes)
        {
            issues.Add(new(ConfigurationIssueCodes.OutOfRange, "LoggingOptions.FileMaxBytes", "An enabled file output requires FileMaxBytes to be at least MaxEntryBytes."));
        }
        string? path = null;
        if (!string.IsNullOrWhiteSpace(FilePath))
        {
            try
            {
                path = LoggingFilePath.Normalize(FilePath.Trim(), baseDirectory);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                issues.Add(new(ConfigurationIssueCodes.InvalidPath, "LoggingOptions.FilePath", "Supply a syntactically valid file path."));
            }
        }
        if (issues.Count != 0)
        {
            throw new CoreConfigurationException(issues);
        }
        FilePath = path;
    }

    private static void CheckEnum<T>(T value, string name, List<ConfigurationIssue> issues) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(new(ConfigurationIssueCodes.InvalidToken, "LoggingOptions." + name, "Use a declared option value."));
        }
    }

    private static void CheckRange(long value, long minimum, long maximum, string name, List<ConfigurationIssue> issues)
    {
        if (value < minimum || value > maximum)
        {
            issues.Add(new(ConfigurationIssueCodes.OutOfRange, "LoggingOptions." + name, "The value is outside the supported range."));
        }
    }
}
