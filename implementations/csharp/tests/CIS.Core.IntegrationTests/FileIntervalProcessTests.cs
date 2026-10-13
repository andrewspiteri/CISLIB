using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileIntervalProcessTests
{
    public static IEnumerable<object[]> Modes => FileIntervalExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFileIntervalRunsInAnExactRuntimeChild(string mode)
    {
        using var root = new TemporaryDirectory();
        var environment = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(k => k.StartsWith("CORE_", StringComparison.Ordinal)).ToDictionary(k => k, _ => (string?)null, StringComparer.Ordinal);
        if (mode == "env")
        {
            environment["CORE_LOG_CONSOLE_LEVEL"] = "NONE"; environment["CORE_LOG_FILE_LEVEL"] = "INFO";
            environment["CORE_LOG_FILE_PATH"] = Path.Combine(root.Path, "owned", "events.jsonl");
            environment["CORE_LOG_FLUSH_INTERVAL_MS"] = "250";
        }
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-interval", mode, root.Path], root.Path, TimeSpan.FromSeconds(60), environment, new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject report = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
        FileIntervalExpectations.Validate(mode, report);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
