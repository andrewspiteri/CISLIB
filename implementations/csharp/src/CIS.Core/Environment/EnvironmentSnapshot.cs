using CIS.Logging;

namespace CIS;

/// <summary>The immutable application startup snapshot, created only from validated owned inputs.</summary>
public sealed class EnvironmentSnapshot
{
    internal EnvironmentSnapshot(EventMetadata eventMetadata, LoggingOptions logging)
    {
        EventMetadata = eventMetadata;
        Logging = logging;
    }

    /// <summary>The application identity, independent of collector destinations.</summary>
    public string ApplicationName => EventMetadata.ApplicationName;

    /// <summary>The actual application build identity, including prerelease and build text.</summary>
    public string BuildVersion => EventMetadata.BuildVersion;

    /// <summary>The deployment or telemetry release label, independent of BuildVersion.</summary>
    public string ServiceVersion => EventMetadata.ServiceVersion;

    /// <summary>The application environment label; it does not modify the host environment.</summary>
    public string EnvironmentName => EventMetadata.EnvironmentName;

    /// <summary>The explicitly selected application execution kind.</summary>
    public EnvironmentKind EnvironmentKind => EventMetadata.EnvironmentKind;

    /// <summary>The UUID identifying this successful application initialization.</summary>
    public string InstanceId => EventMetadata.InstanceId;

    /// <summary>The actual process start instant in UTC, or null when unavailable.</summary>
    public DateTimeOffset? ProcessStartedAtUtc => EventMetadata.ProcessStartedAtUtc;

    /// <summary>The successful initialization instant in UTC.</summary>
    public DateTimeOffset InitializedAtUtc => EventMetadata.InitializedAtUtc;

    /// <summary>The host name observed by this application, or null when unavailable.</summary>
    public string? HostName => EventMetadata.HostName;

    /// <summary>The lowercase operating-system family.</summary>
    public string OperatingSystem => EventMetadata.OperatingSystem;

    /// <summary>The observed operating-system description, or null when unavailable.</summary>
    public string? OperatingSystemDescription => EventMetadata.OperatingSystemDescription;

    /// <summary>The lowercase operating-system architecture, or null when unavailable.</summary>
    public string? OSArchitecture => EventMetadata.OSArchitecture;

    /// <summary>The lowercase process architecture, or null when unavailable.</summary>
    public string? ProcessArchitecture => EventMetadata.ProcessArchitecture;

    /// <summary>The observed runtime description, or null when unavailable.</summary>
    public string? RuntimeDescription => EventMetadata.RuntimeDescription;

    /// <summary>The observed positive process identifier, or null when unavailable.</summary>
    public int? ProcessId => EventMetadata.ProcessId;

    /// <summary>An optional explicit build commit identifier.</summary>
    public string? CommitId => EventMetadata.CommitId;

    /// <summary>An optional explicit build identifier.</summary>
    public string? BuildId => EventMetadata.BuildId;

    /// <summary>The logging configuration for this application.</summary>
    public LoggingOptions Logging { get; }

    /// <summary>The immutable allowlisted application and process facts used by logging.</summary>
    public EventMetadata EventMetadata { get; }
}
