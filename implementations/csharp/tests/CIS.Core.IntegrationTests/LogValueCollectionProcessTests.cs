using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueCollectionProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("value-collection-cases.json")["cases"]!.AsArray().Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualBoundedCollectionVectorsExecuteInFreshProcesses(string id)
    {
        string input = LogValueProcessTests.Find("value-collection-cases.json");
        JsonObject row = LogValueProcessTests.Read("value-collection-cases.json")["cases"]!.AsArray().Single(value => (string?)value!["id"] == id)!.AsObject();
        JsonObject actual = await LogValueProcessTests.Run("log-collection", input, id, TestContext);
        foreach ((string key, JsonNode? expected) in row["expected"]!.AsObject()) { Assert.IsTrue(JsonNode.DeepEquals(expected, actual[key]), key); }
        Assert.IsTrue(actual["metadataMatchesMeasuredShape"]!.GetValue<bool>());
        (int nodes, int depth) = Measure(actual["json"]);
        Assert.AreEqual(nodes, actual["nodes"]!.GetValue<int>(), "Projected JSON node count differs from metadata.");
        Assert.AreEqual(depth, actual["depth"]!.GetValue<int>(), "Projected JSON depth differs from metadata.");
        Assert.IsTrue(nodes <= 1024 && depth <= 8);
    }

    // Count the emitted JSON independently of the fixture's traversal of Core values.
    private static (int Nodes, int Depth) Measure(JsonNode? node, int depth = 1)
    {
        IEnumerable<JsonNode?> children = node switch { JsonObject map => map.Select(pair => pair.Value), JsonArray array => array, _ => [] };
        int nodes = 1, maximum = depth;
        foreach (JsonNode? child in children)
        {
            (int childNodes, int childDepth) = Measure(child, depth + 1);
            nodes += childNodes;
            maximum = Math.Max(maximum, childDepth);
        }
        return (nodes, maximum);
    }
}
