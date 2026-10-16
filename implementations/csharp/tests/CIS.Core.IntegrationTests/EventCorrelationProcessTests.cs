using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventCorrelationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("event-correlation-cases.json", 14).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SharedCorrelationRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("event-correlation-cases.json");
        JsonObject actual = await EventProjectionProcessTests.Capture("event-correlation", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("event-correlation-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => (string?)row!["id"] == id)!.AsObject();
        actual.Remove("observed");
        EventCorrelationExpectations.Validate(row, actual);
    }
}
