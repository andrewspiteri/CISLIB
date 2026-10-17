using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal static class StopLifecycleReaderControls
{
    private static readonly (string Id, string Mode, string Reason, Action<JsonObject> Damage)[] Controls =
    [
        ("pid", "normal", "lifecycle identity", a => { a["pid"] = a["pid"]!.GetValue<int>() + 1; }),
        ("runtime", "normal", "lifecycle identity", a => { a["runtime"] = ".NET 10.0.13"; }),
        ("shape", "normal", "stop result shape", a => { a["result"]!["extra"] = true; }),
        ("summary", "normal", "public stop summary", a => { a["result"]!["succeeded"] = false; }),
        ("boundary", "normal", "public file dispositions", a => { a["result"]!["outputs"]![0]!["boundary"] = "119"; }),
        ("ticket", "normal", "admission tickets", a => { a["receipts"]![0]!["ticket"] = "2"; }),
        ("receipt", "normal", "receipt record order", a => { a["receipts"]![0]!["event"] = "other"; }),
        ("race", "producer-stop-race", "stopped capture", a => { a["capturingStopped"] = false; }),
        ("producer", "producer-stop-race", "attempt accounting", a => { a["attempts"] = 513; }),
        ("stoppers", "producer-stop-race", "caller/starter counts", a => { a["racingStoppers"] = 15; }),
        ("pre-cancelled", "pre-cancelled", "caller/starter counts", a => { a["cancelled"] = 0; }),
        ("pending", "held-write", "kernel dispositions", a => { a["result"]!["outputs"]![0]!["pending"] = "0"; }),
        ("abandoned", "held-write", "kernel dispositions", a => { a["result"]!["outputs"]![0]!["abandoned"] = "4"; }),
        ("flush", "held-write", "kernel dispositions", a => { a["result"]!["outputs"]![0]!["flush"] = "Succeeded"; }),
        ("held", "held-write", "kernel ownership", a => { a["heldLease"] = false; }),
        ("immutable", "held-write", "lifecycle completion identity", a => { a["immutable"] = false; }),
        ("joiners", "held-write", "lifecycle completion identity", a => { a["joiners"] = 63; }),
        ("timer", "held-write", "kernel timer ownership", a => { a["timers"]!["created"] = 2; }),
        ("io", "held-write", "kernel writer lifetime", a => { a["writer"]!["writes"] = 2; }),
        ("dispose", "held-write", "kernel writer lifetime", a => { a["writer"]!["unsafeDispose"] = true; }),
        ("output-token", "cancelled-joiners", "output token isolation", a => { a["outputCancellationRequestedBeforeDeadline"]![0] = true; }),
        ("output-cancel", "cancelled-joiners", "output cancellation counts", a => { a["outputCancellationsBeforeDeadline"]![0] = 1; }),
        ("cancel", "cancelled-joiners", "cancelled callers", a => { a["cancelled"] = 63; }),
        ("late", "late-failure", "kernel live dispositions", a => { a["live"]![0]!["failed"] = "0"; }),
        ("partial", "late-failure", "settled write bytes", a => { a["bytes"] = a["seed"]!.DeepClone(); }),
        ("flush-pending", "held-flush", "kernel dispositions", a => { a["result"]!["outputs"]![0]!["pending"] = "1"; }),
        ("crash-owner", "kill-queued", "checkpoint ownership", a => { a["heldLease"] = false; }),
        ("crash-dispose", "kill-queued", "checkpoint writer lifetime", a => { a["writer"]!["disposals"] = 1; }),
        ("crash-pending", "kill-queued", "checkpoint live dispositions", a => { a["live"]![0]!["pending"] = "5"; }),
        ("crash-bytes", "kill-partial", "checkpoint physical prefix", a => { a["bytes"] = a["seed"]!.DeepClone(); }),
        ("crash-result", "kill-queued", "checkpoint absent stop", a => { a["result"] = new JsonObject(); }),
        ("crash-stop", "kill-after-stop", "checkpoint output dispositions", a => { a["result"]!["outputs"]![0]!["flush"] = "Succeeded"; }),
    ];

    internal static void Check(string mode, JsonObject report, int pid, TestContext context, string stdout = "")
    {
        Assert.AreEqual(32, Controls.Length);
        Assert.AreEqual(32, Controls.Select(control => control.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var control in Controls.Where(control => control.Mode == mode))
        {
            JsonObject damaged = report.DeepClone().AsObject(); control.Damage(damaged);
            Reject(control.Id, control.Reason, () =>
            {
                if (mode.StartsWith("kill-", StringComparison.Ordinal)) { StopLifecycleExpectations.Checkpoint(mode, damaged, pid); }
                else { StopLifecycleExpectations.Validate(mode, damaged, pid); }
            }, context);
        }
        if (mode != "normal-dual") { return; }
        JsonObject changed = report.DeepClone().AsObject(); changed["result"]!["outputs"]![0]!["written"] = "119";
        Reject("console-dispositions", "public console dispositions", () => StopLifecycleExpectations.Validate(mode, changed, pid), context);
        Reject("console-bom", "physical console bytes", () => StopLifecycleExpectations.PhysicalConsole(report, "\uFEFF" + stdout), context);
        const string marker = "application-after-stop\n";
        Reject("console-marker", "physical console marker", () => StopLifecycleExpectations.PhysicalConsole(report, stdout[..^marker.Length]), context);
        Reject("console-bytes", "physical console bytes", () => StopLifecycleExpectations.PhysicalConsole(report, stdout[(stdout.IndexOf('\n') + 1)..]), context);
    }

    internal static void Restart(string mode, JsonObject checkpoint, JsonObject report, int pid, TestContext context)
    {
        if (mode != "kill-partial") { return; }
        (string Id, string Reason, Action<JsonObject> Damage)[] controls =
        [
            ("restart-pid", "lifecycle identity", a => a["pid"] = checkpoint["pid"]!.DeepClone()),
            ("restart-before", "restart recovery", a => a["beforeBytes"] = ""),
            ("restart-recovery", "restart recovery", a => a["recovered"] = false),
            ("restart-damaged", "restart damaged bytes", a => a["damagedBytes"] = ""),
            ("restart-stop", "restart delivery", a => a["result"]!["outputs"]![0]!["pending"] = "1")
        ];
        foreach (var control in controls)
        {
            JsonObject damaged = report.DeepClone().AsObject(); control.Damage(damaged);
            Reject(control.Id, control.Reason, () => StopLifecycleExpectations.Restart(mode, checkpoint, damaged, pid), context);
        }
    }

    private static void Reject(string id, string reason, Action check, TestContext context)
    {
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(check, "Lifecycle corruption was accepted: " + id);
        Assert.AreEqual(reason, error.Message, id);
        context.WriteLine("Rejected lifecycle control: " + id);
    }
}
