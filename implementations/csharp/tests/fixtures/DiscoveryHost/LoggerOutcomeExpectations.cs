using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerOutcomeExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        string[] required = ["results", "enabledBefore", "enabledAfter", "factoryInvocations", "clockCalls", "payloadTrace", "attempts", "tryCalls", "filtered", "unavailable", "accepted", "queueFull", "invalidCount", "truncatedCount", "exceptionSame", "exceptions", "sharedCaptures"];
        var expectedKeys = required.ToHashSet(StringComparer.Ordinal);
        if (row["input"]!["scenario"]!.GetValue<string>() is "capacity" or "sticky") { expectedKeys.Add("enabledAfterFirst"); }
        Require(expectedKeys.SetEquals(row["expected"]!.AsObject().Select(item => item.Key)), "Required expected fields differ.");
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case differs.");
        foreach ((string key, JsonNode? value) in row["expected"]!.AsObject())
        {
            Require(actual.ContainsKey(key) && JsonNode.DeepEquals(value, actual[key]), key + " differs.");
        }
        JsonArray results = row["expected"]!["results"]!.AsArray();
        var acceptedBytes = new Dictionary<int, byte[]>();
        for (int index = 0; index < 2; index++)
        {
            JsonArray wires = actual["wires"]![index]!.AsArray();
            Require(wires.Count == row["expected"]!["accepted"]![index]!.GetValue<int>(), "Accepted bytes differ.");
            int wireIndex = 0;
            for (int call = 0; call < results.Count; call++)
            {
                JsonNode? output = results[call]?["outputs"]?.AsArray().FirstOrDefault(item => item!["name"]!.GetValue<string>() == (index == 0 ? "console" : "file"));
                if (output?["status"]?.GetValue<string>() != "Accepted") { continue; }
                byte[] bytes = Convert.FromBase64String(wires[wireIndex++]!.GetValue<string>());
                Require(bytes.Length is > 0 and <= 16384 && bytes[^1] == (byte)'\n' && bytes.Count(value => value == (byte)'\n') == 1 && !bytes.Contains((byte)'\r') && !bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Raw cap/framing differs.");
                if (acceptedBytes.TryGetValue(call, out byte[]? other)) { Require(bytes.SequenceEqual(other), "Fanout bytes differ."); }
                else { acceptedBytes.Add(call, bytes); }
                JsonObject record = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes))!.AsObject();
                string severity = row["input"]!["level"]!.GetValue<int>() == 1 ? "debug" : "info";
                Require(record["schema_version"]!.GetValue<int>() == 1 && record["status"]!.GetValue<string>() == severity && record["timestamp"]!.GetValue<string>() == "2026-10-12T10:11:12.1234567Z", "Schema/severity/clock differs.");
                Require(record["component"]!.GetValue<string>() == "Outcomes" && record["event_name"]!.GetValue<string>() == "outcome.event" && record["message"]!.GetValue<string>() == "original", "Owned event differs.");
                Require(record["attributes"]!["probe"]!.GetValue<string>() == "original" && record["exception"]!["message"]!.GetValue<string>() == "exception-original", "Owned payload differs.");
                Require(!record.ContainsKey("truncated") && !record.ContainsKey("trace_id") && !record.ContainsKey("span_id"), "Loss/ambient IDs differ.");
                Require(!Encoding.UTF8.GetString(bytes).Contains("CIS_", StringComparison.Ordinal), "Private caller/collector content leaked.");
            }
        }
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
