using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OperationScopeMergeProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("precedence")]
    [DataRow("candidates")]
    [DataRow("inherited-loss")]
    [DataRow("depth")]
    public async Task NativeMergedScopesRunInAnIsolatedExactRuntimeChild(string mode)
    {
        Validate(mode, await Capture(mode));
    }

    [TestMethod]
    [DataRow("depth")]
    [DataRow("precedence")]
    [DataRow("inherited-loss")]
    public async Task ScopeReportCorruptionsAreRejectedAfterTheOriginalPhysicalReportPasses(string mode)
    {
        JsonObject actual = await Capture(mode);
        Validate(mode, actual);
        switch (mode)
        {
            case "depth": actual["scopeDepth"] = true; break;
            case "precedence": actual["caseKeys"] = new JsonArray("Name"); break;
            case "inherited-loss": actual["truncated"] = false; break;
            default: throw new InvalidDataException("Unknown scope control.");
        }
        Assert.ThrowsExactly<AssertFailedException>(() => Validate(mode, actual));
    }

    private async Task<JsonObject> Capture(string mode)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["scope-merge", mode], owned.Path, TestContext,
            environment: new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_SCOPE_TOKEN"] = "CIS_NEVER_CAPTURE_SCOPE"
            }, timeout: TimeSpan.FromSeconds(20));
        Assert.IsFalse(actual["observed"]!["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_SCOPE", StringComparison.Ordinal));
        Assert.IsFalse(actual.ToJsonString().Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        actual.Remove("observed");
        return actual;
    }

    private static void Validate(string mode, JsonObject actual)
    {
        string expected = mode switch
        {
            "precedence" => """{"mode":"precedence","operationId":"inner","shared":"inner","caseKeys":["Name","name"],"ordinaryOperationAttribute":"attribute","scopeDepth":2,"nodes":5,"truncated":false,"retainedAfterDispose":true,"storeEmpty":true}""",
            "candidates" => """{"mode":"candidates","operationId":"inner","first":"last-inner","lastKept":1022,"newKeyOmitted":true,"scopeInputsTruncated":false,"count":1023,"nodes":1024,"scopeDepth":2,"truncated":true}""",
            "inherited-loss" => """{"mode":"inherited-loss","operationId":"outer","kind":"Null","childTruncated":false,"count":1,"nodes":2,"scopeDepth":2,"truncated":true}""",
            "depth" => """{"mode":"depth","operationId":"d31","scopeDepth":32,"nodes":33,"truncated":false,"rejection":"InvalidOperationException","traversals":0,"unchanged":true,"storeEmpty":true}""",
            _ => throw new InvalidDataException("Unknown scenario.")
        };
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), "Scope observation differs: " + mode);
    }
}
