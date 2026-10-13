using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushResultProcessTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("filtered")]
    [DataRow("closed")]
    [DataRow("physical")]
    [DataRow("write-timeout")]
    [DataRow("flush-timeout")]
    [DataRow("cancel")]
    [DataRow("pre-cancel")]
    [DataRow("loss")]
    [DataRow("write-failure")]
    [DataRow("flush-failure")]
    [DataRow("later-failure")]
    [DataRow("exhaustion")]
    [DataRow("clock")]
    [DataRow("dual")]
    [DataRow("long-timeout")]
    [DataRow("system-timeout")]
    public async Task ActualPublicFlushRunsInAnExactRuntimeChild(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "flush-results", mode, root.Path], root.Path, TimeSpan.FromSeconds(60), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", report["runtime"]!.GetValue<string>());
        FlushResultExpectations.Validate(mode, report);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
