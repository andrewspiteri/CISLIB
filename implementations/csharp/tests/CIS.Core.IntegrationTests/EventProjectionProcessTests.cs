using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventProjectionProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("event-projection-cases.json", 32).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SharedProjectionRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("event-projection-cases.json");
        JsonObject actual = await Capture("event-projection", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("event-projection-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(row => (string?)row!["id"] == id)!.AsObject();
        actual.Remove("observed");
        EventProjectionExpectations.Validate(row, catalog["nativeBindings"]!.AsObject(), actual);
    }

    internal static async Task<JsonObject> Capture(string command, string input, string id, TestContext context)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync([command, input, id], owned.Path, context, input,
            new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_PROJECTION_TOKEN"] = "CIS_NEVER_CAPTURE_PROJECTION"
            }, TimeSpan.FromSeconds(30));
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        VerifyPrivacy(actual);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
    [TestMethod]
    [DataRow("CIS_NEVER_CAPTURE_COLLECTOR")]
    [DataRow("CIS_NEVER_CAPTURE_PROJECTION")]
    [DataRow("CIS_NEVER_CAPTURE_ACTIVITY")]
    public void PrivateAmbientTokensCannotAppearInTheReport(string sentinel)
    {
        var report = new JsonObject { ["attributes"] = new JsonObject { ["value"] = "safe" } };
        VerifyPrivacy(report);
        report["attributes"]!["value"] = sentinel;
        StringAssert.Contains(Assert.ThrowsExactly<AssertFailedException>(() => VerifyPrivacy(report)).Message, "Private sentinel appeared");
    }

    private static void VerifyPrivacy(JsonObject actual) =>
        FixtureProcessChecks.ExcludeStrings(actual, "CIS_NEVER_CAPTURE_COLLECTOR", "CIS_NEVER_CAPTURE_PROJECTION", "CIS_NEVER_CAPTURE_ACTIVITY");
}
