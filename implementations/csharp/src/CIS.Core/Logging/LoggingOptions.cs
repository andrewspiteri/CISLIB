using CIS.Configuration;

namespace CIS.Logging;

/// <summary>Immutable validated logging options. Reading options does not initialize any output.</summary>
public sealed class LoggingOptions
{
    internal LoggingOptions(ValidatedConfiguration validated)
    {
        ConfigurationResolution selected = validated.Selected;
        Level = (CoreLogLevel)selected.Level.Value;
        ConsoleFormat = (LogOutputFormat)selected.ConsoleFormat.Value;
        ConsoleLevel = (CoreLogLevel)selected.ConsoleLevel.Value;
        ConsoleStream = (ConsoleOutputStream)selected.ConsoleStream.Value;
        HumanDetail = (HumanOutputDetail)selected.HumanDetail.Value;
        FilePath = validated.FilePath;
        FileFormat = (LogOutputFormat)selected.FileFormat.Value;
        FileLevel = (CoreLogLevel)selected.FileLevel.Value;
        QueueCapacity = selected.QueueCapacity.Value;
        FlushInterval = TimeSpan.FromMilliseconds(selected.FlushIntervalMilliseconds.Value);
        ShutdownTimeout = TimeSpan.FromMilliseconds(selected.ShutdownTimeoutMilliseconds.Value);
        FileMaxBytes = selected.FileMaxBytes.Value;
        FileRetainedFiles = selected.FileRetainedFiles.Value;
        SchemaVersion = selected.SchemaVersion.Value;
        ConsoleEnabled = selected.ConsoleEnabled!.Value;
        FileEnabled = selected.FileEnabled!.Value;
        MaxEntryBytes = selected.MaxEntryBytes.Value;
    }

    /// <summary>The fallback severity used when an output has no explicit threshold.</summary>
    public CoreLogLevel Level { get; }

    /// <summary>The console output representation.</summary>
    public LogOutputFormat ConsoleFormat { get; }

    /// <summary>The independent console severity threshold; None disables console output.</summary>
    public CoreLogLevel ConsoleLevel { get; }

    /// <summary>The selected standard console stream.</summary>
    public ConsoleOutputStream ConsoleStream { get; }

    /// <summary>The detail level of human-readable output.</summary>
    public HumanOutputDetail HumanDetail { get; }

    /// <summary>The file destination; resolved options store an absolute path or null.</summary>
    public string? FilePath { get; }

    /// <summary>The file output representation.</summary>
    public LogOutputFormat FileFormat { get; }

    /// <summary>The independent file severity threshold; None disables file output.</summary>
    public CoreLogLevel FileLevel { get; }

    /// <summary>The maximum number of entries in an output queue.</summary>
    public int QueueCapacity { get; }

    /// <summary>The resolved writer flush interval.</summary>
    public TimeSpan FlushInterval { get; }

    /// <summary>The resolved shared shutdown deadline.</summary>
    public TimeSpan ShutdownTimeout { get; }

    /// <summary>The maximum complete file segment size in bytes.</summary>
    public long FileMaxBytes { get; }

    /// <summary>The number of retained file segments.</summary>
    public int FileRetainedFiles { get; }

    /// <summary>The selected event schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Whether the resolved console threshold enables console output.</summary>
    public bool ConsoleEnabled { get; }

    /// <summary>Whether a path and resolved threshold enable file output.</summary>
    public bool FileEnabled { get; }

    /// <summary>The maximum encoded log-entry size in bytes; the default is 65536.</summary>
    public int MaxEntryBytes { get; }
}
