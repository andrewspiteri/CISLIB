using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CIS.Tests.CompatibilityTests;

internal static class TypeManagerCompatibilityInputs
{
    internal sealed record ApprovedDependency(string FileName, string PackageId, string PackageVersion,
        string SourcePath, string ArchivePath, string ContentHash, string Sha256, string ArchiveSha256,
        string RestoreMetadataPath, string RestoreMetadataSha256, string ArchiveSha512Path, string ArchiveSha512FileSha256,
        string RawArchiveSha512);

    internal const string OriginalHash = "4fd1124533971344d0ca362056450553c5eb0cea2c46e28780cc6301d1ed49d1";
    internal const string BehaviorHash = "d5d26e9f528b3306b8437b9564da6797d660a191dc1f04162d5475ac283e4228";
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    internal static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidDataException(message); } }

    internal static ApprovedDependency DependencyAsset(string packages, string packageId, JsonObject locked)
    {
        Require(packageId is "Microsoft.Extensions.Logging.Abstractions" or "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Unapproved compatibility dependency: " + packageId);
        const string version = "10.0.12";
        Require(locked["resolved"]?.GetValue<string>() == version, "Approved compatibility dependency version changed: " + packageId);
        string contentHash = locked["contentHash"]!.GetValue<string>();
        string id = packageId.ToLowerInvariant(), fileName = packageId + ".dll";
        string root = Path.GetFullPath(Path.Combine(packages, id, version));
        string source = Path.Combine(root, "lib", "net10.0", fileName), archive = Path.Combine(root, id + "." + version + ".nupkg");
        string metadataPath = Path.Combine(root, ".nupkg.metadata"), sha512Path = archive + ".sha512";
        byte[] archiveBytes = File.ReadAllBytes(archive), metadataBytes = File.ReadAllBytes(metadataPath), sha512Bytes = File.ReadAllBytes(sha512Path);
        JsonObject restored = JsonNode.Parse(metadataBytes)!.AsObject();
        // Signed NuGet package content hashes differ from raw archive hashes.
        // The pinned locked-restore metadata is the selected restore attestation;
        // this fixture does not recompute the signed-package content hash.
        Require(restored["version"]?.GetValue<int>() == 2 && restored["contentHash"]?.GetValue<string>() == contentHash,
            "Dependency restore attestation differs from the lock: " + packageId);
        string rawArchiveSha512 = Convert.ToBase64String(SHA512.HashData(archiveBytes));
        Require(rawArchiveSha512 == File.ReadAllText(sha512Path).Trim(), "Dependency archive differs from its restored SHA-512: " + packageId);
        using var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        ZipArchiveEntry[] entries = zip.Entries.Where(entry => entry.FullName == "lib/net10.0/" + fileName).ToArray();
        Require(entries.Length == 1, "Expected exactly one approved dependency asset: " + packageId);
        using Stream assetStream = entries[0].Open();
        string expected = Convert.ToHexStringLower(SHA256.HashData(assetStream));
        Require(Hash(source) == expected, "Dependency asset differs from its locked archive: " + packageId);
        var identity = System.Reflection.AssemblyName.GetAssemblyName(source);
        Require(identity.Name == packageId && identity.Version == new Version(10, 0, 0, 0)
            && Convert.ToHexStringLower(identity.GetPublicKeyToken() ?? []) == "adb9793829ddae60", "Unexpected approved dependency assembly identity: " + packageId);
        return new(fileName, packageId, version, source, archive, contentHash, expected,
            Convert.ToHexStringLower(SHA256.HashData(archiveBytes)), metadataPath, Convert.ToHexStringLower(SHA256.HashData(metadataBytes)),
            sha512Path, Convert.ToHexStringLower(SHA256.HashData(sha512Bytes)), rawArchiveSha512);
    }

    internal static string Within(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string check = Path.GetRelativePath(root, path);
        Require(!Path.IsPathRooted(check) && check != ".." && !check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Input escapes its root: " + relative);
        // Reject links as well as lexical traversal; frozen inputs must be actual owned files.
        for (string? at = path; at != Path.GetFullPath(root); at = Path.GetDirectoryName(at))
        {
            Require(at is not null, "Invalid input root.");
            if (File.Exists(at) || Directory.Exists(at))
            { Require((File.GetAttributes(at!) & FileAttributes.ReparsePoint) == 0, "Linked input: " + relative); }
        }
        return path;
    }

    internal static Dictionary<string, string> Baseline(string root, string expected)
    {
        string manifest = Path.Combine(root, "baseline-hashes.json");
        Require(Hash(manifest) == expected, "Baseline manifest differs from its independently selected hash.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonNode? item in Read(manifest)["files"]!.AsArray())
        {
            string relative = item!["path"]!.GetValue<string>();
            Require(hashes.TryAdd(relative, item["sha256"]!.GetValue<string>()), "Duplicate baseline path.");
            Require(Hash(Within(root, relative)) == hashes[relative], "Frozen baseline changed: " + relative);
        }
        string[] actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path => path != manifest)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToArray();
        Require(hashes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(actual), "Baseline file inventory differs.");
        return hashes;
    }

    internal static Dictionary<string, string> ModuleSources(string language)
    {
        string core = Path.Combine(language, "src/CIS.Core");
        return Directory.GetFiles(Path.Combine(core, "TypeManagement"), "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(core, path).Replace('\\', '/'), Hash, StringComparer.Ordinal);
    }

    internal static void Producer(JsonObject producer, string candidate, string expected, Dictionary<string, string> sources)
    {
        Require(Hash(candidate) == expected && producer["complete"]?.ToJsonString() == "true" && producer["coreSha256"]?.GetValue<string>() == expected,
            "Candidate provenance does not identify this completed runtime candidate.");
        JsonObject recorded = producer["coreSourceInputs"]!.AsObject();
        Require(sources.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(recorded.Select(pair => pair.Key).Where(path => path.StartsWith("TypeManagement/", StringComparison.Ordinal))), "Module source file set differs from the candidate record.");
        foreach ((string path, string hash) in sources)
        { Require(recorded[path]?.GetValue<string>() == hash, "Control source differs from recorded candidate source: " + path); }
    }

    // The default development candidate is the assembly actually loaded by the native test project.
    // Bind its portable PDB to its PE and every TypeManagement document to the current source bytes.
    internal static JsonObject ProjectProducer(string candidate, Dictionary<string, string> sources)
    {
        string pdbPath = Path.ChangeExtension(candidate, ".pdb");
        using var pe = new PEReader(File.OpenRead(candidate));
        DebugDirectoryEntry codeView = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
        MetadataReader reader = provider.GetMetadataReader();
        var id = new BlobContentId(reader.DebugMetadataHeader!.Id);
        Require(pe.ReadCodeViewDebugDirectoryData(codeView).Guid == id.Guid && codeView.Stamp == id.Stamp, "Candidate and PDB identities differ.");
        DebugDirectoryEntry checksum = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum);
        PdbChecksumDebugDirectoryData data = pe.ReadPdbChecksumDebugDirectoryData(checksum);
        // PE-COFF portable-PDB checksums exclude the 20-byte PDB ID, unlike a file inventory hash.
        // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PE-COFF.md#portable-pdb-checksum
        byte[] checksumBytes = File.ReadAllBytes(pdbPath);
        checksumBytes.AsSpan(reader.DebugMetadataHeader.IdStartOffset, 20).Clear();
        Require(data.AlgorithmName == "SHA256" && data.Checksum.AsSpan().SequenceEqual(SHA256.HashData(checksumBytes)), "Candidate PDB checksum differs.");
        var recorded = new JsonObject();
        foreach (DocumentHandle handle in reader.Documents)
        {
            Document document = reader.GetDocument(handle);
            string path = reader.GetString(document.Name).Replace('\\', '/');
            const string marker = "/TypeManagement/";
            int index = path.LastIndexOf(marker, StringComparison.Ordinal);
            if (index < 0) { continue; }
            string relative = path[(index + 1)..];
            Require(reader.GetGuid(document.HashAlgorithm) == new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), "Expected SHA256 source checksums.");
            Require(recorded.TryAdd(relative, JsonValue.Create(Convert.ToHexStringLower(reader.GetBlobBytes(document.Hash)))), "Duplicate module PDB document.");
        }
        var result = new JsonObject { ["complete"] = true, ["coreSha256"] = Hash(candidate), ["coreSourceInputs"] = recorded, ["pdbSha256"] = Hash(pdbPath), ["scope"] = "Native project build; not package/release qualification" };
        Producer(result, candidate, Hash(candidate), sources);
        return result;
    }

    internal static bool Same(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null) { return left is null && right is null; }
        if (left.GetValueKind() != right.GetValueKind()) { return false; }
        if (left is JsonObject a && right is JsonObject b)
        { return a.Count == b.Count && a.All(pair => b.ContainsKey(pair.Key) && Same(pair.Value, b[pair.Key])); }
        if (left is JsonArray x && right is JsonArray y)
        { return x.Count == y.Count && x.Zip(y).All(pair => Same(pair.First, pair.Second)); }
        if (left.GetValueKind() == System.Text.Json.JsonValueKind.Number)
        {
            // Frozen expectations distinguish integral JSON values from fractional/exponent notation.
            bool Integer(JsonNode node) => !node.ToJsonString().Any(c => c is '.' or 'e' or 'E');
            if (Integer(left) != Integer(right)) { return false; }
        }
        return JsonNode.DeepEquals(left, right);
    }
}
