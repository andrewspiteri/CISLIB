using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OutputQueueProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("output-queue-cases.json", 11);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualBoundedWorkersRunInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("output-queue-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("output-queue", "output-queue-cases.json", id, TestContext, 30);
        OutputQueueExpectations.Validate(row, actual);
    }
}
