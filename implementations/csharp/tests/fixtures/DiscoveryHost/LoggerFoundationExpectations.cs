using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class LoggerFoundationExpectations
{
    internal static void Validate(JsonObject row, JsonObject actual)
    {
        Require(actual["caseId"]!.GetValue<string>() == row["id"]!.GetValue<string>(), "Case differs.");
        JsonObject expected = row["expected"]!.AsObject();
        JsonArray results = actual["results"]!.AsArray();
        Require(JsonNode.DeepEquals(expected["outcomes"], new JsonArray(results.Select(result => result!["outcome"]!.DeepClone()).ToArray())), "Outcomes differ.");
        foreach (string key in new[] { "clockCalls", "factoryCalls" }) { Require(JsonNode.DeepEquals(expected[key], actual[key]), key + " differs."); }
        string mode = row["input"]!["mode"]!.GetValue<string>();
        for (int index = 0; index < 2; index++)
        {
            string output = index == 0 ? "console" : "file";
            JsonArray wires = actual[output]!["wires"]!.AsArray();
            Require(wires.Count == expected["accepted"]![index]!.GetValue<int>(), "Actual accepted payload count differs.");
            JsonArray encoded = actual[output]!["wireBase64"]!.AsArray();
            Require(encoded.Count == wires.Count, "Raw byte count differs.");
            for (int recordIndex = 0; recordIndex < wires.Count; recordIndex++)
            {
                byte[] bytes = Convert.FromBase64String(encoded[recordIndex]!.GetValue<string>());
                int cap = mode == "truncate" ? 16384 : 65536;
                Require(bytes.Length > 0 && bytes.Length <= cap && bytes[^1] == (byte)'\n' && bytes.Count(value => value == (byte)'\n') == 1 && !bytes.Contains((byte)'\r') && !bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Actual byte size/framing differs.");
                string wire = new UTF8Encoding(false, true).GetString(bytes);
                Require(wire == wires[recordIndex]!.GetValue<string>(), "Raw bytes and reported text differ.");
                Require(wire.EndsWith('\n') && wire.Count(ch => ch == '\n') == 1 && !wire.Contains('\r') && !wire.StartsWith('\uFEFF') && new UTF8Encoding(false, true).GetByteCount(wire) <= 65536, "Wire size/framing differs.");
                JsonObject record = JsonNode.Parse(wire)!.AsObject();
                Require(record["status"]!.GetValue<string>() == expected["status"]!.GetValue<string>() && record["timestamp"]!.GetValue<string>() == "2026-10-12T10:11:12.1234567Z", "Method severity/clock differs.");
                Require(record["service"]!.GetValue<string>() == "Logger.App" && record["component"]!.GetValue<string>() == "Orders" && record["event_name"]!.GetValue<string>() == "logger.event" && record["message"]!.GetValue<string>() == "original", "Owned identity/data differs.");
                bool lost = mode == "truncate";
                Require(lost ? record["truncated"]!.GetValue<bool>() : !record.ContainsKey("truncated"), "Record loss differs.");
                if (lost) { Require(Encoding.UTF8.GetByteCount(wire) <= 16384 && record["attributes"]!["attachment"]!.GetValue<string>().Length is > 0 and < 50000, "Public write did not apply the selected cap."); }
                if (mode == "fanout")
                {
                    Require(record["operation_id"]!.GetValue<string>() == "scope-op" && record["attributes"]!["tenant"]!.GetValue<string>() == "original", "Scope capture changed.");
                    Require(record["trace_id"]!.GetValue<string>() == actual["sampled"]!["traceId"]!.GetValue<string>() && record["span_id"]!.GetValue<string>() == actual["sampled"]!["spanId"]!.GetValue<string>(), "Current pair changed.");
                }
                if (mode == "settings") { Require(record["attributes"]!["payload"]!.GetValue<string>() == new string('x', 30000) && Encoding.UTF8.GetByteCount(wire) > 16384, "Mutable settings changed the frozen cap."); }
                if (mode == "isolation") { Require(record["operation_id"]!.GetValue<string>() == "first", "Scope stores leaked."); }
            }
        }
        Require(actual["console"]!["filtered"]!.GetValue<int>() == (mode == "union" ? 1 : 0) && actual["file"]!["filtered"]!.GetValue<int>() == 0 && actual["console"]!["unavailable"]!.GetValue<int>() == 0 && actual["file"]!["unavailable"]!.GetValue<int>() == 0, "Observed output counters differ.");
        foreach (JsonNode? node in results)
        {
            JsonObject result = node!.AsObject();
            Require(result["truncated"]!.GetValue<bool>() == (mode == "truncate"), "Result loss differs.");
            bool invalid = result["outcome"]!.GetValue<string>() == "InvalidEvent";
            Require(invalid == (result["errorCode"] is not null), "Error presence differs.");
            if (invalid) { Require(result["outputs"]!.AsArray().Count == 0, "Invalid event reached fanout."); }
            foreach (JsonNode? output in result["outputs"]!.AsArray())
            {
                bool accepted = output!["status"]!.GetValue<string>() == "Accepted";
                Require(accepted ? output["sequence"]!.GetValue<long>() == 1 : output["sequence"] is null, "Output ticket differs.");
            }
        }
        if (mode == "union")
        {
            Require(actual["enabledBefore"]!.GetValue<bool>(), "File debug interest was hidden by the default/console threshold.");
            Require(results[0]!["outputs"]![0]!["name"]!.GetValue<string>() == "console" && results[0]!["outputs"]![0]!["status"]!.GetValue<string>() == "Filtered" && results[0]!["outputs"]![1]!["name"]!.GetValue<string>() == "file", "Output order/filtering differs.");
        }
        if (mode == "disabled") { Require(!actual["enabledBefore"]!.GetValue<bool>() && results[0]!["outputs"]!.AsArray().Count == 0, "Disabled outputs appeared."); }
        if (mode == "full") { Require(actual["enabledWhenFull"]!.GetValue<bool>() && results[1]!["outputs"]![0]!["status"]!.GetValue<string>() == "QueueFull" && actual["console"]!["attempts"]!.GetValue<int>() == 2, "Capacity was represented as an interest reservation."); }
        if (mode == "fanout") { Require(actual["sameCapture"]!.GetValue<bool>() && actual["storeEmpty"]!.GetValue<bool>() && actual["clockCalls"]!.GetValue<int>() == 1, "Capture/fanout was duplicated."); }
        if (mode == "stopped") { Require(results[0]!["outputs"]!.AsArray().Count == 1 && results[0]!["outputs"]![0]!["name"]!.GetValue<string>() == "console" && results[0]!["outputs"]![0]!["status"]!.GetValue<string>() == "Stopped", "Stopped output entries differ."); Require(!actual["enabledAfterClose"]!.GetValue<bool>() && actual["loggerCreatedAfterClose"]!.GetValue<bool>() && actual["console"]!["attempts"]!.GetValue<int>() == 0, "Closure reopened admission."); }
        if (mode == "caller-throws") { Require(actual["sameCallerException"]!.GetValue<bool>(), "Factory exception was concealed."); }
        if (mode == "isolation") { Require(JsonNode.Parse(actual["otherWire"]!.GetValue<string>())!["operation_id"]!.GetValue<string>() == "second", "Separate runtime scope changed."); }
        Require(actual["invalidCount"]!.GetValue<long>() == (mode is "invalid" or "null-result" ? 1 : 0) && actual["truncatedCount"]!.GetValue<long>() == (mode == "truncate" ? 1 : 0), "Global capture counters differ.");
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
