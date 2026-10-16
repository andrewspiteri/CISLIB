using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LoggerOutcomeProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("logger-outcome-cases.json", 125, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task LoggerOutcomeTableRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("logger-outcome-cases.json");
        JsonObject actual = await LoggerFoundationProcessTests.Capture("logger-outcome", input, id, TestContext);
        JsonObject row = LogValueProcessTests.Read("logger-outcome-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        LoggerOutcomeExpectations.Validate(row, actual);
    }
}
