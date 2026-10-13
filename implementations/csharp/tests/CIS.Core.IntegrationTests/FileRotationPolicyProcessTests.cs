using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRotationPolicyProcessTests
{
    [TestMethod]
    [DataRow("collision-jsonl-file")]
    [DataRow("collision-human-file")]
    [DataRow("collision-jsonl-directory")]
    [DataRow("collision-human-directory")]
    [DataRow("metadata-jsonl-directory")]
    [DataRow("metadata-human-directory")]
    [DataRow("write-jsonl-failure")]
    [DataRow("write-human-failure")]
    public async Task RealConsumersExposeTerminalPolicyFailureAndHealthyOutput(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"); ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-rotation-policy", mode, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual("", process.StandardError); JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject(); FileRotationPolicyExpectations.Validate(mode, actual);
        Assert.AreEqual(".NET 10.0.12", actual["runtime"]!.GetValue<string>()); Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["os"]!.GetValue<string>()); Assert.AreEqual(Hash(host), actual["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["coreSha256"]!.GetValue<string>());
        foreach ((string name, JsonNode? bytes) in actual["after"]!.AsObject()) { CollectionAssert.AreEqual(Convert.FromBase64String(bytes!.GetValue<string>()), File.ReadAllBytes(Path.Combine(root.Path, name))); }

    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
