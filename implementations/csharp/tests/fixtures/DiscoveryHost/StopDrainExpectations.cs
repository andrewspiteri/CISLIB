using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopDrainExpectations
{
    internal static readonly string[] Modes = ["normal-dual", "queued-only", "blocked-write", "single-write", "cancel-write", "blocked-flush", "cancel-flush", "blocked-callback"];
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("Stop drain expectation failed."); } }
    private static long Number(JsonNode? value) => LifecycleReportChecks.Number(value);
    internal static void Validate(string mode, JsonObject report, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, report, processId);
        Require(Modes.Contains(mode, StringComparer.Ordinal) && report["mode"]!.GetValue<string>() == mode);
        bool normal = mode == "normal-dual", queued = mode == "queued-only", flush = mode is "blocked-flush" or "cancel-flush";
        bool single = mode == "single-write";
        JsonNode result = report["result"]!;
        Require(result["completion"]!.GetValue<string>() == (normal ? "Completed" : "TimedOut"));
        Require(result["succeeded"]!.GetValue<bool>() == normal && result["loss"]!.GetValue<bool>() == (!normal && !flush && !single));
        JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == (normal ? 2 : 1));
        foreach (JsonNode? o in outputs)
        {
            Require(Number(o!["boundary"]) == (flush || single ? 1 : 3));
            Require(Number(o["written"]) == (normal ? 3 : flush ? 1 : 0));
            Require(Number(o["failed"]) == 0 && Number(o["abandoned"]) == (queued ? 3 : normal || flush || single ? 0 : 2));
            Require(Number(o["pending"]) == (normal || flush || queued ? 0 : 1));
            Require(o["flush"]!.GetValue<string>() == (normal ? "Succeeded" : flush || single ? "Pending" : "Failed"));
        }
        Require(report["sameTask"]!.GetValue<bool>() && report["sameResult"]!.GetValue<bool>() && report["immutable"]!.GetValue<bool>() && report["workersSettled"]!.GetValue<bool>());
        Require(report["cancellationRequested"]!.GetValue<bool>() == (!normal && !queued));
        Require(report["stream"]!["disposed"]!.GetValue<int>() == 0);
        if (mode == "blocked-callback") { Require(report["stream"]!["callbackGateFree"]!.GetValue<bool>()); }
        LifecycleReportChecks.Result(result.AsObject(), globalCounts: true);
        Require(Number(result["invalid"]) == 0 && Number(result["truncated"]) == 0);
        LifecycleReportChecks.Names(outputs, normal ? ["console", "file"] : ["console"]);
        Require(outputs.All(output => new[] { "filtered", "full", "unavailable" }.All(key => Number(output![key]) == 0)));
        if (!normal && !flush)
        {
            int controls = report["registeredAfterDeadline"]!.GetValue<int>();
            Require(mode == "cancel-write" ? controls is 0 or 1 : controls == (queued ? 0 : 1));
        }
        Require(report["registeredAfterIo"]!.GetValue<int>() == 0);
        if (mode is "queued-only" or "blocked-write" or "single-write" or "blocked-flush" or "blocked-callback") { Require(report["pendingBeforeRelease"]!.GetValue<bool>()); }
        JsonNode stream = report["stream"]!;
        (int writes, int written, int flushes, int flushed) expected = normal ? (3, 3, 1, 1) : queued ? (0, 0, 0, 0) : mode == "cancel-write" ? (1, 0, 0, 0)
            : flush ? (1, 1, 1, mode == "cancel-flush" ? 0 : 1) : (1, 1, 0, 0);
        Require((stream["writes"]!.GetValue<int>(), stream["written"]!.GetValue<int>(), stream["flushes"]!.GetValue<int>(), stream["flushed"]!.GetValue<int>()) == expected);
        if (mode == "blocked-callback") { Require(stream["callbacks"]!.GetValue<int>() == 1); }
        JsonObject[] records = LifecycleReportChecks.Records(stream["bytes"], report["pid"]!.GetValue<int>());
        Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(normal ? ["one", "two", "three"] : queued || mode == "cancel-write" ? Array.Empty<string>() : ["one"]));
        Require(records.All(record => (string?)record["status"] == "info"));
        if (normal) { Require(JsonNode.DeepEquals(report["fileBytes"], stream["bytes"])); } else { Require(report["fileBytes"] is null); }
        JsonArray live = report["live"]!.AsArray(); LifecycleReportChecks.Names(live, normal ? ["console", "file"] : ["console"]);
        foreach (JsonNode? output in live)
        {
            (long accepted, long written, long failed, long abandoned, long pending) values = normal ? (3, 3, 0, 0, 0) : queued ? (3, 0, 0, 3, 0)
                : mode == "cancel-write" ? (3, 0, 1, 2, 0) : flush || single ? (1, 1, 0, 0, 0) : (3, 1, 0, 2, 0);
            Require((Number(output!["accepted"]), Number(output["written"]), Number(output["failed"]), Number(output["abandoned"]), Number(output["pending"])) == values);
            Require((string?)output["fault"] == (mode == "cancel-write" ? "WriteFailed" : mode == "cancel-flush" ? "FlushFailed" : null));
        }
        JsonNode timers = report["timers"]!;
        Require((timers["created"]!.GetValue<int>(), timers["disposed"]!.GetValue<int>(), timers["active"]!.GetValue<int>(), timers["periodicFires"]!.GetValue<int>(), timers["deadlineFires"]!.GetValue<int>())
            == (normal ? 2 : 1, normal ? 2 : 1, 0, 0, normal ? 0 : 1));
    }
}
