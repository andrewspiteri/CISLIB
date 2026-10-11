using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LogValueProcessTests
{
    private static readonly string[] Kinds = ["null", "boolean", "string", "number", "integer", "integer-string", "decimal-string", "array", "map"];
    public TestContext TestContext { get; set; } = null!;

    public static IEnumerable<object[]> PortableRows => Read()["cases"]!.AsArray()
        .Where(row => Kinds.Contains(row!["input"]!["kind"]!.GetValue<string>(), StringComparer.Ordinal))
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(PortableRows))]
    public async Task ActualTypedValueRepresentationsExecuteInFreshProcessesWithoutStartup(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string input = Find();
        JsonObject row = Read()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        using var owned = new TemporaryDirectory();
        string parentDirectory = Directory.GetCurrentDirectory();
        ProcessResult process = await ProcessRunner.RunAsync("dotnet",
            ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "log-value", input, id],
            owned.Path, TimeSpan.FromSeconds(15),
            new Dictionary<string, string?> { ["CORE_LOG_LEVEL"] = "invalid-must-not-be-read", ["DD_AGENT_HOST"] = "CIS_NEVER_CAPTURE_COLLECTOR" });
        Assert.AreEqual(0, process.ExitCode, process.StandardError);
        Assert.AreEqual(string.Empty, process.StandardError);
        TestContext.WriteLine(process.StandardOutput);
        JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        bool valid = row["expectedValid"]!.GetValue<bool>();
        Assert.AreEqual(valid, actual["valid"]!.GetValue<bool>());
        if (valid)
        {
            Assert.IsTrue(JsonNode.DeepEquals(row["expectedJson"], actual["json"]), id);
            Assert.IsFalse(actual["isTruncated"]!.GetValue<bool>());
        }
        Assert.AreEqual(id, actual["caseId"]!.GetValue<string>());
        JsonNode observed = actual["observed"]!;
        Assert.IsFalse(observed["publicFacadeInitialized"]!.GetValue<bool>());
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(input), observed["inputsSha256"]!.GetValue<string>());
        Assert.IsFalse(process.StandardOutput.Contains("CIS_NEVER_CAPTURE_COLLECTOR", StringComparison.Ordinal));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        Assert.AreEqual(parentDirectory, Directory.GetCurrentDirectory());
    }

    private static JsonObject Read() => JsonNode.Parse(File.ReadAllText(Find()))!.AsObject();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "event-value-cases.json");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find portable value fixtures.");
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
