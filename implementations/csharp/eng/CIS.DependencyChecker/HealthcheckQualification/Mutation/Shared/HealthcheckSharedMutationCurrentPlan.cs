using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Verification;

// A plan establishes current selection, never native execution. Only the physical
// receiver supplies these records after pinning the current graph and inventories.
internal static class HealthcheckSharedMutationCurrentPlan
{
    internal sealed record Batch(string SourceFile, string Selection, string[] Tests, string Filter,
        string UnitFingerprint, string MutationFingerprint, string[] Dependencies);
    internal sealed record Plan(string GraphBuildId, Batch[] Batches);
    internal sealed record Decision(string SourceFile, bool Reusable, string[] Reasons);

    internal static Plan Read(string output, int exitCode, string[] expectedSources)
    {
        JsonObject value = HealthcheckSharedMutationProtocol.Parse(output);
        Fields(value, "status", "exitCode", "profileId", "graphBuildId", "statePath", "layer", "scopeComplete", "qualified",
            "executedUnitBatches", "executedMutationBatches", "reusedUnitBatches", "reusedMutationBatches", "pendingUnitBatches",
            "pendingMutationBatches", "killed", "survived", "timedOut", "noCoverage", "knownDenominator", "knownScore",
            "completeScopeScore", "thresholdMet", "durationMilliseconds", "retainedBytes", "batches", "diagnostics");
        Need(exitCode == 0 && Number(value, "exitCode") == 0 && Text(value, "status") == "planned"
            && Text(value, "layer") == "all" && !Flag(value, "qualified"), "Current comparison requires a successful idle development plan.");
        _ = Flag(value, "scopeComplete");
        _ = Flag(value, "thresholdMet");
        _ = Text(value, "profileId");
        _ = Text(value, "statePath");
        _ = Strings(value, "diagnostics", false);
        string build = Text(value, "graphBuildId");
        Need(build.Length <= 256, "Current graph identity exceeds its bound.");
        Need(expectedSources.Length is > 0 and <= 46 && expectedSources.Distinct(StringComparer.Ordinal).Count() == expectedSources.Length,
            "Current comparison source inventory is empty, duplicate or excessive.");
        JsonArray records = Array(value, "batches");
        Need(records.Count == expectedSources.Length, "Current plan omits or adds a declared source.");
        Batch[] batches = records.Select(node =>
        {
            JsonObject batch = Object(node);
            Fields(batch, "id", "sourceFile", "selection", "testFiles", "testFilter", "unitFingerprint", "mutationFingerprint",
                "unitCurrent", "mutationCurrent", "dependencyFiles");
            _ = Text(batch, "id");
            _ = Flag(batch, "unitCurrent");
            _ = Flag(batch, "mutationCurrent");
            string selection = Text(batch, "selection");
            Need(selection is "graph" or "all-tests", "Unsupported current selector.");
            string source = Portable(Text(batch, "sourceFile"));
            string[] tests = Strings(batch, "testFiles", true).Select(Portable).ToArray();
            string[] dependencies = Strings(batch, "dependencyFiles", true).Select(Portable).ToArray();
            string filter = Text(batch, "testFilter");
            Need(filter.Length <= 32768, "Current native filter exceeds its bound.");
            return new Batch(source, selection, tests, filter, Hash(batch, "unitFingerprint"), Hash(batch, "mutationFingerprint"), dependencies);
        }).ToArray();
        Need(batches.Select(batch => batch.SourceFile).ToHashSet(StringComparer.Ordinal).SetEquals(expectedSources)
            && batches.Select(batch => batch.SourceFile).Distinct(StringComparer.Ordinal).Count() == batches.Length,
            "Current plan duplicates or changes its source inventory.");
        return new(build, batches);
    }

