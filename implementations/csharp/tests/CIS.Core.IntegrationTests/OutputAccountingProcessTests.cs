using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OutputAccountingProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("output-accounting-cases.json", 11);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task AccountingCheckpointsRunInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("output-accounting-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("output-accounting", "output-accounting-cases.json", id, TestContext, 35);
        OutputAccountingExpectations.Validate(row, actual);
    }
}
