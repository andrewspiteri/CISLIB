using System.Reflection;

namespace CIS.Configuration;

internal sealed class RuntimeApplicationAssemblyMetadata : IApplicationAssemblyMetadata
{
    public static RuntimeApplicationAssemblyMetadata Instance { get; } = new();

    private RuntimeApplicationAssemblyMetadata()
    {
    }

    public Assembly? GetEntryAssembly() => Assembly.GetEntryAssembly();

    public string? GetName(Assembly assembly) => assembly.GetName().Name;

    public string? GetInformationalVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public string? GetVersion(Assembly assembly) => assembly.GetName().Version?.ToString();
}
