using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LoggerFoundationProcessTests
{
#if !CIS_MUTATION_TESTS
    private sealed record MatrixInput(IReadOnlyDictionary<string, string> Rows, int ProcessId);
    private static readonly ConcurrentDictionary<(string Command, string Input), Lazy<Task<MatrixInput>>> Matrices = new();
#endif
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("logger-foundation-cases.json", 23).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task LoggerFoundationRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("logger-foundation-cases.json");
        JsonObject actual = await Capture("logger-foundation", input, id, TestContext);
        JsonObject row = LogValueProcessTests.Read("logger-foundation-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        LoggerFoundationExpectations.Validate(row, actual);
    }

    internal static async Task<JsonObject> Capture(string command, string input, string id, TestContext context)
    {
#if CIS_MUTATION_TESTS
        return command == "presentation" ? await JsonlReuseProcessTests.Capture(command, input, id, context) : await CaptureSingle(command, input, id, context);
#else
        MatrixInput matrix = await Matrices.GetOrAdd((command, input), key => new Lazy<Task<MatrixInput>>(
            () => CaptureMatrix(key.Command, key.Input, context))).Value;
        Assert.IsTrue(matrix.Rows.ContainsKey(id), "The physical logger matrix omitted the requested row.");
        context.WriteLine($"Physical logger matrix input: command={command}, processId={matrix.ProcessId}, rows={matrix.Rows.Count}, selectedCase={id}.");
        // Only immutable completed input text is shared; each native case owns its JSON.
        JsonObject actual = JsonNode.Parse(matrix.Rows[id])!.AsObject();
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        return actual;
#endif
    }

#if !CIS_MUTATION_TESTS
    private static async Task<MatrixInput> CaptureMatrix(string command, string input, TestContext context)
    {
        int count = command switch
        {
            "logger-foundation" => 23,
            "logger-outcome" => 125,
            "logger-decision" => 67,
            "logger-capture" => 41,
            "presentation" => 64,
            _ => throw new ArgumentException("Unknown logger matrix.", nameof(command))
        };
        JsonArray rows = FixtureProcessChecks.Cases(Path.GetFileName(input), count, declaresCount: command == "logger-foundation");
        string[] expected = rows.Select(row => (string)row!["id"]!).ToArray();
        using var owned = new TemporaryDirectory();
        var environment = new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" };
        if (command == "presentation") { environment["TERM"] = "dumb"; environment["NO_COLOR"] = "1"; }
        (JsonObject batch, int pid) = await FixtureProcessChecks.RunAsync(["logger-matrix", command, input], owned.Path, context, input,
            environment, TimeSpan.FromSeconds(30));
        Assert.AreEqual(command, (string?)batch["command"]);
        JsonArray reports = batch["reports"]!.AsArray();
        CollectionAssert.AreEqual(expected, reports.Select(row => (string)row!["caseId"]!).ToArray(), "The physical matrix must execute every original row exactly once, in input order.");
        Assert.IsFalse(batch.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());

        // Retain an independent fresh-process control for each family's single-row CLI.
        JsonObject single = command == "presentation" ? await JsonlReuseProcessTests.Capture(command, input, expected[0], context)
            : await CaptureSingle(command, input, expected[0], context);
        _ = single.Remove("observed");
        if (command == "presentation")
        {
            // Activities intentionally acquire new IDs in each real process. Validate
            // their complete frames and correlation with the unchanged comparator.
            JsonObject grouped = reports[0]!.AsObject();
            PresentationExpectations.Validate(rows[0]!.AsObject(), single);
            PresentationExpectations.Validate(rows[0]!.AsObject(), grouped);
            foreach (JsonObject report in new[] { single, grouped })
            {
                Assert.IsTrue(report["outputRedirected"]!.GetValue<bool>() && report["errorRedirected"]!.GetValue<bool>());
                Assert.IsFalse(report["debuggerAttached"]!.GetValue<bool>());
            }
            foreach (string key in new[] { "enabled", "outcomes", "factoryCalls", "clockCalls", "invalidCount", "truncatedCount", "sharedCaptures", "consoleFiltered", "fileFiltered", "ambientOperation", "outputRedirected", "errorRedirected", "debuggerAttached" })
            { Assert.IsTrue(JsonNode.DeepEquals(single[key], grouped[key]), "Grouped presentation changed the fresh-process representative: " + key); }
        }
        else { Assert.IsTrue(JsonNode.DeepEquals(single, reports[0]), "Grouped execution changed the fresh-process representative's observations."); }
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonNode? node in reports)
        {
            JsonObject row = node!.AsObject();
            row["observed"] = batch["observed"]!.DeepClone();
            texts.Add((string)row["caseId"]!, row.ToJsonString());
        }
        return new(texts, pid);
    }
#endif

    private static async Task<JsonObject> CaptureSingle(string command, string input, string id, TestContext context)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync([command, input, id], owned.Path, context, input,
            new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" },
            TimeSpan.FromSeconds(30));
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
}
