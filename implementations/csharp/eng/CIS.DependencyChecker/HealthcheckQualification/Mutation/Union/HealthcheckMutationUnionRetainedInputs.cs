using System.Text.Json.Nodes;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static RetainedDiagnosticInputs? ReadRetainedDiagnosticInputs(PinnedInputs files,
        JsonObject descriptor, string[] physicalLogs, string lane)
    {
        if (!descriptor.TryGetPropertyValue("parentExtentManifest", out JsonNode? reference)) { return null; }
        Need(lane == "ubuntu" && reference is JsonObject && descriptor["kernelManifest"] is JsonObject,
            "Explicit parent extents require an Ubuntu campaign and its complete native kernel inventory.");
        string path = files.Pin(Item(reference));
        files.GuardRoot(Path.GetDirectoryName(path)!);
        JsonObject document = files.ReadDocument(path);
        string[] fields = ["schema", "originalLogRoot", "completePhysicalLogs", "mappings", "semanticContextSupplied", "qualificationClaim", "appliedKillCredit"];
        // Keep the original campaign's pinned declaration readable; future
        // writers use the stable verification schema rather than a run number.
        Need(document.Count == fields.Length && document.Select(row => row.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields)
            && String(document, "schema") is "cis.healthcheck.native-parent-extents/v1-development" or "cis.healthcheck.current194-explicit-parent-extents/v1",
            "Unknown explicit parent extent manifest.");
        Need(document["semanticContextSupplied"] is JsonValue semantic && semantic.TryGetValue(out bool semanticValue) && !semanticValue
            && document["qualificationClaim"] is JsonValue qualification && qualification.TryGetValue(out bool qualified) && !qualified
            && Integer(document, "appliedKillCredit") == 0, "A physical extent declaration cannot supply semantic or qualification credit.");
        string root = String(document, "originalLogRoot");
        JsonObject logs = files.ReadDocument(files.Pin(Item(descriptor["logsManifest"])));
        Need(Path.IsPathFullyQualified(root) && files.Resolve(root) == root && files.Resolve(String(logs, "root")) == root,
            "Explicit extents must refer to the same original physical log root.");
        files.GuardRoot(root);
        var inventory = new List<NativeDiagnosticExtentReader.Extent>();
        foreach (JsonNode? value in Items(document, "completePhysicalLogs"))
        {
            JsonObject row = Item(value);
            Need(row.Count == 3 && row.ContainsKey("Path") && row.ContainsKey("Bytes") && row.ContainsKey("Sha256"),
                "Exact original physical extent fields required.");
            var pin = new JsonObject { ["path"] = String(row, "Path"), ["bytes"] = Integer(row, "Bytes"), ["sha256"] = String(row, "Sha256") };
            inventory.Add(new(files.Pin(pin), Integer(pin, "bytes"), String(pin, "sha256")));
        }
        Need(inventory.Select(row => row.Path).Order(StringComparer.Ordinal).SequenceEqual(physicalLogs.Order(StringComparer.Ordinal)),
            "Explicit extents must declare every original physical log exactly once.");
        var parents = new Dictionary<string, IReadOnlyList<NativeDiagnosticExtentReader.Extent>>(StringComparer.Ordinal);
        foreach (JsonNode? value in Items(document, "mappings"))
        {
            JsonObject row = Item(value);
            Need(row.Count == 2 && row.ContainsKey("logicalPath") && row.ContainsKey("extents"), "Exact original parent mapping required.");
            var chain = new List<NativeDiagnosticExtentReader.Extent>();
            foreach (JsonNode? entry in Items(row, "extents"))
            {
                JsonObject pin = Item(entry);
                Need(pin.Count == 3 && pin.ContainsKey("path") && pin.ContainsKey("bytes") && pin.ContainsKey("sha256"),
                    "Exact ordered physical extent pin required.");
                chain.Add(new(files.Pin(pin), Integer(pin, "bytes"), String(pin, "sha256")));
            }
            Need(parents.TryAdd(String(row, "logicalPath"), chain), "Repeated original parent mapping.");
        }
        Need(parents.Keys.Order(StringComparer.Ordinal).SequenceEqual(physicalLogs.Where(value => value.EndsWith("-log.txt", StringComparison.Ordinal)).Order(StringComparer.Ordinal)),
            "Every original active parent must have its explicit extent mapping.");
        return new(root, inventory, parents);
    }
}
