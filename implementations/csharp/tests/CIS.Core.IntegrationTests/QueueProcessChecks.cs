using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal static class QueueProcessChecks
{
    internal static IEnumerable<object[]> Cases(string name, int count)
    {
        string[] expected = name switch
        {
            "output-state-cases.json" => ["STATE-HEALTHY-CONSOLE", "STATE-HEALTHY-FILE", "STATE-HEALTHY-BOTH", "STATE-DISABLED", "STATE-FAIL-CONSOLE", "STATE-FAIL-FILE", "STATE-ISOLATE-CONSOLE", "STATE-ISOLATE-FILE", "STATE-FAIL-BOTH-CONSOLE-FIRST", "STATE-FAIL-BOTH-FILE-FIRST", "STATE-CLOSE-THEN-FAIL", "STATE-CANCEL-FILE"],
            "output-accounting-cases.json" => ["ACCOUNT-CONSOLE", "ACCOUNT-FILE", "ACCOUNT-BOTH", "ACCOUNT-MIXED", "ACCOUNT-LOW-INTEREST", "ACCOUNT-CONSOLE-FAULT", "ACCOUNT-FILE-FAULT", "ACCOUNT-ONLY-CONSOLE-FAULT", "ACCOUNT-ONLY-FILE-FAULT", "ACCOUNT-BOTH-FAULT", "ACCOUNT-DISABLED"],
            "queue-burst-cases.json" => ["BURST-CONSOLE-1", "BURST-FILE-1", "BURST-BOTH-1", "BURST-CONSOLE-2", "BURST-FILE-2", "BURST-BOTH-2", "BURST-CONSOLE-8", "BURST-FILE-8", "BURST-BOTH-8", "BURST-FILE-THEN-CONSOLE", "BURST-CONSOLE-THEN-FILE", "BURST-DISABLED", "BURST-NONE", "BURST-FILE-THRESHOLD", "BURST-CONSOLE-THRESHOLD"],
            "output-queue-cases.json" => ["QUEUE-CONSOLE-1", "QUEUE-CONSOLE-2", "QUEUE-CONSOLE-8", "QUEUE-FILE-1", "QUEUE-FILE-2", "QUEUE-FILE-8", "QUEUE-BOTH-1", "QUEUE-BOTH-2", "QUEUE-BOTH-8", "QUEUE-DISABLED", "QUEUE-INDEPENDENT"],
            "diagnostics-cases.json" => ["DIAGNOSTICS-DELIVERY-CONSOLE-PARTIAL-SUCCESS", "DIAGNOSTICS-DELIVERY-FILE-PARTIAL-SUCCESS", "DIAGNOSTICS-DELIVERY-BOTH-PARTIAL-SUCCESS", "DIAGNOSTICS-DELIVERY-CONSOLE-PARTIAL-FAILURE", "DIAGNOSTICS-DELIVERY-FILE-PARTIAL-FAILURE", "DIAGNOSTICS-DELIVERY-FILE-COMPLETE-FAILURE", "DIAGNOSTICS-DELIVERY-CONSOLE-ZERO-FAILURE", "DIAGNOSTICS-DELIVERY-CONSOLE-PARTIAL-CANCEL", "DIAGNOSTICS-DELIVERY-FILE-PARTIAL-CANCEL", "DIAGNOSTICS-DELIVERY-CONSOLE-CANCEL-THEN-SUCCESS", "DIAGNOSTICS-DELIVERY-DISABLED", "DIAGNOSTICS-STATE-HEALTHY-CONSOLE", "DIAGNOSTICS-STATE-HEALTHY-FILE", "DIAGNOSTICS-STATE-HEALTHY-BOTH", "DIAGNOSTICS-STATE-DISABLED", "DIAGNOSTICS-STATE-FAIL-CONSOLE", "DIAGNOSTICS-STATE-FAIL-FILE", "DIAGNOSTICS-STATE-ISOLATE-CONSOLE", "DIAGNOSTICS-STATE-ISOLATE-FILE", "DIAGNOSTICS-STATE-FAIL-BOTH-CONSOLE-FIRST", "DIAGNOSTICS-STATE-FAIL-BOTH-FILE-FIRST", "DIAGNOSTICS-STATE-CLOSE-THEN-FAIL", "DIAGNOSTICS-STATE-CANCEL-FILE"],
            "queued-fanout-cases.json" => ["FANOUT-BOTH", "FANOUT-CONSOLE", "FANOUT-FILE", "FANOUT-DISABLED", "FANOUT-UNION", "FANOUT-REJECT-THEN-ACCEPT", "FANOUT-INDEPENDENT", "FANOUT-LATE-CLOSE", "FANOUT-LATE-FILE-FAULT", "FANOUT-EARLY-BOTH-FAULT", "FANOUT-FROZEN-SETTINGS", "FANOUT-PRODUCERS"],
            "delivery-observation-cases.json" => ["DELIVERY-CONSOLE-PARTIAL-SUCCESS", "DELIVERY-FILE-PARTIAL-SUCCESS", "DELIVERY-BOTH-PARTIAL-SUCCESS", "DELIVERY-CONSOLE-PARTIAL-FAILURE", "DELIVERY-FILE-PARTIAL-FAILURE", "DELIVERY-FILE-COMPLETE-FAILURE", "DELIVERY-CONSOLE-ZERO-FAILURE", "DELIVERY-CONSOLE-PARTIAL-CANCEL", "DELIVERY-FILE-PARTIAL-CANCEL", "DELIVERY-CONSOLE-CANCEL-THEN-SUCCESS", "DELIVERY-DISABLED"],
            "queue-overflow-cases.json" => ["OVERFLOW-CONSOLE-ONLY", "OVERFLOW-FILE-ONLY", "OVERFLOW-BOTH-FULL", "OVERFLOW-CAPACITY-TWO", "OVERFLOW-CONSOLE-FULL-FILE-FILTERED", "OVERFLOW-CONSOLE-FILTERED-FILE-FULL", "OVERFLOW-FILE-FAULT-CONSOLE-FULL", "OVERFLOW-CONSOLE-FAULT-FILE-FULL", "OVERFLOW-FILE-INDEPENDENT", "OVERFLOW-CONSOLE-RECOVERS", "OVERFLOW-FILE-RECOVERS", "OVERFLOW-NONE-WHILE-FULL"],
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        JsonArray rows = LogValueProcessTests.Read(name)["cases"]!.AsArray();
        string[] ids = rows.Select(row => (string)row!["id"]!).ToArray();
        Assert.AreEqual(count, rows.Count);
        Assert.AreEqual(count, ids.Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(expected, ids);
        return ids.Select(id => new object[] { id });
    }

    internal static async Task<JsonObject> Capture(string command, string input, string id, TestContext context, int seconds)
    {
        string path = FixtureProcessChecks.Find(input);
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync([command, path, id], owned.Path, context, path,
            new Dictionary<string, string?> { ["CORE_LOG_QUEUE_CAPACITY"] = "invalid-must-not-be-read", ["TERM"] = "dumb", ["NO_COLOR"] = "1" }, TimeSpan.FromSeconds(seconds));
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
}
