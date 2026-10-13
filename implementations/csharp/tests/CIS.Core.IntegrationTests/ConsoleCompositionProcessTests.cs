using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleCompositionProcessTests
{
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("console-composition-cases.json")["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ConfiguredConsoleRunsInExactRuntimeChildrenWithBothStreamsDrained(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"), path = LogValueProcessTests.Find("console-composition-cases.json");
        JsonObject catalog = LogValueProcessTests.Read("console-composition-cases.json"), row = catalog["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "console-composition", path, id], owned.Path, TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_STREAM"] = "invalid-must-not-be-read", ["TERM"] = "dumb", ["NO_COLOR"] = "1" }, outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, process.ExitCode, process.StandardError);
        int reportStart = process.StandardOutput.LastIndexOf('\n', process.StandardOutput.Length - 2) + 1;
        JsonObject actual = JsonNode.Parse(process.StandardOutput[reportStart..])!.AsObject();
        if (row["physical"]!.GetValue<bool>())
        {
            actual["stdout"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(process.StandardOutput[..reportStart]));
            actual["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(process.StandardError));
        }
        else { Assert.AreEqual("", process.StandardError); Assert.AreEqual(0, reportStart); }
        ConsoleCompositionExpectations.Validate(catalog, row, actual, physicalBytes: true);
        Assert.AreEqual(".NET 10.0.12", actual["observed"]!["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["observed"]!["operatingSystem"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), actual["observed"]!["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["observed"]!["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(path), actual["observed"]!["inputsSha256"]!.GetValue<string>());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
