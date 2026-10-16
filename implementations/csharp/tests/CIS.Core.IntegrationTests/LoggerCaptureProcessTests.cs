using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LoggerCaptureProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("logger-capture-cases.json", 41, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task LoggerCaptureOrderRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("logger-capture-cases.json");
        JsonObject actual = await LoggerFoundationProcessTests.Capture("logger-capture", input, id, TestContext);
        JsonObject row = LogValueProcessTests.Read("logger-capture-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        LoggerCaptureExpectations.Validate(row, actual);
    }
}
