using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private sealed class PinnedInputs(string repository, string output)
    {
        internal string Repository { get; } = Path.GetFullPath(repository);
        private readonly Dictionary<string, (string Hash, long Bytes)> pins = new(StringComparer.Ordinal);
        private readonly List<Action> inventoryChecks = [];
        internal Dictionary<string, JsonObject> SnapshotPins { get; } = new(StringComparer.Ordinal);

        internal void TrackInventory(string root, Func<string, bool>? include = null)
        {
            GuardRoot(root);
            string[] Read() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => include?.Invoke(path) ?? true).Order(StringComparer.Ordinal).ToArray();
            string[] original = Read();
            inventoryChecks.Add(() => Need(original.SequenceEqual(Read()), "Physical input directory inventory changed during validation: " + root));
        }

        internal void GuardRoot(string root)
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            NoLinks(full);
            NoLinks(destination);
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Need(!destination.StartsWith(full, comparison) && !full.StartsWith(destination, comparison), "Output must be separate from immutable input roots.");
        }

        internal string Resolve(string path)
        {
            string full = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(Repository, Portable(path)));
            NoLinks(full);
            return full;
        }

        private static void NoLinks(string path)
        {
            // Textual containment does not identify the physical destination of
            // a junction/symlink. Check every existing ancestor before reading
            // inputs or creating output, and again before closing the receipt.
            for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                { Need((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Linked input/output paths are forbidden: " + current); }
            }
        }
        internal string Pin(JsonObject descriptor)
        {
            string path = Resolve(String(descriptor, "path")), hash = String(descriptor, "sha256");
            string destination = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Need(!path.StartsWith(destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "An input is inside the output directory.");
            Need(hash.Length == 64 && hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'), "Malformed SHA-256 input pin.");
            long bytes = Integer(descriptor, "bytes");
            Need(bytes >= 0 && new FileInfo(path).Length == bytes && Hash(path) == hash, "Missing or changed pinned input: " + path);
            Add(path, hash, bytes);
            return path;
        }
        internal JsonObject ReadDocument(string path)
        {
            path = Resolve(path);
            Need(new FileInfo(path).Length <= 128L * 1024 * 1024, "JSON input exceeds the bounded document size.");
            string hash = Hash(path);
            Add(path, hash, new FileInfo(path).Length);
            byte[] bytes = File.ReadAllBytes(path);
            // JsonNode overwrites duplicate object keys lazily. Reject them before
            // allowing any path, identity, selector or source pin to be read.
            using JsonDocument document = JsonDocument.Parse(bytes);
            Unique(document.RootElement);
            return JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Expected JSON object: " + path);
        }
        private void Add(string path, string hash, long bytes)
        {
            Need(!pins.TryGetValue(path, out var previous) || previous == (hash, bytes), "Conflicting pins for one physical input.");
            pins[path] = (hash, bytes);
        }
        internal void VerifyAgain()
        {
            NoLinks(output);
            foreach ((string path, var value) in pins)
            {
                NoLinks(path);
                Need(new FileInfo(path).Length == value.Bytes && Hash(path) == value.Hash, "Input changed during validation: " + path);
            }
            foreach (Action check in inventoryChecks) { check(); }
        }
        internal JsonArray Inventory() => new(pins.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (JsonNode)new JsonObject
        { ["path"] = p.Key, ["sha256"] = p.Value.Hash, ["bytes"] = p.Value.Bytes }).ToArray());
    }

    private static Source[] ReadSources(PinnedInputs files, JsonObject manifest)
    {
        string root = files.Resolve(String(manifest, "snapshotRoot"));
        files.TrackInventory(root);
        JsonObject snapshot = files.ReadDocument(files.Pin(Item(manifest["sourceManifest"])));
        Need(String(snapshot, "schema") == "cis.healthcheck.selected-source-inputs/v1-development", "Unsupported frozen source inventory.");
        JsonObject[] pins = Items(snapshot, "files").Select(Item).ToArray();
        Need(pins.Length > 0 && pins.Select(p => String(p, "path")).Distinct(StringComparer.Ordinal).Count() == pins.Length, "Empty or duplicate frozen source inventory.");
        foreach (JsonObject pin in pins)
        {
            string relative = Portable(String(pin, "path"));
            files.SnapshotPins.Add(String(pin, "path"), pin);
            var physical = (JsonObject)pin.DeepClone(); physical["path"] = Path.Combine(root, relative);
            files.Pin(physical);
        }
        string records = string.Concat(pins.OrderBy(p => String(p, "path"), StringComparer.Ordinal)
            .Select(p => String(p, "path") + "\t" + String(p, "sha256") + "\n"));
        Need(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(records))) == String(snapshot, "inputsSha256"), "Frozen source inventory digest differs.");
        string currentCore = Path.Combine(files.Repository, Portable(CorePrefix.TrimEnd('/')));
        files.TrackInventory(currentCore, p => p.EndsWith(".cs", StringComparison.Ordinal)
            && !Path.GetRelativePath(currentCore, p).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"));
        string[] physicalCore = Directory.EnumerateFiles(currentCore, "*.cs", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(currentCore, p).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .Select(p => CorePrefix + Path.GetRelativePath(currentCore, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        JsonObject[] corePins = pins.Where(p => String(p, "path").StartsWith(CorePrefix, StringComparison.Ordinal) && String(p, "path").EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        Need(physicalCore.SequenceEqual(corePins.Select(p => String(p, "path")).Order(StringComparer.Ordinal)), "Frozen/current Core source inventories differ.");
        foreach (JsonObject pin in corePins) { files.Pin(pin); }
        // Shipping compilation depends on the generator/build closure as well
        // as Core text. Test additions may qualify old definitions separately;
        // they do not alter the original report's native test cohort.
        foreach (JsonObject pin in pins.Where(p => ShippingInput(String(p, "path")))) { files.Pin(pin); }
        var shipping = new List<string>();
        foreach (string relativeRoot in new[] { "implementations/csharp/src", "implementations/csharp/build", "implementations/csharp/eng/CIS.TypeManager.Build" })
        {
            string shippingRoot = Path.Combine(files.Repository, Portable(relativeRoot));
            bool Include(string path) => !Path.GetRelativePath(shippingRoot, path).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")
                && ShippingInput(Path.GetRelativePath(files.Repository, path).Replace('\\', '/'));
            files.TrackInventory(shippingRoot, Include);
            shipping.AddRange(Directory.EnumerateFiles(shippingRoot, "*", SearchOption.AllDirectories).Where(Include)
                .Select(p => Path.GetRelativePath(files.Repository, p).Replace('\\', '/')));
        }
        string[] expectedShipping = pins.Select(p => String(p, "path")).Where(p => ShippingInput(p)
            && (p.StartsWith("implementations/csharp/src/", StringComparison.Ordinal) || p.StartsWith("implementations/csharp/build/", StringComparison.Ordinal)
                || p.StartsWith("implementations/csharp/eng/CIS.TypeManager.Build/", StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToArray();
        Need(shipping.Order(StringComparer.Ordinal).SequenceEqual(expectedShipping), "Current/frozen shipping build input inventories differ.");
        var sources = new List<Source>();
        foreach (string group in Groups)
        {
            JsonObject config = files.ReadDocument(MaintainedConfig(files, group));
            string configRelative = "planning/healthcheck/qualification/gate-closure/" + group + ".json";
            Need(files.SnapshotPins.TryGetValue(configRelative, out JsonObject? configPin), "Frozen snapshot lacks its whole maintained configuration.");
            files.Pin(configPin);
            foreach (JsonNode? node in Items(Item(config["stryker-config"]), "mutate"))
            {
                string relative = node?.GetValue<string>() ?? throw new InvalidDataException("Invalid configured mutation source.");
                Portable(relative);
                JsonObject pin = corePins.Single(p => String(p, "path") == CorePrefix + relative);
                string text = ReadUtf8(Path.Combine(root, Portable(CorePrefix + relative)));
                sources.Add(new Source(relative, String(pin, "sha256"), group, text));
            }
        }
        Need(sources.Select(s => s.Path).Distinct(StringComparer.Ordinal).Count() == sources.Count
            && sources.Select(s => CorePrefix + s.Path).Order(StringComparer.Ordinal).SequenceEqual(physicalCore.Where(p => p.StartsWith(CorePrefix + "Healthcheck/", StringComparison.Ordinal))), "Configured scopes must disjointly cover every Healthcheck source.");
        return sources.ToArray();
    }

    private static string MaintainedConfig(PinnedInputs files, string group) =>
        Path.Combine(files.Repository, "planning", "healthcheck", "qualification", "gate-closure", group + ".json");

    private static bool ShippingInput(string path) => ((path.StartsWith("implementations/csharp/src/", StringComparison.Ordinal)
        || path.StartsWith("implementations/csharp/build/", StringComparison.Ordinal)
        || path.StartsWith("implementations/csharp/eng/CIS.TypeManager.Build/", StringComparison.Ordinal))
        && Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".config")
        || path is "implementations/csharp/global.json" or "implementations/csharp/Directory.Build.props"
            or "implementations/csharp/Directory.Build.targets" or "implementations/csharp/Directory.Packages.props"
            or "implementations/csharp/NuGet.config" or ".editorconfig";

    private static JsonObject ValidateConfiguration(PinnedInputs files, JsonObject descriptor)
    {
        string group = String(descriptor, "group");
        JsonObject original = files.ReadDocument(files.Pin(Item(descriptor["configuration"])));
        JsonObject maintained = files.ReadDocument(MaintainedConfig(files, group));
        HealthcheckMutationCampaignConfiguration.Validate(group, original, maintained);
        return original;
    }

    private static string[] ReadLogs(PinnedInputs files, JsonObject descriptor)
    {
        JsonObject inventory = files.ReadDocument(files.Pin(Item(descriptor["logsManifest"])));
        Need(String(inventory, "schema") == "cis.healthcheck.native-log-inventory/v1-development", "Unknown native log inventory.");
        string root = files.Resolve(String(inventory, "root"));
        files.TrackInventory(root);
        JsonObject[] entries = Items(inventory, "files").Select(Item).ToArray();
        string[] paths = entries.Select(p => String(p, "path")).ToArray();
        Need(paths.Length > 0 && paths.Distinct(StringComparer.Ordinal).Count() == paths.Length, "Empty/duplicate native log inventory.");
        // Include every physical file, including .txt diagnostics and process traces;
        // choosing a filtered list could conceal an aborted sibling host.
        string[] actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Need(actual.SequenceEqual(paths.Order(StringComparer.Ordinal)), "Native log inventory is incomplete or contains foreign files.");
        return entries.Select(entry =>
        {
            var pin = (JsonObject)entry.DeepClone(); pin["path"] = Path.Combine(root, Portable(String(entry, "path")));
            return files.Pin(pin);
        }).ToArray();
    }

    private static string ReadUtf8(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int start = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
    }
    private static string Hash(string path)
    { using FileStream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string Portable(string path)
    {
        Need(!string.IsNullOrWhiteSpace(path) && !path.Contains('\\') && !path.Contains(':') && !path.StartsWith('/')
            && path.Split('/').All(s => s.Length > 0 && s is not ("." or "..")), "Unsafe portable input path.");
        return path.Replace('/', Path.DirectorySeparatorChar);
    }
    private static string String(JsonObject value, string name) => value[name] is JsonValue node && node.TryGetValue(out string? text) && text is not null
        ? text : throw new InvalidDataException("Missing/malformed string: " + name);
    private static long Integer(JsonObject value, string name) => value[name] is JsonValue node && node.TryGetValue(out long number)
        ? number : throw new InvalidDataException("Missing/malformed integer: " + name);
    private static JsonArray Items(JsonObject value, string name) => value[name] as JsonArray ?? throw new InvalidDataException("Missing/malformed array: " + name);
    private static JsonObject Item(JsonNode? node) => node as JsonObject ?? throw new InvalidDataException("Expected JSON object.");
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            { Need(names.Add(property.Name), "Duplicate JSON property: " + property.Name); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement child in value.EnumerateArray()) { Unique(child); } }
    }
    private static void Need([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) { throw new InvalidDataException(message); } }
}
