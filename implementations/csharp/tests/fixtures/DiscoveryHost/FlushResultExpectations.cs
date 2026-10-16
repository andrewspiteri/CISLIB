using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushResultExpectations
{
    private static void Require(bool condition) { if (!condition) { throw new InvalidOperationException("Public flush expectation failed."); } }
    internal static void Validate(string mode, JsonObject report, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, report, processId); ValidateReader(mode, report);
        static bool Bool(JsonNode? n) => n!.GetValue<bool>();
        static long Number(JsonNode? n) => LifecycleReportChecks.Number(n);
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12" && Bool(report["immutableAfterIo"]));
        bool cancelled = mode is "cancel" or "pre-cancel" or "long-timeout";
        Require(Bool(report["cancelled"]) == cancelled);
        if (mode != "physical") { Require(report["timers"]!["created"]!.GetValue<int>() == report["timers"]!["disposed"]!.GetValue<int>()); }
        if (mode == "long-timeout")
        {
            JsonNode phase = report["longTimerPhase"]!;
            Require(phase["created"]!.GetValue<int>() == 1 && phase["disposed"]!.GetValue<int>() == 0 && phase["fires"]!.GetValue<int>() == 1 &&
                Number(phase["nextDueTicks"]) == 1728000000000L);
        }
        if (cancelled) { Require(report["result"] is null && report["controlsAfterWait"]!.GetValue<int>() == 1); return; }
        JsonObject result = report["result"]!.AsObject(); JsonArray outputs = result["outputs"]!.AsArray();
        bool timeout = mode is "write-timeout" or "flush-timeout" or "exhaustion" or "clock" or "dual" or "system-timeout";
        bool loss = mode is "loss" or "write-failure" or "flush-failure" or "exhaustion" or "closed";
        Require(result["completion"]!.GetValue<string>() == (timeout ? "TimedOut" : "Completed") && Bool(result["succeeded"]) == (!timeout && !loss || mode == "loss") && Bool(result["loss"]) == loss);
        Require(outputs.Count == (mode == "empty" ? 0 : mode is "physical" or "dual" ? 2 : 1));
        foreach (JsonNode? node in outputs)
        {
            JsonObject output = node!.AsObject();
            long target = Number(output["boundary"]), written = Number(output["written"]), failed = Number(output["failed"]), abandoned = Number(output["abandoned"]), pending = Number(output["pending"]);
            Require(target == written + failed + abandoned + pending);
            string status = output["flush"]!.GetValue<string>();
            Require(Bool(output["flushSucceeded"]) == (status == "Succeeded"));
            string name = output["name"]!.GetValue<string>();
            (long b, long w, long f, long a, long p, string flush) expected = mode switch
            {
                "filtered" => (0, 0, 0, 0, 0, "Succeeded"),
                "closed" => (0, 0, 0, 0, 0, "Failed"),
                "physical" or "loss" => (2, 2, 0, 0, 0, "Succeeded"),
                "write-failure" => (2, 0, 1, 1, 0, "Failed"),
                "flush-failure" => (1, 1, 0, 0, 0, "Failed"),
                "later-failure" => (1, 1, 0, 0, 0, "Succeeded"),
                "exhaustion" => (long.MaxValue, long.MaxValue - 2, 0, 1, 1, "Failed"),
                "flush-timeout" => (1, 1, 0, 0, 0, "Pending"),
                "dual" when name == "console" => (1, 1, 0, 0, 0, "Succeeded"),
                _ => (1, 0, 0, 0, 1, "Pending")
            };
            Require((target, written, failed, abandoned, pending, status) == expected);
            Require(Number(output["full"]) == (mode == "loss" ? 2 : 0) && Number(output["filtered"]) == (mode is "loss" or "filtered" ? 1 : 0) && Number(output["unavailable"]) == 0);
        }
        Require(Number(result["invalid"]) == (mode == "loss" ? 1 : 0) && Number(result["truncated"]) == (mode == "loss" ? 1 : 0));
        if (mode == "clock") { Require(Bool(report["survivedUtcCorrections"])); }
        if (mode == "exhaustion") { Require(Bool(report["stillPendingAfterFault"])); }
        if (mode == "physical") { Require(Bool(report["leaseReleased"])); }
    }
    private static void ValidateReader(string mode, JsonObject report)
    {
        static long Count(JsonNode? value) => LifecycleReportChecks.Number(value);
        bool cancelled = mode is "cancel" or "pre-cancel" or "long-timeout";
        if (mode != "physical")
        {
            Require(report["heldControls"]!.GetValue<int>() == (mode is "empty" or "closed" ? 0 : 1));
            Require(report["controlsAfterWait"]!.GetValue<int>() == (mode is "empty" or "loss" or "write-failure" or "flush-failure" or "later-failure" or "filtered" or "closed" or "dual" ? 0 : 1));
        }
        if (cancelled) { Require(!report.ContainsKey("result")); }
        else
        {
            JsonObject result = report["result"]!.AsObject(); LifecycleReportChecks.Result(result, globalCounts: true);
            LifecycleReportChecks.Names(result["outputs"]!.AsArray(), mode == "empty" ? [] : mode is "physical" or "dual" ? ["console", "file"] : ["console"]);
        }
        int pid = report["pid"]!.GetValue<int>();
        JsonObject[] console = LifecycleReportChecks.Records(report["bytes"], pid), file = LifecycleReportChecks.Records(report["fileBytes"], pid);
        string[] names = mode is "empty" or "write-failure" or "filtered" or "closed" ? [] : mode is "loss" or "physical" ? ["first", "second"] : ["first"];
        Require(console.Select(record => (string)record["event_name"]!).SequenceEqual(names));
        Require(file.Select(record => (string)record["event_name"]!).SequenceEqual(mode == "physical" ? ["first", "second"] : mode == "dual" ? ["first"] : Array.Empty<string>()));
        Require(console.Concat(file).All(record => (string?)record["status"] == "info"));
        if (mode == "physical") { Require(report["leaseReleased"]!.GetValue<bool>()); return; }
        JsonArray diagnostics = report["diagnostics"]!.AsArray();
        Require(diagnostics.All(output => Count(output!["pending"]) == 0));
        if (mode == "loss") { Require(Count(diagnostics[0]!["invalid"]) == 2 && Count(diagnostics[0]!["filtered"]) == 2); }
        if (mode == "later-failure") { Require(Count(diagnostics[0]!["failed"]) == 1); }
    }
}
