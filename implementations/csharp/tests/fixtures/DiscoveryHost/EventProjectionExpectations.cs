using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class EventProjectionExpectations
{
    internal static JsonObject Expected(JsonObject row, JsonObject bindings)
    {
        JsonObject expected = row["expected"]!.DeepClone().AsObject();
        if (expected["projection"]?["exception"] is JsonObject error)
        {
            if (error["type"]!.GetValue<string>() != "@exception-type") { throw new InvalidDataException("Unknown native exception binding."); }
            error["type"] = bindings["csharp"]!["exceptionType"]!.DeepClone();
        }
        return expected;
    }
    internal static void Validate(JsonObject row, JsonObject bindings, JsonObject actual)
    {
        if (!JsonNode.DeepEquals(Expected(row, bindings), actual))
        {
            throw new InvalidDataException("Event projection differs. Expected=" + Expected(row, bindings).ToJsonString() + "; Actual=" + actual.ToJsonString());
        }
    }
}
