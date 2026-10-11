namespace CIS.Logging;

/// <summary>Caller-owned optional logging inputs, copied during initialization. Later changes do not affect published options. Null means absent; selected invalid values fail initialization.</summary>
public sealed class LoggingSetup
{
    /// <summary>The fallback severity used when an output has no explicit threshold.</summary>
    public CoreLogLevel? Level { get; set; }

    /// <summary>The console output representation.</summary>
    public LogOutputFormat? ConsoleFormat { get; set; }

    /// <summary>The independent console severity threshold; None disables console output.</summary>
    public CoreLogLevel? ConsoleLevel { get; set; }

    /// <summary>The selected standard console stream.</summary>
    public ConsoleOutputStream? ConsoleStream { get; set; }

    /// <summary>The detail level of human-readable output.</summary>
    public HumanOutputDetail? HumanDetail { get; set; }

    /// <summary>The file destination; resolved options store an absolute path or null.</summary>
    public string? FilePath { get; set; }

    /// <summary>The file output representation.</summary>
    public LogOutputFormat? FileFormat { get; set; }

    /// <summary>The independent file severity threshold; None disables file output.</summary>
    public CoreLogLevel? FileLevel { get; set; }

    /// <summary>The maximum number of entries in an output queue.</summary>
    public int? QueueCapacity { get; set; }

    /// <summary>The writer flush interval in milliseconds.</summary>
    public int? FlushIntervalMilliseconds { get; set; }

    /// <summary>The shared shutdown deadline in milliseconds.</summary>
    public int? ShutdownTimeoutMilliseconds { get; set; }

    /// <summary>The maximum complete file segment size in bytes.</summary>
    public long? FileMaxBytes { get; set; }

    /// <summary>The number of retained file segments.</summary>
    public int? FileRetainedFiles { get; set; }

    /// <summary>The selected event schema version.</summary>
    public int? SchemaVersion { get; set; }

    /// <summary>The maximum encoded log-entry size in bytes; the default is 65536.</summary>
    public int? MaxEntryBytes { get; set; }
}
