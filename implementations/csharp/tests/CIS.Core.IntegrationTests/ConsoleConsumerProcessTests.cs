using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConsoleConsumerProcessTests
{
    public static IEnumerable<object[]> Rows => LogValueProcessTests.Read("console-consumer-cases.json")["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SameCompiledConsumerUsesEnvironmentAndDrainsBothStreams(string id) => await Run(id);
    [TestMethod]
    [DataRow("settings", "Environment-selected settings differ.")]
    [DataRow("outcome", "Consumer outcomes/tickets differ.")]
    [DataRow("factory", "Consumer factory calls differ.")]
    [DataRow("clock", "Consumer clock calls differ.")]
    [DataRow("final", "Consumer final accounting differs.")]
    [DataRow("checkpoint", "Independent file progress differs.")]
    [DataRow("dispose", "Borrowed console disposal differs.")]
    [DataRow("application", "Borrowed application bytes differ.")]
    [DataRow("route", "Unselected consumer stream has records.")]
    [DataRow("identity", "Consumer JSONL facts differ.")]
    [DataRow("resource", "Consumer JSONL facts differ.")]
    [DataRow("bom", "Consumer entry framing/cap differs.")]
    [DataRow("records", "Consumer record count differs.")]
    public async Task ConsumerComparerRejectsSpecificCorruption(string fault, string reason)
    {
        (JsonObject catalog, JsonObject row, JsonObject actual) = await Run("CONSUMER-BLOCK-JSONL-STDERR");
        ConsoleConsumerExpectations.Damage(actual, fault);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true));
        Assert.AreEqual(reason, error.Message);
    }
    private static async Task<(JsonObject Catalog, JsonObject Row, JsonObject Actual)> Run(string id)
    {
        string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll"), path = LogValueProcessTests.Find("console-consumer-cases.json");
        JsonObject catalog = LogValueProcessTests.Read("console-consumer-cases.json"), row = catalog["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        using var owned = new TemporaryDirectory();
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "console-consumer", path, id],
            owned.Path, TimeSpan.FromSeconds(30), ConsoleConsumerExpectations.EnvironmentFor(row), new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError);
        if (row["mode"]!.GetValue<string>() == "pressure")
        {
            Assert.IsTrue(Encoding.UTF8.GetByteCount(child.StandardOutput) >= row["minimumStdoutBytes"]!.GetValue<int>());
            Assert.IsTrue(Encoding.UTF8.GetByteCount(child.StandardError) >= row["minimumStderrBytes"]!.GetValue<int>());
        }
        int reportStart = child.StandardOutput.LastIndexOf('\n', child.StandardOutput.Length - 2) + 1;
        JsonObject actual = JsonNode.Parse(child.StandardOutput[reportStart..])!.AsObject();
        if (row["physical"]!.GetValue<bool>())
        {
            actual["stdout"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(child.StandardOutput[..reportStart]));
            actual["stderr"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(child.StandardError));
        }
        else { Assert.AreEqual(0, reportStart); Assert.AreEqual("", child.StandardError); }
        ConsoleConsumerExpectations.Validate(catalog, row, actual, physicalBytes: true);
        JsonNode observed = actual["observed"]!;
        Assert.AreEqual(".NET 10.0.12", observed["runtimeDescription"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", observed["operatingSystem"]!.GetValue<string>());
        Assert.AreEqual(observed["operatingSystem"]!.GetValue<string>(), actual["resource"]!["os_family"]!.GetValue<string>());
        Assert.AreEqual(observed["processId"]!.GetValue<int>(), actual["resource"]!["process_id"]!.GetValue<int>());
        Assert.AreEqual(Hash(host), observed["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), observed["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(path), observed["inputsSha256"]!.GetValue<string>());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
        return (catalog, row, actual);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
