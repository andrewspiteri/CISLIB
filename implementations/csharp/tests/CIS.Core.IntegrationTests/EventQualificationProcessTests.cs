using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.ArchitectureTests.Contracts;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("event-qualification-cases.json", 23).Select(row => new object[] { (string)row!["id"]! });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task CompleteEventsRunInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("event-qualification-cases.json");
        JsonObject actual = await EventProjectionProcessTests.Capture("event-qualification", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("event-qualification-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        EventQualificationExpectations.Validate(catalog, row, actual);
        ValidateSchema(actual, row["input"]!["maxEntryBytes"]!.GetValue<int>());
    }
    private static void ValidateSchema(JsonObject actual, int cap)
    {
        string root = Path.GetDirectoryName(LogValueProcessTests.Find("event-qualification-cases.json"))!;
        root = Path.GetFullPath(Path.Combine(root, "../.."));
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/logging/v1/event.schema.json")));
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/logging/v1/event-contract.json")));
        var validator = new EventRecordValidator(schema.RootElement, contract.RootElement);
        foreach (JsonNode? record in actual["records"]!.AsArray())
        {
            var transport = new JsonObject { ["mode"] = "producer", ["maxEntryBytes"] = cap, ["wire"] = record!["wire"]!.DeepClone() };
            using JsonDocument fixture = JsonDocument.Parse(transport.ToJsonString());
            Assert.IsTrue(validator.Accepts(fixture.RootElement), "Actual wire violates the producer contract.");
        }
    }
}
