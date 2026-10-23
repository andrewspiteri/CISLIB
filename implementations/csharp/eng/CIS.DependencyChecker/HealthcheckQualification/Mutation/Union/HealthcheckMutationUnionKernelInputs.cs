using System.Text.Json.Nodes;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private static HealthcheckMutationNativeKernelEvidence? ReadKernelInputs(PinnedInputs files, JsonObject descriptor, string lane)
    {
        if (!descriptor.TryGetPropertyValue("kernelManifest", out JsonNode? reference)) { return null; }
        Need(reference is JsonObject && lane == "ubuntu" && String(descriptor, "group") == "unit-runtime", "Optional kernel evidence currently supports only standard Ubuntu Unit discovery.");
        JsonObject document = files.ReadDocument(files.Pin(Item(reference)));
        bool projected = String(document, "schema") == "cis.healthcheck.native-kernel-inventory/v2-process-projection";
        string[] fields = projected ? ["schema", "root", "nativeLogRoot", "dotnet", "files", "projection"] : ["schema", "root", "nativeLogRoot", "dotnet", "files"];
        Need(document.Count == fields.Length && document.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields)
            && (projected || String(document, "schema") == "cis.healthcheck.native-kernel-inventory/v1-development"), "Unsupported native kernel inventory.");
        string root = files.Resolve(String(document, "root")); files.GuardRoot(root);
        JsonObject logInventory = files.ReadDocument(files.Pin(Item(descriptor["logsManifest"])));
        string logRoot = files.Resolve(String(logInventory, "root"));
        JsonObject executable = Item(document["dotnet"]);
        Need(executable.Count == 2 && executable.ContainsKey("file") && executable.ContainsKey("nativePath"), "Kernel executable requires its physical pin and native origin.");
        JsonObject executablePin = Item(executable["file"]); string dotnet = files.Pin(executablePin);
        var pins = new List<HealthcheckMutationNativeKernelEvidence.Pin>();
        foreach (JsonNode? node in Items(document, "files"))
        {
            JsonObject pin = (JsonObject)Item(node).DeepClone(); string relative = String(pin, "path");
            Need(relative == Path.GetFileName(relative) && relative.StartsWith("actual-process.", StringComparison.Ordinal), "Kernel inventory entries must name actual top-level process traces.");
            pin["path"] = Path.Combine(root, Portable(relative)); string path = files.Pin(pin);
            pins.Add(new(path, String(pin, "sha256"), Integer(pin, "bytes")));
        }
        Need(pins.Count > 0 && pins.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == pins.Count
            && Directory.EnumerateFiles(root, "actual-process.*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).SequenceEqual(pins.Select(pin => pin.Path).Order(StringComparer.Ordinal)), "Kernel process inventory is empty, duplicate or incomplete.");
        files.TrackInventory(root, path => Path.GetDirectoryName(path) == root && Path.GetFileName(path).StartsWith("actual-process.", StringComparison.Ordinal));
        if (projected)
        {
            JsonObject origin = Item(document["projection"]);
            Need(origin.Count == 7 && origin.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["sourceRoot", "nativeRoot", "results", "sink", "capture", "sourceManifest", "origins"]), "Unsupported process-origin input shape.");
            string primaryRoot = files.Resolve(String(origin, "sourceRoot")), nativeRoot = String(origin, "nativeRoot"); files.GuardRoot(primaryRoot);
            Need(String(document, "nativeLogRoot") == nativeRoot + "/native/logs", "Derived process and diagnostic native roots differ.");
            HealthcheckMutationNativeProcessProjection.Pin CapturePin(string field)
            {
                JsonObject pin = Item(origin[field]); string path = files.Pin(pin);
                return new(path, String(pin, "sha256"), Integer(pin, "bytes"));
            }
            var results = CapturePin("results"); var sink = CapturePin("sink"); var capture = CapturePin("capture");
            var manifest = CapturePin("sourceManifest"); var origins = CapturePin("origins");
            Need(results.Path == Path.Combine(primaryRoot, "results.json") && sink.Path == Path.Combine(primaryRoot, "wire", "sink-result.json")
                && capture.Path == Path.Combine(primaryRoot, "wire", "capture-process.json") && manifest.Path == Path.Combine(primaryRoot, "source-inputs.json")
                && origins.Path == Path.Combine(Path.GetDirectoryName(root)!, "process-origins.jsonl"), "Process-origin physical role mapping differs.");
            JsonObject sinkDocument = files.ReadDocument(sink.Path);
            var primary = new List<HealthcheckMutationNativeProcessProjection.Pin>();
            foreach (JsonNode? member in Items(sinkDocument, "members"))
            {
                JsonObject row = Item(member); string name = Path.GetFileName(String(row, "path"));
                Need(name.StartsWith("trace-", StringComparison.Ordinal) && name.EndsWith(".strace.gz", StringComparison.Ordinal), "Unsupported primary member name.");
                string path = Path.Combine(primaryRoot, "wire", name);
                var pin = new JsonObject { ["path"] = path, ["sha256"] = String(row, "compressedSha256"), ["bytes"] = Integer(row, "compressedBytes") };
                primary.Add(new(files.Pin(pin), String(pin, "sha256"), Integer(pin, "bytes")));
            }
            string wire = Path.Combine(primaryRoot, "wire");
            files.TrackInventory(wire, path => Path.GetDirectoryName(path) == wire && Path.GetFileName(path).StartsWith("trace-", StringComparison.Ordinal) && path.EndsWith(".strace.gz", StringComparison.Ordinal));
            var input = new HealthcheckMutationNativeProcessProjection.Input(primaryRoot, nativeRoot, results, sink, capture, manifest, origins, primary.ToArray());
            var verification = HealthcheckMutationNativeProcessProjection.Read(input,
                pins.Select(pin => new HealthcheckMutationNativeProcessProjection.Pin(pin.Path, pin.Sha256, pin.Bytes)).ToArray());
            return HealthcheckMutationNativeKernelEvidence.ReadProjected(verification,
                new(new(dotnet, String(executablePin, "sha256"), Integer(executablePin, "bytes")), String(executable, "nativePath")),
                logRoot, String(document, "nativeLogRoot"));
        }
        return HealthcheckMutationNativeKernelEvidence.Read(pins,
            new(new(dotnet, String(executablePin, "sha256"), Integer(executablePin, "bytes")), String(executable, "nativePath")),
            logRoot, String(document, "nativeLogRoot"));
    }
}
