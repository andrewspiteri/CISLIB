using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace CIS.TypeManagement.Build;

/// <summary>Reads executable metadata from SDK-selected output files without loading application code.</summary>
public sealed class WriteTypeInventory : Microsoft.Build.Utilities.Task
{
    [Required]
    public string OutputDirectory { get; set; } = "";

    [Required]
    public string HostFileName { get; set; } = "";

    [Required]
    public ITaskItem[] Assets { get; set; } = [];

    [Output]
    public string ManifestPath { get; private set; } = "";

    public override bool Execute()
    {
        try
        {
            WriteInventory();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            BadImageFormatException or ArgumentException or InvalidOperationException)
        {
            Log.LogError("CISTM001: {0}", error.Message);
            return false;
        }
    }

    private void WriteInventory()
    {
        var outputDirectory = Path.GetFullPath(OutputDirectory);
        var hostPath = ResolveOutputPath(outputDirectory, HostFileName);
        var hostName = AssemblyName.GetAssemblyName(hostPath);
        var hostIdentity = hostName.FullName;
        var bySimpleName = new Dictionary<string, InventoryEntry>(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, InventoryEntry>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        AddAssembly(HostFileName, required: true);
        foreach (var asset in Assets)
        {
            bool explicitSelection = string.Equals(asset.GetMetadata("CisExplicitSelection"), "true", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(asset.GetMetadata("AssetType"), "native", StringComparison.OrdinalIgnoreCase))
            {
                if (explicitSelection)
                {
                    throw new InvalidOperationException($"Explicit discovery asset is native: {asset.ItemSpec}");
                }
                continue;
            }

            var relativePath = asset.GetMetadata("RelativePath");
            if (string.IsNullOrEmpty(relativePath))
            {
                throw new InvalidOperationException($"Selected asset has no output path: {asset.ItemSpec}");
            }

            var extension = Path.GetExtension(relativePath);
            if (!string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (explicitSelection)
                {
                    throw new InvalidOperationException($"Explicit discovery asset must be a managed DLL or EXE: {relativePath}");
                }
                continue;
            }

            try
            {
                AddAssembly(relativePath, required: explicitSelection);
            }
            catch (BadImageFormatException error)
            {
                throw new BadImageFormatException($"Selected managed asset has invalid executable metadata: {relativePath}", error);
            }
        }

        var entries = bySimpleName.Values.OrderBy(entry => entry.AssemblyName, StringComparer.Ordinal)
            .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray();
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = hostIdentity,
            assemblies = entries.Select(entry => new
            {
                assemblyName = entry.AssemblyName,
                relativePath = entry.RelativePath
            })
        }, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";

        ManifestPath = Path.Combine(outputDirectory, hostName.Name + ".types.json");
        if (!File.Exists(ManifestPath) || !string.Equals(File.ReadAllText(ManifestPath), json, StringComparison.Ordinal))
        {
            File.WriteAllText(ManifestPath, json, new System.Text.UTF8Encoding(false));
        }

        Log.LogMessage(MessageImportance.Normal, "Type Manager inventory: {0} assemblies -> {1}", entries.Length, ManifestPath);

        void AddAssembly(string relativePath, bool required)
        {
            var path = ResolveOutputPath(outputDirectory, relativePath);
            // Inspect the deployed file, never a compile-time reference or the original package asset.
            using var stream = File.OpenRead(path);
            using var executable = new PEReader(stream);
            if (!executable.HasMetadata)
            {
                throw new BadImageFormatException($"Selected managed asset has no executable metadata: {path}");
            }

            var metadata = executable.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                throw new BadImageFormatException($"Selected asset is not an assembly: {path}");
            }

            var identity = AssemblyName.GetAssemblyName(path);
            if (!string.IsNullOrEmpty(identity.CultureName))
            {
                if (required)
                {
                    throw new InvalidOperationException($"Explicit required assembly cannot be a satellite: {path}");
                }
                return;
            }

            if (IsReferenceAssembly(metadata))
            {
                throw new InvalidOperationException($"Selected runtime asset is a reference assembly: {path}");
            }

            var normalizedPath = Path.GetRelativePath(outputDirectory, path).Replace('\\', '/');
            var entry = new InventoryEntry(identity.FullName, normalizedPath);
            var simpleName = identity.Name ?? throw new InvalidOperationException($"Assembly has no name: {path}");
            if (bySimpleName.TryGetValue(simpleName, out var existing) && existing != entry)
            {
                throw new InvalidOperationException($"Conflicting assembly selection: {existing} and {entry}");
            }
            if (byPath.TryGetValue(normalizedPath, out existing) && existing != entry)
            {
                throw new InvalidOperationException($"Conflicting output path: {existing} and {entry}");
            }
            bySimpleName[simpleName] = entry;
            byPath[normalizedPath] = entry;
        }
    }

    private static string ResolveOutputPath(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
        {
            throw new ArgumentException($"Expected a relative output path: {relativePath}");
        }
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
        var normalized = Path.GetRelativePath(directory, fullPath);
        if (normalized == ".." || normalized.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(normalized))
        {
            throw new ArgumentException($"Output path escapes the output directory: {relativePath}");
        }
        return fullPath;
    }

    private static bool IsReferenceAssembly(MetadataReader metadata)
    {
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var constructor = metadata.GetCustomAttribute(handle).Constructor;
            StringHandle typeNamespace;
            StringHandle typeName;
            if (constructor.Kind == HandleKind.MemberReference)
            {
                var parent = metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference)
                {
                    continue;
                }
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
            else
            {
                continue;
            }
            if (metadata.StringComparer.Equals(typeNamespace, "System.Runtime.CompilerServices") &&
                metadata.StringComparer.Equals(typeName, "ReferenceAssemblyAttribute"))
            {
                return true;
            }
        }
        return false;
    }

    private sealed record InventoryEntry(string AssemblyName, string RelativePath);
}
