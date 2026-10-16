using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleStartupProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => ConsoleProcessChecks.Cases("startup", 36);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task FailedStartupConsumersJoinWorkersAndKeepApplicationStreams(string id)
    {
        JsonObject catalog = LogValueProcessTests.Read("console-startup-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await ConsoleProcessChecks.Capture("startup", row, TestContext);
        ConsoleStartupExpectations.Validate(row, actual, physicalBytes: true);
    }
}
