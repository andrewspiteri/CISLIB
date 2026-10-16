using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class QueueOverflowProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("queue-overflow-cases.json", 12);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task IncomingQueueOverflowRunsInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("queue-overflow-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("queued-fanout", "queue-overflow-cases.json", id, TestContext, 35);
        QueuedFanoutExpectations.Validate(row, actual);
    }
}
