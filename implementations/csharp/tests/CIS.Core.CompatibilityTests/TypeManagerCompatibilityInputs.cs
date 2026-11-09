using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CIS.Tests.CompatibilityTests;

internal static class TypeManagerCompatibilityInputs
{
    internal sealed record ApprovedDependency(string SourcePath, string Sha256);

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
        string version = locked["resolved"]!.GetValue<string>();
        Require(version == "10.0.12", "Approved compatibility dependency version changed: " + packageId);
        string path = Path.Combine(packages, packageId.ToLowerInvariant(), version, "lib/net10.0", packageId + ".dll");
        var identity = System.Reflection.AssemblyName.GetAssemblyName(path);
        Require(identity.Name == packageId && identity.Version == new Version(10, 0, 0, 0)
            && Convert.ToHexStringLower(identity.GetPublicKeyToken() ?? []) == "adb9793829ddae60",
            "Unexpected approved dependency assembly identity: " + packageId);
        return new(path, Hash(path));
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
