using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FlushBoundaryProcessTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("both")]
    [DataRow("saturated")]
    [DataRow("faulted")]
    [DataRow("closed")]
    [DataRow("concurrent")]
    [DataRow("physical")]
    public async Task RegisteredBoundaryFixtureRunsActualCoreInASeparateExactRuntime(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "flush-boundary", mode, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", report["runtime"]!.GetValue<string>());
        FlushBoundaryExpectations.Validate(mode, report);
        if (mode == "physical")
        {
            Assert.IsTrue(report["leaseReleased"]!.GetValue<bool>());
            CollectionAssert.AreEqual(Convert.FromBase64String(report["bytes"]!.GetValue<string>()), File.ReadAllBytes(report["path"]!.GetValue<string>()));
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
