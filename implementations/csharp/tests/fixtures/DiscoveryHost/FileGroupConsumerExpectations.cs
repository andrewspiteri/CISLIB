using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileGroupConsumerExpectations
{
    private static void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
    internal static void Validate(JsonObject actual, string status, string path, int count, string? code = null)
    {
        Require(actual["status"]!.GetValue<string>() == status && actual["capturedPath"]!.GetValue<string>() == path, "Consumer status/path differs.");
        Require(actual["workers"]!.GetValue<int>() == (status == "prepared" ? 0 : 2) && actual["borrowedDisposes"]!.GetValue<int>() == 0 && actual["factories"]!.GetValue<int>() == count, "Worker/factory ownership differs.");
        bool joined = status is "prepared" or "failed" or "released" or "completed";
        Require(actual["joined"]!.GetValue<bool>() == joined, "Consumer worker join differs.");
        if (code is not null)
        {
            Require(JsonNode.DeepEquals(actual["error"], new JsonObject { ["output"] = "file", ["code"] = code, ["message"] = "Logging output 'file' could not initialize (" + code + ").", ["inner"] = false, ["dataCount"] = 0 }), "Safe group error differs.");
        }
        else { Require(actual["error"] is null, "Unexpected group failure."); }
        if (code is "FileInventoryMissing" or "FileInventoryIncompatible" or "FileInventoryInitializationFailed") { Require(actual["leaseReleased"]?.GetValue<bool>() == true && actual["activeReleased"]?.GetValue<bool>() == true, "Failed startup retained an owned handle in its process."); }
        if (status is "prepared" or "failed") { Require(actual["outputs"] is null && actual["state"] is null && actual["consoleBytes"]!.GetValue<string>() == "" && actual["fileBytes"]!.GetValue<string>() == "", "Unpublished group emitted work."); return; }
        Require(actual["state"]!.GetValue<string>() == (joined ? "Stopped" : "Running"), "Runtime state differs.");
        JsonArray outputs = actual["outputs"]!.AsArray(); Require(outputs.Count == 2, "Group output count differs.");
        for (int n = 0; n < 2; n++) { Require(JsonNode.DeepEquals(outputs[n], new JsonObject { ["name"] = n == 0 ? "console" : "file", ["state"] = joined ? "Stopped" : "Running", ["accepted"] = count, ["written"] = count, ["failed"] = 0, ["abandoned"] = 0, ["pending"] = 0 }), "Group accounting differs."); }
    }
    internal static void Records(byte[] bytes, bool human, string fileName, (string Event, int Pid)[] expected)
    {
        string text = new UTF8Encoding(false, true).GetString(bytes); string[] rows = text.Split('\n'); Require(rows.Length == expected.Length + 1 && rows[^1] == "" && !text.StartsWith('\uFEFF'), "Group record framing differs.");
        for (int n = 0; n < expected.Length; n++)
        {
            byte[] line = Encoding.UTF8.GetBytes(rows[n] + "\n"); Require(line.Length <= 16384 && !rows[n].Contains('\r'), "Group record bounds differ.");
            JsonObject value;
            if (human) { Dictionary<string, JsonElement> fields = HumanRenderingExpectations.Read(line, 16384); value = new JsonObject(); foreach ((string key, JsonElement field) in fields) { value[key] = JsonNode.Parse(field.GetRawText()); } }
            else { value = JsonNode.Parse(line)!.AsObject(); }
            foreach ((string key, string wanted) in new[] { ("event_name", expected[n].Event), ("message", fileName), ("component", "Group"), (human ? "severity" : "status", "info"), ("service", "Console.App"), ("env", "test"), ("version", "1.2.3"), ("timestamp", "2026-10-13T10:11:12.0000000Z") }) { Require(value[key]!.GetValue<string>() == wanted, "Group record identity differs."); }
            Require(value["attributes"]!["process_id"]!.GetValue<int>() == expected[n].Pid && value["truncated"] is null, "Cross-process payload/loss differs.");
            if (!human) { Require(value["schema_version"]!.GetValue<int>() == 1 && (string?)value["resource"]!["instance_id"] == "11111111-1111-1111-1111-111111111111", "Protected group facts differ."); }
        }
    }
}
