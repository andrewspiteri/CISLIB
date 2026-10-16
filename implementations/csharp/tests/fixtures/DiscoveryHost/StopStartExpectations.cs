using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopStartExpectations
{
    internal static readonly string[] Modes = ["empty", "normal", "dual", "configured-default", "configured-min", "configured-max", "configured-below", "configured-above", "env", "shared", "pre-cancelled", "blocked-write", "blocked-flush", "clock-correction", "late-facts", "closed-writes"];
    private static void Require(bool condition) { if (!condition) { throw new InvalidOperationException("Stop boundary expectation failed."); } }
    private static long Number(JsonNode? value) => LifecycleReportChecks.Number(value);
    internal static void Validate(string mode, JsonObject report, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, report, processId);
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        if (mode is "configured-below" or "configured-above")
        {
            Require(report["error"]!.GetValue<string>() == "ArgumentOutOfRangeException" && report["acquired"]!.GetValue<int>() == 0 && report["timers"]!["created"]!.GetValue<int>() == 0);
            return;
        }
        bool empty = mode == "empty", dual = mode == "dual", flush = mode == "blocked-flush", late = mode == "late-facts";
        bool success = mode is "empty" or "normal" or "dual" or "closed-writes";
        JsonObject result = report["result"]!.AsObject();
        Require(result["completion"]!.GetValue<string>() == (success ? "Completed" : "TimedOut") && result["succeeded"]!.GetValue<bool>() == success && result["loss"]!.GetValue<bool>() == late);
        Require(Number(result["invalid"]) == (late ? 1 : 0) && Number(result["truncated"]) == 0 && Number(report["liveInvalid"]) == (late ? 2 : 0));
        JsonArray outputs = result["outputs"]!.AsArray();
        Require(outputs.Count == (empty ? 0 : dual ? 2 : 1));
        for (int i = 0; i < outputs.Count; i++)
        {
            JsonNode output = outputs[i]!; Require(output.AsObject().Count == 10);
            long target = dual && i == 0 ? 2 : 1;
            Require(output["name"]!.GetValue<string>() == (dual && i == 1 ? "file" : "console"));
            Require(Number(output["boundary"]) == target && Number(output["written"]) == (success || flush ? target : 0));
            Require(Number(output["failed"]) == 0 && Number(output["abandoned"]) == 0 && Number(output["pending"]) == (success || flush ? 0 : 1));
            Require(output["flush"]!.GetValue<string>() == (success ? "Succeeded" : "Pending"));
            Require(Number(output["full"]) == 0 && Number(output["unavailable"]) == 0 && Number(output["filtered"]) == (dual && i == 1 ? 1 : 0));
        }
        Require(report["cachedIdentity"]!.GetValue<bool>() && report["immutableAfterIo"]!.GetValue<bool>() && report["sharedWorkersJoined"]!.GetValue<bool>() && report["finalState"]!.GetValue<string>() == "Stopped");
        Require(report["joinedCallers"]!.GetValue<int>() == 64 && report["concurrentStarters"]!.GetValue<int>() == (mode == "shared" ? 32 : 0) && report["lateFactories"]!.GetValue<int>() == 0);
        Require(report["tokenPreserved"]!.GetValue<bool>() == (mode == "pre-cancelled"));
        int milliseconds = mode switch { "configured-default" => 5000, "configured-min" => 1, "configured-max" => 300000, "env" => 250, _ => 10 };
        Require(report["configuredMilliseconds"]!.GetValue<int>() == milliseconds);
        JsonNode timers = report["timers"]!;
        Require(timers["created"]!.GetValue<int>() == (empty ? 0 : 1) && timers["disposed"]!.GetValue<int>() == (empty ? 0 : 1) && timers["active"]!.GetValue<int>() == 0);
        Require(timers["fires"]!.GetValue<int>() == (success ? 0 : 1) && timers["callbacks"]!.GetValue<int>() == 0 && timers["pendingJoins"]!.GetValue<int>() == 0);
        LifecycleReportChecks.Result(result, globalCounts: true);
        Require(timers["maximumActive"]!.GetValue<int>() == (empty ? 0 : 1));
        foreach ((string key, string[] names) in new[] { ("writer", empty ? Array.Empty<string>() : dual ? new[] { "one", "both" } : new[] { "one" }), ("file", dual ? new[] { "both" } : Array.Empty<string>()) })
        {
            JsonNode writer = report[key]!;
            int count = names.Length, flushes = count > 0 && (success || flush) ? 1 : 0;
            Require(writer["writes"]!.GetValue<int>() == count && writer["flushes"]!.GetValue<int>() == flushes && writer["maximumIo"]!.GetValue<int>() == (count > 0 ? 1 : 0));
            string[] order = Enumerable.Range(1, count).Select(index => "write." + index).Concat(flushes > 0 ? new[] { "flush." + count } : []).ToArray();
            Require(writer["order"]!.AsArray().Select(value => (string)value!).SequenceEqual(order));
            JsonObject[] records = LifecycleReportChecks.Records(writer["bytes"], report["pid"]!.GetValue<int>());
            Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(names));
            Require(records.All(record => (string?)record["component"] == "StopStart" && (string?)record["status"] == ((string?)record["event_name"] == "both" ? "warn" : "info")));
        }
        JsonArray diagnostics = report["diagnostics"]!.AsArray();
        LifecycleReportChecks.Names(diagnostics, empty ? [] : dual ? ["console", "file"] : ["console"]);
        for (int index = 0; index < diagnostics.Count; index++)
        {
            JsonNode output = diagnostics[index]!; long target = dual && index == 0 ? 2 : 1;
            Require(Number(output["accepted"]) == target && Number(output["written"]) == target);
            Require(new[] { "failed", "abandoned", "pending", "full", "unavailable" }.All(key => Number(output[key]) == 0));
            Require(Number(output["filtered"]) == (dual && index == 1 ? 1 : 0) && output["fault"] is null);
        }
    }
}
