using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushReaderMigrationTests
{
#if !CIS_MUTATION_TESTS
    private static readonly ConcurrentDictionary<(string Command, string Mode), Lazy<Task<string>>> ReaderPositives = new();
#endif
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Controls =>
    [
        new object[] { "flush-boundary", "both", "serialization", "snapshot serialization" },
        new object[] { "flush-boundary", "both", "global", "initial global facts" },
        new object[] { "flush-boundary", "both", "boundary", "initial output facts" },
        new object[] { "flush-boundary", "both", "rejection", "initial output facts" },
        new object[] { "flush-boundary", "both", "order", "fixed target ordering" },
        new object[] { "flush-boundary", "saturated", "controls", "bounded controls" },
        new object[] { "flush-boundary", "saturated", "coalescing", "shared target registration" },
        new object[] { "flush-boundary", "both", "terminal", "terminal output facts" },
        new object[] { "flush-boundary", "both", "flush-result", "real writer flush" },
        new object[] { "flush-boundary", "closed", "wire", "complete record boundary" },
        new object[] { "flush-boundary", "empty", "child-pid", "actual child PID/runtime" },
        new object[] { "flush-boundary", "closed", "cleanup", "released coordination" },
        new object[] { "flush-coordination", "write-cancel", "retained", "detached coordination" },
        new object[] { "flush-coordination", "write-cancel", "controls", "bounded target controls" },
        new object[] { "flush-coordination", "write-cancel", "storage", "bounded continuation storage" },
        new object[] { "flush-coordination", "write-cancel", "iterations", "full caller count" },
        new object[] { "flush-coordination", "write-cancel", "shared", "shared controls" },
        new object[] { "flush-coordination", "write-deadline", "deadline", "actual deadline callback" },
        new object[] { "flush-coordination", "write-cancel", "cancelled", "caller cancellation" },
        new object[] { "flush-coordination", "write-cancel", "initial", "original caller rejection facts" },
        new object[] { "flush-coordination", "write-cancel", "order", "fixed target writer order" },
        new object[] { "flush-coordination", "cohort", "file-controls", "bounded file target controls" },
        new object[] { "flush-coordination", "dual", "transition", "live caller transition phases" },
        new object[] { "flush-coordination", "fault", "result", "actual writer status" },
        new object[] { "flush-coordination", "pre-cancel", "pre-cancel", "cancelled completed caller" },
        new object[] { "flush-coordination", "fault", "accounting", "terminal output facts" },
        new object[] { "flush-coordination", "pre-cancel", "wire", "complete record boundary" },
        new object[] { "flush-coordination", "pre-cancel", "child", "actual child PID" },
        new object[] { "flush-results", "write-timeout", "completion", "flush summary" },
        new object[] { "flush-results", "write-timeout", "pending", "covered dispositions" },
        new object[] { "flush-results", "flush-timeout", "flush-flag", "flush flag" },
        new object[] { "flush-results", "loss", "initial-global", "initial global facts" },
        new object[] { "flush-results", "loss", "initial-reject", "initial rejection facts" },
        new object[] { "flush-results", "loss", "success", "flush summary" },
        new object[] { "flush-results", "write-timeout", "immutable", "immutable observation" },
        new object[] { "flush-results", "physical", "UTC", "Lifecycle UTC label differs." },
        new object[] { "flush-results", "physical", "framing", "Lifecycle record framing differs." },
        new object[] { "flush-results", "clock", "timer", "timer disposal" },
        new object[] { "flush-results", "clock", "clock", "monotonic deadline" },
        new object[] { "flush-results", "pre-cancel", "cancel", "caller cancellation" },
        new object[] { "flush-results", "later-failure", "later-boundary", "covered dispositions" },
        new object[] { "flush-results", "exhaustion", "exhaustion", "exhaustion pending" },
        new object[] { "flush-results", "physical", "lease", "physical lease" },
        new object[] { "flush-results", "empty", "pid", "Actual child PID differs." },
        new object[] { "flush-results", "long-timeout", "long-fire", "long deadline timer" },
        new object[] { "flush-results", "long-timeout", "long-due", "long deadline timer" },
        new object[] { "flush-results", "write-timeout", "registered", "registered control" },
        new object[] { "flush-results", "cancel", "retained", "retained control" },
        new object[] { "flush-results", "loss", "later-facts", "later global facts" },
        new object[] { "flush-results", "write-timeout", "eventual", "eventual pending" },
        new object[] { "flush-results", "dual", "outputs", "Lifecycle output set/order differs." },
        new object[] { "flush-results", "cancel", "console-record", "console record selection" },
        new object[] { "flush-results", "physical", "file-record", "file record selection" },
        new object[] { "flush-results", "later-failure", "later-failure", "later failure" },
        new object[] { "flush-stress", "repeat-write", "completion", "flush summary" },
        new object[] { "flush-stress", "repeat-write", "pending", "covered dispositions" },
        new object[] { "flush-stress", "repeat-write", "controls", "bounded controls" },
        new object[] { "flush-stress", "repeat-write", "retained", "detached controls" },
        new object[] { "flush-stress", "repeat-write", "slots", "bounded backing slots" },
        new object[] { "flush-stress", "repeat-write", "owners", "collected caller owners" },
        new object[] { "flush-stress", "repeat-write", "timers", "timer ownership" },
        new object[] { "flush-stress", "repeat-write", "global", "initial global facts" },
        new object[] { "flush-stress", "repeat-progress", "progress", "progress storage" },
        new object[] { "flush-stress", "repeat-progress", "signal", "progress cleared" },
        new object[] { "flush-stress", "saturation", "boundary", "covered dispositions" },
        new object[] { "flush-stress", "saturation", "reject", "initial rejection facts" },
        new object[] { "flush-stress", "saturation", "file-control", "retained file control" },
        new object[] { "flush-stress", "saturation", "framing", "Lifecycle record framing differs." },
        new object[] { "flush-stress", "saturation", "order", "serialized order" },
        new object[] { "flush-stress", "cohort", "overlap", "writer serialization" },
        new object[] { "flush-stress", "cohort", "cohort-detach", "cohort detachment" },
        new object[] { "flush-stress", "cohort", "cohort-controls", "detached controls retained" },
        new object[] { "flush-stress", "cohort", "file-attached", "file caller phase" },
        new object[] { "flush-stress", "cohort", "file-detach", "file detachment" },
        new object[] { "flush-stress", "failed-flush", "loss", "flush summary" },
        new object[] { "flush-stress", "failed-flush", "later-abandon", "covered dispositions" },
        new object[] { "flush-stress", "failed-flush", "later-wire", "later wire mapping" },
        new object[] { "flush-stress", "failed-flush", "cancel-count", "caller cancellation" },
        new object[] { "flush-stress", "concurrent", "concurrent-target", "fixed concurrent boundary" },
        new object[] { "flush-stress", "concurrent", "ticket", "admission tickets" },
        new object[] { "flush-stress", "concurrent", "records", "actual record order" },
        new object[] { "flush-stress", "saturation", "UTC-due", "monotonic due" },
        new object[] { "flush-stress", "cohort", "file-budget", "monotonic due" },
        new object[] { "flush-stress", "deadline-race", "callback", "callback joins" },
        new object[] { "flush-stress", "deadline-race", "notification", "completion notification" },
        new object[] { "flush-stress", "deadline-race", "source", "callback source lifetime" },
        new object[] { "flush-stress", "deadline-race", "freeze", "covered dispositions" },
        new object[] { "flush-stress", "repeat-write", "pid", "Actual child PID differs." },
    ];

    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task OriginalFlushReaderCorruptionsAreRejectedAfterPositivePhysicalOutput(string command, string mode, string fault, string reason)
    {
        JsonObject original = await ReadPositive(command, mode);
        int pid = original["pid"]!.GetValue<int>();
        LifecycleProcessChecks.Validate(command, mode, original, pid);
        JsonObject a = original.DeepClone().AsObject();
        switch ((command, fault))
        {
            case ("flush-boundary", "serialization"): { a["after"]![0]!["invalid"] = "99"; break; }
            case ("flush-boundary", "global"): { foreach (string key in new[] { "captures", "after" }) { a[key]![0]!["invalid"] = "0"; } break; }
            case ("flush-boundary", "boundary"): { foreach (string key in new[] { "captures", "after" }) { a[key]![0]!["console"]!["boundary"] = "4"; } break; }
            case ("flush-boundary", "rejection"): { foreach (string key in new[] { "captures", "after" }) { a[key]![0]!["file"]!["filtered"] = "2"; } break; }
            case ("flush-boundary", "order"): { a["writers"]!["console"]!["order"]![3] = "flush.4"; break; }
            case ("flush-boundary", "controls"): { a["heldControls"]!["console"] = 2; break; }
            case ("flush-boundary", "coalescing"): { a["coalesced"] = false; break; }
            case ("flush-boundary", "terminal"): { a["final"]!["outputs"]![0]!["pending"] = "1"; break; }
            case ("flush-boundary", "flush-result"): { a["flushResults"]![0]!["console"] = false; break; }
            case ("flush-boundary", "wire"): { JsonNode writer = a["writers"]!["console"]!; writer["bytes"] = Convert.ToBase64String(Convert.FromBase64String((string)writer["bytes"]!)[..^1]); break; }
            case ("flush-boundary", "child-pid"): { a["pid"] = pid + 1; break; }
            case ("flush-boundary", "cleanup"): { a["finalControls"]!["console"] = 1; break; }
            case ("flush-coordination", "retained"): { a["retainedAfterDetach"] = 1; break; }
            case ("flush-coordination", "controls"): { a["controlsPeak"] = 10000; break; }
            case ("flush-coordination", "storage"): { a["continuationSlotsAfterDetach"] = 10000; break; }
            case ("flush-coordination", "iterations"): { a["iterations"] = 9999; break; }
            case ("flush-coordination", "shared"): { a["shared"] = false; break; }
            case ("flush-coordination", "deadline"): { a["deadlineFires"] = 0; break; }
            case ("flush-coordination", "cancelled"): { a["cancelled"] = 9999; break; }
            case ("flush-coordination", "initial"): { a["snapshots"]!.AsArray()[^2]!["console"]!["queueFull"] = "4"; break; }
            case ("flush-coordination", "order"): { a["writers"]!["console"]!["order"]![1] = "flush.4"; break; }
            case ("flush-coordination", "file-controls"): { a["fileControlsPeak"] = 64; break; }
            case ("flush-coordination", "transition"): { a["phases"]![1]!["fileAttached"] = 0; break; }
            case ("flush-coordination", "result"): { a["succeeded"] = 1; break; }
            case ("flush-coordination", "pre-cancel"): { a["completedTokenCancelled"] = false; break; }
            case ("flush-coordination", "accounting"): { a["outputs"]![0]!["failed"] = "0"; break; }
            case ("flush-coordination", "wire"): { JsonNode writer = a["writers"]!["console"]!; writer["bytes"] = Convert.ToBase64String(Convert.FromBase64String((string)writer["bytes"]!)[..^1]); break; }
            case ("flush-coordination", "child"): { a["pid"] = pid + 1; break; }
            case ("flush-results", "completion"): { a["result"]!["completion"] = "Completed"; break; }
            case ("flush-results", "pending"): { a["result"]!["outputs"]![0]!["pending"] = "0"; a["result"]!["outputs"]![0]!["written"] = "1"; break; }
            case ("flush-results", "flush-flag"): { a["result"]!["outputs"]![0]!["flushSucceeded"] = true; break; }
            case ("flush-results", "initial-global"): { a["result"]!["invalid"] = "2"; break; }
            case ("flush-results", "initial-reject"): { a["result"]!["outputs"]![0]!["full"] = "3"; break; }
            case ("flush-results", "success"): { a["result"]!["succeeded"] = false; break; }
            case ("flush-results", "immutable"): { a["immutableAfterIo"] = false; break; }
            case ("flush-results", "UTC"): { a["result"]!["observed"] = "bad"; break; }
            case ("flush-results", "framing"): { a["fileBytes"] = Convert.ToBase64String(Convert.FromBase64String((string)a["fileBytes"]!)[..^1]); break; }
            case ("flush-results", "timer"): { a["timers"]!["disposed"] = 0; break; }
            case ("flush-results", "clock"): { a["survivedUtcCorrections"] = false; break; }
            case ("flush-results", "cancel"): { a["cancelled"] = false; break; }
            case ("flush-results", "later-boundary"): { a["result"]!["outputs"]![0]!["boundary"] = "2"; a["result"]!["outputs"]![0]!["failed"] = "1"; break; }
            case ("flush-results", "exhaustion"): { a["stillPendingAfterFault"] = false; break; }
            case ("flush-results", "lease"): { a["leaseReleased"] = false; break; }
            case ("flush-results", "pid"): { a["pid"] = pid + 1; break; }
            case ("flush-results", "long-fire"): { a["longTimerPhase"]!["fires"] = 0; break; }
            case ("flush-results", "long-due"): { a["longTimerPhase"]!["nextDueTicks"] = "9223372036854775807"; break; }
            case ("flush-results", "registered"): { a["heldControls"] = 0; break; }
            case ("flush-results", "retained"): { a["controlsAfterWait"] = 0; break; }
            case ("flush-results", "later-facts"): { a["diagnostics"]![0]!["invalid"] = "1"; break; }
            case ("flush-results", "eventual"): { a["diagnostics"]![0]!["pending"] = "1"; break; }
            case ("flush-results", "outputs"): { a["result"]!["outputs"] = new JsonArray(a["result"]!["outputs"]!.AsArray().Reverse().Select(value => value!.DeepClone()).ToArray()); break; }
            case ("flush-results", "console-record"): { a["bytes"] = ""; break; }
            case ("flush-results", "file-record"): { a["fileBytes"] = ""; break; }
            case ("flush-results", "later-failure"): { a["diagnostics"]![0]!["failed"] = "0"; break; }
            case ("flush-stress", "completion"): { a["repeated"]!["first"]!["completion"] = "Completed"; break; }
            case ("flush-stress", "pending"): { a["repeated"]!["first"]!["outputs"]![0]!["pending"] = "3"; break; }
            case ("flush-stress", "controls"): { a["afterDetach"]!["controls"] = 5; break; }
            case ("flush-stress", "retained"): { a["afterDetach"]!["attached"] = 1; break; }
            case ("flush-stress", "slots"): { a["repeated"]!["peakSlots"] = 100; break; }
            case ("flush-stress", "owners"): { a["collectedSamples"] = 4; break; }
            case ("flush-stress", "timers"): { a["timers"]!["active"] = 1; break; }
            case ("flush-stress", "global"): { a["repeated"]!["first"]!["invalid"] = "2"; break; }
            case ("flush-stress", "progress"): { a["progressSlots"] = 100; break; }
            case ("flush-stress", "signal"): { a["progressCleared"] = false; break; }
            case ("flush-stress", "boundary"): { a["early"]!["outputs"]![0]!["boundary"] = "4"; a["early"]!["outputs"]![0]!["pending"] = "4"; break; }
            case ("flush-stress", "reject"): { a["late"]!["outputs"]![0]!["full"] = "0"; break; }
            case ("flush-stress", "file-control"): { a["detachedFile"]!["controls"] = 0; break; }
            case ("flush-stress", "framing"): { a["console"]!["bytes"] = Convert.ToBase64String(Convert.FromBase64String((string)a["console"]!["bytes"]!)[..^1]); break; }
            case ("flush-stress", "order"): { a["console"]!["order"]![1] = "flush.4"; break; }
            case ("flush-stress", "overlap"): { a["console"]!["maximumIo"] = 2; break; }
            case ("flush-stress", "cohort-detach"): { a["phases"]![1]!["console"]!["attached"] = 64; break; }
            case ("flush-stress", "cohort-controls"): { a["phases"]![1]!["file"]!["controls"] = 0; break; }
            case ("flush-stress", "file-attached"): { a["phases"]![2]!["file"]!["attached"] = 0; break; }
            case ("flush-stress", "file-detach"): { a["phases"]![3]!["timers"] = 16; break; }
            case ("flush-stress", "loss"): { a["timeouts"]![0]!["loss"] = true; break; }
            case ("flush-stress", "later-abandon"): { a["results"]![0]!["outputs"]![0]!["boundary"] = "4"; a["results"]![0]!["outputs"]![0]!["abandoned"] = "3"; break; }
            case ("flush-stress", "later-wire"): { JsonArray accepted = a["laterAccepted"]!.AsArray(); accepted.RemoveAt(accepted.Count - 1); break; }
            case ("flush-stress", "cancel-count"): { a["cancelled"] = 31; break; }
            case ("flush-stress", "concurrent-target"): { JsonNode output = a["observations"]![0]!["final"]!["outputs"]![0]!; output["boundary"] = (long.Parse((string)output["boundary"]!, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); output["written"] = (long.Parse((string)output["written"]!, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); break; }
            case ("flush-stress", "ticket"): { a["writes"]!.AsArray().Single(row => (string?)row!["name"] == "first")!["outputs"]![0]!["ticket"] = "2"; break; }
            case ("flush-stress", "records"): { a["console"]!["bytes"] = ""; break; }
            case ("flush-stress", "UTC-due"): { a["dueBeforeTimeout"] = new JsonArray("100000000", "100000000"); break; }
            case ("flush-stress", "file-budget"): { a["phases"]![3]!["dueTicks"]![0] = "100000000"; break; }
            case ("flush-stress", "callback"): { a["observations"]![0]!["during"]!["callbacks"] = 0; break; }
            case ("flush-stress", "notification"): { a["observations"]![1]!["notificationHeldBeforeCompletion"] = false; break; }
            case ("flush-stress", "source"): { a["observations"]![0]!["sourceReadableDuringJoin"] = false; break; }
            case ("flush-stress", "freeze"): { JsonNode output = a["observations"]![1]!["result"]!["outputs"]![0]!; output["written"] = "1"; output["pending"] = "0"; output["flush"] = "Succeeded"; output["flushSucceeded"] = true; break; }
            case ("flush-stress", "pid"): { a["pid"] = pid + 1; break; }
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        Exception error = command is "flush-results" or "flush-stress"
            ? Assert.ThrowsExactly<InvalidOperationException>(() => LifecycleProcessChecks.Validate(command, mode, a, pid))
            : Assert.ThrowsExactly<InvalidDataException>(() => LifecycleProcessChecks.Validate(command, mode, a, pid));
        Assert.AreEqual(reason, error.Message, command + "/" + fault);
    }

    private async Task<JsonObject> ReadPositive(string command, string mode)
    {
#if CIS_MUTATION_TESTS
        return await LifecycleProcessChecks.Run(command, mode, TestContext);
#else
        // Use only a completed physical capture; never wait for a functional case.
        string text = await ReaderPositives.GetOrAdd((command, mode), key => new Lazy<Task<string>>(async () =>
        {
            if (LifecycleProcessChecks.TryGetCompletedFlushReaderInput(key.Command, key.Mode, out string? completed)
                && completed is not null)
            {
                return completed;
            }
            return (await LifecycleProcessChecks.Run(key.Command, key.Mode, TestContext)).ToJsonString();
        })).Value;
        return JsonNode.Parse(text)!.AsObject();
#endif
    }
}
