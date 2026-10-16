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
        JsonObject result = actual.DeepClone().AsObject();
        if (result["reads"]!.AsObject().Any(probe => probe.Value!["forbidden"]!.GetValue<int>() != 0))
        {
            throw new InvalidDataException("Excluded getters were read.");
        }
        JsonNode? diagnostic = expected["error"] is JsonNode error ? nativeBindings[error.GetValue<string>()] : null;
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
            ["reads"] = (expected["reads"] ?? row["nativeExpectedReads"]!["csharp"])!.DeepClone()
        };
        if (!JsonNode.DeepEquals(exact, result))
        {
            throw new InvalidDataException("Exception qualification observation differs: " + row["id"]!.GetValue<string>());
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
