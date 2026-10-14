using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace CIS.TypeManagement;

/// <summary>Prepares and owns a fixed catalog of the host's selected runtime assemblies and types.</summary>
public sealed class TypeManager : ITypeManager
{
    private readonly Dictionary<Assembly, IReadOnlyList<Type>> definedTypes;
    private readonly Dictionary<string, Type> exactNames;
    private readonly Dictionary<Type, IReadOnlyList<Type>> implementations = new();
    private readonly object queryLock = new();

    private TypeManager(IReadOnlyList<Assembly> assemblies, IReadOnlyList<Type> types,
        Dictionary<Assembly, IReadOnlyList<Type>> definedTypes, Dictionary<string, Type> exactNames)
    {
        Assemblies = assemblies;
        Types = types;
        this.definedTypes = definedTypes;
        this.exactNames = exactNames;
        PreparationReport = null!; // Assigned before the instance can leave CreateCoreAsync.
    }

    /// <inheritdoc />
    public IReadOnlyList<Assembly> Assemblies { get; }
    /// <inheritdoc />
    public IReadOnlyList<Type> Types { get; }
    /// <inheritdoc />
    public TypeManagerPreparationReport PreparationReport { get; private set; }

    /// <summary>Captures inputs and asynchronously prepares a complete catalog before returning it.</summary>
    /// <remarks>Use in an untrimmed, framework-dependent .NET 10 host before building its service container.</remarks>
    /// <exception cref="ArgumentException">An option or precomputed contract is invalid.</exception>
    /// <exception cref="ArgumentNullException">Options, the host, contracts list, or a contract element is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation is observed before publication.</exception>
    /// <exception cref="TypeManagerInitializationException">The selected deployment cannot be prepared completely.</exception>
    [RequiresUnreferencedCode("Type Manager enumerates all selected assembly definitions and requires an untrimmed deployment.")]
    public static Task<ITypeManager> CreateAsync(TypeManagerOptions options, CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        var inputs = PreparationInputs.Capture(options);
        return CreateCoreAsync(inputs, started, cancellationToken);
    }

    private static async Task<ITypeManager> CreateCoreAsync(PreparationInputs inputs, long started,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(inputs.HostAssembly.Location))
        {
            throw new TypeManagerInitializationException(TypeManagerFailureKind.UnsupportedDeployment,
                "Type Manager requires a conventional, untrimmed, framework-dependent deployment.",
                new NotSupportedException("The host assembly has no conventional file location."),
                manifestPath: inputs.ManifestPath, assemblyIdentity: inputs.HostAssembly.FullName);
        }
        var inventory = await AssemblyInventory.ReadAsync(inputs.ManifestPath, inputs.HostAssembly, cancellationToken).ConfigureAwait(false);
        var initiallyLoaded = new HashSet<string>(AssemblyLoadContext.Default.Assemblies
            .Where(assembly => !assembly.IsDynamic).Select(assembly => assembly.FullName).OfType<string>(), StringComparer.Ordinal);
        var assemblies = SelectedAssemblyLoader.Load(inventory, cancellationToken);
        var allTypes = new List<Type>();
        var definedTypes = new Dictionary<Assembly, IReadOnlyList<Type>>();
        var names = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Force relationship metadata while failures still prevent publication.
                    _ = type.BaseType;
                    _ = type.GetInterfaces();
                    string? name = type.AssemblyQualifiedName;
                    if (!string.IsNullOrWhiteSpace(name) && !names.TryAdd(name, type) && names[name] != type)
                    {
                        throw new TypeLoadException("Distinct definitions have the same textual identity: " + name);
                    }
                }
                Array.Sort(types, CompareTypes);
                definedTypes.Add(assembly, Array.AsReadOnly(types));
                allTypes.AddRange(types);
            }
            catch (Exception error) when (IsMetadataFailure(error))
            {
                throw DiscoveryFailure(inputs.ManifestPath, assembly, error);
            }
        }
        allTypes.Sort(CompareTypes);
        var manager = new TypeManager(assemblies, Array.AsReadOnly(allTypes.ToArray()), definedTypes, names);
        foreach (var contract in inputs.Contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            manager.GetOrComputeImplementations(contract, inputs.ManifestPath);
        }
        var identities = assemblies.Select(assembly => assembly.FullName!).ToArray();
        manager.PreparationReport = new TypeManagerPreparationReport(Stopwatch.GetElapsedTime(started),
            allTypes.Count, inputs.Contracts.Length, identities,
            identities.Where(identity => !initiallyLoaded.Contains(identity)),
            identities.Where(initiallyLoaded.Contains));
        cancellationToken.ThrowIfCancellationRequested();
        return manager;
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> GetDefinedTypes(Assembly assembly)
    {
        ;
        if (!definedTypes.TryGetValue(assembly, out var types))
        {
            throw new ArgumentException("The assembly is outside this catalog.", nameof(assembly));
        }
        return types;
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> GetImplementations(Type contractType)
    {
        PreparationInputs.ValidateContract(contractType);
        return GetOrComputeImplementations(contractType, preparationManifest: null);
    }

    private IReadOnlyList<Type> GetOrComputeImplementations(Type contractType, string? preparationManifest)
    {
        lock (queryLock)
        {
            if (implementations.TryGetValue(contractType, out var result))
            {
                return result;
            }
            var matches = new List<Type>();
            foreach (var candidate in Types)
            {
                try
                {
                    if (candidate.IsClass && !candidate.IsAbstract && !candidate.ContainsGenericParameters &&
                        contractType.IsAssignableFrom(candidate))
                    {
                        matches.Add(candidate);
                    }
                }
                catch (Exception error) when (preparationManifest is not null && IsMetadataFailure(error))
                {
                    throw DiscoveryFailure(preparationManifest, candidate.Assembly, error);
                }
            }
            result = Array.AsReadOnly(matches.ToArray());
            // CLR metadata resolution may reenter through a host callback on this thread.
            // Keep a result already published by that reentrant query.
            implementations.TryAdd(contractType, result);
            return implementations[contractType];
        }
    }

    /// <inheritdoc />
    public bool TryGetType(string assemblyQualifiedTypeName, [NotNullWhen(true)] out Type? type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyQualifiedTypeName);
        return exactNames.TryGetValue(assemblyQualifiedTypeName, out type);
    }

    private static int CompareTypes(Type? left, Type? right)
    {
        int comparison = StringComparer.Ordinal.Compare(left!.AssemblyQualifiedName, right!.AssemblyQualifiedName);
        if (comparison != 0) { return comparison; }
        comparison = StringComparer.Ordinal.Compare(left.Assembly.FullName, right.Assembly.FullName);
        if (comparison != 0) { return comparison; }
        comparison = StringComparer.Ordinal.Compare(left.FullName ?? left.Name, right.FullName ?? right.Name);
        return comparison != 0 ? comparison : left.MetadataToken.CompareTo(right.MetadataToken);
    }

    private static bool IsMetadataFailure(Exception error)
    {
        return error is ReflectionTypeLoadException or TypeLoadException or IOException or BadImageFormatException or NotSupportedException;
    }

    private static TypeManagerInitializationException DiscoveryFailure(string manifestPath, Assembly assembly, Exception error)
    {
        return new TypeManagerInitializationException(TypeManagerFailureKind.TypeDiscoveryFailure,
            "Selected type metadata could not be discovered completely.", error,
            manifestPath: manifestPath, assetPath: assembly.Location, assemblyIdentity: assembly.FullName,
            loaderExceptions: (error as ReflectionTypeLoadException)?.LoaderExceptions.OfType<Exception>());
    }
}
