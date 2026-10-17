using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.ProviderReportChecks;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderOperationExpectations
{
    internal static readonly string[] Modes = ["owned", "host", "nested-owned", "nested-host", "duplicates", "nonstring", "event", "protected", "bounds", "invalid", "host-omitted", "callback-errors"];
    internal static void Validate(string mode, JsonObject a, int? expectedPid = null, bool physical = false)
    {
        Header(mode, Modes, a, expectedPid);
        Require(Same(a["physical"], JsonValue.Create(physical)) && Same(a["leaseReleased"], JsonValue.Create(physical)), "physical lifetime");
        int count = mode switch { "nested-owned" => 4, "nested-host" or "event" => 3, "duplicates" => 5, "bounds" => 2, _ => 1 };
        int calls = mode == "event" ? 1 : mode == "callback-errors" ? 3 : mode == "host-omitted" ? 2 : count;
        Require(Integer(a["formatterCalls"], calls), "formatter calls");
        Require(Integer(a["ownedErrors"], mode == "invalid" ? 3 : mode is "duplicates" or "nonstring" ? 2 : 0), "owned ID errors");
        Require((string?)a["invalid"] == (mode == "callback-errors" ? "2" : mode == "host-omitted" ? "1" : "0"), "invalid accounting");
        Require((string?)a["truncated"] == (mode == "bounds" ? "2" : "0"), "truncation accounting");
        Stop(a, count, physical, mode is "bounds" or "host-omitted" or "callback-errors");
        JsonObject[] rows = Rows(a, count);
        string[] names = mode switch
        {
            "nested-owned" => ["outer", "inner", "blank", "after"],
            "nested-host" => ["outer", "inner", "blank"],
            "duplicates" => ["rejected.0", "rejected.1", "duplicate.2", "duplicate.3", "duplicate.4"],
            "nonstring" or "host-omitted" or "callback-errors" => ["after"],
            "event" => ["provider", "explicit", "blank"],
            "bounds" => ["retained", "late"],
            _ => ["entry"]
        };
        Require(rows.Select(row => (string?)row["event_name"]).SequenceEqual(names), "event names");
        foreach (JsonObject row in rows)
        {
            Require(Integer(row["schema_version"], 1) && (string?)row["status"] == "info" && (string?)row["message"] == "rendered" &&
                (string?)row["component"] == (mode == "event" && (string?)row["event_name"] != "provider" ? "Direct" : "Provider.Operation"), "core wire mapping");
            Identity(row, a, physical);
            Require(mode == "bounds" ? Same(row["truncated"], "true") : !row.ContainsKey("truncated"), "truncation marker");
        }
        void Row(int i, string? operationId, string attributes) =>
            Require((string?)rows[i]["operation_id"] == operationId && Same(rows[i]["attributes"], attributes), "operation and attributes");
        if (mode == "owned") { Row(0, "owned.operation", """{"common":"original","event_id":17}"""); }
        if (mode == "host") { Row(0, "host.operation", """{"common":"at-capture","event_id":17}"""); }
        if (mode == "nested-owned")
        {
            Row(0, "outer", """{"common":"outer","event_id":17}"""); Row(1, "inner", """{"common":"inner","event_id":17}""");
            Row(2, "inner", """{"common":"event","event_id":17,"operation_id":"attribute-only"}"""); Row(3, null, """{"event_id":17}""");
        }
        if (mode == "nested-host")
        {
            Row(0, "host.outer", """{"core":true,"common":"outer","event_id":17}""");
            Row(1, "host.inner", """{"core":true,"common":"inner","event_id":17}"""); Row(2, "host.inner", """{"core":true,"common":"event","event_id":17}""");
        }
        if (mode == "duplicates")
        {
            for (int i = 0; i < 5; i++) { Row(i, "parent", i == 3 ? """{"event_id":17,"Operation_Id":"ordinary"}""" : """{"event_id":17}"""); }
        }
        if (mode == "nonstring") { Row(0, null, """{"event_id":17}"""); }
        if (mode == "event")
        {
            Row(0, "scope", """{"common":"event","event_id":17,"operation_id":"attribute-only"}""");
            Row(1, "event.id", """{"common":"direct"}"""); Row(2, "scope", """{"common":"scope"}""");
        }
        if (mode == "protected")
        {
            Row(0, "protected.operation", """{"event_id":17,"service":"event.fake","env":"fake","version":"fake","component":"fake","resource":"fake","trace_id":"event.fake","span_id":"fake"}""");
            Activity(a);
            Require(rows[0]["env"]?.GetValueKind() == JsonValueKind.String && rows[0]["version"]?.GetValueKind() == JsonValueKind.String &&
                (string?)rows[0]["env"] != "fake" && (string?)rows[0]["version"] != "fake" &&
                (string?)rows[0]["trace_id"] == (string?)a["trace"] && (string?)rows[0]["span_id"] == (string?)a["span"], "protected identity and Activity");
        }
        if (mode == "bounds")
        {
            Require((string?)rows[0]["operation_id"] == "bounded" && Same(rows[0]["attributes"], Fields(1021, common: "event")) &&
                !rows[1].ContainsKey("operation_id") && Same(rows[1]["attributes"], Fields(1022)), "operation and attributes");
        }
        if (mode == "invalid") { Row(0, new string('x', 255), """{"common":"valid","event_id":17}"""); }
        if (mode is "host-omitted" or "callback-errors") { Row(0, null, """{"event_id":17}"""); }
    }
}
