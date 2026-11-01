using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventProjectionProcessTests
{
#if !CIS_MUTATION_TESTS
    private sealed record MatrixInput(IReadOnlyDictionary<string, string> Rows, int ProcessId);
    private static readonly ConcurrentDictionary<(string Command, string Input), Lazy<Task<MatrixInput>>> Matrices = new();
#endif
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("event-projection-cases.json", 32).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SharedProjectionRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("event-projection-cases.json");
        JsonObject actual = await Capture("event-projection", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("event-projection-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => (string?)row!["id"] == id)!.AsObject();
        actual.Remove("observed");
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
    }

    internal static async Task<JsonObject> Capture(string command, string input, string id, TestContext context)
    {
#if CIS_MUTATION_TESTS
        return await CaptureSingle(command, input, id, context);
#else
        int count = MatrixCount(command, input);
        if (count == 0 || !string.Equals(Path.GetFullPath(input), Path.GetFullPath(FixtureProcessChecks.Find(Path.GetFileName(input))), StringComparison.Ordinal))
        {
            return await CaptureSingle(command, input, id, context);
        }
        MatrixInput matrix = await Matrices.GetOrAdd((command, input), key => new Lazy<Task<MatrixInput>>(
            () => CaptureMatrix(key.Command, key.Input, count, context))).Value;
        Assert.IsTrue(matrix.Rows.ContainsKey(id), "The physical event matrix omitted the requested row.");
        context.WriteLine($"Physical event matrix input: command={command}, catalog={Path.GetFileName(input)}, processId={matrix.ProcessId}, rows={matrix.Rows.Count}, selectedCase={id}.");
        JsonObject actual = JsonNode.Parse(matrix.Rows[id])!.AsObject();
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        VerifyPrivacy(actual);
        return actual;
#endif
    }

#if !CIS_MUTATION_TESTS
    private static int MatrixCount(string command, string input) => (command, Path.GetFileName(input)) switch
    {
        ("event-projection", "event-projection-cases.json") => 32,
        ("event-precedence", "event-precedence-cases.json") => 28,
        ("event-correlation", "event-correlation-cases.json") => 14,
        _ => 0
    };

    private static async Task<MatrixInput> CaptureMatrix(string command, string input, int count, TestContext context)
    {
        JsonArray rows = FixtureProcessChecks.Cases(Path.GetFileName(input), count);
        string[] expected = rows.Select(row => (string)row!["id"]!).ToArray();
        using var owned = new TemporaryDirectory();
        (JsonObject batch, int pid) = await FixtureProcessChecks.RunAsync(["event-projection-matrix", command, input], owned.Path, context, input,
            new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_PROJECTION_TOKEN"] = "CIS_NEVER_CAPTURE_PROJECTION"
            }, TimeSpan.FromSeconds(30));
        Assert.AreEqual(command, (string?)batch["command"]);
        Assert.AreEqual(Path.GetFileName(input), (string?)batch["catalog"]);
        Assert.IsFalse(batch["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        JsonArray reports = batch["reports"]!.AsArray();
        CollectionAssert.AreEqual(expected, reports.Select(row => (string)row!["caseId"]!).ToArray(), "Every original event row must execute exactly once, in input order.");
        VerifyPrivacy(batch);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());

        // Keep each family's original physical single-row CLI and compare its complete representative.
        JsonObject single = await CaptureSingle(command, input, expected[0], context);
        _ = single.Remove("observed");
        Assert.IsTrue(JsonNode.DeepEquals(single, reports[0]), "Grouped event execution changed the fresh-process representative.");
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonNode? node in reports)
        {
            JsonObject report = node!.AsObject();
            report["observed"] = batch["observed"]!.DeepClone();
            texts.Add((string)report["caseId"]!, report.ToJsonString());
        }
        return new(new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(texts), pid);
    }
#endif

    private static async Task<JsonObject> CaptureSingle(string command, string input, string id, TestContext context)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync([command, input, id], owned.Path, context, input,
            new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_PROJECTION_TOKEN"] = "CIS_NEVER_CAPTURE_PROJECTION"
            }, TimeSpan.FromSeconds(30));
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        VerifyPrivacy(actual);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
    [TestMethod]
    [DataRow("CIS_NEVER_CAPTURE_COLLECTOR")]
    [DataRow("CIS_NEVER_CAPTURE_PROJECTION")]
    [DataRow("CIS_NEVER_CAPTURE_ACTIVITY")]
    public void PrivateAmbientTokensCannotAppearInTheReport(string sentinel)
    {
        var report = new JsonObject { ["attributes"] = new JsonObject { ["value"] = "safe" } };
        VerifyPrivacy(report);
        report["attributes"]!["value"] = sentinel;
        StringAssert.Contains(Assert.ThrowsExactly<AssertFailedException>(() => VerifyPrivacy(report)).Message, "Private sentinel appeared");
    }

    private static void VerifyPrivacy(JsonObject actual) =>
        FixtureProcessChecks.ExcludeStrings(actual, "CIS_NEVER_CAPTURE_COLLECTOR", "CIS_NEVER_CAPTURE_PROJECTION", "CIS_NEVER_CAPTURE_ACTIVITY");
}
