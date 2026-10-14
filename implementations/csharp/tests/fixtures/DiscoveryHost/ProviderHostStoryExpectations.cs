using System.Text;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderHostStoryExpectations
{
    internal static readonly string[] Modes = ["async-owned", "async-external", "factory-precedence", "factory-lifetime", "shutdown", "shutdown-loss", "factory-formatter"];
    internal static void Validate(string mode, JsonObject report)
    {
        void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
        bool concurrent = mode.StartsWith("async-", StringComparison.Ordinal), shutdown = mode is "shutdown" or "shutdown-loss", loss = mode == "shutdown-loss";
        int count = concurrent ? 9 : mode == "factory-precedence" ? 4 : mode == "factory-formatter" ? 1 : 3;
        int formatters = concurrent ? 9 : mode is "factory-lifetime" or "shutdown" or "factory-formatter" ? 2 : 3;
        Require((string?)report["mode"] == mode && (string?)report["runtime"] == ".NET 10.0.12" && report["leaseReleased"]!.GetValue<bool>(), "Actual mode/runtime/lease.");
        Require(report["formatterExceptionPreserved"]!.GetValue<bool>() == (mode == "factory-formatter"), "Factory aggregates original formatter failure.");
        Require(report["formatterCalls"]!.GetValue<int>() == formatters && report["arrivals"]!.GetValue<int>() == (concurrent ? 2 : 0), "Actual callback/concurrent cohort.");
        Require(report["sameStopTask"]!.GetValue<bool>() && report["sameStopResult"]!.GetValue<bool>() && report["workFinishedAtStop"]!.GetValue<bool>() == shutdown && report["ownerAttention"]!.GetValue<bool>() == loss, "Application observes shared stop and actual loss.");
        Require(report["sharedLiveAfterFactoryDispose"]!.GetValue<bool>() == (mode == "factory-lifetime") && report["disposedGuards"]!.GetValue<int>() == (mode == "factory-lifetime" ? 2 : 0), "Factory/provider/Core lifetimes.");
        Require((string?)report["invalid"] == (loss ? "1" : "0") && (string?)report["truncated"] == "0", "Actual rejection/truncation.");
        string[] phases = report["phases"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Require(phases.SequenceEqual(shutdown ? new[] { "work.started", "shutdown.requested", "work.finished", "runtime.stop", "factory.dispose" } : []), "Work finishes before runtime stop/factory disposal.");
        byte[] data = Convert.FromBase64String(report["bytes"]!.GetValue<string>()); string text = Encoding.UTF8.GetString(data);
        Require(text.EndsWith('\n') && !text.StartsWith('\uFEFF'), "Actual JSONL framing.");
        JsonObject[] rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        Require(rows.Length == count, "Actual record count.");
        foreach (JsonObject row in rows)
        {
            Require((string?)row["status"] == "info" && row["schema_version"]!.GetValue<int>() == 1 && row["resource"]!["process_id"]!.GetValue<int>() == report["pid"]!.GetValue<int>() && row["truncated"] is null, "Core wire identity and no truncation.");
        }
        if (concurrent)
        {
            string category = mode == "async-owned" ? "Owned.Host.Story" : "CIS.Tests.DiscoveryFixture.ProviderHostStoryFixture.Worker";
            for (int owner = 1; owner <= 2; owner++)
            {
                JsonObject[] operation = rows.Where(x => x["event_name"]!.GetValue<string>().StartsWith($"owner.{owner}.", StringComparison.Ordinal)).ToArray();
                Require(operation.Select(x => x["event_name"]!.GetValue<string>()).SequenceEqual(new[] { $"owner.{owner}.outer", $"owner.{owner}.inner", $"owner.{owner}.event", $"owner.{owner}.restored" }), "Per-operation order.");
                for (int i = 0; i < operation.Length; i++)
                {
                    JsonObject row = operation[i]; JsonObject attributes = row["attributes"]!.AsObject();
                    Require((string?)row["operation_id"] == (i is 1 or 2 ? "nested." : "operation.") + owner && (string?)row["component"] == category && (string?)row["message"] == "rendered", "Distinct operation/typed category.");
                    Require(attributes["owner"]!.GetValue<int>() == owner && (string?)attributes["phase"] == (mode == "async-owned" ? "initial" : "changed") && (string?)attributes["common"] == (i == 2 ? "event" : i == 1 ? "inner" : "outer") && attributes["event_id"]!.GetValue<int>() == 17 && (string?)attributes["operation_id"] == (i == 2 ? "ordinary-event-field" : null), "Copied/borrowed nested/event state.");
                }
            }
            Require((string?)rows[^1]["event_name"] == "after" && rows[^1]["operation_id"] is null && rows[^1]["attributes"]!.AsObject().Count == 1, "No scope leaked to caller.");
        }
        if (mode == "factory-precedence")
        {
            Require(rows.Select(x => (string?)x["operation_id"]).SequenceEqual(new string?[] { "host.outer", "host.inner", "event.explicit", null }), "Core/host/event operation precedence.");
            Require(rows.Take(3).All(x => (string?)x["trace_id"] == (string?)report["trace"] && (string?)x["span_id"] == (string?)report["span"] && (string?)x["service"] == "CIS.Core.DiscoveryFixture" && (string?)x["env"] != "fake" && (string?)x["version"] != "fake" && (string?)x["component"] != "fake" && x["resource"] is JsonObject), "Protected identity and Activity.");
            Require(rows[1]["attributes"]!["common"]!.GetValue<string>() == "event" && rows[1]["attributes"]!["core"]!.GetValue<bool>() && (string?)rows[1]["attributes"]!["operation_id"] == "ordinary-event-field", "Factory layers and event override.");
        }
        if (mode == "factory-lifetime") { Require(rows.Select(x => (string?)x["event_name"]).SequenceEqual(new[] { "before", "direct.live", "other.live" }) && rows.All(x => (string?)x["operation_id"] == "core.owner"), "Runtime and caller scope survive factory disposal."); }
        if (shutdown) { Require(rows.Select(x => (string?)x["event_name"]).SequenceEqual(new[] { "work.started", "work.finished", "owner.finishing" }) && (string?)rows[0]["operation_id"] == "background" && (string?)rows[1]["operation_id"] == "background" && rows[2]["operation_id"] is null, "Application completion and no rejected record."); }
        if (mode == "factory-formatter") { Require((string?)rows[0]["event_name"] == "after.formatter" && (string?)rows[0]["component"] == "CIS.Tests.DiscoveryFixture.ProviderHostStoryFixture.Worker", "No failed-formatter admission; later calls remain usable."); }
        JsonObject result = report["result"]!.AsObject();
        Require((string?)result["completion"] == "Completed" && result["succeeded"]!.GetValue<bool>() && result["loss"]!.GetValue<bool>() == loss, "Actual stop result.");
        JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 1, "File composition.");
        JsonObject output = outputs[0]!.AsObject(); string n = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Require((string?)output["boundary"] == n && (string?)output["written"] == n && (string?)output["failed"] == "0" && (string?)output["abandoned"] == "0" && (string?)output["pending"] == "0" && (string?)output["flush"] == "Succeeded", "Exact stopped delivery.");
    }
}
