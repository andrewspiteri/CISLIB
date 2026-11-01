using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.ArchitectureTests.Contracts;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class EventQualificationTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task CompleteEventCasesUseRealCaptureAndEncoding(string id)
    {
        JsonObject catalog = Catalog();
        JsonObject row = Find(catalog, id);
        JsonObject actual = await EventQualificationFixture.ExecuteAsync(row);
        EventQualificationExpectations.Validate(catalog, row, actual);
        ValidateSchema(actual, row["input"]!["maxEntryBytes"]!.GetValue<int>());
    }

    private static void ValidateSchema(JsonObject actual, int cap)
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "../.."));
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

    [TestMethod]
    [DataRow("severity")]
    [DataRow("null")]
    [DataRow("resource")]
    [DataRow("correlation")]
    [DataRow("optional")]
    [DataRow("loss")]
    public async Task ComparatorRejectsIndependentlyCorruptedWire(string fault)
    {
        JsonObject catalog = Catalog(), row = Find(catalog, "EVENT-QUAL-INFORMATION");
        JsonObject actual = await EventQualificationFixture.ExecuteAsync(row);
        JsonObject item = actual["records"]![0]!.AsObject();
        JsonObject record = JsonNode.Parse(item["wire"]!.GetValue<string>())!.AsObject();
        switch (fault)
        {
            case "severity": record["status"] = "debug"; break;
            case "null": record["attributes"]!["null"] = "null"; break;
            case "resource": record["resource"]!.AsObject().Remove("instance_id"); break;
            case "correlation": record["span_id"] = "2222222222222222"; break;
            case "optional": record["operation_id"] = null; break;
            case "loss": record["truncated"] = false; break;
        }
        string wire = record.ToJsonString() + "\n";
        item["wire"] = wire; item["utf8Bytes"] = Encoding.UTF8.GetByteCount(wire);
        Assert.ThrowsExactly<InvalidDataException>(() => EventQualificationExpectations.Validate(catalog, row, actual));
    }

    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "event-qualification-cases.json")))!.AsObject();
    private static JsonObject Find(JsonObject catalog, string id) => catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
