using System.Security.Cryptography;
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
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "scope-merge", mode],
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
        Assert.IsFalse(observed["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        actual.Remove("observed");
        string expected = mode switch
        {
            "precedence" => """{"mode":"precedence","operationId":"inner","shared":"inner","caseKeys":["Name","name"],"ordinaryOperationAttribute":"attribute","scopeDepth":2,"nodes":5,"truncated":false,"retainedAfterDispose":true,"storeEmpty":true}""",
            "candidates" => """{"mode":"candidates","operationId":"inner","first":"last-inner","lastKept":1022,"newKeyOmitted":true,"scopeInputsTruncated":false,"count":1023,"nodes":1024,"scopeDepth":2,"truncated":true}""",
            "inherited-loss" => """{"mode":"inherited-loss","operationId":"outer","kind":"Null","childTruncated":false,"count":1,"nodes":2,"scopeDepth":2,"truncated":true}""",
            "depth" => """{"mode":"depth","operationId":"d31","scopeDepth":32,"nodes":33,"truncated":false,"rejection":"InvalidOperationException","traversals":0,"unchanged":true,"storeEmpty":true}""",
            _ => throw new InvalidDataException("Unknown scenario.")
        };
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(expected), actual));
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_SCOPE", StringComparison.Ordinal));
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
