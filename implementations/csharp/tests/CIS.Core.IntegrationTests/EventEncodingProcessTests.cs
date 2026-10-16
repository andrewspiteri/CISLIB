using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventEncodingProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("event-encoding-cases.json", 8).Select(row => new object[] { (string)row!["id"]! });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task JsonEncodingRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("event-encoding-cases.json");
        JsonObject actual = await EventProjectionProcessTests.Capture("event-encoding", input, id, TestContext);
        JsonObject row = LogValueProcessTests.Read("event-encoding-cases.json")["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        EventEncodingExpectations.Validate(row, actual);
    }
}
