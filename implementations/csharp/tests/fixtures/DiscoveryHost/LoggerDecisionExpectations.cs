using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerDecisionExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case differs.");
        foreach ((string key, JsonNode? value) in row["expected"]!.AsObject())
        {
            Require(actual.ContainsKey(key) && JsonNode.DeepEquals(value, actual[key]), key + " differs.");
        }
        JsonArray wires = actual["wires"]!.AsArray();
        Require(wires.Count == 2, "Output wire groups differ.");
        JsonArray? outputs = row["expected"]!["result"]?["outputs"]?.AsArray();
        for (int index = 0; index < 2; index++)
        {
            string name = index == 0 ? "console" : "file";
            bool accepted = outputs?.Any(item => item!["name"]!.GetValue<string>() == name && item["status"]!.GetValue<string>() == "Accepted") == true;
            Require(wires[index]!.AsArray().Count == (accepted ? 1 : 0), "Actual accepted bytes differ.");
            foreach (JsonNode? encoded in wires[index]!.AsArray())
            {
                byte[] bytes = Convert.FromBase64String(encoded!.GetValue<string>());
                Require(bytes.Length is > 0 and <= 65536 && bytes[^1] == (byte)'\n' && bytes.Count(value => value == (byte)'\n') == 1 && !bytes.Contains((byte)'\r') && !bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Byte cap/framing differs.");
                JsonObject record = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes))!.AsObject();
                Require(record["schema_version"]!.GetValue<int>() == 1 && !record.ContainsKey("trace_id") && !record.ContainsKey("span_id"), "Schema/ambient correlation differs.");
                Require(record["attributes"]!.AsObject().Count == 1, "Unexpected attribute.");
                string status = row["input"]!["level"]!.GetValue<int>() == 1 ? "debug" : "info";
                Require(record["status"]!.GetValue<string>() == status && record["timestamp"]!.GetValue<string>() == "2026-10-12T10:11:12.1234567Z", "Severity/clock differs.");
                Require(record["component"]!.GetValue<string>() == "Decisions" && record["event_name"]!.GetValue<string>() == "decision.event" && record["message"]!.GetValue<string>() == "original", "Event differs.");
                Require(record["attributes"]!["probe"]!.GetValue<string>() == "original" && record["exception"]!["message"]!.GetValue<string>() == "exception-original" && !record.ContainsKey("truncated"), "Payload differs.");
            }
        }
        if (wires[0]!.AsArray().Count == 1 && wires[1]!.AsArray().Count == 1)
        {
            Require(actual["sameCapture"]!.GetValue<bool>() && JsonNode.DeepEquals(wires[0], wires[1]), "Fanout captured twice.");
        }
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
