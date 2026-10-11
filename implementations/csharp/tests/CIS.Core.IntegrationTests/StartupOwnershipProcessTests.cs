using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class StartupOwnershipProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("mutate")]
    [DataRow("replace")]
    [DataRow("sources")]
    [DataRow("collected")]
    public async Task PublishedSnapshotSurvivesCallerMutationSourceChangesAndInputCollection(string scenario)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        var variables = RecognizedNames().ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        Assert.AreEqual(21, variables.Count);
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before"); string after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before); Directory.CreateDirectory(after);
        string parentDirectory = Directory.GetCurrentDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "startup-ownership", scenario, after], before, TimeSpan.FromSeconds(15), variables);
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual(string.Empty, child.StandardError);
        TestContext.WriteLine(child.StandardOutput);
        JsonNode result = JsonNode.Parse(child.StandardOutput)!;
        Assert.AreEqual(scenario, result["scenario"]!.GetValue<string>());
        JsonNode observed = result["observed"]!;
        Assert.AreEqual(before, observed["beforeDirectory"]!.GetValue<string>()); Assert.AreEqual(after, observed["afterDirectory"]!.GetValue<string>());
        foreach (string proof in new[] { "storedProjectionStable", "instanceStable", "snapshotReferenceStable", "loggingReferenceStable", "metadataReferenceStable" }) { Assert.IsTrue(observed[proof]!.GetValue<bool>(), proof); }
        Assert.AreEqual(scenario == "replace" ? 24 : scenario == "mutate" ? 23 : 0, observed["changedFields"]!.GetValue<int>());
        Assert.AreEqual(scenario == "sources" ? 21 : 0, observed["changedVariables"]!.GetValue<int>());
        Assert.AreEqual(scenario == "collected", observed["callerInputsCollected"]!.GetValue<bool>());
        Assert.AreEqual(17, observed["metadataFieldCount"]!.GetValue<int>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        JsonNode snapshot = result["snapshot"]!;
        var expected = new Dictionary<string, JsonNode?> { ["ApplicationName"] = "Owned.App", ["BuildVersion"] = "1.2.3-rc.4+abc", ["ServiceVersion"] = "owned-release", ["EnvironmentName"] = "Owned.Live", ["EnvironmentKind"] = 4, ["CommitId"] = "commit-owned", ["BuildId"] = "build-owned" };
        foreach ((string name, JsonNode? value) in expected) { Assert.IsTrue(JsonNode.DeepEquals(value, snapshot[name]), name); }
        JsonNode logging = snapshot["Logging"]!;
        var options = new Dictionary<string, JsonNode?>
        {
            ["Level"] = 3,
            ["ConsoleFormat"] = 1,
            ["ConsoleLevel"] = 0,
            ["ConsoleStream"] = 1,
            ["HumanDetail"] = 1,
            ["FilePath"] = Path.Combine(before, "logs", "~owned.jsonl"),
            ["FileFormat"] = 0,
            ["FileLevel"] = 5,
            ["QueueCapacity"] = 17,
            ["FlushInterval"] = "00:00:00.0310000",
            ["ShutdownTimeout"] = "00:00:00.0610000",
            ["FileMaxBytes"] = 131072,
            ["FileRetainedFiles"] = 7,
            ["SchemaVersion"] = 1,
            ["MaxEntryBytes"] = 65536,
            ["ConsoleEnabled"] = true,
            ["FileEnabled"] = true
        };
        Assert.AreEqual(17, logging.AsObject().Count);
        foreach ((string name, JsonNode? value) in options) { Assert.IsTrue(JsonNode.DeepEquals(value, logging[name]), name); }
        JsonObject metadata = snapshot["EventMetadata"]!.AsObject(); Assert.AreEqual(17, metadata.Count);
        foreach ((string name, JsonNode? value) in metadata) { Assert.IsTrue(JsonNode.DeepEquals(value, snapshot[name]), name); }
        JsonObject convenience = result["convenience"]!.AsObject(); Assert.AreEqual(8, convenience.Count);
        foreach ((string name, JsonNode? value) in convenience) { Assert.IsTrue(JsonNode.DeepEquals(value, snapshot[name]), name); }
        Assert.IsFalse(child.StandardOutput.Contains("CIS_CHANGED", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(before).Count()); Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(after).Count());
        Assert.AreEqual(2, Directory.EnumerateFileSystemEntries(owned.Path).Count()); Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static IEnumerable<string> RecognizedNames()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "contracts", "logging", "v1", "configuration.json");
            if (File.Exists(path)) { return JsonNode.Parse(File.ReadAllText(path))!["settings"]!.AsArray().SelectMany(row => new[] { row!["key"]!.GetValue<string>() }.Concat(row["aliases"]!.AsArray().Select(alias => alias!.GetValue<string>()))); }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find startup variable catalog.");
    }
}
