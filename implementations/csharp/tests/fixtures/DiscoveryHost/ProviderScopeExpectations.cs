using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderScopeExpectations
{
    internal static readonly string[] Modes = ["owned", "external", "hybrid", "extensions", "async-owned", "async-external", "bounds", "invalid", "host-incompatible", "depth", "filtered", "correlation", "replacement"];
    private static void Require(bool value) { if (!value) { throw new InvalidDataException("Provider scope expectation failed."); } }
    internal static void Validate(string mode, JsonObject a)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        Require(a["leaseReleased"]!.GetValue<bool>() == a["physical"]!.GetValue<bool>() && a["filteredHostCalls"]!.GetValue<int>() == 0);
        int count = mode is "invalid" or "host-incompatible" ? 4 : mode is "hybrid" or "async-owned" or "async-external" or "replacement" ? 3 : mode == "bounds" ? 2 : mode == "filtered" ? 0 : 1;
        int calls = mode is "async-owned" or "async-external" or "depth" ? 3 : mode is "hybrid" or "bounds" or "replacement" ? 2 : mode is "invalid" or "host-incompatible" ? 4 : mode == "filtered" ? 0 : 1;
        Require(a["formatterCalls"]!.GetValue<int>() == calls && a["ownedErrors"]!.GetValue<int>() == (mode is "invalid" or "depth" ? 1 : 0) && a["fieldVisits"]!.GetValue<int>() == (mode == "bounds" ? 1025 : 0));
        Require(a["invalid"]!.GetValue<string>() == (mode == "depth" ? "2" : "0") && a["truncated"]!.GetValue<string>() == (mode == "bounds" ? "2" : mode is "invalid" or "host-incompatible" ? "3" : "0"));
        Require(a["result"]!["completion"]!.GetValue<string>() == "Completed" && a["result"]!["succeeded"]!.GetValue<bool>() && a["result"]!["loss"]!.GetValue<bool>() == (mode is "bounds" or "invalid" or "host-incompatible" or "depth"));
        JsonNode output = a["result"]!["outputs"]![0]!; Require(output["boundary"]!.GetValue<string>() == count.ToString(System.Globalization.CultureInfo.InvariantCulture) && output["written"]!.GetValue<string>() == count.ToString(System.Globalization.CultureInfo.InvariantCulture) && output["failed"]!.GetValue<string>() == "0" && output["abandoned"]!.GetValue<string>() == "0" && output["pending"]!.GetValue<string>() == "0" && output["flush"]!.GetValue<string>() == "Succeeded");
        byte[] bytes = Convert.FromBase64String(a["bytes"]!.GetValue<string>()); Require(count == 0 ? bytes.Length == 0 : bytes[^1] == (byte)'\n' && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries); Require(lines.Length == count && lines.All(line => Encoding.UTF8.GetByteCount(line) + 1 <= 16384));
        JsonObject[] rows = lines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        foreach (JsonObject row in rows) { Require(row["status"]!.GetValue<string>() == "info" && row["message"]!.GetValue<string>() == "rendered"); }
        void Attributes(int index, string json) => Require(JsonNode.DeepEquals(rows[index]["attributes"], JsonNode.Parse(json)));
        if (mode == "owned") { Attributes(0, "{\"owner\":\"original\",\"items\":[1,true],\"event_id\":17}"); }
        if (mode == "external") { Attributes(0, "{\"owner\":\"at-capture\",\"event_id\":17}"); Require(rows[0]["operation_id"]!.GetValue<string>() == "host.operation"); }
        if (mode == "hybrid")
        {
            Attributes(0, "{\"common\":\"host\",\"core\":true,\"host\":true,\"event_id\":17}"); Attributes(1, "{\"common\":\"event\",\"core\":true,\"host\":true,\"event_id\":17}"); Attributes(2, "{\"common\":\"core\",\"core\":true}");
            Require(rows[0]["operation_id"]!.GetValue<string>() == "host.operation" && rows[1]["operation_id"]!.GetValue<string>() == "host.operation" && rows[2]["operation_id"]!.GetValue<string>() == "core.operation");
        }
        if (mode == "extensions") { Attributes(0, "{\"Owner\":\"Ω\",\"event_id\":17}"); }
        if (mode is "async-owned" or "async-external")
        {
            Require(rows.Select(row => row["event_name"]!.GetValue<string>()).Order(StringComparer.Ordinal).SequenceEqual(new[] { "after", "operation.1", "operation.2" }));
            foreach (JsonObject row in rows.Where(row => row["event_name"]!.GetValue<string>() != "after"))
            { int owner = row["attributes"]!["owner"]!.GetValue<int>(); Require(owner is 1 or 2 && row["operation_id"]!.GetValue<string>() == "operation." + owner && row["event_name"]!.GetValue<string>() == "operation." + owner); }
            Attributes(2, "{\"event_id\":17}"); Require(rows[2]["operation_id"] is null);
        }
        if (mode == "bounds")
        {
            JsonObject fields = rows[0]["attributes"]!.AsObject(); Require(fields.Count == 1023 && fields["k1022"]!.GetValue<int>() == 1022 && fields["event_id"] is null && rows.All(row => row["truncated"]!.GetValue<bool>()));
            JsonNode deep = new JsonArray(); for (int i = 0; i < 6; i++) { deep = new JsonArray(deep); }
            Require(JsonNode.DeepEquals(rows[1]["attributes"]!["deep"], deep) && rows[1]["attributes"]!["event_id"]!.GetValue<int>() == 17);
        }
        if (mode == "depth") { Attributes(0, "{\"event_id\":17}"); Require(rows[0]["event_name"]!.GetValue<string>() == "after"); }
        if (mode is "invalid" or "host-incompatible")
        {
            for (int i = 0; i < 4; i++) { Attributes(i, "{\"event_id\":17}"); Require(i < 3 ? rows[i]["truncated"]!.GetValue<bool>() : rows[i]["truncated"] is null); }
            Require(rows[3]["event_name"]!.GetValue<string>() == "after");
        }
        if (mode == "correlation") { Attributes(0, "{\"trace_id\":\"fake\",\"event_id\":17}"); Require(rows[0]["trace_id"]!.GetValue<string>() == a["trace"]!.GetValue<string>() && rows[0]["span_id"]!.GetValue<string>() == a["span"]!.GetValue<string>()); }
        if (mode == "replacement") { Attributes(0, "{\"owner\":\"first\",\"event_id\":17}"); Attributes(1, "{\"owner\":\"second\",\"event_id\":17}"); Attributes(2, "{}"); }
    }
}
