using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopJoinExpectations
{
    internal static readonly string[] Modes = ["empty", "dispose-first", "stop-first", "pre-cancelled", "cancel-later", "cancelled-joins", "deadline-joins", "after-completion-cancel"];
    private static void Require(bool condition) { if (!condition) { throw new InvalidOperationException("Stop join expectation failed."); } }
    private static long Number(JsonNode? value) => LifecycleReportChecks.Number(value);
    internal static void Validate(string mode, JsonObject a, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, a, processId);
        bool empty = mode == "empty", deadline = mode == "deadline-joins"; Require(Modes.Contains(mode, StringComparer.Ordinal));
        Require(a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        if (mode == "pre-cancelled")
        {
            JsonObject started = a["preCancelledStart"]!.AsObject();
            Require(started["admissionClosed"]!.GetValue<bool>() && started["shutdownStarted"]!.GetValue<bool>() && started["state"]!.GetValue<string>() == "Stopping" && started["writeOutcome"]!.GetValue<string>() == "Stopped" && started["deadlineCreated"]!.GetValue<int>() == 1);
        }
        else { Require(a["preCancelledStart"] is null); }
        Require(a["result"]!["completion"]!.GetValue<string>() == (deadline ? "TimedOut" : "Completed"));
        Require(a["result"]!["succeeded"]!.GetValue<bool>() == !deadline && !a["result"]!["loss"]!.GetValue<bool>());
        JsonArray outputs = a["result"]!["outputs"]!.AsArray(); Require(outputs.Count == (empty ? 0 : 1));
        if (!empty)
        {
            Require(Number(outputs[0]!["boundary"]) == 1 && Number(outputs[0]!["written"]) == (deadline ? 0 : 1));
            Require(Number(outputs[0]!["pending"]) == (deadline ? 1 : 0));
            Require(outputs[0]!["flush"]!.GetValue<string>() == (deadline ? "Pending" : "Succeeded"));
        }
        Require(a["sameTask"]!.GetValue<bool>() && a["sameResult"]!.GetValue<bool>() && a["immutable"]!.GetValue<bool>() && a["tokenIdentity"]!.GetValue<bool>());
        Require(a["disposeJoiners"]!.GetValue<int>() == 64 && a["cancelledWaiters"]!.GetValue<int>() == (mode is "cancelled-joins" or "deadline-joins" ? 64 : mode is "pre-cancelled" or "cancel-later" or "after-completion-cancel" or "empty" ? 1 : 0));
        Require(!a["outputCancelledBeforeDeadline"]!.GetValue<bool>() && a["outputCancelledAtReturn"]!.GetValue<bool>() == deadline);
        Require(a["pendingAtReturn"]!.GetValue<bool>() == deadline && a["finalState"]!.GetValue<string>() == "Stopped");
        Require(a["stream"]!["disposed"]!.GetValue<int>() == 0);
        JsonObject result = a["result"]!.AsObject(); LifecycleReportChecks.Result(result, globalCounts: false);
        LifecycleReportChecks.Names(outputs, empty ? [] : ["console"]);
        Require(outputs.All(output => Number(output!["failed"]) == 0 && Number(output["abandoned"]) == 0));
        if (mode == "pre-cancelled") { Require(a["preCancelledStart"]!.AsObject().Count == 5); }
        JsonNode stream = a["stream"]!;
        Require((stream["writes"]!.GetValue<int>(), stream["written"]!.GetValue<int>(), stream["flushes"]!.GetValue<int>(), stream["flushed"]!.GetValue<int>())
            == (empty ? (0, 0, 0, 0) : deadline ? (1, 1, 0, 0) : (1, 1, 1, 1)));
        JsonObject[] records = LifecycleReportChecks.Records(stream["bytes"], a["pid"]!.GetValue<int>());
        Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(empty ? Array.Empty<string>() : ["one"]) && records.All(record => (string?)record["status"] == "info"));
        JsonArray live = a["live"]!.AsArray(); LifecycleReportChecks.Names(live, empty ? [] : ["console"]);
        foreach (JsonNode? output in live)
        {
            Require(Number(output!["accepted"]) == 1 && Number(output["written"]) == 1 && new[] { "failed", "abandoned", "pending" }.All(key => Number(output[key]) == 0) && output["fault"] is null);
        }
        JsonNode timers = a["timers"]!;
        Require((timers["created"]!.GetValue<int>(), timers["disposed"]!.GetValue<int>(), timers["active"]!.GetValue<int>(), timers["periodicFires"]!.GetValue<int>(), timers["deadlineFires"]!.GetValue<int>())
            == (empty ? 0 : 1, empty ? 0 : 1, 0, 0, deadline ? 1 : 0));
    }
}
