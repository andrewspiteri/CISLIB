using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class JsonlReuseProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => FixtureProcessChecks.Cases("jsonl-reuse-cases.json", 20, declaresCount: false).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task JsonlReuseTableRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string input = FixtureProcessChecks.Find("jsonl-reuse-cases.json");
        JsonObject actual = await Capture("jsonl-reuse", input, id, TestContext);
        JsonObject catalog = LogValueProcessTests.Read("jsonl-reuse-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonlReuseExpectations.Validate(catalog, row, actual);
    }

    internal static async Task<JsonObject> Capture(string command, string input, string id, TestContext context)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync([command, input, id], owned.Path, context, input,
            new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR", ["TERM"] = "dumb", ["NO_COLOR"] = "1" },
            TimeSpan.FromSeconds(30));
        Assert.AreEqual(id, (string?)actual["caseId"]);
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return actual;
    }
}
