using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class RenderingStoryProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("rendering-story-cases.json", 21, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task CombinedRenderingRunsAgainstRealCoreInExactRuntimeChildren(string id)
    {
        string input = FixtureProcessChecks.Find("rendering-story-cases.json");
        JsonObject actual = await JsonlReuseProcessTests.Capture("rendering-story", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("rendering-story-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        RenderingStoryExpectations.Validate(catalog, row, actual);
    }
}
