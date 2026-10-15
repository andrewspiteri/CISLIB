using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderOperationExpectations
{
    internal static readonly string[] Modes = ["owned", "host", "nested-owned", "nested-host", "duplicates", "nonstring", "event", "protected", "bounds", "invalid", "host-omitted", "callback-errors"];
    private static void Require(bool value) { if (!value) { throw new InvalidDataException("Provider operation expectation failed."); } }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12" && a["leaseReleased"]!.GetValue<bool>() == a["physical"]!.GetValue<bool>());
        int count = mode switch { "nested-owned" => 4, "nested-host" or "event" => 3, "duplicates" => 5, "bounds" => 2, _ => 1 };
        int calls = mode == "event" ? 1 : mode == "callback-errors" ? 3 : mode == "host-omitted" ? 2 : count;
        Require(a["formatterCalls"]!.GetValue<int>() == calls && a["ownedErrors"]!.GetValue<int>() == (mode == "invalid" ? 3 : mode is "duplicates" or "nonstring" ? 2 : 0));
        Require(a["invalid"]!.GetValue<string>() == (mode == "callback-errors" ? "2" : mode == "host-omitted" ? "1" : "0") && a["truncated"]!.GetValue<string>() == (mode == "bounds" ? "2" : "0"));
        Require(a["result"]!["succeeded"]!.GetValue<bool>() && a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["loss"]!.GetValue<bool>() == (mode is "bounds" or "host-omitted" or "callback-errors"));
        JsonNode output = a["result"]!["outputs"]![0]!; Require(output["boundary"]!.GetValue<string>() == count.ToString(System.Globalization.CultureInfo.InvariantCulture) && output["written"]!.GetValue<string>() == count.ToString(System.Globalization.CultureInfo.InvariantCulture) && output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>()); Require(bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries); Require(lines.Length == count && lines.All(line => Encoding.UTF8.GetByteCount(line) + 1 <= 16384)); JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        void Row(int index, string? operationId, string attributes)
        { Require((string?)rows[index]["operation_id"] == operationId && JsonNode.DeepEquals(rows[index]["attributes"], JsonNode.Parse(attributes))); }
        foreach (JsonObject row in rows) { Require(row["message"]!.GetValue<string>() == "rendered" && row["status"]!.GetValue<string>() == "info"); }
        if (mode == "owned") { Row(0, "owned.operation", "{\"common\":\"original\",\"event_id\":17}"); }
        if (mode == "host") { Row(0, "host.operation", "{\"common\":\"at-capture\",\"event_id\":17}"); }
        if (mode == "nested-owned")
        { Row(0, "outer", "{\"common\":\"outer\",\"event_id\":17}"); Row(1, "inner", "{\"common\":\"inner\",\"event_id\":17}"); Row(2, "inner", "{\"common\":\"event\",\"event_id\":17,\"operation_id\":\"attribute-only\"}"); Row(3, null, "{\"event_id\":17}"); }
        if (mode == "nested-host")
        { Row(0, "host.outer", "{\"core\":true,\"common\":\"outer\",\"event_id\":17}"); Row(1, "host.inner", "{\"core\":true,\"common\":\"inner\",\"event_id\":17}"); Row(2, "host.inner", "{\"core\":true,\"common\":\"event\",\"event_id\":17}"); }
        if (mode == "duplicates")
        { Row(0, "parent", "{\"event_id\":17}"); Row(1, "parent", "{\"event_id\":17}"); Row(2, "parent", "{\"event_id\":17}"); Row(3, "parent", "{\"event_id\":17,\"Operation_Id\":\"ordinary\"}"); Row(4, "parent", "{\"event_id\":17}"); }
        if (mode == "nonstring") { Row(0, null, "{\"event_id\":17}"); Require(rows[0]["event_name"]!.GetValue<string>() == "after"); }
        if (mode == "event")
        { Row(0, "scope", "{\"common\":\"event\",\"operation_id\":\"attribute-only\",\"event_id\":17}"); Row(1, "event.id", "{\"common\":\"direct\"}"); Row(2, "scope", "{\"common\":\"scope\"}"); }
        if (mode == "protected")
        { Row(0, "protected.operation", "{\"event_id\":17,\"service\":\"event.fake\",\"env\":\"fake\",\"version\":\"fake\",\"component\":\"fake\",\"resource\":\"fake\",\"trace_id\":\"event.fake\",\"span_id\":\"fake\"}"); Require(rows[0]["service"]!.GetValue<string>() != "event.fake" && rows[0]["env"]!.GetValue<string>() != "fake" && rows[0]["version"]!.GetValue<string>() != "fake" && rows[0]["component"]!.GetValue<string>() == "Provider.Operation" && rows[0]["resource"] is JsonObject && (string?)rows[0]["trace_id"] == (string?)a["trace"] && (string?)rows[0]["span_id"] == (string?)a["span"]); }
        if (mode == "bounds")
        { Require((string?)rows[0]["operation_id"] == "bounded" && rows[0]["attributes"]!["common"]!.GetValue<string>() == "event" && rows[0]["attributes"]!.AsObject().Count == 1023 && rows[1]["operation_id"] is null && rows[1]["attributes"]!.AsObject().Count == 1023 && rows.All(row => row["truncated"]!.GetValue<bool>() && row["attributes"]!["event_id"]!.GetValue<int>() == 17)); }
        if (mode == "invalid") { Row(0, new string('x', 255), "{\"common\":\"valid\",\"event_id\":17}"); }
        if (mode == "host-omitted") { Row(0, null, "{\"event_id\":17}"); Require(rows[0]["truncated"] is null && rows[0]["event_name"]!.GetValue<string>() == "after"); }
        if (mode == "callback-errors") { Row(0, null, "{\"event_id\":17}"); Require(rows[0]["event_name"]!.GetValue<string>() == "after"); }
    }
}
