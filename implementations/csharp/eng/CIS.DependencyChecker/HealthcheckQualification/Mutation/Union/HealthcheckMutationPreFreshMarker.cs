using System.Text.Json.Nodes;

namespace CIS.Verification;

// Reads an original pinned timestamp; booleans describe its source writer's
// shape only. Actual body hashes/MVIDs and fresh TRX ordering remain mandatory.
internal static class HealthcheckMutationPreFreshMarker
{
    internal readonly record struct Parsed(HealthcheckMutationUtcObservation.Observation Observation,
        string Format, string? RequiredInventoryOrigin);

    internal static Parsed Read(string markerText, JsonObject? document, string group, string nativeCoreRoot)
    {
        Need(group is "unit-runtime" or "pipes" or "aspnet", "Unknown mutation group for original pre-fresh marker.");
        string text = markerText.Trim(), format, raw;
        string? originalOrigin = null;
        if (!text.StartsWith('{'))
        {
            Need(document is null, "A plain original marker cannot have an alternate JSON document.");
            raw = text; format = "legacy-plain";
        }
        else
        {
            Need(document is not null, "Original JSON marker must be read through the duplicate-rejecting pinned reader.");
            Need(!document.ContainsKey("observedUtc") || !document.ContainsKey("observedBeforeFreshUtc"),
                "An original marker cannot contain both timestamp spellings, even when equal.");
            if (document.ContainsKey("observedBeforeFreshUtc"))
            {
                // Preserve existing rich legacy support. Other legacy metadata
                // never substitutes for the independent restoration checks.
                raw = Text(document, "observedBeforeFreshUtc"); format = "legacy-rich";
            }
            else
            {
                string[] fields = group == "unit-runtime"
                    ? ["observedUtc", "nativeExit", "actualNativeCoreDllRestorationVerified", "explicitControllerPdbCorrection", "historicalLoadedSymbolClaim"]
                    : ["observedUtc", "nativeExit", "nativeCoreDllRestorationVerified", "explicitControllerSymbolRepair", "historicalLoadedSymbolsClaim"];
                Need(document.Count == fields.Length && document.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal).SetEquals(fields),
                    "Only the exact source-proven original Unit79 or transport79 marker shape is accepted.");
                Need(document["nativeExit"] is JsonValue exit && exit.TryGetValue(out int observedExit) && observedExit is 0 or 2,
                    "Original native-exit metadata must retain a terminal integer0/2.");
                for (int index = 2; index < fields.Length; index++)
                {
                    Need(document[fields[index]] is JsonValue flag && flag.TryGetValue(out bool value) && value == (index < 4),
                        "Original marker metadata differs from its source writer:" + fields[index]);
                }
                // Do not use a true/false marker assertion to establish that
                // anything was restored, loaded, completed or qualified.
                raw = Text(document, "observedUtc");
                const string suffix = "/source/implementations/csharp/src/CIS.Core";
                string root = nativeCoreRoot.Replace('\\', '/').TrimEnd('/');
                Need(root.EndsWith(suffix, StringComparison.Ordinal) && !root.Split('/').Any(part => part is "." or ".."),
                    "Controller79 marker requires its exact native Core/source/output relationship.");
                originalOrigin = root[..^suffix.Length] + "/before-fresh-marker.json";
                Need(originalOrigin.StartsWith('/') && originalOrigin.Length > "/before-fresh-marker.json".Length,
                    "Controller79 marker must bind a real Linux output origin.");
                format = group == "unit-runtime" ? "controller79-unit" : "controller79-transport";
            }
        }
        Need(HealthcheckMutationUtcObservation.TryRead(raw, out var observed), "Original pre-fresh timestamp is missing or malformed.");
        return new(observed, format, originalOrigin);
    }

    internal static void RequireInventoryPin(Parsed parsed, IReadOnlyDictionary<string, string> inventory, string physicalMarkerSha256)
    {
        Need(physicalMarkerSha256.Length == 64 && physicalMarkerSha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
            "Actual physical marker SHA-256 is required.");
        Need(inventory.Values.Contains(physicalMarkerSha256, StringComparer.Ordinal), "Original physical marker hash is absent from the capture inventory.");
        if (parsed.RequiredInventoryOrigin is { } origin)
        {
            Need(inventory.TryGetValue(origin, out string? hash) && hash == physicalMarkerSha256,
                "Controller79 marker must bind its exact original output path and physical bytes in the inventory.");
        }
    }

    private static string Text(JsonObject document, string name)
    {
        Need(document[name] is JsonValue value && value.TryGetValue(out string? _), "Original timestamp must be a JSON string:" + name);
        return document[name]!.GetValue<string>();
    }
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) { throw new InvalidDataException(message); } }
}
