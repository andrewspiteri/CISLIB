using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class JsonlRecoveryProcessTests
{
    [TestMethod]
    [DataRow("repeat")]
    [DataRow("with-retained")]
    [DataRow("collision-file")]
    [DataRow("collision-directory")]
    [DataRow("retention-full")]
    [DataRow("identity-exhausted")]
    public async Task ManagedJsonlRecoveryPreservesDamageAndRejectsUnsafeTransitions(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"); ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "jsonl-boundary", mode, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError); JsonObject actual = JsonNode.Parse(child.StandardOutput)!.AsObject(); JsonlBoundaryExpectations.Validate(mode, actual); Assert.AreEqual(".NET 10.0.12", actual["runtime"]!.GetValue<string>()); Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["os"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), actual["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["coreSha256"]!.GetValue<string>()); CollectionAssert.AreEqual(Convert.FromBase64String(actual["after"]!.GetValue<string>()), File.ReadAllBytes(actual["path"]!.GetValue<string>()));
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
