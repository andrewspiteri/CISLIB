using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerCoordinationExpectations
{
    internal static readonly string[] Modes = ["blocked-write", "blocked-flush", "continuous", "duplicate-start", "closed-start", "detach-before-arm", "detach-system-before-arm", "arm-false", "arm-disposed"];
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("File timer coordination expectation failed."); } }
    internal static void Validate(string mode, JsonObject report)
    {
        Require(report["mode"]!.GetValue<string>() == mode && report["runtime"]!.GetValue<string>() == ".NET 10.0.12");
        JsonNode timers = report["timers"]!, writer = report["writer"]!;
        Require(timers["active"]!.GetValue<int>() == 0 && timers["created"]!.GetValue<int>() == timers["disposed"]!.GetValue<int>() && report["finalControls"]!.GetValue<int>() == 0);
        Require(writer["maximumIo"]!.GetValue<int>() <= 1);
        if (mode.StartsWith("blocked-", StringComparison.Ordinal))
        {
            Require(report["repeated"]!["cancelled"]!.GetValue<int>() == 500 && report["repeated"]!["ticks"]!.GetValue<int>() == 3000);
            Require(report["afterDetach"]!["controls"]!.GetValue<int>() == 4 && report["afterDetach"]!["attached"]!.GetValue<int>() == 1);
            Require(writer["writes"]!.GetValue<int>() == 4 && writer["flushes"]!.GetValue<int>() == 4 && timers["periodicFires"]!.GetValue<int>() == 3004);
            Require(report["result"]!["succeeded"]!.GetValue<bool>() && report["result"]!["loss"]!.GetValue<bool>());
        }
        else if (mode == "continuous")
        { Require(report["boundaries"]!.AsArray().Count == 128 && writer["writes"]!.GetValue<int>() == 128 && writer["flushes"]!.GetValue<int>() == 128); }
        else
        {
            string? error = report["startError"]?.GetValue<string>();
            Require(error == (mode is "duplicate-start" or "arm-false" ? "InvalidOperationException" : mode == "arm-disposed" ? "ObjectDisposedException" : null));
            Require(writer["flushes"]!.GetValue<int>() == (mode == "duplicate-start" ? 1 : 0));
        }
    }
}
