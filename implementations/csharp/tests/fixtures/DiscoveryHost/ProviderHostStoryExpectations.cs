using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Tests.DiscoveryFixture.ProviderReportChecks;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderHostStoryExpectations
{
    internal static readonly string[] Modes = ["async-owned", "async-external", "factory-precedence", "factory-lifetime", "shutdown", "shutdown-loss", "factory-formatter"];
    private const string Category = "CIS.Tests.DiscoveryFixture.ProviderHostStoryFixture.Worker";
    internal static void Validate(string mode, JsonObject a, int? expectedPid = null)
    {
        Header(mode, Modes, a, expectedPid);
        bool concurrent = mode.StartsWith("async-", StringComparison.Ordinal), shutdown = mode is "shutdown" or "shutdown-loss", loss = mode == "shutdown-loss";
        int count = concurrent ? 9 : mode == "factory-precedence" ? 4 : mode == "factory-formatter" ? 1 : 3;
        int formats = concurrent ? 9 : mode is "factory-lifetime" or "shutdown" or "factory-formatter" ? 2 : 3;
        Require(Same(a["formatterExceptionPreserved"], JsonValue.Create(mode == "factory-formatter")), "formatter exception identity");
        Require(Integer(a["formatterCalls"], formats) && Integer(a["arrivals"], concurrent ? 2 : 0), "callbacks and cohort");
        Require(Same(a["sameStopTask"], "true") && Same(a["sameStopResult"], "true") &&
            Same(a["workFinishedAtStop"], JsonValue.Create(shutdown)) && Same(a["ownerAttention"], JsonValue.Create(loss)), "observed stop and attention");
        Require(Same(a["sharedLiveAfterFactoryDispose"], JsonValue.Create(mode == "factory-lifetime")) &&
            Integer(a["disposedGuards"], mode == "factory-lifetime" ? 2 : 0), "factory and Core ownership");
        Require((string?)a["invalid"] == (loss ? "1" : "0") && (string?)a["truncated"] == "0" && Same(a["leaseReleased"], "true"), "actual accounting and lease");
        string[] phases = a["phases"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        Require(phases.SequenceEqual(shutdown ? ["work.started", "shutdown.requested", "work.finished", "runtime.stop", "factory.dispose"] : Array.Empty<string>()), "shutdown ordering");
        JsonObject[] rows = Rows(a, count);
        foreach (JsonObject row in rows)
        {
            Require(Integer(row["schema_version"], 1) && (string?)row["status"] == "info" && !row.ContainsKey("truncated"), "wire schema/status/truncation");
            Identity(row, a, true, "automatic identity");
        }
        void Event(JsonObject row, string name, string? operation, string category, JsonObject attributes, string message = "rendered")
        {
            Require((string?)row["event_name"] == name && (string?)row["operation_id"] == operation &&
                (string?)row["component"] == category && (string?)row["message"] == message, "operation category message");
            Require(Same(row["attributes"], attributes), "exact event attributes");
        }
        if (concurrent)
        {
            string category = mode == "async-owned" ? "Owned.Host.Story" : Category;
            for (int owner = 1; owner <= 2; owner++)
            {
                JsonObject[] selected = rows.Where(row => row["event_name"]!.GetValue<string>().StartsWith("owner." + owner + ".", StringComparison.Ordinal)).ToArray();
                string[] names = new[] { "outer", "inner", "event", "restored" }.Select(name => "owner." + owner + "." + name).ToArray();
                Require(selected.Select(row => (string?)row["event_name"]).SequenceEqual(names), "per-operation order");
                for (int i = 0; i < selected.Length; i++)
                {
                    var attributes = new JsonObject
                    {
                        ["owner"] = owner,
                        ["phase"] = mode == "async-owned" ? "initial" : "changed",
                        ["common"] = i == 2 ? "event" : i == 1 ? "inner" : "outer",
                        ["event_id"] = 17
                    };
                    if (i == 2) { attributes["operation_id"] = "ordinary-event-field"; }
                    Event(selected[i], names[i], (i is 1 or 2 ? "nested." : "operation.") + owner, category, attributes);
                }
            }
            Event(rows[^1], "after", null, category, new JsonObject { ["event_id"] = 17 });
        }
        else if (mode == "factory-precedence")
        {
            JsonObject first = JsonNode.Parse("""{"core":true,"service":"fake","trace_id":"fake","span_id":"fake","event_id":17,"common":"host"}""")!.AsObject();
            Event(rows[0], "outer", "host.outer", Category, first);
            JsonObject second = JsonNode.Parse("""{"core":true,"service":"fake","trace_id":"fake","span_id":"fake","event_id":17,"common":"event","operation_id":"ordinary-event-field","env":"fake","version":"fake","component":"fake","resource":"fake"}""")!.AsObject();
            Event(rows[1], "inner", "host.inner", Category, second);
            Event(rows[2], "explicit", "event.explicit", "Direct.Host.Story", new JsonObject { ["core"] = true, ["common"] = "core" }, "direct");
            Event(rows[3], "after", null, Category, new JsonObject { ["event_id"] = 17 });
            Activity(a);
            Require(rows.Take(3).All(row => Same(row["trace_id"], a["trace"]) && Same(row["span_id"], a["span"]) &&
                row["env"]?.GetValueKind() == JsonValueKind.String && row["version"]?.GetValueKind() == JsonValueKind.String &&
                (string?)row["env"] != "fake" && (string?)row["version"] != "fake"), "protected Activity and identity");
            Require(!rows[3].ContainsKey("trace_id") && !rows[3].ContainsKey("span_id"), "Activity does not leak");
        }
        else if (mode == "factory-lifetime")
        {
            Event(rows[0], "before", "core.owner", Category, new JsonObject { ["owner"] = "application", ["event_id"] = 17 });
            Event(rows[1], "direct.live", "core.owner", "Direct.Host.Story", new JsonObject { ["owner"] = "application" }, "direct");
            Event(rows[2], "other.live", "core.owner", "Other.Host.Story", new JsonObject { ["owner"] = "application", ["event_id"] = 17 });
        }
        else if (mode == "factory-formatter") { Event(rows[0], "after.formatter", null, Category, new JsonObject { ["event_id"] = 17 }); }
        else
        {
            Event(rows[0], "work.started", "background", Category, new JsonObject { ["owner"] = "worker", ["event_id"] = 17 });
            Event(rows[1], "work.finished", "background", Category, new JsonObject { ["owner"] = "worker", ["event_id"] = 17 });
            Event(rows[2], "owner.finishing", null, "Direct.Host.Story", new JsonObject(), "direct");
        }
        Stop(a, count, true, loss, "actual Core stop result", "exact drain delivery");
    }
}
