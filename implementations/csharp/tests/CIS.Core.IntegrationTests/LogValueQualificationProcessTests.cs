using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueQualificationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("value-qualification-cases.json")["cases"]!.AsArray()
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualScalarErrorsAndMutationVectorsExecuteInFreshProcesses(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string input = LogValueProcessTests.Find("value-qualification-cases.json");
        JsonObject row = LogValueProcessTests.Read("value-qualification-cases.json")["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        using var owned = new TemporaryDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "log-qualification", input, id],
            owned.Path, TimeSpan.FromSeconds(15), new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(0, child.ExitCode, child.StandardError);
        Assert.AreEqual(string.Empty, child.StandardError);
        TestContext.WriteLine(child.StandardOutput);
        JsonObject actual = JsonNode.Parse(child.StandardOutput)!.AsObject();
        foreach ((string key, JsonNode? expected) in row["expected"]!.AsObject())
        {
            Assert.IsTrue(JsonNode.DeepEquals(expected, actual[key]), key);
        }
        Assert.IsTrue(actual["rejectedInputNotEchoed"]!.GetValue<bool>());
        Assert.IsTrue(actual["rangeActualValueAbsent"]!.GetValue<bool>());
        JsonNode observed = actual["observed"]!;
        Assert.IsFalse(observed["lifecycleApiPresent"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(input), observed["inputsSha256"]!.GetValue<string>());
        Assert.IsFalse(child.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
