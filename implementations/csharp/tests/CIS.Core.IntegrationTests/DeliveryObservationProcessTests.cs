using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class DeliveryObservationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => QueueProcessChecks.Cases("delivery-observation-cases.json", 11);

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task DeliveryBoundariesRunInExactRuntimeChildren(string id)
    {
        JsonObject row = LogValueProcessTests.Read("delivery-observation-cases.json")["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = await QueueProcessChecks.Capture("delivery-observation", "delivery-observation-cases.json", id, TestContext, 35);
        DeliveryObservationExpectations.Validate(row, actual);
    }
}
