using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace CIS.TypeManagement;

/// <summary>A complete, fixed runtime type catalog prepared for one host.</summary>
public interface ITypeManager
{
    /// <summary>Gets selected actual assemblies in ordinal full-identity order.</summary>
    IReadOnlyList<Assembly> Assemblies { get; }
    /// <summary>Gets all defined types, including nonpublic and open generic definitions, in deterministic order.</summary>
    IReadOnlyList<Type> Types { get; }
    /// <summary>Gets immutable diagnostics captured during preparation.</summary>
    TypeManagerPreparationReport PreparationReport { get; }
    /// <summary>Gets cached definitions for an actual assembly in this catalog.</summary>
    /// <exception cref="ArgumentNullException">The assembly is null.</exception>
    /// <exception cref="ArgumentException">The assembly is not in this catalog.</exception>
    IReadOnlyList<Type> GetDefinedTypes(Assembly assembly);
    /// <summary>Gets closed concrete classes assignable to a closed runtime class or interface contract.</summary>
    /// <remarks>Results include nonpublic classes without usable constructors. Queries never create instances or extend catalog membership.</remarks>
    /// <exception cref="ArgumentNullException">The contract is null.</exception>
    /// <exception cref="ArgumentException">The contract is not a supported default-context runtime class or interface.</exception>
    IReadOnlyList<Type> GetImplementations(Type contractType);
    /// <summary>Looks up an exact, ordinal assembly-qualified name without normalization or runtime loading.</summary>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    /// <exception cref="ArgumentException">The name is empty or whitespace.</exception>
    bool TryGetType(string assemblyQualifiedTypeName, [NotNullWhen(true)] out Type? type);
}

/// <summary>Convenience queries that preserve the non-generic matching contract.</summary>
public static class TypeManagerExtensions
{
    /// <summary>Queries the manager using the actual runtime type of <typeparamref name="TContract"/>.</summary>
    /// <exception cref="ArgumentNullException">The manager is null.</exception>
    public static IReadOnlyList<Type> GetImplementations<TContract>(this ITypeManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.GetImplementations(typeof(TContract));
    }
}
