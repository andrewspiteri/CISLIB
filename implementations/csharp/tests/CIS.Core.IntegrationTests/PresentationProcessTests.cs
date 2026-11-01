using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class PresentationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("presentation-cases.json", 64, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ExplicitPresentationRunsWithRedirectedStreamsInExactRuntimeChildren(string id)
    {
        string input = FixtureProcessChecks.Find("presentation-cases.json");
        JsonObject actual = await LoggerFoundationProcessTests.Capture("presentation", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("presentation-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        PresentationExpectations.Validate(row, actual);
        Assert.IsTrue(actual["outputRedirected"]!.GetValue<bool>() && actual["errorRedirected"]!.GetValue<bool>());
        Assert.IsFalse(actual["debuggerAttached"]!.GetValue<bool>());
    }
}
