using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopDrainExpectations
{
    internal static readonly string[] Modes = ["normal-dual", "queued-only", "blocked-write", "single-write", "cancel-write", "blocked-flush", "cancel-flush", "blocked-callback"];
    private static void Require(bool value) { if (!value) { throw new InvalidOperationException("Stop drain expectation failed."); } }
    private static long Number(JsonNode? n) => long.Parse(n!.GetValue<string>(), CultureInfo.InvariantCulture);
    internal static void Validate(string mode, JsonObject report)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal) && report["mode"]!.GetValue<string>() == mode);
        bool normal = mode == "normal-dual", queued = mode == "queued-only", flush = mode is "blocked-flush" or "cancel-flush";
        bool single = mode == "single-write";
        JsonNode result = report["result"]!;
        Require(result["completion"]!.GetValue<string>() == (normal ? "Completed" : "TimedOut"));
        Require(result["succeeded"]!.GetValue<bool>() == normal && result["loss"]!.GetValue<bool>() == (!normal && !flush && !single));
        JsonArray outputs = result["outputs"]!.AsArray(); Require(outputs.Count == (normal ? 2 : 1));
        foreach (JsonNode? o in outputs)
        {
            Require(Number(o!["boundary"]) == (flush || single ? 1 : 3));
            Require(Number(o["written"]) == (normal ? 3 : flush ? 1 : 0));
            Require(Number(o["failed"]) == 0 && Number(o["abandoned"]) == (queued ? 3 : normal || flush || single ? 0 : 2));
            Require(Number(o["pending"]) == (normal || flush || queued ? 0 : 1));
            Require(o["flush"]!.GetValue<string>() == (normal ? "Succeeded" : flush || single ? "Pending" : "Failed"));
        }
        Require(report["sameTask"]!.GetValue<bool>() && report["sameResult"]!.GetValue<bool>() && report["immutable"]!.GetValue<bool>() && report["workersSettled"]!.GetValue<bool>());
        Require(report["cancellationRequested"]!.GetValue<bool>() == (!normal && !queued));
        Require(report["stream"]!["disposed"]!.GetValue<int>() == 0);
        if (mode == "blocked-callback") { Require(report["stream"]!["callbackGateFree"]!.GetValue<bool>()); }
    }
}
