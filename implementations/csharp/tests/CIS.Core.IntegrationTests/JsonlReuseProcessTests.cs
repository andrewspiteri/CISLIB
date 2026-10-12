using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class JsonlReuseProcessTests
{
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("jsonl-reuse-cases.json")["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task JsonlReuseTableRunsInAnIsolatedExactRuntimeChild(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"), input = LogValueProcessTests.Find("jsonl-reuse-cases.json");
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "jsonl-reuse", input, id],
            owned.Path, TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual("", process.StandardError);
        JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        JsonObject row = LogValueProcessTests.Read("jsonl-reuse-cases.json")["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonlReuseExpectations.Validate(LogValueProcessTests.Read("jsonl-reuse-cases.json"), row, actual);
        Assert.AreEqual(".NET 10.0.12", actual["observed"]!["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["observed"]!["operatingSystem"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), actual["observed"]!["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["observed"]!["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(input), actual["observed"]!["inputsSha256"]!.GetValue<string>());
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
