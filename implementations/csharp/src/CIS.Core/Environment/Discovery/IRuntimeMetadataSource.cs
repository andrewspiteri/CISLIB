using System.Runtime.InteropServices;

namespace CIS.Configuration;

internal interface IRuntimeMetadataSource
{
    DateTimeOffset? GetProcessStartedAt();
    string? GetHostName();
    OSPlatform? GetOperatingSystem();
    string? GetOperatingSystemDescription();
    Architecture? GetOSArchitecture();
    Architecture? GetProcessArchitecture();
    string? GetRuntimeDescription();
    int? GetProcessId();
}
