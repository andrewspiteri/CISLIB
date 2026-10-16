using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

// Literal record facts shared by physical file readers.
internal static class FileRecordChecks
{
    internal static void Record(byte[] line, bool human, string component, string eventName, string message, int pid)
    {
        Require(line.Length is > 0 and <= 16384 && line[^1] == 10, "policy record bounds");
        Require(!line.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), "policy record BOM");
        string text = new UTF8Encoding(false, true).GetString(line);
        Require(text.Count(c => c == '\n') == 1 && !text.Contains('\r'), "policy record framing");
        JsonObject value;
        if (human)
        {
            value = new JsonObject();
            foreach ((string key, JsonElement field) in HumanRenderingExpectations.Read(line, 16384)) { value.Add(key, JsonNode.Parse(field.GetRawText())); }
        }
        else { value = JsonNode.Parse(line)!.AsObject(); }
        foreach ((string key, string wanted) in new[] { ("component", component), ("event_name", eventName), ("message", message), ("service", "Console.App"), ("env", "test"), ("version", "1.2.3"), ("timestamp", "2026-10-13T10:11:12.0000000Z"), (human ? "severity" : "status", "info") })
        { Require((string?)value[key] == wanted, "literal policy record: " + key); }
        Require(JsonNode.DeepEquals(value["attributes"], new JsonObject { ["producer_pid"] = pid }) && !value.ContainsKey("truncated"), "policy producer/loss");
        if (!human) { Require(value["schema_version"]!.GetValue<int>() == 1 && (string?)value["resource"]!["instance_id"] == "11111111-1111-1111-1111-111111111111", "protected policy facts"); }
    }

    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
