using System.Reflection;

namespace CIS.TypeManagement;

/// <summary>Input values captured and validated when preparation starts.</summary>
public sealed class TypeManagerOptions
{
    /// <summary>Creates options for the application's explicitly selected host assembly.</summary>
    public TypeManagerOptions(Assembly hostAssembly)
    {
        HostAssembly = hostAssembly;
    }

    /// <summary>Gets the host assembly. Preparation requires an ordinary default-context runtime assembly.</summary>
    public Assembly HostAssembly { get; }
    /// <summary>Gets an optional inventory path. Relative paths resolve from the application directory; the default is the host name plus .types.json there.</summary>
    public string? AssemblyManifestPath { get; init; }
    /// <summary>Gets closed class or interface queries to prepare before returning the manager.</summary>
    public IReadOnlyList<Type> PrecomputedContracts { get; init; } = Array.Empty<Type>();
}
