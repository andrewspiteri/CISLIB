using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileIntervalExpectations
{
    internal static readonly string[] Modes = ["idle-min", "idle-default", "idle-max", "configured", "console-only", "file-none", "create-failure", "arm-failure", "below", "above", "env"];
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("File interval report expectation failed."); } }
    internal static void Validate(string mode, JsonObject report)
    {
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        JsonNode timers = report["timers"]!;
        Require(timers["active"]!.GetValue<int>() == 0 && timers["created"]!.GetValue<int>() == timers["disposed"]!.GetValue<int>() && timers["flowSuppressed"]!.GetValue<bool>());
        if (mode is "below" or "above")
        { Require(report["errorType"]!.GetValue<string>() == "ArgumentOutOfRangeException" && report["workers"]!.GetValue<int>() == 0 && report["selections"]!.GetValue<int>() == 0 && timers["attempts"]!.GetValue<int>() == 0); return; }
        if (mode.EndsWith("-failure", StringComparison.Ordinal))
        { Require(report["safeCode"]!.GetValue<string>() == "FileFlushTimerInitializationFailed" && report["joined"]!.GetValue<bool>() && report["borrowedOpen"]!.GetValue<bool>() && timers["attempts"]!.GetValue<int>() == 1); return; }
        bool disabled = mode is "console-only" or "file-none";
        Require(timers["created"]!.GetValue<int>() == (disabled ? 0 : 1) && timers["fires"]!.GetValue<int>() == (disabled ? 0 : mode.StartsWith("idle-", StringComparison.Ordinal) ? 3 : 2));
        Require(report["targets"]!.AsArray().Count == (disabled ? 0 : 2) && report["targets"]!.AsArray().All(t => t!.GetValue<string>() == "1"));
        if (!disabled)
        {
            int interval = mode == "idle-min" ? 10 : mode == "idle-default" ? 1000 : mode == "idle-max" ? 60000 : mode == "env" ? 250 : 500;
            Require(report["interval"]!.GetValue<int>() == interval && timers["period"]!.GetValue<int>() == interval && report["beforeDue"]!.GetValue<bool>() && report["idleNoNewEvents"]!.GetValue<bool>());
        }
        Require(report["diagnostics"]!.AsArray().Count == 1 && report["diagnostics"]![0]!["written"]!.GetValue<string>() == "1" && report["diagnostics"]![0]!["pending"]!.GetValue<string>() == "0");
    }
}
