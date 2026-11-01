using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueProcessTests
{
#if !CIS_MUTATION_TESTS
    private sealed record MatrixInput(IReadOnlyDictionary<string, string> Rows, int ProcessId);
    private static readonly ConcurrentDictionary<(string Command, string Input), Lazy<Task<MatrixInput>>> Matrices = new();
#endif
    private static readonly string[] Kinds = ["null", "boolean", "string", "number", "integer", "integer-string", "decimal-string", "array", "map"];
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> PortableRows => Read()["cases"]!.AsArray()
        .Where(row => Kinds.Contains((string)row!["input"]!["kind"]!, StringComparer.Ordinal))
        .Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    public void NativeValueCatalogMembershipIsComplete()
    {
        foreach ((string name, int count) in new[] { ("event-value-cases.json", 47), ("value-normalization-cases.json", 12), ("value-collection-cases.json", 22), ("value-qualification-cases.json", 71) })
        {
            _ = FixtureProcessChecks.Cases(name, count);
        }
    }

    [TestMethod]
    [DynamicData(nameof(PortableRows))]
    public async Task ActualTypedValueRepresentationsExecuteInFreshProcessesWithoutStartup(string id)
    {
        await Verify(Find(), id, TestContext);
    }

    internal static async Task Verify(string input, string id, TestContext context)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(input))!["cases"]!.AsArray().Single(value => (string?)value!["id"] == id)!.AsObject();
        JsonObject actual = await Run("log-value", input, id, context);
        bool valid = row["expectedValid"]!.GetValue<bool>();
        Assert.AreEqual(valid, actual["valid"]!.GetValue<bool>());
        string kind = (string)row["input"]!["kind"]!;
        string? expectedKind = kind switch
        {
            "null" => "Null",
            "boolean" => "Boolean",
            "string" => "String",
            "number" => "Number",
            "integer" => "Integer",
            "integer-string" => "IntegerString",
            "decimal-string" => "DecimalString",
            "uuid" => "Uuid",
            "timestamp" => "Timestamp",
            "array" => "Array",
            "map" => "Map",
            _ => null
        };
        if (valid)
        {
            Assert.IsTrue(JsonNode.DeepEquals(row["expectedJson"], actual["json"]), id);
            Assert.IsFalse(actual["isTruncated"]!.GetValue<bool>());
            Assert.AreEqual(expectedKind, (string?)actual["kind"]);
            Assert.IsTrue(actual["factoryCalls"]!.GetValue<int>() > 0);
        }
        else
        {
            bool adapterOnly = id is "VALUE-INVALID-BOOL" or "VALUE-INVALID-INTEGER-TYPE" or "VALUE-BAD-UUID" or "VALUE-BAD-TIMESTAMP" || expectedKind is null;
            Assert.AreEqual(adapterOnly, actual["factoryCalls"]!.GetValue<int>() == 0, "Core/transport failure attribution differs.");
            if (adapterOnly) { Assert.AreEqual("InputAdapterTypeMismatch", (string?)actual["error"]); }
            else
            {
                Assert.IsTrue((string?)actual["error"] is "ArgumentException" or "ArgumentOutOfRangeException");
                Assert.AreEqual(id == "VALUE-INVALID-MAP-KEY" ? "properties" : "value", (string?)actual["parameter"]);
            }
        }
    }

    internal static async Task<JsonObject> Run(string command, string input, string id, TestContext context)
    {
#if CIS_MUTATION_TESTS
        return await RunSingle(command, input, id, context);
#else
        int count = MatrixCount(command, input);
        if (count == 0 || !string.Equals(Path.GetFullPath(input), Path.GetFullPath(Find(Path.GetFileName(input))), StringComparison.Ordinal))
        {
            return await RunSingle(command, input, id, context);
        }
        MatrixInput matrix = await Matrices.GetOrAdd((command, input), key => new Lazy<Task<MatrixInput>>(
            () => CaptureMatrix(key.Command, key.Input, count, context))).Value;
        Assert.IsTrue(matrix.Rows.ContainsKey(id), "The physical value matrix omitted the requested row.");
        context.WriteLine($"Physical value matrix input: command={command}, catalog={Path.GetFileName(input)}, processId={matrix.ProcessId}, rows={matrix.Rows.Count}, selectedCase={id}.");
        // Each row was executed in the real child; only completed immutable text is shared.
        JsonObject actual = JsonNode.Parse(matrix.Rows[id])!.AsObject();
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        return actual;
#endif
    }

#if !CIS_MUTATION_TESTS
    private static int MatrixCount(string command, string input) => (command, Path.GetFileName(input)) switch
    {
        ("log-value", "event-value-cases.json") => 47,
        ("log-value", "value-normalization-cases.json") => 12,
        ("log-collection", "value-collection-cases.json") => 22,
        ("log-qualification", "value-qualification-cases.json") => 71,
        _ => 0
    };

    private static async Task<MatrixInput> CaptureMatrix(string command, string input, int count, TestContext context)
    {
        JsonArray rows = FixtureProcessChecks.Cases(Path.GetFileName(input), count);
        string[] expected = rows.Select(row => (string)row!["id"]!).ToArray();
        using var owned = new TemporaryDirectory();
        (JsonObject batch, int pid) = await FixtureProcessChecks.RunAsync(["log-value-matrix", command, input], owned.Path, context, input,
            new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(command, (string?)batch["command"]);
        Assert.AreEqual(Path.GetFileName(input), (string?)batch["catalog"]);
        Assert.IsFalse(batch["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        JsonArray reports = batch["reports"]!.AsArray();
        CollectionAssert.AreEqual(expected, reports.Select(row => (string)row!["caseId"]!).ToArray(), "Every original value row must execute exactly once, in input order.");
        Assert.IsFalse(batch.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());

        // Independently preserve each catalog's original single-row physical CLI.
        JsonObject single = await RunSingle(command, input, expected[0], context);
        _ = single.Remove("observed");
        Assert.IsTrue(JsonNode.DeepEquals(single, reports[0]), "Grouped value execution changed the fresh-process representative.");
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

    private static async Task<JsonObject> RunSingle(string command, string input, string id, TestContext context)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync([command, input, id], owned.Path, context, input,
            new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }

    internal static JsonObject Read(string name = "event-value-cases.json") => JsonNode.Parse(File.ReadAllText(Find(name)))!.AsObject();
    internal static string Find(string name = "event-value-cases.json") => FixtureProcessChecks.Find(name);
}
