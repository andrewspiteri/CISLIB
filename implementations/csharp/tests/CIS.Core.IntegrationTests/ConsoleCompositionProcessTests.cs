using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleCompositionProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => ConsoleProcessChecks.Cases("composition", 41);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ConfiguredConsoleRunsInExactRuntimeChildrenWithBothStreamsDrained(string id)
    {
        JsonObject catalog = LogValueProcessTests.Read("console-composition-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await ConsoleProcessChecks.Capture("composition", row, TestContext);
        ConsoleCompositionExpectations.Validate(catalog, row, actual, physicalBytes: true);
    }
}
