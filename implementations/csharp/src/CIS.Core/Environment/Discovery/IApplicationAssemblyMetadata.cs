using System.Reflection;

namespace CIS.Configuration;

internal interface IApplicationAssemblyMetadata
{
    Assembly? GetEntryAssembly();
    string? GetName(Assembly assembly);
    string? GetInformationalVersion(Assembly assembly);
    string? GetVersion(Assembly assembly);
}
