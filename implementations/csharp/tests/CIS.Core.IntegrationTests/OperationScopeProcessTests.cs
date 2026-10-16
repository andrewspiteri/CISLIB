using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OperationScopeProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("copy")]
    [DataRow("inherit")]
    [DataRow("isolated")]
    public async Task NativeScopeFoundationRunsInAnIsolatedExactRuntimeChild(string mode)
    {
        Validate(mode, await Capture(mode));
    }

    [TestMethod]
    [DataRow("copy")]
    [DataRow("inherit")]
    [DataRow("isolated")]
    public async Task ScopeReportCorruptionsAreRejectedAfterTheOriginalPhysicalReportPasses(string mode)
    {
        JsonObject actual = await Capture(mode);
        Validate(mode, actual);
        switch (mode)
        {
            case "copy": actual["nodes"] = true; break;
            case "inherit": actual["childId"] = "parent"; break;
            case "isolated": actual["second"] = new JsonArray("a", "b"); break;
            default: throw new InvalidDataException("Unknown scope control.");
        }
        Assert.ThrowsExactly<AssertFailedException>(() => Validate(mode, actual));
    }

    private async Task<JsonObject> Capture(string mode)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["scope-foundation", mode], owned.Path, TestContext,
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
            "copy" => """{"mode":"copy","operationId":"original","value":"original","nodes":2,"depth":2,"truncated":false,"cleared":true}""",
            "inherit" => """{"mode":"inherit","childInherited":true,"childId":"child","restored":true,"parentUnchanged":true,"cleared":true}""",
            "isolated" => """{"mode":"isolated","first":["a","b"],"second":["a-other","b-other"],"parentEmpty":true,"afterEmpty":true}""",
            _ => throw new InvalidDataException("Unknown scenario.")
        };
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), "Scope observation differs: " + mode);
    }
}
