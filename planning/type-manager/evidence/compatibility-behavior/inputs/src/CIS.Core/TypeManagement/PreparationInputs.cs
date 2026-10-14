using System.Reflection;
using System.Runtime.Loader;

namespace CIS.TypeManagement;

internal sealed record PreparationInputs(Assembly HostAssembly, string ManifestPath, Type[] Contracts)
{
    internal static PreparationInputs Capture(TypeManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.HostAssembly, nameof(options));
        ArgumentNullException.ThrowIfNull(options.PrecomputedContracts, nameof(options));

        var host = options.HostAssembly;
        if (host.GetType() != typeof(object).Assembly.GetType() || host.IsDynamic ||
            AssemblyLoadContext.GetLoadContext(host) != AssemblyLoadContext.Default)
        {
            throw new ArgumentException("HostAssembly must be an ordinary default-context runtime assembly.", nameof(options));
        }

        string? suppliedPath = options.AssemblyManifestPath;
        if (suppliedPath is not null && string.IsNullOrWhiteSpace(suppliedPath))
        {
            throw new ArgumentException("AssemblyManifestPath must not be blank.", nameof(options));
        }
        string path;
        try
        {
            path = Path.GetFullPath(suppliedPath ?? host.GetName().Name + ".types.json", AppContext.BaseDirectory);
        }
        catch (ArgumentException error)
        {
            throw new ArgumentException("AssemblyManifestPath must be a valid path.", nameof(options), error);
        }
        var contracts = new List<Type>();
        var seen = new HashSet<Type>();
        foreach (var contract in options.PrecomputedContracts)
        {
            ValidateContract(contract, nameof(options));
            if (seen.Add(contract))
            {
                contracts.Add(contract);
            }
        }
        return new PreparationInputs(host, path, contracts.ToArray());
    }

    internal static void ValidateContract(Type contractType, string parameterName = "contractType")
    {
        ArgumentNullException.ThrowIfNull(contractType, parameterName);
        if (contractType.GetType() != typeof(object).GetType() ||
            (!contractType.IsClass && !contractType.IsInterface) || contractType.ContainsGenericParameters ||
            contractType.IsArray || contractType.IsPointer || contractType.IsByRef || contractType.IsFunctionPointer ||
            !IsDefaultRuntimeType(contractType))
        {
            throw new ArgumentException("Expected a closed runtime class or interface from the default loading context.", parameterName);
        }
    }

    private static bool IsDefaultRuntimeType(Type type)
    {
        if (type.GetType() != typeof(object).GetType() || type.Assembly.IsDynamic ||
            AssemblyLoadContext.GetLoadContext(type.Assembly) != AssemblyLoadContext.Default)
        {
            return false;
        }
        if (type.HasElementType && !IsDefaultRuntimeType(type.GetElementType()!))
        {
            return false;
        }
        foreach (var argument in type.GenericTypeArguments)
        {
            if (!IsDefaultRuntimeType(argument))
            {
                return false;
            }
        }
        return true;
    }
}
