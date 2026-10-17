using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushResultExpectations
{
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidOperationException(reason); } }
    internal static void Validate(string mode, JsonObject report, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, report, processId); ValidateReader(mode, report);
        static bool Bool(JsonNode? n) => n!.GetValue<bool>();
        static long Number(JsonNode? n) => LifecycleReportChecks.Number(n);
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12" && Bool(report["immutableAfterIo"]), "immutable observation");
        bool cancelled = mode is "cancel" or "pre-cancel" or "long-timeout";
        Require(Bool(report["cancelled"]) == cancelled, "caller cancellation");
        if (mode != "physical") { Require(report["timers"]!["created"]!.GetValue<int>() == report["timers"]!["disposed"]!.GetValue<int>(), "timer disposal"); }
        if (mode == "long-timeout")
        {
            JsonNode phase = report["longTimerPhase"]!;
            Require(phase["created"]!.GetValue<int>() == 1 && phase["disposed"]!.GetValue<int>() == 0 && phase["fires"]!.GetValue<int>() == 1 &&
                Number(phase["nextDueTicks"]) == 1728000000000L, "long deadline timer");
        }
        if (cancelled) { Require(report["result"] is null && report["controlsAfterWait"]!.GetValue<int>() == 1, "cancelled retained control"); return; }
        JsonObject result = report["result"]!.AsObject(); JsonArray outputs = result["outputs"]!.AsArray();
        bool timeout = mode is "write-timeout" or "flush-timeout" or "exhaustion" or "clock" or "dual" or "system-timeout";
        bool loss = mode is "loss" or "write-failure" or "flush-failure" or "exhaustion" or "closed";
        Require(result["completion"]!.GetValue<string>() == (timeout ? "TimedOut" : "Completed") && Bool(result["succeeded"]) == (!timeout && !loss || mode == "loss") && Bool(result["loss"]) == loss, "flush summary");
        Require(outputs.Count == (mode == "empty" ? 0 : mode is "physical" or "dual" ? 2 : 1), "output count");
        foreach (JsonNode? node in outputs)
        {
            JsonObject output = node!.AsObject();
            long target = Number(output["boundary"]), written = Number(output["written"]), failed = Number(output["failed"]), abandoned = Number(output["abandoned"]), pending = Number(output["pending"]);
            Require(target == written + failed + abandoned + pending, "boundary partition");
            string status = output["flush"]!.GetValue<string>();
            Require(Bool(output["flushSucceeded"]) == (status == "Succeeded"), "flush flag");
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
            Require((target, written, failed, abandoned, pending, status) == expected, "covered dispositions");
            Require(Number(output["full"]) == (mode == "loss" ? 2 : 0) && Number(output["filtered"]) == (mode is "loss" or "filtered" ? 1 : 0) && Number(output["unavailable"]) == 0, "initial rejection facts");
        }
        Require(Number(result["invalid"]) == (mode == "loss" ? 1 : 0) && Number(result["truncated"]) == (mode == "loss" ? 1 : 0), "initial global facts");
        if (mode == "clock") { Require(Bool(report["survivedUtcCorrections"]), "monotonic deadline"); }
        if (mode == "exhaustion") { Require(Bool(report["stillPendingAfterFault"]), "exhaustion pending"); }
        if (mode == "physical") { Require(Bool(report["leaseReleased"]), "physical lease"); }
    }
    private static void ValidateReader(string mode, JsonObject report)
    {
        static long Count(JsonNode? value) => LifecycleReportChecks.Number(value);
        bool cancelled = mode is "cancel" or "pre-cancel" or "long-timeout";
        if (mode != "physical")
        {
            Require(report["heldControls"]!.GetValue<int>() == (mode is "empty" or "closed" ? 0 : 1), "registered control");
            Require(report["controlsAfterWait"]!.GetValue<int>() == (mode is "empty" or "loss" or "write-failure" or "flush-failure" or "later-failure" or "filtered" or "closed" or "dual" ? 0 : 1), "retained control");
        }
        if (cancelled) { Require(!report.ContainsKey("result"), "cancelled result absence"); }
        else
        {
            JsonObject result = report["result"]!.AsObject(); LifecycleReportChecks.Result(result, globalCounts: true);
            LifecycleReportChecks.Names(result["outputs"]!.AsArray(), mode == "empty" ? [] : mode is "physical" or "dual" ? ["console", "file"] : ["console"]);
        }
        int pid = report["pid"]!.GetValue<int>();
        JsonObject[] console = LifecycleReportChecks.Records(report["bytes"], pid), file = LifecycleReportChecks.Records(report["fileBytes"], pid);
        string[] names = mode is "empty" or "write-failure" or "filtered" or "closed" ? [] : mode is "loss" or "physical" ? ["first", "second"] : ["first"];
        Require(console.Select(record => (string)record["event_name"]!).SequenceEqual(names), "console record selection");
        Require(file.Select(record => (string)record["event_name"]!).SequenceEqual(mode == "physical" ? ["first", "second"] : mode == "dual" ? ["first"] : Array.Empty<string>()), "file record selection");
        Require(console.Concat(file).All(record => (string?)record["status"] == "info"), "wire severity");
        if (mode == "physical") { Require(report["leaseReleased"]!.GetValue<bool>(), "physical lease"); return; }
        JsonArray diagnostics = report["diagnostics"]!.AsArray();
        Require(diagnostics.All(output => Count(output!["pending"]) == 0), "eventual pending");
        if (mode == "loss") { Require(Count(diagnostics[0]!["invalid"]) == 2 && Count(diagnostics[0]!["filtered"]) == 2, "later global facts"); }
        if (mode == "later-failure") { Require(Count(diagnostics[0]!["failed"]) == 1, "later failure"); }
    }
}
