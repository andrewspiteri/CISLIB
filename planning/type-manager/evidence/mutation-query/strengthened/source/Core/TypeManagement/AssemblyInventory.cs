using System.Reflection;
using System.Text.Json;

namespace CIS.TypeManagement;

// Owned validated selections. No type loading, directory scanning or fallback discovery occurs here.
internal sealed class AssemblyInventory
{
    private AssemblyInventory(string manifestPath, IReadOnlyList<SelectedAssembly> assemblies)
    {
        ManifestPath = manifestPath;
        Assemblies = assemblies;
    }

    internal string ManifestPath { get; }
    internal IReadOnlyList<SelectedAssembly> Assemblies { get; }

    internal static async Task<AssemblyInventory> ReadAsync(string manifestPath, Assembly hostAssembly,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Invalid(manifestPath, "The required assembly inventory could not be read.", error);
        }

        try
        {
            ReadOnlyMemory<byte> json = bytes;
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { json = json[3..]; }
            using var document = JsonDocument.Parse(json);
            ValidateUniqueProperties(document.RootElement, cancellationToken);
            var root = document.RootElement;
            RequireKind(root, JsonValueKind.Object, "inventory");
            if (!root.TryGetProperty("schemaVersion", out var version))
            {
                throw new FormatException("Required inventory schemaVersion is missing.");
            }
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            {
                throw new FormatException($"Expected integer inventory schemaVersion 1; observed {version.ValueKind}.");
            }
            if (number != 1)
            {
                throw new FormatException($"Unsupported inventory schemaVersion {number}; expected 1.");
            }
            string hostIdentity = ReadString(root, "hostAssembly");
            if (!string.Equals(hostIdentity, hostAssembly.FullName, StringComparison.Ordinal))
            {
                throw new TypeManagerInitializationException(TypeManagerFailureKind.InvalidInventory,
                    "The inventory hostAssembly does not match the supplied host assembly.",
                    new FormatException("Host identity mismatch."), manifestPath: manifestPath,
                    assemblyIdentity: hostIdentity, actualAssemblyIdentity: hostAssembly.FullName);
            }
            if (!root.TryGetProperty("assemblies", out var entries))
            {
                throw new FormatException("Required assemblies list is missing.");
            }
            RequireKind(entries, JsonValueKind.Array, "assemblies");
            if (entries.GetArrayLength() == 0)
            {
                throw new FormatException("The assemblies list must not be empty.");
            }

            string directory = Path.GetDirectoryName(manifestPath)!;
            var byName = new Dictionary<string, SelectedAssembly>(StringComparer.OrdinalIgnoreCase);
            var byPath = new Dictionary<string, SelectedAssembly>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            bool containsHost = false;
            foreach (var entry in entries.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? identity = null;
                string? assetPath = null;
                try
                {
                    RequireKind(entry, JsonValueKind.Object, "assembly entry");
                    identity = ReadString(entry, "assemblyName");
                    string relativePath = ReadString(entry, "relativePath");
                    assetPath = relativePath;
                    AssemblyName name;
                    try
                    {
                        name = new AssemblyName(identity);
                    }
                    catch (Exception error) when (error is ArgumentException or FileLoadException)
                    {
                        throw Invalid(manifestPath, "Invalid selected assembly identity.", error, assetPath: assetPath, assemblyIdentity: identity);
                    }
                    if (string.IsNullOrWhiteSpace(name.Name) || name.Version is null ||
                        name.CultureName is null || name.GetPublicKeyToken() is null ||
                        !string.Equals(name.FullName, identity, StringComparison.Ordinal))
                    {
                        throw new FormatException($"Expected a complete canonical assembly identity: {identity}");
                    }
                    string path = ResolvePath(directory, relativePath);
                    assetPath = path;
                    if (!File.Exists(path))
                    {
                        throw Invalid(manifestPath, "A required selected assembly file is missing or inaccessible.",
                            new FileNotFoundException("Required selected assembly was not found.", path), path, identity);
                    }
                    var selected = new SelectedAssembly(identity, name.Name, path);
                    if (byName.TryGetValue(name.Name, out var previous))
                    {
                        if (string.Equals(previous.Identity, identity, StringComparison.Ordinal) && byPath.Comparer.Equals(previous.Path, path))
                        {
                            continue;
                        }
                        throw Invalid(manifestPath, "Conflicting selections have the same simple assembly name.",
                            new FormatException($"Conflicting selections: {previous.Identity} and {identity}."), path, identity);
                    }
                    if (byPath.TryGetValue(path, out previous) && previous != selected)
                    {
                        throw Invalid(manifestPath, "Conflicting identities select the same assembly file.",
                            new FormatException($"Conflicting selections: {previous.Identity} and {identity}."), path, identity);
                    }
                    byName[name.Name] = selected;
                    byPath[path] = selected;
                    containsHost |= string.Equals(identity, hostIdentity, StringComparison.Ordinal);
                }
                catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
                {
                    throw Invalid(manifestPath, "The selected inventory entry is invalid: " + error.Message,
                        error, assetPath, identity);
                }
            }
            if (!containsHost)
            {
                throw new FormatException("The assemblies list must contain the host assembly.");
            }
            var selectedAssemblies = byName.Values.OrderBy(item => item.Identity, StringComparer.Ordinal).ToArray();
            return new AssemblyInventory(manifestPath, Array.AsReadOnly(selectedAssemblies));
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw Invalid(manifestPath, "The assembly inventory is invalid: " + error.Message, error);
        }
    }

    private static string ResolvePath(string directory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') || relativePath.Contains('\\') ||
            relativePath.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." ||
                (OperatingSystem.IsWindows() && (segment.EndsWith('.') || segment.EndsWith(' ')))))
        {
            throw new FormatException($"Expected a normalized relative assembly path: {relativePath}");
        }
        string path = Path.GetFullPath(relativePath, directory);
        string check = Path.GetRelativePath(directory, path);
        if (Path.IsPathRooted(check) || check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new FormatException($"Selected assembly path escapes the inventory directory: {relativePath}");
        }
        return path;
    }

    private static string ReadString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Expected a nonblank string for {property}.");
        }
        return value.GetString()!;
    }

    private static void RequireKind(JsonElement value, JsonValueKind kind, string description)
    {
        if (value.ValueKind != kind)
        {
            throw new FormatException($"Expected {description} to be {kind}.");
        }
    }

    private static void ValidateUniqueProperties(JsonElement value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new FormatException($"Duplicate JSON property: {property.Name}");
                }
                ValidateUniqueProperties(property.Value, cancellationToken);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in value.EnumerateArray())
            {
                ValidateUniqueProperties(element, cancellationToken);
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            // JsonDocument defers string decoding; unknown fields must still contain valid UTF-8/Unicode.
            _ = value.GetString();
        }
    }

    private static TypeManagerInitializationException Invalid(string manifestPath, string message, Exception inner,
        string? assetPath = null, string? assemblyIdentity = null)
    {
        return new TypeManagerInitializationException(TypeManagerFailureKind.InvalidInventory, message, inner,
            manifestPath: manifestPath, assetPath: assetPath, assemblyIdentity: assemblyIdentity);
    }
}

internal sealed record SelectedAssembly(string Identity, string SimpleName, string Path);
