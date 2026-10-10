using System.Runtime.InteropServices;
using CIS.Configuration;

namespace CIS.Tests.UnitTests.Discovery;

internal sealed class RuntimeMetadataStub : IRuntimeMetadataSource
{
    public DateTimeOffset? ProcessStartedAt { get; set; } = new(2026, 10, 10, 9, 0, 0, TimeSpan.FromHours(2));
    public string? HostName { get; set; } = "ApplicationVisible.Host";
    public OSPlatform? Platform { get; set; } = OSPlatform.Windows;
    public string? OSDescription { get; set; } = "Windows Fixture";
    public Architecture? OSArchitecture { get; set; } = Architecture.X64;
    public Architecture? ProcessArchitecture { get; set; } = Architecture.X86;
    public string? RuntimeDescription { get; set; } = ".NET Fixture";
    public int? ProcessId { get; set; } = 123;
    public string? FailingField { get; init; }
    public Exception? Failure { get; init; }
    public List<string> Reads { get; } = [];

    public DateTimeOffset? GetProcessStartedAt() => Read("start", ProcessStartedAt);
    public string? GetHostName() => Read("host", HostName);
    public OSPlatform? GetOperatingSystem() => Read("family", Platform);
    public string? GetOperatingSystemDescription() => Read("description", OSDescription);
    public Architecture? GetOSArchitecture() => Read("osArchitecture", OSArchitecture);
    public Architecture? GetProcessArchitecture() => Read("processArchitecture", ProcessArchitecture);
    public string? GetRuntimeDescription() => Read("runtime", RuntimeDescription);
    public int? GetProcessId() => Read("pid", ProcessId);

    private T Read<T>(string field, T value)
    {
        Reads.Add(field);
        if (field == FailingField)
        {
            throw Failure ?? new System.ComponentModel.Win32Exception("Metadata unavailable");
        }

        return value;
    }
}
