namespace CIS.Configuration;

internal sealed record RuntimeMetadata(
    DateTimeOffset? ProcessStartedAtUtc,
    string? HostName,
    string OperatingSystem,
    string? OperatingSystemDescription,
    string? OSArchitecture,
    string? ProcessArchitecture,
    string? RuntimeDescription,
    int? ProcessId);
