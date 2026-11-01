using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ExceptionQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    // The complete 68-row descriptor/bounds matrix runs directly against Core in
    // ExceptionQualificationTests (including all three cultures). These children
    // retain the distinct exact-runtime/JSON transport boundary: ordered trees,
    // native malformed text failure and Unicode/control escaping.
    public static IEnumerable<object[]> Rows => new[]
    {
        "EXCEPTION-AGGREGATE-ORDER", "EXCEPTION-MESSAGE-UTF16-RETAINED-HIGH", "EXCEPTION-MESSAGE-CONTROLS"
    }.Select(id => new object[] { id });

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
