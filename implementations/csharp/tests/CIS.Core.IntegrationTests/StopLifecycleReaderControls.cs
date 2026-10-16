using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal static class StopLifecycleReaderControls
{
    private static readonly (string Id, string Mode, Action<JsonObject> Damage)[] Controls =
    [
        ("pid", "normal", a => { a["pid"] = a["pid"]!.GetValue<int>() + 1; }),
        ("runtime", "normal", a => { a["runtime"] = ".NET 10.0.13"; }),
        ("shape", "normal", a => { a["result"]!["extra"] = true; }),
        ("summary", "normal", a => { a["result"]!["succeeded"] = false; }),
        ("boundary", "normal", a => { a["result"]!["outputs"]![0]!["boundary"] = "119"; }),
        ("ticket", "normal", a => { a["receipts"]![0]!["ticket"] = "2"; }),
        ("receipt", "normal", a => { a["receipts"]![0]!["event"] = "other"; }),
        ("race", "producer-stop-race", a => { a["capturingStopped"] = false; }),
        ("producer", "producer-stop-race", a => { a["attempts"] = 513; }),
        ("stoppers", "producer-stop-race", a => { a["racingStoppers"] = 15; }),
        ("pre-cancelled", "pre-cancelled", a => { a["cancelled"] = 0; }),
        ("pending", "held-write", a => { a["result"]!["outputs"]![0]!["pending"] = "0"; }),
        ("abandoned", "held-write", a => { a["result"]!["outputs"]![0]!["abandoned"] = "4"; }),
        ("flush", "held-write", a => { a["result"]!["outputs"]![0]!["flush"] = "Succeeded"; }),
        ("held", "held-write", a => { a["heldLease"] = false; }),
        ("immutable", "held-write", a => { a["immutable"] = false; }),
        ("joiners", "held-write", a => { a["joiners"] = 63; }),
        ("timer", "held-write", a => { a["timers"]!["created"] = 2; }),
        ("io", "held-write", a => { a["writer"]!["writes"] = 2; }),
        ("dispose", "held-write", a => { a["writer"]!["unsafeDispose"] = true; }),
        ("output-token", "cancelled-joiners", a => { a["outputCancellationRequestedBeforeDeadline"]![0] = true; }),
        ("output-cancel", "cancelled-joiners", a => { a["outputCancellationsBeforeDeadline"]![0] = 1; }),
        ("cancel", "cancelled-joiners", a => { a["cancelled"] = 63; }),
        ("late", "late-failure", a => { a["live"]![0]!["failed"] = "0"; }),
        ("partial", "late-failure", a => { a["bytes"] = a["seed"]!.DeepClone(); }),
        ("flush-pending", "held-flush", a => { a["result"]!["outputs"]![0]!["pending"] = "1"; }),
        ("crash-owner", "kill-queued", a => { a["heldLease"] = false; }),
        ("crash-dispose", "kill-queued", a => { a["writer"]!["disposals"] = 1; }),
        ("crash-pending", "kill-queued", a => { a["live"]![0]!["pending"] = "5"; }),
        ("crash-bytes", "kill-partial", a => { a["bytes"] = a["seed"]!.DeepClone(); }),
        ("crash-result", "kill-queued", a => { a["result"] = new JsonObject(); }),
        ("crash-stop", "kill-after-stop", a => { a["result"]!["outputs"]![0]!["flush"] = "Succeeded"; }),
    ];

    internal static void Check(string mode, JsonObject report, int pid, TestContext context, string stdout = "")
    {
        Assert.AreEqual(32, Controls.Length);
        Assert.AreEqual(32, Controls.Select(control => control.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var control in Controls.Where(control => control.Mode == mode))
        {
            JsonObject damaged = report.DeepClone().AsObject(); control.Damage(damaged);
            Reject(control.Id, () =>
            {
                if (mode.StartsWith("kill-", StringComparison.Ordinal)) { StopLifecycleExpectations.Checkpoint(mode, damaged, pid); }
                else { StopLifecycleExpectations.Validate(mode, damaged, pid); }
            }, context);
        }
        if (mode != "normal-dual") { return; }
        JsonObject changed = report.DeepClone().AsObject(); changed["result"]!["outputs"]![0]!["written"] = "119";
        Reject("console-dispositions", () => StopLifecycleExpectations.Validate(mode, changed, pid), context);
        const string marker = "application-after-stop\n";
        Reject("console-marker", () => StopLifecycleExpectations.PhysicalConsole(report, stdout[..^marker.Length]), context);
        Reject("console-bytes", () => StopLifecycleExpectations.PhysicalConsole(report, stdout[(stdout.IndexOf('\n') + 1)..]), context);
    }

    internal static void Restart(string mode, JsonObject checkpoint, JsonObject report, int pid, TestContext context)
    {
        if (mode != "kill-partial") { return; }
        (string Id, Action<JsonObject> Damage)[] controls =
        [
            ("restart-pid", a => a["pid"] = checkpoint["pid"]!.DeepClone()),
            ("restart-before", a => a["beforeBytes"] = ""),
            ("restart-recovery", a => a["recovered"] = false),
            ("restart-damaged", a => a["damagedBytes"] = ""),
            ("restart-stop", a => a["result"]!["outputs"]![0]!["pending"] = "1")
        ];
        foreach (var control in controls)
        {
            JsonObject damaged = report.DeepClone().AsObject(); control.Damage(damaged);
            Reject(control.Id, () => StopLifecycleExpectations.Restart(mode, checkpoint, damaged, pid), context);
        }
    }

    private static void Reject(string id, Action check, TestContext context)
    {
        Assert.ThrowsExactly<InvalidDataException>(check, "Original lifecycle corruption was accepted: " + id);
        context.WriteLine("Rejected original lifecycle control: " + id);
    }
}
