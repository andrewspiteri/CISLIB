using System.Runtime.InteropServices;

namespace CIS.Configuration;

internal static class RuntimeMetadataDiscovery
{
    public static RuntimeMetadata Discover(IRuntimeMetadataSource? source = null)
    {
        source ??= RuntimeMetadataSource.Instance;
        int? processId = OptionalMetadataDiscovery.ReadValue(source.GetProcessId);
        DateTimeOffset? processStart = OptionalMetadataDiscovery.ReadValue(source.GetProcessStartedAt);

        return new RuntimeMetadata(
            processStart?.ToUniversalTime(),
            Nonblank(OptionalMetadataDiscovery.Read(source.GetHostName)),
            Family(OptionalMetadataDiscovery.ReadValue(source.GetOperatingSystem)),
            Nonblank(OptionalMetadataDiscovery.Read(source.GetOperatingSystemDescription)),
            ArchitectureName(OptionalMetadataDiscovery.ReadValue(source.GetOSArchitecture)),
            ArchitectureName(OptionalMetadataDiscovery.ReadValue(source.GetProcessArchitecture)),
            Nonblank(OptionalMetadataDiscovery.Read(source.GetRuntimeDescription)),
            processId > 0 ? processId : null);
    }

    private static string Family(OSPlatform? platform)
    {
        if (platform == OSPlatform.Windows)
        {
            return "windows";
        }

        if (platform == OSPlatform.Linux)
        {
            return "linux";
        }

        return platform == OSPlatform.OSX ? "macos" : "unknown";
    }

    private static string? ArchitectureName(Architecture? architecture) =>
        architecture is { } value && Enum.IsDefined(value) ? value.ToString().ToLowerInvariant() : null;

    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
