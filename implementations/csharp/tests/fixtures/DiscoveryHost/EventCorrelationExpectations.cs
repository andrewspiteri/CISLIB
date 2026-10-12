using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class EventCorrelationExpectations
{
    internal static JsonObject Expected(JsonObject row, JsonObject actual)
    {
        JsonObject expected = row["expected"]!.DeepClone().AsObject();
        string mode = row["input"]!["activity"]!["mode"]!.GetValue<string>();
        JsonObject? source = actual["sourceActivity"] as JsonObject;
        if (mode is "absent" or "unstarted" or "stopped")
        {
            if (source is not null) { throw new InvalidDataException("Unexpected current activity."); }
        }
        else
        {
            if (source is null) { throw new InvalidDataException("Missing current activity."); }
            string trace = source["traceId"]!.GetValue<string>(); string span = source["spanId"]!.GetValue<string>();
            string parent = source["parentSpanId"]!.GetValue<string>();
            if (mode == "hierarchical")
            {
                if (source["format"]!.GetValue<string>() != "Hierarchical" || trace != new string('0', 32)
                    || span != new string('0', 16) || parent != new string('0', 16)) { throw new InvalidDataException("Hierarchy binding differs."); }
            }
            else
            {
                if (source["format"]!.GetValue<string>() != "W3C" || !Valid(span, 16)
                    || (mode == "root" ? !Valid(trace, 32) : trace != new string(mode == "zero-trace" ? '0' : '1', 32))
                    || parent != new string(mode is "parent" or "zero-trace" ? '2' : '0', 16)
                    || span == parent) { throw new InvalidDataException("W3C source binding differs."); }
            }
            Bind(expected, "@source-trace-id", trace); Bind(expected, "@source-span-id", span);
        }
        return expected;
    }
    private static bool Valid(string value, int length) => value.Length == length && value.Any(c => c != '0')
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Bind(JsonNode node, string marker, string value)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(pair => pair.Key).ToArray())
            {
                if (obj[key] is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && text == marker) { obj[key] = value; }
                else if (obj[key] is { } child) { Bind(child, marker, value); }
            }
        }
    }
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        JsonObject expected = Expected(row, actual);
        if (!JsonNode.DeepEquals(expected, actual)) { throw new InvalidDataException("Correlation differs. Expected=" + expected.ToJsonString() + "; Actual=" + actual.ToJsonString()); }
    }
}
