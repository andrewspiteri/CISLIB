using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ExceptionQualificationExpectations
{
    // Native type bindings are explicit. Never derive expected identities from the captured output.
    internal static string NativeType(string tag) => tag switch
    {
        "single" => "CIS.Tests.DiscoveryFixture.ExceptionQualificationSources+Single",
        "aggregate" => "CIS.Tests.DiscoveryFixture.ExceptionQualificationSources+Aggregate",
        _ => throw new InvalidDataException("Unknown native type binding.")
    };

    internal static JsonNode? Tree(JsonNode? descriptor)
    {
        if (descriptor is null) { return null; }
        JsonObject source = descriptor.AsObject();
        var tree = new JsonObject
        {
            ["type"] = NativeType(source["type"]!.GetValue<string>()),
            ["message"] = Text(source["message"]!)
        };
        if (source["stack_trace"] is JsonNode stack) { tree["stack_trace"] = Text(stack); }
        if (source["inner_exceptions"] is JsonArray children)
        {
            tree["inner_exceptions"] = new JsonArray(children.Select(Tree).ToArray());
        }
        if (source["truncated"] is JsonNode marker) { tree["truncated"] = marker.DeepClone(); }
        return tree;
    }

    // C# diagnostic and failure-read bindings are native observations, not portable requirements.
    internal static void Validate(JsonObject row, JsonObject actual, JsonObject nativeBindings)
    {
        foreach (string key in new[] { "nodes", "depth", "cachedNodes", "cachedDepth" })
        {
            if (actual[key] is not JsonValue count || !count.TryGetValue<int>(out _)) { throw new InvalidDataException("Exception count must be an integer: " + key); }
        }
        JsonObject expected = row["expected"]!.AsObject();
        if (!expected.ContainsKey("exception") || !expected.ContainsKey("error")) { throw new InvalidDataException("Expected exception and error members are required."); }
        JsonObject result = actual.DeepClone().AsObject();
        JsonNode? expectedReads = expected["reads"] ?? row["nativeExpectedReads"]?["csharp"];
        ValidateReadCounts(result["reads"]);
        ValidateReadCounts(expectedReads);
        if (result["reads"]!.AsObject().Any(probe => probe.Value!["forbidden"]!.GetValue<int>() != 0))
        {
            throw new InvalidDataException("Excluded getters were read.");
        }
        JsonObject? diagnostic = null;
        if (expected["error"] is JsonNode error)
        {
            string tag = error.GetValue<string>();
            if (nativeBindings[tag] is not JsonObject binding || binding["nativeError"] is not JsonValue nativeError ||
                !nativeError.TryGetValue<string>(out _) || !binding.ContainsKey("parameter") ||
                (binding["parameter"] is JsonNode parameter && (parameter is not JsonValue scalar || !scalar.TryGetValue<string>(out _))))
            {
                throw new InvalidDataException("Invalid native exception error binding: " + tag);
            }
            diagnostic = binding;
        }
        result.Remove("observed");
        var exact = new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["valid"] = expected["valid"]!.DeepClone(),
            ["exception"] = Tree(expected["exception"]),
            ["nodes"] = expected["nodes"]!.DeepClone(),
            ["depth"] = expected["depth"]!.DeepClone(),
            ["cachedNodes"] = expected["nodes"]!.DeepClone(),
            ["cachedDepth"] = expected["depth"]!.DeepClone(),
            ["hasTruncatedContent"] = expected["hasTruncatedContent"]!.DeepClone(),
            ["error"] = expected["error"]?.DeepClone(),
            ["nativeError"] = diagnostic?["nativeError"]?.DeepClone(),
            ["parameter"] = diagnostic?["parameter"]?.DeepClone(),
            ["stableAfterMutation"] = true,
            ["safeError"] = true,
            ["reads"] = expectedReads!.DeepClone()
        };
        if (!JsonNode.DeepEquals(exact, result))
        {
            throw new InvalidDataException("Exception qualification observation differs: " + row["id"]!.GetValue<string>());
        }
    }

    private static void ValidateReadCounts(JsonNode? value)
    {
        if (value is not JsonObject reads) { throw new InvalidDataException("Exception read observations must be an object."); }
        foreach ((string label, JsonNode? observation) in reads)
        {
            if (observation is not JsonObject counts) { throw new InvalidDataException("Exception read observations must be objects."); }
            foreach (string key in new[] { "message", "stack", "forbidden" })
            {
                if (counts[key] is not JsonValue scalar || !scalar.TryGetValue<int>(out int number) || number < 0)
                {
                    throw new InvalidDataException("Exception read count must be an integer: " + label + "." + key);
                }
            }
        }
    }

    private static string Text(JsonNode descriptor)
    {
        if (descriptor is JsonValue) { return descriptor.GetValue<string>(); }
        JsonObject value = descriptor.AsObject();
        if (value["parts"] is JsonArray parts) { return string.Concat(parts.Select(part => Text(part!))); }
        string pattern = value["pattern"]!.GetValue<string>();
        int scalars = value["scalars"]!.GetValue<int>();
        string[] tokens = pattern.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        int repetitions = scalars / tokens.Length;
        int remainder = scalars % tokens.Length;
        return string.Concat(Enumerable.Repeat(pattern, repetitions)) + string.Concat(tokens.Take(remainder));
    }
}
