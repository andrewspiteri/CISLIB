using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LoggerDecisionProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("logger-decision-cases.json", 67, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task LoggerDecisionOrderRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("logger-decision-cases.json");
        JsonObject actual = await LoggerFoundationProcessTests.Capture("logger-decision", input, id, TestContext);
        JsonObject row = LogValueProcessTests.Read("logger-decision-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        LoggerDecisionExpectations.Validate(row, actual);
    }
}
