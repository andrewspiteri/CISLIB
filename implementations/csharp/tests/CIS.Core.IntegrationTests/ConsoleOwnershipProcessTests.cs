using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleOwnershipProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => ConsoleProcessChecks.Cases("ownership", 22);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task BorrowedConsoleConsumersPreserveApplicationStreamsAndTicketOrder(string id)
    {
        JsonObject catalog = LogValueProcessTests.Read("console-ownership-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await ConsoleProcessChecks.Capture("ownership", row, TestContext);
        ConsoleOwnershipExpectations.Validate(catalog, row, actual, physicalBytes: true);
    }
}
