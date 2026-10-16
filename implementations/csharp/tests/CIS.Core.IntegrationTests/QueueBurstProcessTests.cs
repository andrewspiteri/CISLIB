using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class QueueBurstProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("queue-burst-cases.json", 15);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task BoundedProducerCohortsRunInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("queue-burst-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("queued-fanout", "queue-burst-cases.json", id, TestContext, 35);
        QueueBurstExpectations.Validate(row, actual);
    }
}
