using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggingFixtureInputsTests
{
    private static readonly string[] Catalogs =
    [
        "queue-burst-cases.json", "delivery-observation-cases.json", "output-state-cases.json",
        "output-accounting-cases.json", "output-queue-cases.json", "queued-fanout-cases.json",
        "queue-overflow-cases.json", "human-rendering-cases.json", "presentation-cases.json"
    ];

    [TestMethod]
    public void ProjectionPreservesEveryOriginalRowFactAndItsOrder()
    {
        foreach (string name in Catalogs)
        {
            JsonObject original = QualificationFixtureInputs.ReadLogging(name);
            var inputs = new LoggingFixtureInputs(name);
            JsonObject[] rows = original["cases"]!.AsArray().Select(row => row!.AsObject()).ToArray();
            CollectionAssert.AreEqual(rows.Select(row => row["id"]!.GetValue<string>()).ToArray(), inputs.Ids.ToArray());
            Assert.IsTrue(JsonNode.DeepEquals(original, inputs.Catalog()));
            foreach (JsonObject row in rows)
            {
                Assert.IsTrue(JsonNode.DeepEquals(row, inputs.Row(row["id"]!.GetValue<string>())));
            }
        }
    }

    [TestMethod]
    public void EachReadHasPrivateMutableCatalogAndRowNodes()
    {
        var inputs = new LoggingFixtureInputs("queue-burst-cases.json");
        string id = inputs.Ids[0];
        JsonObject originalRow = inputs.Row(id), originalCatalog = inputs.Catalog();
        JsonObject row = inputs.Row(id), catalog = inputs.Catalog();
        row["id"] = "changed-private-row";
        row["expected"]!.AsObject().Clear();
        catalog["cases"]!.AsArray().Clear();
        Assert.IsTrue(JsonNode.DeepEquals(originalRow, inputs.Row(id)));
        Assert.IsTrue(JsonNode.DeepEquals(originalCatalog, inputs.Catalog()));
        Assert.AreEqual(id, inputs.Ids[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)inputs.Ids)[0] = "changed-id");
    }

    [TestMethod]
    public async Task ParallelReadersKeepEveryPrivateMutationSeparate()
    {
        var inputs = new LoggingFixtureInputs("queue-burst-cases.json");
        string id = inputs.Ids[0];
        string expectedText = inputs.Row(id).ToJsonString();
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            JsonObject expected = JsonNode.Parse(expectedText)!.AsObject();
            JsonObject row = inputs.Row(id);
            Assert.IsTrue(JsonNode.DeepEquals(expected, row));
            row["id"] = index;
            Assert.IsTrue(JsonNode.DeepEquals(expected, inputs.Row(id)));
        })));
    }
}
