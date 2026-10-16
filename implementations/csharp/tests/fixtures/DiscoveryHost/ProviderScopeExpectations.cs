using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.ProviderReportChecks;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderScopeExpectations
{
    internal static readonly string[] Modes = ["owned", "external", "hybrid", "extensions", "async-owned", "async-external", "bounds", "invalid", "host-incompatible", "depth", "filtered", "correlation", "replacement"];
    internal static void Validate(string mode, JsonObject a, int? expectedPid = null, bool physical = false)
    {
        Header(mode, Modes, a, expectedPid);
        Require(Same(a["physical"], JsonValue.Create(physical)) && Same(a["leaseReleased"], JsonValue.Create(physical)), "physical lifetime");
        int count = mode is "hybrid" or "async-owned" or "async-external" or "replacement" ? 3 : mode == "bounds" ? 2 : mode == "filtered" ? 0 : 1;
        int calls = mode is "async-owned" or "async-external" or "depth" ? 3 : mode is "hybrid" or "bounds" or "replacement" ? 2 :
            mode is "invalid" or "host-incompatible" ? 4 : mode == "filtered" ? 0 : 1;
        Require(Integer(a["formatterCalls"], calls), "formatter calls");
        Require(Integer(a["ownedErrors"], mode is "invalid" or "depth" ? 1 : 0), "owned scope errors");
        Require(Integer(a["fieldVisits"], mode == "bounds" ? 1025 : 0), "bounded scope visits");
        Require(Integer(a["filteredHostCalls"], 0), "filtered host capture");
        Require((string?)a["invalid"] == (mode == "depth" ? "2" : mode is "invalid" or "host-incompatible" ? "3" : "0"), "invalid accounting");
        Require((string?)a["truncated"] == (mode == "bounds" ? "2" : "0"), "truncation accounting");
        Stop(a, count, physical, mode is "bounds" or "invalid" or "host-incompatible" or "depth");
        JsonObject[] rows = Rows(a, count);
        string[] names = mode switch
        {
            "hybrid" => ["host", "event", "direct"],
            "bounds" => ["fields", "depth"],
            "replacement" => ["first", "second", "direct"],
            "invalid" or "host-incompatible" or "depth" => ["after"],
            "filtered" => [],
            _ => ["entry"]
        };
        if (mode is "async-owned" or "async-external")
        {
            Require(rows.Select(row => (string?)row["event_name"]).Order(StringComparer.Ordinal)
                .SequenceEqual(["after", "operation.1", "operation.2"]) && (string?)rows[2]["event_name"] == "after", "async completion order");
        }
        else { Require(rows.Select(row => (string?)row["event_name"]).SequenceEqual(names), "event names"); }
        foreach (JsonObject row in rows)
        {
            Require(Integer(row["schema_version"], 1) && (string?)row["status"] == "info" &&
                (string?)row["component"] == ((string?)row["event_name"] == "direct" ? "Direct" : "Provider.Scope") &&
                (string?)row["message"] == "rendered", "core wire mapping");
            Identity(row, a, physical);
            Require(mode == "bounds" ? Same(row["truncated"], "true") : !row.ContainsKey("truncated"), "truncation marker");
        }
        void Attributes(int index, string json, string reason) => Require(Same(rows[index]["attributes"], json), reason);
        if (mode == "owned") { Attributes(0, """{"owner":"original","items":[1,true],"event_id":17}""", "owned snapshot"); }
        if (mode == "external")
        {
            Require(Same(rows[0]["attributes"], """{"owner":"at-capture","event_id":17}""") &&
                (string?)rows[0]["operation_id"] == "host.operation", "host capture snapshot");
        }
        if (mode == "hybrid")
        {
            Require(Same(rows[0]["attributes"], """{"common":"host","core":true,"host":true,"event_id":17}""") &&
                (string?)rows[0]["operation_id"] == "host.operation", "host overlay");
            Require(Same(rows[1]["attributes"], """{"common":"event","core":true,"host":true,"event_id":17}""") &&
                (string?)rows[1]["operation_id"] == "host.operation", "event overlay");
            Require(Same(rows[2]["attributes"], """{"common":"core","core":true}""") &&
                (string?)rows[2]["operation_id"] == "core.operation", "direct core scope");
        }
        if (mode == "extensions")
        {
            Require(Same(rows[0]["attributes"], """{"Owner":"Ω","event_id":17}""") && !rows[0].ContainsKey("message_template"), "Microsoft scope fields");
        }
        if (mode is "async-owned" or "async-external")
        {
            foreach (JsonObject row in rows.Take(2))
            {
                int owner = row["attributes"]!["owner"]!.GetValue<int>();
                Require(owner is 1 or 2 && Same(row["attributes"], JsonNode.Parse($$"""{"owner":{{owner}},"event_id":17}""")) &&
                    (string?)row["operation_id"] == "operation." + owner && (string?)row["event_name"] == "operation." + owner, "isolated async scopes");
            }
            Require(Same(rows[2]["attributes"], """{"event_id":17}""") && !rows[2].ContainsKey("operation_id"), "popped async scopes");
        }
        if (mode == "bounds")
        {
            Require(Same(rows[0]["attributes"], Fields(1022)), "bounded owned prefix");
            JsonArray deep = [];
            for (int i = 0; i < 6; i++) { deep = new JsonArray(deep); }
            Require(Same(rows[1]["attributes"], new JsonObject { ["event_id"] = 17, ["deep"] = deep }), "bounded host depth");
        }
        if (mode == "depth") { Attributes(0, """{"event_id":17}""", "no partial invalid scope"); }
        if (mode is "invalid" or "host-incompatible")
        {
            Require(Same(rows[0]["attributes"], """{"event_id":17}""") && !rows[0].ContainsKey("operation_id"), "incompatible frame rejected");
        }
        if (mode == "correlation")
        {
            Activity(a);
            Require((string?)rows[0]["trace_id"] == (string?)a["trace"] && (string?)rows[0]["span_id"] == (string?)a["span"] &&
                Same(rows[0]["attributes"], """{"trace_id":"fake","event_id":17}"""), "frozen Activity");
        }
        if (mode == "replacement")
        {
            Require(Same(rows[0]["attributes"], """{"owner":"first","event_id":17}""") &&
                Same(rows[1]["attributes"], """{"owner":"second","event_id":17}""") && Same(rows[2]["attributes"], "{}"), "replacement and ownership");
        }
    }
}
