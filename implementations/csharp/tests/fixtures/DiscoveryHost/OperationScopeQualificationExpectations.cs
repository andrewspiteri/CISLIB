using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OperationScopeQualificationExpectations
{
    internal static JsonObject Expected(JsonObject row, JsonObject bindings)
    {
        JsonObject expected = row["expected"]!.DeepClone().AsObject();
        if (row["input"]!["scenario"]!.GetValue<string>() == "invalid-order")
        {
            JsonObject native = bindings["csharp"]!["invalid-disposal-order"]!.AsObject();
            foreach (JsonNode? child in expected["children"]!.AsArray())
            {
                foreach ((string key, JsonNode? value) in native) { child![key] = value?.DeepClone(); }
            }
        }
        return expected;
    }

    internal static void Validate(JsonObject row, JsonObject bindings, JsonObject actual)
    {
        if (!JsonNode.DeepEquals(Expected(row, bindings), actual))
        {
            throw new InvalidDataException("Scope qualification observation differs. Expected=" + Expected(row, bindings).ToJsonString() + "; Actual=" + actual.ToJsonString());
        }
    }
}
