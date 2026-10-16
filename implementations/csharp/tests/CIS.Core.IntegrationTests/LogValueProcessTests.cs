using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueProcessTests
{
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