    internal static Decision[] Compare(JsonObject original, HealthcheckSharedMutationProtocol.Input[] inputs, Plan current,
        IReadOnlyDictionary<string, string> currentHashes, string[] originalTestInventory, string[] currentTestInventory)
    {
        Need(inputs.Select(input => input.Path).Distinct(StringComparer.Ordinal).Count() == inputs.Length,
            "Original input paths are duplicated.");
        foreach (var pair in currentHashes) { Portable(pair.Key); Need(IsHash(pair.Value), "A current file hash is malformed."); }
        foreach (string path in originalTestInventory.Concat(currentTestInventory)) { Portable(path); }
        Need(originalTestInventory.Length is > 0 and <= 16384 && currentTestInventory.Length is > 0 and <= 16384
            && originalTestInventory.Distinct(StringComparer.Ordinal).Count() == originalTestInventory.Length
            && currentTestInventory.Distinct(StringComparer.Ordinal).Count() == currentTestInventory.Length,
            "Test discovery inventories contain duplicates.");
        bool inventoryMatches = originalTestInventory.ToHashSet(StringComparer.Ordinal).SetEquals(currentTestInventory);
        var oldBatches = Array(original, "batches").Select(Object).ToDictionary(batch => Text(batch, "sourceFile"), StringComparer.Ordinal);
        var closures = Array(original, "closures").Select(Object).ToDictionary(closure => Text(closure, "id"), StringComparer.Ordinal);
        Need(current.Batches.Length == oldBatches.Count && current.Batches.Select(batch => batch.SourceFile).ToHashSet(StringComparer.Ordinal).SetEquals(oldBatches.Keys),
            "Original and current plan scopes differ.");
        return current.Batches.Select(batch =>
        {
            var reasons = new List<string>();
            if (!inventoryMatches) { reasons.Add("TestDiscoveryInventoryChanged"); }
            JsonObject old = oldBatches[batch.SourceFile];
            Need(closures.TryGetValue(Text(old, "closureId"), out JsonObject? closure), "Original closure is missing.");
            string[] tests = Paths(closure!, "testInputIndices", "test");
            string[] dependencies = Paths(closure!, "dependencyInputIndices", "dependency");
            if (!tests.ToHashSet(StringComparer.Ordinal).SetEquals(batch.Tests)) { reasons.Add("SelectedTestsChanged"); }
            if (!dependencies.ToHashSet(StringComparer.Ordinal).SetEquals(batch.Dependencies)) { reasons.Add("DependencyClosureChanged"); }
            if (Text(old, "unitFingerprint") != batch.UnitFingerprint) { reasons.Add("UnitFingerprintChanged"); }
            if (Text(old, "mutationFingerprint") != batch.MutationFingerprint) { reasons.Add("MutationFingerprintChanged"); }
            if (Text(old, "testFilterSha256") != Digest(batch.Filter)) { reasons.Add("NativeFilterChanged"); }
            foreach (string path in tests.Concat(dependencies).Append(batch.SourceFile).Distinct(StringComparer.Ordinal))
            {
                var input = inputs.SingleOrDefault(input => input.Path == path);
                Need(input is not null, "Original closure/source lacks an input binding.");
                if (!currentHashes.TryGetValue(path, out string? sha256) || input!.Sha256 != sha256) { reasons.Add("CurrentInputChanged:" + path); }
            }
            return new Decision(batch.SourceFile, reasons.Count == 0, reasons.ToArray());
        }).ToArray();

        string[] Paths(JsonObject closure, string name, string role)
        {
            long[] indices = Array(closure, name).Select(node => Integer(node)).ToArray();
            Need(indices.Distinct().Count() == indices.Length && indices.All(index => index >= 0 && index < inputs.Length
                && inputs[(int)index].Roles.Contains(role, StringComparer.Ordinal)), "Original closure indices are invalid.");
            return indices.Select(index => Portable(inputs[(int)index].Path)).ToArray();
        }
    }

    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })));
    private static string Portable(string path)
    {
        Need(path.Length <= 4096 && !Path.IsPathFullyQualified(path) && !path.Contains('\\') && !path.Contains(':')
            && path.Split('/').All(part => part.Length > 0 && part is not ("." or "..")), "Unsafe current comparison path.");
        return path;
    }
    private static string Hash(JsonObject value, string name)
    { string result = Text(value, name); Need(IsHash(result), "Malformed current fingerprint."); return result; }
    private static bool IsHash(string value) => value.Length == 64 && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static string Text(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out string? text) && !string.IsNullOrEmpty(text)
        ? text : throw new InvalidDataException("Expected current comparison text: " + name);
    private static bool Flag(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out bool flag)
        ? flag : throw new InvalidDataException("Expected current comparison boolean: " + name);
    private static long Number(JsonObject value, string name) => Integer(value[name]);
    private static long Integer(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out long number)) { return number; }
        if (node is JsonValue integer && integer.TryGetValue(out int small)) { return small; }
        throw new InvalidDataException("Expected current comparison integer.");
    }
    private static string[] Strings(JsonObject value, string name, bool required)
    {
        JsonArray array = Array(value, name);
        Need(array.Count <= 16384 && (!required || array.Count > 0), "Current comparison array is empty or excessive.");
        string[] result = array.Select(node => node is JsonValue item && item.TryGetValue(out string? text) && !string.IsNullOrEmpty(text)
            ? text : throw new InvalidDataException("Expected current comparison string.")).ToArray();
        Need(result.Distinct(StringComparer.Ordinal).Count() == result.Length, "Current comparison array is duplicated.");
        return result;
    }
    private static JsonArray Array(JsonObject value, string name) => value[name] as JsonArray ?? throw new InvalidDataException("Expected current comparison array: " + name);
    private static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new InvalidDataException("Expected current comparison object.");
    private static void Fields(JsonObject value, params string[] fields) => Need(value.Count == fields.Length && fields.All(value.ContainsKey), "Unknown or missing current plan fields.");
    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
