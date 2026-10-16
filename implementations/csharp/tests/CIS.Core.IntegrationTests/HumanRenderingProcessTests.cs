using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class HumanRenderingProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("human-rendering-cases.json", 12, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task HumanRenderingRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("human-rendering-cases.json");
        JsonObject actual = await JsonlReuseProcessTests.Capture("human-rendering", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("human-rendering-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        HumanRenderingExpectations.Validate(row, actual);
    }
}
