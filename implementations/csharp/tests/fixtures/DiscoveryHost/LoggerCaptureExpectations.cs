using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerCaptureExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case differs.");
        foreach ((string key, JsonNode? value) in row["expected"]!.AsObject())
        {
            if (key != "wireMessages") { Require(actual.ContainsKey(key) && JsonNode.DeepEquals(value, actual[key]), key + " differs."); }
        }
        JsonArray expectedResults = row["expected"]!["results"]!.AsArray();
        int shared = 0;
        foreach (JsonNode? result in expectedResults)
        {
            if (result?["outputs"]?.AsArray().Count(item => item!["status"]!.GetValue<string>() == "Accepted") == 2) { shared++; }
        }
        Require(actual["sharedCaptures"]!.GetValue<int>() == shared, "Capture fanout differs.");
        JsonArray messages = row["expected"]!["wireMessages"]!.AsArray();
        for (int index = 0; index < 2; index++)
        {
            JsonArray wires = actual["wires"]![index]!.AsArray();
            Require(wires.Count == row["expected"]!["accepted"]![index]!.GetValue<int>(), "Accepted bytes differ.");
            int wireIndex = 0;
            for (int call = 0; call < expectedResults.Count; call++)
            {
                JsonNode? output = expectedResults[call]?["outputs"]?.AsArray().FirstOrDefault(item => item!["name"]!.GetValue<string>() == (index == 0 ? "console" : "file"));
                if (output?["status"]?.GetValue<string>() != "Accepted") { continue; }
                byte[] bytes = Convert.FromBase64String(wires[wireIndex++]!.GetValue<string>());
                Require(bytes.Length is > 0 and <= 16384 && bytes[^1] == (byte)'\n' && bytes.Count(value => value == (byte)'\n') == 1 && !bytes.Contains((byte)'\r') && !bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Raw framing/cap differs.");
                JsonObject record = JsonNode.Parse(new UTF8Encoding(false, true).GetString(bytes))!.AsObject();
                string message = messages.Count == expectedResults.Count ? messages[call]!.GetValue<string>() : messages[wireIndex - 1]!.GetValue<string>();
                Require(record["schema_version"]!.GetValue<int>() == 1 && record["status"]!.GetValue<string>() == "info" && record["timestamp"]!.GetValue<string>() == "2026-10-12T10:11:12.1234567Z", "Schema/severity/clock differs.");
                Require(record["component"]!.GetValue<string>() == "Capture" && record["event_name"]!.GetValue<string>() == "capture.event" && record["message"]!.GetValue<string>() == message, "Owned event differs.");
                bool lost = row["input"]!["mode"]!.GetValue<string>() == "truncate";
                Require(lost ? record["truncated"]!.GetValue<bool>() : !record.ContainsKey("truncated"), "Wire loss differs.");
                Require(!record.ContainsKey("trace_id") && !record.ContainsKey("span_id"), "Ambient IDs leaked.");
            }
        }
        Require(!actual.ToJsonString().Contains("CIS_PRIVATE", StringComparison.Ordinal) && !actual.ToJsonString().Contains("CIS_FACTORY_PRIVATE", StringComparison.Ordinal), "Safe errors leaked caller content.");
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
