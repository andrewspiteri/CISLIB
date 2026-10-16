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
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        Assert.ThrowsExactly<InvalidDataException>(() => LifecycleProcessChecks.Validate(command, mode, a, pid));
    }
}
