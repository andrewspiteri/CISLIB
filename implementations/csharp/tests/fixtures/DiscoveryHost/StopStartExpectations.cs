using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopStartExpectations
{
    internal static readonly string[] Modes = ["empty", "normal", "dual", "configured-default", "configured-min", "configured-max", "configured-below", "configured-above", "env", "shared", "pre-cancelled", "blocked-write", "blocked-flush", "clock-correction", "late-facts", "closed-writes"];
    private static void Require(bool condition) { if (!condition) { throw new InvalidOperationException("Stop boundary expectation failed."); } }
    private static long Number(JsonNode? node) => long.Parse(node!.GetValue<string>(), CultureInfo.InvariantCulture);
    internal static void Validate(string mode, JsonObject report)
    {
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
            JsonNode output = outputs[i]!;
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
    }
}
