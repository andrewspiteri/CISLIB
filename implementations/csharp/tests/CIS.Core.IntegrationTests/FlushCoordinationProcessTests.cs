using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushCoordinationProcessTests
{
    [TestMethod]
    [DataRow("write-cancel")]
    [DataRow("write-deadline")]
    [DataRow("flush-cancel")]
    [DataRow("flush-deadline")]
    [DataRow("cohort")]
    [DataRow("dual")]
    [DataRow("pre-cancel")]
    [DataRow("fault")]
    [DataRow("complete-race")]
    public async Task ActualCallerDetachmentRunsInAnExactRuntimeChild(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "flush-coordination", mode, root.Path], root.Path, TimeSpan.FromSeconds(60), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", report["runtime"]!.GetValue<string>());
        FlushCoordinationExpectations.Validate(mode, report);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
