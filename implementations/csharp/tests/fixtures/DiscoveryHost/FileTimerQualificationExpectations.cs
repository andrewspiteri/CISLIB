using System.Text.Json.Nodes;
namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerQualificationExpectations
{
    internal static readonly string[] Modes = ["stopped-rearm", "faulted-rearm"];
    internal static void Validate(string mode, JsonObject report)
    {
        void Require(bool value) { if (!value) { throw new InvalidOperationException("File timer qualification failed."); } }
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        Require(report["sameWorker"]!.GetValue<bool>() && report["sameSharedCompletion"]!.GetValue<bool>() && report["sameDisposal"]!.GetValue<bool>());
        Require(report["workerFaulted"]!.GetValue<bool>() == (mode == "faulted-rearm"));
        Require(report["before"]!.ToJsonString() == report["after"]!.ToJsonString());
        Require(report["before"]!["state"]!.GetValue<string>() == (mode == "faulted-rearm" ? "Faulted" : "Stopped"));
        Require(report["probes"]!.AsArray().Count == 64 && report["timers"]!["created"]!.GetValue<int>() == 65 && report["timers"]!["disposed"]!.GetValue<int>() == 65);
        Require(report["timers"]!["active"]!.GetValue<int>() == 0 && report["timers"]!["periodicFires"]!.GetValue<int>() == 1 && report["finalControls"]!.GetValue<int>() == 0);
    }
}
