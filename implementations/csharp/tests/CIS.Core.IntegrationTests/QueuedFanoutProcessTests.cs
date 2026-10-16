using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class QueuedFanoutProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("queued-fanout-cases.json", 12);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualTicketedRuntimeFanoutRunsInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("queued-fanout-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("queued-fanout", "queued-fanout-cases.json", id, TestContext, 35);
        QueuedFanoutExpectations.Validate(row, actual);
    }
}
