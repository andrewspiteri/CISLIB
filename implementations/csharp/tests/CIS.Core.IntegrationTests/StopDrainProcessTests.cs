using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StopDrainProcessTests
{
    public static IEnumerable<object[]> Modes => StopDrainExpectations.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualSharedStopRunsInAnExactRuntimeChild(string mode)
    {
        using var root = new TemporaryDirectory();
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CORE_", StringComparison.Ordinal)).ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "stop-drain", mode, root.Path],
            root.Path, TimeSpan.FromSeconds(60), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", report["os"]!.GetValue<string>());
        StopDrainExpectations.Validate(mode, report);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
