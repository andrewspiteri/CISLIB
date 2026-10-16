using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

internal static class ConsoleProcessChecks
{
    internal static IEnumerable<object[]> Cases(string family, int count)
    {
        JsonArray rows = LogValueProcessTests.Read("console-" + family + "-cases.json")["cases"]!.AsArray();
        IEnumerable<string> expected = family switch
        {
            "composition" => (from profile in new[] { "CONSOLE", "BOTH", "FILE", "DISABLED" } from format in Enumerable.Range(0, 2) from detail in Enumerable.Range(0, 2) from stream in Enumerable.Range(0, 2) select $"CONSOLE-{profile}-{format}{detail}{stream}")
                .Concat(new[] { "CONSOLE-CAP-00-65536", "CONSOLE-CAP-01-65536", "CONSOLE-CAP-01-16384", "CONSOLE-CAP-01-131072", "CONSOLE-CAP-11-65536" })
                .Concat(from format in Enumerable.Range(0, 2) from stream in Enumerable.Range(0, 2) select $"CONSOLE-PHYSICAL-{format}{stream}"),
            "ownership" => (from mode in new[] { "DRAIN", "FAULT", "SERIALIZED", "REUSE", "PHYSICAL" } from format in Enumerable.Range(0, 2) from stream in Enumerable.Range(0, 2) select $"BORROW-{mode}-{format}{stream}")
                .Concat(new[] { "BORROW-IDLE-00", "BORROW-IDLE-01" }),
            "startup" => (from mode in new[] { "SELECT-THROW", "SELECT-NULL", "READ-ONLY", "CLOSED", "CANWRITE-THROW", "LAUNCH-CONSOLE" } from profile in new[] { "CONSOLE", "BOTH" } from stream in Enumerable.Range(0, 2) select $"STARTUP-{mode}-{profile}-{stream}")
                .Concat(from choice in new[] { ("LAUNCH-FILE", "BOTH"), ("EXISTING", "CONSOLE"), ("DISABLED", "DISABLED"), ("FILE-ONLY", "FILE") } from stream in Enumerable.Range(0, 2) select $"STARTUP-{choice.Item1}-{choice.Item2}-{stream}")
                .Concat(from profile in new[] { "CONSOLE", "BOTH" } from stream in Enumerable.Range(0, 2) select $"STARTUP-PHYSICAL-READONLY-{profile}-{stream}"),
            "consumer" => (from profile in new[] { "CONSOLE", "BOTH", "FILE", "DISABLED", "BLOCK", "FAIL" } from presentation in new[] { "JSONL", "NORMAL", "VERBOSE" } from stream in new[] { "STDERR", "STDOUT" } select $"CONSUMER-{profile}-{presentation}-{stream}")
                .Concat(new[] { "CONSUMER-PRESSURE-JSONL-STDERR", "CONSUMER-PRESSURE-JSONL-STDOUT" }),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        string[] ids = rows.Select(row => (string)row!["id"]!).ToArray();
        Assert.AreEqual(count, rows.Count);
        Assert.AreEqual(count, ids.Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(expected.ToArray(), ids);
        return ids.Select(id => new object[] { id });
    }

    internal static async Task<JsonObject> Capture(string family, JsonObject row, TestContext context, IReadOnlyDictionary<string, string?>? environment = null)
    {
        string path = FixtureProcessChecks.Find("console-" + family + "-cases.json"), id = (string)row["id"]!;
        bool pressure = (string?)row["mode"] == "pressure";
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["console-" + family, path, id], owned.Path, context, path,
            environment ?? new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_STREAM"] = "invalid-must-not-be-read", ["TERM"] = "dumb", ["NO_COLOR"] = "1" },
            TimeSpan.FromSeconds(30), physicalStreams: row["physical"]!.GetValue<bool>(),
            minimumStdoutBytes: pressure ? row["minimumStdoutBytes"]!.GetValue<int>() : 0,
            minimumStderrBytes: pressure ? row["minimumStderrBytes"]!.GetValue<int>() : 0);
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
}
