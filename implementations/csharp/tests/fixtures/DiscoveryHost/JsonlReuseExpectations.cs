using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class JsonlReuseExpectations
{
    internal static void Validate(JsonObject catalog, JsonObject row, JsonObject actual)
    {
        Require(JsonNode.DeepEquals(row["id"], actual["caseId"]), "Case differs.");
        Require(new HashSet<string>(["level", "messages", "mode", "maxEntryBytes"], StringComparer.Ordinal).SetEquals(row["input"]!.AsObject().Select(pair => pair.Key)), "Input fields differ.");
        Require(new HashSet<string>(["status", "truncated", "records", "omittedFields"], StringComparer.Ordinal).SetEquals(row["expected"]!.AsObject().Select(pair => pair.Key)), "Expected fields differ.");
        int count = row["expected"]!["records"]!.GetValue<int>();
        bool lost = row["expected"]!["truncated"]!.GetValue<bool>();
        Require(actual["factoryCalls"]!.GetValue<int>() == count && actual["clockCalls"]!.GetValue<int>() == count && actual["invalidCount"]!.GetValue<long>() == 0, "Capture counts differ.");
        Require(actual["truncatedCount"]!.GetValue<long>() == (lost ? count : 0) && actual["sharedCaptures"]!.GetValue<int>() == count && actual["reusedMemory"]!.GetValue<bool>(), "Owned capture/loss differs.");
        JsonArray wires = actual["wires"]!.AsArray(), results = actual["results"]!.AsArray();
        Require(wires.Count == 2 && wires[0]!.AsArray().Count == count && JsonNode.DeepEquals(wires[0], wires[1]) && results.Count == count, "Fanout/record count differs.");
        var joined = new List<byte>();
        for (int index = 0; index < count; index++)
        {
            JsonObject result = results[index]!.AsObject();
            Require(result["outcome"]!.GetValue<string>() == "Accepted" && result["truncated"]!.GetValue<bool>() == lost && result["errorCode"] is null, "Write outcome differs.");
            JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 2, "Output count differs.");
            for (int output = 0; output < 2; output++)
            {
                Require(outputs[output]!["name"]!.GetValue<string>() == (output == 0 ? "console" : "file") && outputs[output]!["status"]!.GetValue<string>() == "Accepted" && outputs[output]!["sequence"]!.GetValue<long>() == index + 1, "Output ticket/status differs.");
            }
            byte[] raw = Convert.FromBase64String(wires[0]![index]!.GetValue<string>());
            Require(raw.Length is > 0 && raw.Length <= row["input"]!["maxEntryBytes"]!.GetValue<int>() && raw[^1] == 10 && raw.Count(value => value == 10) == 1 && raw.Take(raw.Length - 1).All(value => value >= 32) && !raw.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "Framing/cap/control differs.");
            if (lost) { Require(raw.Length >= row["input"]!["maxEntryBytes"]!.GetValue<int>() - 5, "Greedy capture ignored configured cap."); }
            joined.AddRange(raw);
            string text = new UTF8Encoding(false, true).GetString(raw);
            Require(!text.Any(value => value is >= '\u007f' and <= '\u009f' or '\u2028' or '\u2029'), "Raw Unicode controls/newlines differ.");
            JsonObject record = JsonNode.Parse(text)!.AsObject();
            JsonObject expected = Expected(catalog, row, index);
            if (lost)
            {
                string retained = record["attributes"]!["zPayload"]!.GetValue<string>();
                Require(retained.Length is > 0 and < 30000 && retained.All(value => value == '\u0001'), "Bounded escaped prefix differs.");
                record["attributes"]!.AsObject().Remove("zPayload");
            }
            Require(JsonNode.DeepEquals(expected, record), "Parsed structure/type differs: " + string.Join(",", expected.Select(pair => pair.Key).Concat(record.Select(pair => pair.Key)).Distinct(StringComparer.Ordinal).Where(key => !JsonNode.DeepEquals(expected[key], record[key]))));
        }
        byte[] stream = Convert.FromBase64String(actual["stream"]!.GetValue<string>());
        Require(stream.SequenceEqual(joined) && stream.Count(value => value == 10) == count, "Concatenated record stream differs.");
    }
    internal static JsonObject Expected(JsonObject catalog, JsonObject row, int index)
    {
        JsonObject result = catalog["common"]!.DeepClone().AsObject();
        result["schema_version"] = 1; result["status"] = row["expected"]!["status"]!.DeepClone();
        string message = row["input"]!["messages"]![index]!.GetValue<string>();
        result["message"] = message; result["message_template"] = message; result["operation_id"] = "event";
        result["attributes"]!["text"] = message;
        string mode = row["input"]!["mode"]!.GetValue<string>();
        if (mode is "absent" or "empty")
        {
            foreach (string key in new[] { "process_started_at", "host_name", "os_description", "os_architecture", "process_architecture", "runtime_description", "process_id", "commit_id", "build_id" }) { result["resource"]!.AsObject().Remove(key); }
            result.Remove("operation_id"); result.Remove("exception");
            result["attributes"] = mode == "empty" ? new JsonObject { ["null"] = null } : new JsonObject();
            if (mode == "empty") { result["message_template"] = ""; } else { result.Remove("message_template"); }
        }
        foreach (JsonNode? field in row["expected"]!["omittedFields"]!.AsArray())
        {
            string[] path = field!.GetValue<string>().Split('.');
            if (path.Length == 2) { result[path[0]]!.AsObject().Remove(path[1]); }
            else { result.Remove(path[0]); }
        }
        if (row["expected"]!["truncated"]!.GetValue<bool>()) { result["truncated"] = true; }
        return result;
    }
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidDataException(reason); } }
}
