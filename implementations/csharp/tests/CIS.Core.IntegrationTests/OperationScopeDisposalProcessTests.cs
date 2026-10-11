using System.Security.Cryptography;
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
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "scope-disposal", mode],
            owned.Path, TimeSpan.FromSeconds(20), new Dictionary<string, string?>
            {
                ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read",
                ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR",
                ["CIS_PRIVATE_SCOPE_TOKEN"] = "CIS_NEVER_CAPTURE_SCOPE"
            });
        Assert.AreEqual(0, process.ExitCode, process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        JsonObject observed = actual["observed"]!.AsObject();
        Assert.IsFalse(observed["publicFacadeInitialized"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        actual.Remove("observed");
        string expected = mode switch
        {
            "lifo" => """{"mode":"lifo","error":"InvalidOperationException","message":"Dispose operation scopes in LIFO order.","headUnchanged":true,"restoredIds":["middle","outer",null],"storeEmpty":true}""",
            "repeat" => """{"mode":"repeat","sameValueReplacementUntouched":true,"ownerRepeatsHarmless":true,"newOperationUntouched":true,"storeEmpty":true}""",
            "await" => """{"mode":"await","ownerFramePreserved":true,"childInherited":true,"childOwnPopRestoredParent":true,"parentAfterChildUnchanged":true,"ownerRepeatsHarmless":true,"storeEmpty":true}""",
            "absent" => """{"mode":"absent","error":"InvalidOperationException","message":"The operation scope is not active in the current asynchronous context.","foreignHeadUnchanged":true,"creatorHeadUnchanged":true,"absentGuardStillRejects":true,"storeEmpty":true}""",
            _ => throw new InvalidDataException("Unknown scenario.")
        };
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), actual));
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_SCOPE", StringComparison.Ordinal));
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
