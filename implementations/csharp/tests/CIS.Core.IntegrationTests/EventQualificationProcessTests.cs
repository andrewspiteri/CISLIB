using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.ArchitectureTests.Contracts;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class EventQualificationProcessTests
{
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("event-qualification-cases.json")["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task CompleteEventsRunInAnIsolatedExactRuntimeChild(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"), input = LogValueProcessTests.Find("event-qualification-cases.json");
        using var owned = new TemporaryDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "event-qualification", input, id],
            owned.Path, TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual("", process.StandardError);
        JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        JsonObject catalog = LogValueProcessTests.Read("event-qualification-cases.json");
        JsonObject row = catalog["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        EventQualificationExpectations.Validate(catalog, row, actual);
        ValidateSchema(actual, row["input"]!["maxEntryBytes"]!.GetValue<int>());
        Assert.AreEqual(".NET 10.0.12", actual["observed"]!["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["observed"]!["operatingSystem"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), actual["observed"]!["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["observed"]!["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(input), actual["observed"]!["inputsSha256"]!.GetValue<string>());
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
    private static void ValidateSchema(JsonObject actual, int cap)
    {
        string root = Path.GetDirectoryName(LogValueProcessTests.Find("event-qualification-cases.json"))!;
        root = Path.GetFullPath(Path.Combine(root, "../.."));
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/logging/v1/event.schema.json")));
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts/logging/v1/event-contract.json")));
        var validator = new EventRecordValidator(schema.RootElement, contract.RootElement);
        foreach (JsonNode? record in actual["records"]!.AsArray())
        {
            var transport = new JsonObject { ["mode"] = "producer", ["maxEntryBytes"] = cap, ["wire"] = record!["wire"]!.DeepClone() };
            using JsonDocument fixture = JsonDocument.Parse(transport.ToJsonString());
            Assert.IsTrue(validator.Accepts(fixture.RootElement), "Actual wire violates the producer contract.");
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
