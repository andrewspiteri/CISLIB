using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CIS.Configuration;

internal sealed class RuntimeMetadataSource : IRuntimeMetadataSource
{
    public static RuntimeMetadataSource Instance { get; } = new();

    private RuntimeMetadataSource()
    {
    }

    public DateTimeOffset? GetProcessStartedAt()
    {
        using Process process = Process.GetCurrentProcess();
        return new DateTimeOffset(process.StartTime.ToUniversalTime());
    }

    public string? GetHostName() => System.Environment.MachineName;

    public OSPlatform? GetOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return OSPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return OSPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OSPlatform.OSX;
        }

        return null;
    }

    public string? GetOperatingSystemDescription() => RuntimeInformation.OSDescription;

    public Architecture? GetOSArchitecture() => RuntimeInformation.OSArchitecture;

    public Architecture? GetProcessArchitecture() => RuntimeInformation.ProcessArchitecture;

    public string? GetRuntimeDescription() => RuntimeInformation.FrameworkDescription;

    public int? GetProcessId() => System.Environment.ProcessId;
}
