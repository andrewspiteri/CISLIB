using CIS.Configuration;

namespace CIS.Logging;

/// <summary>Immutable allowlisted application/process facts, excluding settings, paths and operation context.</summary>
public sealed class EventMetadata
{
    internal EventMetadata(ValidatedConfiguration validated, RuntimeMetadata runtime, InitializationIdentity initialization)
    {
        ConfigurationResolution selected = validated.Selected;
        ApplicationName = selected.ApplicationName.Value;
        BuildVersion = selected.BuildVersion;
        ServiceVersion = selected.ServiceVersion.Value;
        EnvironmentName = selected.EnvironmentName.Value;
        EnvironmentKind = (EnvironmentKind)selected.EnvironmentKind;
        InstanceId = initialization.InstanceId;
        ProcessStartedAtUtc = runtime.ProcessStartedAtUtc;
        InitializedAtUtc = initialization.InitializedAtUtc;
        HostName = runtime.HostName;
        OperatingSystem = runtime.OperatingSystem;
        OperatingSystemDescription = runtime.OperatingSystemDescription;
        OSArchitecture = runtime.OSArchitecture;
        ProcessArchitecture = runtime.ProcessArchitecture;
        RuntimeDescription = runtime.RuntimeDescription;
        ProcessId = runtime.ProcessId;
        CommitId = selected.CommitId;
        BuildId = selected.BuildId;
    }

    /// <summary>The application identity, independent of collector destinations.</summary>
    public string ApplicationName { get; }

    /// <summary>The actual application build identity, including prerelease and build text.</summary>
    public string BuildVersion { get; }

    /// <summary>The deployment or telemetry release label, independent of BuildVersion.</summary>
    public string ServiceVersion { get; }

    /// <summary>The application environment label; it does not modify the host environment.</summary>
    public string EnvironmentName { get; }

    /// <summary>The explicitly selected application execution kind.</summary>
    public EnvironmentKind EnvironmentKind { get; }

    /// <summary>The UUID identifying this successful application initialization.</summary>
    public string InstanceId { get; }

    /// <summary>The actual process start instant in UTC, or null when unavailable.</summary>
    public DateTimeOffset? ProcessStartedAtUtc { get; }

    /// <summary>The successful initialization instant in UTC.</summary>
    public DateTimeOffset InitializedAtUtc { get; }

    /// <summary>The host name observed by this application, or null when unavailable.</summary>
    public string? HostName { get; }

    /// <summary>The lowercase operating-system family.</summary>
    public string OperatingSystem { get; }

    /// <summary>The observed operating-system description, or null when unavailable.</summary>
    public string? OperatingSystemDescription { get; }

    /// <summary>The lowercase operating-system architecture, or null when unavailable.</summary>
    public string? OSArchitecture { get; }

    /// <summary>The lowercase process architecture, or null when unavailable.</summary>
    public string? ProcessArchitecture { get; }

    /// <summary>The observed runtime description, or null when unavailable.</summary>
    public string? RuntimeDescription { get; }

    /// <summary>The observed positive process identifier, or null when unavailable.</summary>
    public int? ProcessId { get; }

    /// <summary>An optional explicit build commit identifier.</summary>
    public string? CommitId { get; }

    /// <summary>An optional explicit build identifier.</summary>
    public string? BuildId { get; }
}
