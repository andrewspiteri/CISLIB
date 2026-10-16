using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OperationScopeDisposalProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("lifo")]
    [DataRow("repeat")]
    [DataRow("await")]
    [DataRow("absent")]
    public async Task NativeScopeDisposalRunsInAnIsolatedExactRuntimeChild(string mode)
    {
        Validate(mode, await Capture(mode));
    }

    [TestMethod]
    [DataRow("lifo")]
    [DataRow("repeat")]
    [DataRow("absent")]
    public async Task ScopeReportCorruptionsAreRejectedAfterTheOriginalPhysicalReportPasses(string mode)
    {
        JsonObject actual = await Capture(mode);
        Validate(mode, actual);
        switch (mode)
        {
            case "lifo": actual["restoredIds"] = new JsonArray("outer", "middle", null); break;
            case "repeat": actual["ownerRepeatsHarmless"] = 1; break;
            case "absent": actual["message"] = "creating logical flow"; break;
            default: throw new InvalidDataException("Unknown scope control.");
        }
        Assert.ThrowsExactly<AssertFailedException>(() => Validate(mode, actual));
    }

    private async Task<JsonObject> Capture(string mode)
    {
        using var owned = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["scope-disposal", mode], owned.Path, TestContext,
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
            "lifo" => """{"mode":"lifo","error":"InvalidOperationException","message":"Dispose operation scopes in LIFO order.","headUnchanged":true,"restoredIds":["middle","outer",null],"storeEmpty":true}""",
            "repeat" => """{"mode":"repeat","sameValueReplacementUntouched":true,"ownerRepeatsHarmless":true,"newOperationUntouched":true,"storeEmpty":true}""",
            "await" => """{"mode":"await","ownerFramePreserved":true,"childInherited":true,"childOwnPopRestoredParent":true,"parentAfterChildUnchanged":true,"ownerRepeatsHarmless":true,"storeEmpty":true}""",
            "absent" => """{"mode":"absent","error":"InvalidOperationException","message":"The operation scope is not active in the current asynchronous context.","foreignHeadUnchanged":true,"creatorHeadUnchanged":true,"absentGuardStillRejects":true,"storeEmpty":true}""",
            _ => throw new InvalidDataException("Unknown scenario.")
        };
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), "Scope observation differs: " + mode);
    }
}
