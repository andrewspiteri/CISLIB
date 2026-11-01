using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class QueueBurstTests
{
    private static readonly Lazy<LoggingFixtureInputs> Inputs = new(() => new("queue-burst-cases.json"));

    public static IEnumerable<object[]> Rows => Inputs.Value.Ids.Select(id => new object[] { id });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ProducerCohortsPreserveBoundedWorkersTicketsAndThresholds(string id)
    { JsonObject row = Row(id); QueueBurstExpectations.Validate(row, await QueuedFanoutFixture.ExecuteAsync(row)); }
    [TestMethod]
    [DataRow("pending")]
    [DataRow("parallel")]
    [DataRow("worker")]
    [DataRow("clock")]
    [DataRow("outcome")]
    [DataRow("ticket")]
    [DataRow("gap")]
    [DataRow("order")]
    [DataRow("eviction")]
    public async Task BurstMatcherRejectsUnboundedLostOrInventedWork(string fault)
    {
        JsonObject row = Row("BURST-BOTH-1"), actual = await QueuedFanoutFixture.ExecuteAsync(row);
        QueueBurstExpectations.Validate(row, actual);
        JsonNode binding = actual["bindings"]![0]!, result = actual["semantic"]!["results"]![0]!;
        switch (fault)
        {
            case "pending": actual["bounds"]!["console"]!["pendingMaximum"] = 2; break;
            case "parallel": actual["bounds"]!["console"]!["maximumActive"] = 2; break;
            case "worker": actual["workers"] = 3; break;
            case "clock": actual["semantic"]!["clockCalls"] = 66; break;
            case "outcome": binding["outcome"] = "Filtered"; result["outcome"] = "Filtered"; break;
            case "ticket": binding["outputs"]![0]!["ticket"] = null; result["outputs"]![0]!["ticket"] = null; break;
            case "gap": binding["outputs"]![0]!["ticket"] = 3L; result["outputs"]![0]!["ticket"] = 3L; break;
            case "order": actual["semantic"]!["outputs"]!["console"]!["writtenTickets"] = new JsonArray(2L, 1L); break;
            default:
                int rejected = actual["bindings"]!.AsArray().First(item => item!["outputs"]![0]!["status"]!.GetValue<string>() == "QueueFull")!["caller"]!.GetValue<int>();
                JsonNode wire = actual["wires"]!["console"]!, item = wire["attempted"]![0]!; JsonNode body = JsonNode.Parse(Convert.FromBase64String(item["wire"]!.GetValue<string>()))!;
                body["message"] = "record-" + rejected; body["attributes"]!["input"] = rejected; string raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(body.ToJsonString() + "\n")); item["wire"] = raw; item["after"] = raw;
                wire["stream"] = Convert.ToBase64String(wire["attempted"]!.AsArray().SelectMany(record => Convert.FromBase64String(record!["wire"]!.GetValue<string>())).ToArray()); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => QueueBurstExpectations.Validate(row, actual));
    }
    [TestMethod]
    public void CatalogCoversCapacitiesOutputCombinationsStagedThresholdsAndNoInterest()
    {
        JsonArray rows = Catalog()["cases"]!.AsArray(); Assert.AreEqual(15, rows.Count); Assert.AreEqual(15, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        foreach (int capacity in new[] { 1, 2, 8 }) { Assert.AreEqual(3, rows.Count(row => row!["capacity"]!.GetValue<int>() == capacity && row["id"]!.GetValue<string>().EndsWith("-" + capacity, StringComparison.Ordinal))); }
        foreach (string id in new[] { "BURST-FILE-THEN-CONSOLE", "BURST-CONSOLE-THEN-FILE", "BURST-DISABLED", "BURST-NONE", "BURST-FILE-THRESHOLD", "BURST-CONSOLE-THRESHOLD" }) { Assert.IsTrue(rows.Any(row => row!["id"]!.GetValue<string>() == id)); }
    }
    private static JsonObject Catalog() => Inputs.Value.Catalog();
    private static JsonObject Row(string id) => Inputs.Value.Row(id);
}
