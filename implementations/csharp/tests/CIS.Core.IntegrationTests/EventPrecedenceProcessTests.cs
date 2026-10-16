using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventPrecedenceProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("event-precedence-cases.json", 28).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SharedPrecedenceRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("event-precedence-cases.json");
        JsonObject actual = await EventProjectionProcessTests.Capture("event-precedence", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("event-precedence-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => (string?)row!["id"] == id)!.AsObject();
        actual.Remove("observed");
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
    }
}
