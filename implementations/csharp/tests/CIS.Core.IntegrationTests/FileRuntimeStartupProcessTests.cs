using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRuntimeStartupProcessTests
{
    public static IEnumerable<object[]> Modes => FileRuntimeStartupExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ConfiguredFileAndConsoleStartupInFreshConsumer(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-runtime-startup", mode, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, process.ExitCode, process.StandardError); JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        FileRuntimeStartupExpectations.Validate(mode, actual, mode == "physical-console" ? Encoding.UTF8.GetBytes(process.StandardError) : null);
        if (mode != "physical-console") { Assert.AreEqual("", process.StandardError); }
        Assert.AreEqual(".NET 10.0.12", actual["observed"]!["runtime"]!.GetValue<string>()); Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["observed"]!["os"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), actual["observed"]!["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["observed"]!["coreSha256"]!.GetValue<string>());
        string path = Path.Combine(root.Path, "logs", "\u03a9-\U0001f642.jsonl");
        if (File.Exists(path)) { CollectionAssert.AreEqual(File.ReadAllBytes(path), Convert.FromBase64String(actual["fileBytes"]!.GetValue<string>())); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
