using System.Text.Json.Nodes;

namespace CIS.Verification;

internal static class HealthcheckSharedMutationConfiguration
{
    internal static void Validate(string group, JsonObject native, JsonObject maintained, string[] selectedFiles)
    {
        string[] allowed = ["project", "test-projects", "test-case-filter", "test-runner", "target-framework", "configuration", "mutate",
            "mutation-level", "reporters", "coverage-analysis", "concurrency", "disable-bail", "additional-timeout", "thresholds", "disable-mix-mutants"];
        Need(native.All(property => allowed.Contains(property.Key, StringComparer.Ordinal)), "Unapproved native ignore/filter/tool override.");
        Need(group is "unit-runtime" or "pipes" or "aspnet", "Unknown shared qualification partition.");
        string[] scope = Strings(native, "mutate");
        string[] complete = Strings(maintained, "mutate");
        Need(selectedFiles.Length > 0 && selectedFiles.Distinct(StringComparer.Ordinal).Count() == selectedFiles.Length
            && complete.Length > 0 && complete.Distinct(StringComparer.Ordinal).Count() == complete.Length
            && selectedFiles.All(complete.Contains)
            && scope.Distinct(StringComparer.Ordinal).Count() == scope.Length
            && (scope.Order(StringComparer.Ordinal).SequenceEqual(complete.Order(StringComparer.Ordinal))
                || scope.Order(StringComparer.Ordinal).SequenceEqual(selectedFiles.Order(StringComparer.Ordinal))),
            "Original mutation scope must select complete files from its declared partition.");
        Need(Text(native, "mutation-level") == "Complete" && Text(native, "configuration") == "Release"
            && Text(native, "target-framework") == "net10.0" && Text(native, "test-runner") == "vstest",
            "Use Complete Release/net10.0/vstest settings.");
        Need(Integer(native, "concurrency") is 2 or 4 or 8 && Integer(native, "additional-timeout") is 1000 or 15000,
            "Use a reviewed worker count and bounded additional mutant timeout.");
        Need(Boolean(native, "disable-mix-mutants"), "Explicit non-mixing is required.");
        Need(native["disable-bail"] is JsonValue bail && bail.TryGetValue(out bool _),
            "Explicit non-mixing and native bail settings are required.");
        string coverage = native.ContainsKey("coverage-analysis") ? Text(native, "coverage-analysis") : "perTest";
        Need(group == "unit-runtime" ? coverage is "perTest" or "off" : coverage == "off",
            "Child-process groups require coverage off; Unit requires a reviewed coverage mode.");
        string[] reporters = Strings(native, "reporters");
        Need(reporters.Contains("json", StringComparer.Ordinal) && reporters.Distinct(StringComparer.Ordinal).Count() == reporters.Length
            && reporters.All(reporter => reporter is "json" or "dots" or "cleartext" or "progress"), "Use bounded native console/JSON reporters.");
        JsonObject thresholds = Object(native, "thresholds");
        Need(Integer(thresholds, "high") == 100 && Integer(thresholds, "low") == 90 && Integer(thresholds, "break") == 90
            && thresholds.Count == 3, "Native thresholds must remain 100/90/90.");
        JsonObject normalized = (JsonObject)native.DeepClone();
        foreach (string key in new[] { "mutate", "concurrency", "additional-timeout", "reporters", "coverage-analysis", "disable-bail", "disable-mix-mutants" })
        {
            if (maintained.TryGetPropertyValue(key, out JsonNode? reference)) { normalized[key] = reference?.DeepClone(); }
            else { normalized.Remove(key); }
        }
        Need(JsonNode.DeepEquals(normalized, maintained), "Native project/cohort/settings differ beyond the explicitly reviewed incremental deltas.");
    }

    private static string[] Strings(JsonObject value, string name)
    {
        Need(value[name] is JsonArray, "Missing or malformed native array: " + name);
        return value[name]!.AsArray().Select(node => node is JsonValue item && item.TryGetValue(out string? text)
            && text is not null ? text : throw new InvalidDataException("Malformed native string array: " + name)).ToArray();
    }

    private static string Text(JsonObject value, string name) => value[name] is JsonValue item
        && item.TryGetValue(out string? text) && text is not null ? text : throw new InvalidDataException("Missing or malformed native string: " + name);

    private static int Integer(JsonObject value, string name) => value[name] is JsonValue item
        && item.TryGetValue(out int number) ? number : throw new InvalidDataException("Missing or malformed native integer: " + name);

    private static bool Boolean(JsonObject value, string name) => value[name] is JsonValue item
        && item.TryGetValue(out bool flag) ? flag : throw new InvalidDataException("Missing or malformed native Boolean: " + name);

    private static JsonObject Object(JsonObject value, string name) => value[name] is JsonObject item
        ? item : throw new InvalidDataException("Missing or malformed native object: " + name);

    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
