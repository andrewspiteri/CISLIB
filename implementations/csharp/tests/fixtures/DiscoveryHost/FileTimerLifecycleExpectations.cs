using System.Text.Json.Nodes;
namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerLifecycleExpectations
{
    internal static readonly string[] Modes = ["close-write", "close-flush", "close-callback", "flush-failure", "flush-failure-callback", "admission-fault", "detached-arm-throw"];
    internal static void Validate(string mode, JsonObject report)
    {
        void Require(bool value) { if (!value) { throw new InvalidOperationException("Timer lifecycle expectation failed: " + mode); } }
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        Require(report["sameSharedCompletion"]!.GetValue<bool>() && report["sameDisposal"]!.GetValue<bool>() && report["sharedJoined"]!.GetValue<bool>());
        Require(report["finalControls"]!.GetValue<int>() == 0 && report["repeatedClosures"]!.GetValue<int>() == 64);
        JsonNode timer = report["timers"]!;
        Require(timer["active"]!.GetValue<int>() == 0 && timer["callbacks"]!.GetValue<int>() == 0 && timer["pendingJoins"]!.GetValue<int>() == 0);
        Require(timer["created"]!.GetValue<int>() == timer["disposed"]!.GetValue<int>());
        foreach (string key in new[] { "suppressedCreation", "outsideGate", "withoutCaller", "published", "afterCloser" }) { Require(timer[key]!.GetValue<bool>()); }
        Require(report["held"]!["timerDetached"]!.GetValue<bool>());
        if (mode == "detached-arm-throw") { Require(report["startError"]!.GetValue<string>() == "ObjectDisposedException" && timer["syncDisposes"]!.GetValue<int>() == 0); return; }
        if (mode == "close-callback") { Require(report["held"]!["ownershipHeld"]!.GetValue<bool>() && report["ownershipReleased"]!.GetValue<bool>()); }
        else { Require(report["writer"]!["maximumIo"]!.GetValue<int>() == 1 && report["workerFaulted"]!.GetValue<bool>() == mode.StartsWith("flush-failure", StringComparison.Ordinal)); }
    }
}
