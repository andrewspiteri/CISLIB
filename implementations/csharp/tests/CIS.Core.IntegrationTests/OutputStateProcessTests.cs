using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OutputStateProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("output-state-cases.json", 12);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task OutputStatesRunInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("output-state-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("output-states", "output-state-cases.json", id, TestContext, 35);
        OutputStateExpectations.Validate(row, actual);
    }
}
