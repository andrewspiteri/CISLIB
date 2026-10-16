using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class EventEncodingFixtureTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void SharedCasesExecuteTheActualCaptureAndEncoder(string id)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        EventEncodingExpectations.Validate(row, EventEncodingFixture.Execute(row));
    }
    [TestMethod]
    [DataRow("build", "complete")]
    [DataRow("span", "complete")]
    [DataRow("map-order", "complete")]
    [DataRow("array-tail", "complete")]
    [DataRow("prior-text", "prior")]
    public void MigratedEncodingChecksRejectChangedProtectedValuesAndWireShape(string fault, string mode)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => (string?)item!["input"]!["mode"] == mode)!.AsObject();
        JsonObject actual = EventEncodingFixture.Execute(row);
        EventEncodingExpectations.Validate(row, actual);
        JsonObject record = JsonNode.Parse((string)actual["wire"]!)!.AsObject();
        switch (fault)
        {
            case "build": record["resource"]!["build_version"] = "changed"; break;
            case "span": record["span_id"] = new string('g', 16); break;
            case "map-order":
                JsonObject map = record["attributes"]!["map"]!.AsObject();
                record["attributes"]!["map"] = new JsonObject(map.Reverse().Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
                break;
            case "array-tail": record["attributes"]!["array"]!.AsArray().Add(false); break;
            case "prior-text": record["message"] = new string('y', 8192); break;
            default: throw new InvalidDataException("Unknown encoding control.");
        }
        string wire = record.ToJsonString() + "\n";
        actual["wire"] = wire;
        actual["utf8Bytes"] = System.Text.Encoding.UTF8.GetByteCount(wire);
        Assert.ThrowsExactly<InvalidOperationException>(() => EventEncodingExpectations.Validate(row, actual));
    }

    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "event-encoding-cases.json")))!.AsObject();
}
