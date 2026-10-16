using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopCleanupExpectations
{
    internal static readonly string[] Modes = ["normal", "write-resource-failure", "flush-resource-failure", "write-timer-resource-failure", "resource-failure", "timer-failure", "callback-write-failure", "callback-held", "ownership-held", "deadline-cleanup-failure", "deadline-cleanup-held", "post-stop-flush", "primitive-close"];
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("Cleanup expectation failed."); } }
    private static long Number(JsonNode? value) => LifecycleReportChecks.Number(value);
    internal static void Validate(string mode, JsonObject a, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, a, processId);
        bool write = mode is "write-resource-failure" or "write-timer-resource-failure";
        bool flush = mode == "flush-resource-failure", early = write || flush;
        bool deadline = mode is "callback-write-failure" or "callback-held" or "ownership-held" or "deadline-cleanup-failure" or "deadline-cleanup-held";
        Require(Modes.Contains(mode, StringComparer.Ordinal) && a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        JsonObject result = a["result"]!.AsObject();
        Require(result["completion"]!.GetValue<string>() == (deadline ? "TimedOut" : "Completed"));
        Require(result["succeeded"]!.GetValue<bool>() == (!deadline && !early && mode != "primitive-close"));
        Require(result["loss"]!.GetValue<bool>() == (early || mode == "primitive-close"));
        JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == 2);
        Require(outputs[0]!["name"]!.GetValue<string>() == "console" && outputs[1]!["name"]!.GetValue<string>() == "file");
        Require(Number(outputs[0]!["boundary"]) == (early ? 2 : 1) && Number(outputs[0]!["written"]) == (early ? 2 : 1));
        Require(Number(outputs[1]!["boundary"]) == 1 && Number(outputs[1]!["written"]) == (deadline || write ? 0 : 1));
        Require(Number(outputs[1]!["failed"]) == (write ? 1 : 0) && Number(outputs[1]!["pending"]) == (deadline ? 1 : 0));
        Require(outputs[1]!["flush"]!.GetValue<string>() == (deadline ? "Pending" : early || mode == "primitive-close" ? "Failed" : "Succeeded"));
        string? fault = mode switch
        {
            "write-resource-failure" or "flush-resource-failure" or "write-timer-resource-failure" or "resource-failure" => "OutputCleanupFailed",
            "timer-failure" => "FileFlushTimerCleanupFailed",
            "callback-write-failure" => "CancellationCallbacksFailed",
            "deadline-cleanup-failure" or "deadline-cleanup-held" => "ShutdownTimerCleanupFailed",
            _ => null
        };
        JsonArray live = a["live"]!.AsArray(); Require(live.Count == 2);
        Require(live[0]!["fault"]?.GetValue<string>() == (mode is "deadline-cleanup-failure" or "deadline-cleanup-held" ? fault : null) && live[1]!["fault"]?.GetValue<string>() == fault);
        Require(Number(live[1]!["failed"]) == (write || mode == "callback-write-failure" ? 1 : 0) && Number(live[1]!["written"]) == (write || mode == "callback-write-failure" ? 0 : 1));
        Require(a["owner"]!["calls"]!.GetValue<int>() == 1 && a["owner"]!["gateFree"]!.GetValue<bool>() && !a["owner"]!["activeIo"]!.GetValue<bool>() && !a["owner"]!["activeCallback"]!.GetValue<bool>());
        Require(a["sameTask"]!.GetValue<bool>() && a["immutable"]!.GetValue<bool>() && a["disposeJoiners"]!.GetValue<int>() == 64 && a["cleanupPreserved"]!.GetValue<bool>());
        Require(a["heldOwned"]!.GetValue<bool>() == deadline && a["heldCallback"]!.GetValue<bool>() == (mode == "callback-held"));
        Require(a["primaryPreserved"]?.GetValue<bool>() == (early || mode == "callback-write-failure" ? true : null));
        Require(a["inactiveFlushResolved"]!.GetValue<bool>() == (mode == "deadline-cleanup-held") && a["lateFlushCompleted"]!.GetValue<bool>() == (mode == "deadline-cleanup-held"));
        Require(a["primaryFirst"]?.GetValue<bool>() == (early || mode == "callback-write-failure" ? true : null) && a["borrowedConsoleOpen"]!.GetValue<bool>());
        Require(a["workerErrors"]!.GetValue<int>() == (mode == "write-timer-resource-failure" ? 3 : early || mode == "callback-write-failure" ? 2 : mode is "resource-failure" or "timer-failure" ? 1 : 0));
        Require(a["file"]!["closed"]!.GetValue<int>() == 1 && a["file"]!["writes"]!.GetValue<int>() == 1 && a["file"]!["written"]!.GetValue<int>() == (write || mode == "callback-write-failure" ? 0 : 1));
        Require(a["file"]!["flushes"]!.GetValue<int>() == (deadline || write || mode == "primitive-close" ? 0 : 1) && a["file"]!["flushed"]!.GetValue<int>() == (deadline || early || mode == "primitive-close" ? 0 : 1));
        Require(a["timerDisposeCalls"]!.GetValue<int>() == 1 && a["deadlineDisposeCalls"]!.GetValue<int>() == (mode == "post-stop-flush" ? 5 : mode == "deadline-cleanup-held" ? 2 : 1) && a["timers"]!["active"]!.GetValue<int>() == 0);
        Require(a["postFlushes"]!.GetValue<int>() == (mode == "post-stop-flush" ? 4 : mode == "deadline-cleanup-held" ? 1 : 0) && a["postFlushSucceeded"]!.GetValue<bool>() == (mode is not ("post-stop-flush" or "deadline-cleanup-held")));
        LifecycleReportChecks.Result(result, globalCounts: false);
        Require(new[] { "failed", "abandoned", "pending" }.All(key => Number(outputs[0]![key]) == 0) && Number(outputs[1]!["abandoned"]) == 0);
        Require((string?)outputs[0]!["flush"] == (mode == "primitive-close" ? "Failed" : "Succeeded"));
        LifecycleReportChecks.Names(live, ["console", "file"]);
        Require(Number(live[1]!["accepted"]) == 1 && Number(live[1]!["abandoned"]) == 0 && Number(live[1]!["pending"]) == 0);
        bool failed = write || mode == "callback-write-failure";
        JsonObject[] fileRecords = LifecycleReportChecks.Records(a["file"]!["bytes"], a["pid"]!.GetValue<int>());
        JsonObject[] consoleRecords = LifecycleReportChecks.Records(a["consoleBytes"], a["pid"]!.GetValue<int>());
        Require(fileRecords.Select(record => (string)record["event_name"]!).SequenceEqual(failed ? Array.Empty<string>() : ["one"]));
        Require(consoleRecords.Select(record => (string)record["event_name"]!).SequenceEqual(early ? ["one", "healthy"] : ["one"]));
        Require(fileRecords.Concat(consoleRecords).All(record => (string?)record["status"] == "info"));
        JsonNode timers = a["timers"]!; int count = mode == "post-stop-flush" ? 6 : mode == "deadline-cleanup-held" ? 3 : 2;
        Require((timers["created"]!.GetValue<int>(), timers["disposed"]!.GetValue<int>(), timers["active"]!.GetValue<int>(), timers["periodicFires"]!.GetValue<int>(), timers["deadlineFires"]!.GetValue<int>())
            == (count, count, 0, 0, deadline ? 1 : 0));
    }
}
