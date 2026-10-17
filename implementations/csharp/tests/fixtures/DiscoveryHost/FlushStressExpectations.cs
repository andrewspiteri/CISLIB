using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushStressExpectations
{
    private static void Require(bool value, string reason) { if (!value) { throw new InvalidOperationException(reason); } }
    private static long Number(JsonNode? value) => LifecycleReportChecks.Number(value);
    internal static void Validate(string mode, JsonObject report, int? processId = null)
    {
        FlushStressReaderChecks.Validate(mode, report, processId);
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12", "mode/runtime");
        if (mode == "deadline-race")
        {
            JsonArray observations = report["observations"]!.AsArray(); Require(observations.Count == 32, "deadline observation count");
            for (int i = 0; i < observations.Count; i++)
            {
                JsonNode row = observations[i]!; Require(row["iteration"]!.GetValue<int>() == i && row["cancelled"]!.GetValue<bool>() == (i % 2 == 0), "deadline observation identity");
                Require(row["during"]!["callbacks"]!.GetValue<int>() == 1 && row["during"]!["pendingJoins"]!.GetValue<int>() == 1, "callback joins");
                Require(row["notificationHeldBeforeCompletion"]!.GetValue<bool>(), "completion notification");
                Require(row["sourceReadableDuringJoin"]!.GetValue<bool>() && row["sourceDisposedAfterJoin"]!.GetValue<bool>(), "callback source lifetime");
                Timer(row["timers"]!);
                if (i % 2 == 0) { Require(row["result"] is null, "cancelled result absence"); } else { Result(row["result"]!); }
                Require(Number(row["diagnostics"]![0]!["written"]) == 1 && Number(row["diagnostics"]![0]!["pending"]) == 0, "eventual diagnostics");
            }
            return;
        }
        Timer(report["timers"]!);
        if (mode.StartsWith("repeat-", StringComparison.Ordinal))
        {
            JsonNode repeated = report["repeated"]!;
            Require(repeated["iterations"]!.GetValue<int>() == 5000 && repeated["cancelled"]!.GetValue<int>() == 2500 && repeated["timedOut"]!.GetValue<int>() == 2500, "full caller count");
            Require(repeated["peakAttached"]!.GetValue<int>() == 1 && repeated["peakSlots"]!.GetValue<int>() <= 4, "bounded caller storage");
            Require(report["weakSamples"]!.GetValue<int>() == 10 && report["collectedSamples"]!.GetValue<int>() == 10, "collected caller owners");
            Require(report["afterDetach"]!["controls"]!.GetValue<int>() == (mode == "repeat-progress" ? 1 : 4), "bounded controls");
            Require(report["afterDetach"]!["attached"]!.GetValue<int>() == 0 && report["finalControls"]!.GetValue<int>() == 0, "detached controls");
            Result(repeated["first"]!); Result(repeated["last"]!); Result(report["final"]!);
        }
        else if (mode == "saturation")
        { Result(report["early"]!); Result(report["late"]!); Result(report["final"]!); Require(report["immutableAfterIo"]!.GetValue<bool>() && report["utcCorrectionsPreservedDue"]!.GetValue<bool>(), "frozen monotonic observation"); }
        else if (mode == "concurrent")
        {
            Require(report["writes"]!.AsArray().Count == 257 && report["observations"]!.AsArray().Count == 64, "concurrent cohort counts");
            Require(report["heldConsole"]!["attached"]!.GetValue<int>() == 64 && report["heldFile"]!["attached"]!.GetValue<int>() == 0, "concurrent attachments");
            foreach (JsonNode? row in report["observations"]!.AsArray()) { Result(row!["initial"]!); Result(row["final"]!); }
        }
        else
        {
            Require(report["timeouts"]!.AsArray().Count == 16 && report["results"]!.AsArray().Count == (mode == "cohort" ? 8 : 16), "result cohort counts");
            Require(report["cancelled"]!.GetValue<int>() == (mode == "cohort" ? 40 : 32) && report["immutableAfterIo"]!.GetValue<bool>(), "caller cancellation");
            foreach (JsonNode? result in report["timeouts"]!.AsArray().Concat(report["results"]!.AsArray())) { Result(result!); }
        }
    }
    private static void Timer(JsonNode timer) => Require(timer["created"]!.GetValue<int>() == timer["disposed"]!.GetValue<int>() &&
        timer["active"]!.GetValue<int>() == 0 && timer["callbacks"]!.GetValue<int>() == 0 && timer["pendingJoins"]!.GetValue<int>() == 0, "timer ownership");
    private static void Result(JsonNode result)
    {
        JsonArray outputs = result["outputs"]!.AsArray();
        foreach (JsonNode? output in outputs)
        {
            Require(Number(output!["boundary"]) == Number(output["written"]) + Number(output["failed"]) + Number(output["abandoned"]) + Number(output["pending"]), "boundary partition");
            Require(output["flushSucceeded"]!.GetValue<bool>() == (output["flush"]!.GetValue<string>() == "Succeeded"), "flush flag");
        }
        bool completed = outputs.All(o => Number(o!["pending"]) == 0 && o["flush"]!.GetValue<string>() is "Succeeded" or "Failed");
        bool succeeded = outputs.All(o => Number(o!["pending"]) == 0 && Number(o["failed"]) == 0 && Number(o["abandoned"]) == 0 && o["flushSucceeded"]!.GetValue<bool>());
        bool loss = Number(result["invalid"]) > 0 || Number(result["truncated"]) > 0 || outputs.Any(o => Number(o!["failed"]) > 0 || Number(o["abandoned"]) > 0 ||
            Number(o["full"]) > 0 || Number(o["unavailable"]) > 0 || o["flush"]!.GetValue<string>() == "Failed");
        Require(result["completion"]!.GetValue<string>() == (completed ? "Completed" : "TimedOut") && result["succeeded"]!.GetValue<bool>() == succeeded && result["loss"]!.GetValue<bool>() == loss, "flush summary");
    }
}
