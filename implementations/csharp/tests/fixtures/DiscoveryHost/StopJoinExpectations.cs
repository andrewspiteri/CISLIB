using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopJoinExpectations
{
    internal static readonly string[] Modes = ["empty", "dispose-first", "stop-first", "pre-cancelled", "cancel-later", "cancelled-joins", "deadline-joins", "after-completion-cancel"];
    private static void Require(bool condition, string reason) { if (!condition) { throw new InvalidOperationException(reason); } }
    private static long Number(JsonNode? value) => LifecycleReportChecks.Number(value);
    internal static void Validate(string mode, JsonObject a, int? processId = null)
    {
        LifecycleReportChecks.Identity(mode, a, processId);
        bool empty = mode == "empty", deadline = mode == "deadline-joins"; Require(Modes.Contains(mode, StringComparer.Ordinal), "stop-join mode");
        Require(a["mode"]!.GetValue<string>() == mode && a["runtime"]!.GetValue<string>() == ".NET 10.0.12", "stop-join mode/runtime");
        if (mode == "pre-cancelled")
        {
            JsonObject started = a["preCancelledStart"]!.AsObject();
            Require(started["deadlineCreated"] is JsonValue deadlineValue && deadlineValue.TryGetValue<int>(out _), "pre-admission deadline type");
            Require(started["admissionClosed"]!.GetValue<bool>() && started["shutdownStarted"]!.GetValue<bool>() && started["state"]!.GetValue<string>() == "Stopping" && started["writeOutcome"]!.GetValue<string>() == "Stopped" && started["deadlineCreated"]!.GetValue<int>() == 1, "pre-admission start");
        }
        else { Require(a["preCancelledStart"] is null, "stop-join absent pre-start"); }
        Require(a["result"]!["completion"]!.GetValue<string>() == (deadline ? "TimedOut" : "Completed"), "stop-join completion");
        Require(a["result"]!["succeeded"]!.GetValue<bool>() == !deadline && !a["result"]!["loss"]!.GetValue<bool>(), "stop-join summary");
        JsonArray outputs = a["result"]!["outputs"]!.AsArray(); Require(outputs.Count == (empty ? 0 : 1), "stop-join outputs");
        if (!empty)
        {
            Require(Number(outputs[0]!["boundary"]) == 1 && Number(outputs[0]!["written"]) == (deadline ? 0 : 1), "stop-join target/written");
            Require(Number(outputs[0]!["pending"]) == (deadline ? 1 : 0), "stop-join pending");
            Require(outputs[0]!["flush"]!.GetValue<string>() == (deadline ? "Pending" : "Succeeded"), "stop-join flush");
        }
        Require(a["sameTask"]!.GetValue<bool>() && a["sameResult"]!.GetValue<bool>() && a["immutable"]!.GetValue<bool>() && a["tokenIdentity"]!.GetValue<bool>(), "stop-join shared identity");
        Require(a["disposeJoiners"]!.GetValue<int>() == 64 && a["cancelledWaiters"]!.GetValue<int>() == (mode is "cancelled-joins" or "deadline-joins" ? 64 : mode is "pre-cancelled" or "cancel-later" or "after-completion-cancel" or "empty" ? 1 : 0), "stop-join caller joins");
        Require(!a["outputCancelledBeforeDeadline"]!.GetValue<bool>() && a["outputCancelledAtReturn"]!.GetValue<bool>() == deadline, "stop-join output cancellation");
        Require(a["pendingAtReturn"]!.GetValue<bool>() == deadline && a["finalState"]!.GetValue<string>() == "Stopped", "stop-join completion state");
        Require(a["stream"]!["disposed"]!.GetValue<int>() == 0, "stop-join borrowed stream");
        JsonObject result = a["result"]!.AsObject(); LifecycleReportChecks.Result(result, globalCounts: false);
        LifecycleReportChecks.Names(outputs, empty ? [] : ["console"]);
        Require(outputs.All(output => Number(output!["failed"]) == 0 && Number(output["abandoned"]) == 0), "stop-join dispositions");
        if (mode == "pre-cancelled") { Require(a["preCancelledStart"]!.AsObject().Count == 5, "stop-join pre-start shape"); }
        JsonNode stream = a["stream"]!;
        Require((stream["writes"]!.GetValue<int>(), stream["written"]!.GetValue<int>(), stream["flushes"]!.GetValue<int>(), stream["flushed"]!.GetValue<int>())
            == (empty ? (0, 0, 0, 0) : deadline ? (1, 1, 0, 0) : (1, 1, 1, 1)), "stop-join writer calls");
        JsonObject[] records = LifecycleReportChecks.Records(stream["bytes"], a["pid"]!.GetValue<int>());
        Require(records.Select(record => (string)record["event_name"]!).SequenceEqual(empty ? Array.Empty<string>() : ["one"]) && records.All(record => (string?)record["status"] == "info"), "stop-join record selection");
        JsonArray live = a["live"]!.AsArray(); LifecycleReportChecks.Names(live, empty ? [] : ["console"]);
        foreach (JsonNode? output in live)
        {
            Require(Number(output!["accepted"]) == 1 && Number(output["written"]) == 1 && new[] { "failed", "abandoned", "pending" }.All(key => Number(output[key]) == 0) && output["fault"] is null, "stop-join live delivery");
        }
        JsonNode timers = a["timers"]!;
        Require((timers["created"]!.GetValue<int>(), timers["disposed"]!.GetValue<int>(), timers["active"]!.GetValue<int>(), timers["periodicFires"]!.GetValue<int>(), timers["deadlineFires"]!.GetValue<int>())
            == (empty ? 0 : 1, empty ? 0 : 1, 0, 0, deadline ? 1 : 0), "stop-join timer ownership");
    }
}
