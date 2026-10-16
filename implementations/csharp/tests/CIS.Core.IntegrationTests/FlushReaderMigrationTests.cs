using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushReaderMigrationTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Controls =>
    [
        new object[] { "flush-boundary", "both", "serialization" },
        new object[] { "flush-boundary", "both", "global" },
        new object[] { "flush-boundary", "both", "boundary" },
        new object[] { "flush-boundary", "both", "rejection" },
        new object[] { "flush-boundary", "both", "order" },
        new object[] { "flush-boundary", "saturated", "controls" },
        new object[] { "flush-boundary", "saturated", "coalescing" },
        new object[] { "flush-boundary", "both", "terminal" },
        new object[] { "flush-boundary", "both", "flush-result" },
        new object[] { "flush-boundary", "closed", "wire" },
        new object[] { "flush-boundary", "empty", "child-pid" },
        new object[] { "flush-boundary", "closed", "cleanup" },
        new object[] { "flush-coordination", "write-cancel", "retained" },
        new object[] { "flush-coordination", "write-cancel", "controls" },
        new object[] { "flush-coordination", "write-cancel", "storage" },
        new object[] { "flush-coordination", "write-cancel", "iterations" },
        new object[] { "flush-coordination", "write-cancel", "shared" },
        new object[] { "flush-coordination", "write-deadline", "deadline" },
        new object[] { "flush-coordination", "write-cancel", "cancelled" },
        new object[] { "flush-coordination", "write-cancel", "initial" },
        new object[] { "flush-coordination", "write-cancel", "order" },
        new object[] { "flush-coordination", "cohort", "file-controls" },
        new object[] { "flush-coordination", "dual", "transition" },
        new object[] { "flush-coordination", "fault", "result" },
        new object[] { "flush-coordination", "pre-cancel", "pre-cancel" },
        new object[] { "flush-coordination", "fault", "accounting" },
        new object[] { "flush-coordination", "pre-cancel", "wire" },
        new object[] { "flush-coordination", "pre-cancel", "child" },
        new object[] { "flush-results", "write-timeout", "completion" },
        new object[] { "flush-results", "write-timeout", "pending" },
        new object[] { "flush-results", "flush-timeout", "flush-flag" },
        new object[] { "flush-results", "loss", "initial-global" },
        new object[] { "flush-results", "loss", "initial-reject" },
        new object[] { "flush-results", "loss", "success" },
        new object[] { "flush-results", "write-timeout", "immutable" },
        new object[] { "flush-results", "physical", "UTC" },
        new object[] { "flush-results", "physical", "framing" },
        new object[] { "flush-results", "clock", "timer" },
        new object[] { "flush-results", "clock", "clock" },
        new object[] { "flush-results", "pre-cancel", "cancel" },
        new object[] { "flush-results", "later-failure", "later-boundary" },
        new object[] { "flush-results", "exhaustion", "exhaustion" },
        new object[] { "flush-results", "physical", "lease" },
        new object[] { "flush-results", "empty", "pid" },
        new object[] { "flush-results", "long-timeout", "long-fire" },
        new object[] { "flush-results", "long-timeout", "long-due" },
        new object[] { "flush-results", "write-timeout", "registered" },
        new object[] { "flush-results", "cancel", "retained" },
        new object[] { "flush-results", "loss", "later-facts" },
        new object[] { "flush-results", "write-timeout", "eventual" },
        new object[] { "flush-results", "dual", "outputs" },
        new object[] { "flush-results", "cancel", "console-record" },
        new object[] { "flush-results", "physical", "file-record" },
        new object[] { "flush-results", "later-failure", "later-failure" },
    ];

    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task OriginalFlushReaderCorruptionsAreRejectedAfterPositivePhysicalOutput(string command, string mode, string fault)
    {
        JsonObject original = await LifecycleProcessChecks.Run(command, mode, TestContext);
        int pid = original["pid"]!.GetValue<int>();
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
            case ("flush-results", "pending"): { a["result"]!["outputs"]![0]!["pending"] = "0"; break; }
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
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        if (command == "flush-results") { Assert.ThrowsExactly<InvalidOperationException>(() => LifecycleProcessChecks.Validate(command, mode, a, pid)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => LifecycleProcessChecks.Validate(command, mode, a, pid)); }
    }
}
