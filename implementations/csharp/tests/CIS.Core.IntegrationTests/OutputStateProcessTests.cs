using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class OutputStateProcessTests
{
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("output-state-cases.json")["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task OutputStatesRunInExactRuntimeChildren(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"), input = LogValueProcessTests.Find("output-state-cases.json");
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "output-states", input, id], owned.Path, TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["CORE_LOG_QUEUE_CAPACITY"] = "invalid-must-not-be-read", ["TERM"] = "dumb", ["NO_COLOR"] = "1" });
        Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual("", process.StandardError);
        JsonObject catalog = LogValueProcessTests.Read("output-state-cases.json"), row = catalog["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject(); OutputStateExpectations.Validate(row, actual);
        Assert.AreEqual(".NET 10.0.12", actual["observed"]!["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["observed"]!["operatingSystem"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), actual["observed"]!["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["observed"]!["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(input), actual["observed"]!["inputsSha256"]!.GetValue<string>()); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
