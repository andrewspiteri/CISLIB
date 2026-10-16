using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OperationScopeQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("scope-qualification-cases.json", 14).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task EverySharedScopeScenarioRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("scope-qualification-cases.json");
        using var owned = new TemporaryDirectory();
        (JsonObject result, _) = await FixtureProcessChecks.RunAsync(["scope-qualification", input, id], owned.Path, TestContext, input,
            new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_SCOPE_TOKEN"] = "CIS_NEVER_CAPTURE_SCOPE"
            }, TimeSpan.FromSeconds(30));
        Assert.AreEqual(id, (string?)result["caseId"]);
        Assert.IsFalse(result["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.IsFalse(result.ToJsonString().Contains("CIS_NEVER_CAPTURE_SCOPE", StringComparison.Ordinal));
        Assert.IsFalse(result.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        JsonObject catalog = LogValueProcessTests.Read("scope-qualification-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => (string?)row!["id"] == id)!.AsObject();
        result.Remove("observed");
        OperationScopeQualificationExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), result);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
}
