using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ExceptionQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("exception-qualification-cases.json", 68).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task EverySharedExceptionDescriptorRunsInAnIsolatedChild(string id)
    {
        string input = FixtureProcessChecks.Find("exception-qualification-cases.json");
        JsonObject actual = await ExceptionProjectionProcessTests.Capture("exception-qualification", id, TestContext, input, "CIS_PRIVATE_QUALIFICATION_DATA");
        JsonObject catalog = LogValueProcessTests.Read("exception-qualification-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => (string?)row!["id"] == id)!.AsObject();
        ExceptionQualificationExpectations.Validate(row, actual, catalog["adapterObservations"]!["csharp"]!.AsObject());
        ExceptionBoundaryProcessTests.CheckShape(actual, requirePreorder: false);
    }
}
