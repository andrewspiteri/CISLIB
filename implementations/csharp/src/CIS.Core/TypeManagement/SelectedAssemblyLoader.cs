using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace CIS.TypeManagement;

internal static class SelectedAssemblyLoader
{
    internal static IReadOnlyList<Assembly> Load(AssemblyInventory inventory, CancellationToken cancellationToken)
    {
        // Reject cheaply detectable defects across the complete selection before any new load.
        // Loading into Default is permanent and can invoke runtime-controlled callbacks.
        foreach (var selected in inventory.Assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ValidateExecutable(selected.Path);
                ValidateIdentity(inventory, selected, AssemblyName.GetAssemblyName(selected.Path).FullName);
                var existing = FindLoaded(selected);
                if (existing is not null) { ValidateIdentity(inventory, selected, existing.FullName); }
            }
            catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                throw LoadingFailure(inventory, selected, error);
            }
        }
        var assemblies = new List<Assembly>();
        foreach (var selected in inventory.Assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assembly loaded;
            try
            {
                loaded = FindLoaded(selected)!;
                if (loaded is null)
                {
                    try
                    {
                        loaded = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(selected.Identity));
                    }
                    catch (FileNotFoundException)
                    {
                        loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(selected.Path);
                    }
                }
                if (loaded.IsDynamic || AssemblyLoadContext.GetLoadContext(loaded) != AssemblyLoadContext.Default)
                {
                    throw new BadImageFormatException("A selected runtime asset must bind to a nondynamic assembly in the default loading context.", selected.Path);
                }
                ValidateIdentity(inventory, selected, loaded.FullName);
            }
            catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                throw LoadingFailure(inventory, selected, error);
            }
            assemblies.Add(loaded);
        }
        return Array.AsReadOnly(assemblies.OrderBy(assembly => assembly.FullName, StringComparer.Ordinal).ToArray());
    }

    private static Assembly? FindLoaded(SelectedAssembly selected) => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
        !assembly.IsDynamic && string.Equals(assembly.GetName().Name, selected.SimpleName, StringComparison.OrdinalIgnoreCase));

    private static TypeManagerInitializationException LoadingFailure(AssemblyInventory inventory, SelectedAssembly selected, Exception error)
    {
        if (error is FileLoadException && FindRuntimeBindingConflict(selected) is string actualIdentity)
        {
            return new TypeManagerInitializationException(TypeManagerFailureKind.AssemblyIdentityConflict,
                "The runtime's bound assembly identity conflicts with the selected inventory.", error,
                manifestPath: inventory.ManifestPath, assetPath: selected.Path,
                assemblyIdentity: selected.Identity, actualAssemblyIdentity: actualIdentity);
        }
        return new TypeManagerInitializationException(TypeManagerFailureKind.AssemblyLoadFailure,
            "A selected assembly could not be loaded.", error, manifestPath: inventory.ManifestPath,
            assetPath: selected.Path, assemblyIdentity: selected.Identity);
    }

    private static string? FindRuntimeBindingConflict(SelectedAssembly selected)
    {
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (!assembly.IsDynamic && string.Equals(assembly.GetName().Name, selected.SimpleName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(assembly.FullName, selected.Identity, StringComparison.Ordinal))
            {
                return assembly.FullName;
            }
        }
        // Read the runtime's existing binding facts only to diagnose a failed bind.
        // This never chooses assets, parses deps/RID rules, loads an assembly, or extends the catalog.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platformAssemblies)
        {
            foreach (string path in platformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var identity = AssemblyName.GetAssemblyName(path);
                    if (string.Equals(identity.Name, selected.SimpleName, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(identity.FullName, selected.Identity, StringComparison.Ordinal))
                    {
                        return identity.FullName;
                    }
                }
                catch (Exception error) when (error is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    // A diagnostic lookup must not replace the original selected-asset failure.
                }
            }
        }
        return null;
    }

    private static void ValidateExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        using var executable = new PEReader(stream);
        if (!executable.HasMetadata)
        {
            throw new BadImageFormatException("Selected file has no managed assembly metadata.", path);
        }
        var metadata = executable.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException("Selected file is not an assembly.", path);
        }
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var constructor = metadata.GetCustomAttribute(handle).Constructor;
            StringHandle typeNamespace;
            StringHandle typeName;
            if (constructor.Kind == HandleKind.MemberReference)
            {
                var parent = metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference) { continue; }
                var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
                typeNamespace = type.Namespace;
                typeName = type.Name;
            }
            else if (constructor.Kind == HandleKind.MethodDefinition)
            {
                var type = metadata.GetTypeDefinition(metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                typeNamespace = type.Namespace;
                typeName = type.Name;
            }
            else { continue; }
            if (metadata.StringComparer.Equals(typeNamespace, "System.Runtime.CompilerServices") &&
                metadata.StringComparer.Equals(typeName, "ReferenceAssemblyAttribute"))
            {
                throw new BadImageFormatException("Selected file is a reference assembly, not an executable runtime asset.", path);
            }
        }
    }

    private static void ValidateIdentity(AssemblyInventory inventory, SelectedAssembly selected, string? actual)
    {
        if (!string.Equals(selected.Identity, actual, StringComparison.Ordinal))
        {
            throw new TypeManagerInitializationException(TypeManagerFailureKind.AssemblyIdentityConflict,
                "A selected assembly identity conflicts with its deployed or loaded identity.",
                new FileLoadException("Expected " + selected.Identity + "; observed " + actual, selected.Path),
                manifestPath: inventory.ManifestPath, assetPath: selected.Path,
                assemblyIdentity: selected.Identity, actualAssemblyIdentity: actual);
        }
    }
}
