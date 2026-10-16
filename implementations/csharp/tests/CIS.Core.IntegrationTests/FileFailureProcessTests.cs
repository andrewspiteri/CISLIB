using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileFailureProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("delete-jsonl-directory")]
    [DataRow("delete-human-directory")]
    [DataRow("delete-jsonl-damaged-directory")]
    [DataRow("render-empty")]
    [DataRow("render-active")]
    public async Task RealConsumersExposeTerminalPolicyFailureAndHealthyOutput(string mode)
    {
        using var root = new TemporaryDirectory();
        (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["file-failure", mode, root.Path], root.Path, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: TimeSpan.FromSeconds(30), flatReport: true);
        FileFailureExpectations.Validate(mode, actual);
        Assert.AreEqual(Path.Combine(root.Path, "logs", "Ω-🙂.log"), (string?)actual["path"]);
        foreach ((string name, JsonNode? bytes) in actual["after"]!.AsObject()) { CollectionAssert.AreEqual(Convert.FromBase64String(bytes!.GetValue<string>()), File.ReadAllBytes(Path.Combine(root.Path, name))); }
        CollectionAssert.AreEqual(actual["namesAfter"]!.AsArray().Select(p => (string)p!).ToArray(), Directory.GetFileSystemEntries(Path.Combine(root.Path, "logs")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        string[] lines = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(actual["consoleBytes"]!.GetValue<string>())).Split('\n'); Assert.AreEqual(5, lines.Length);
        for (int index = 0; index < 4; index++) { JsonObject record = JsonNode.Parse(lines[index])!.AsObject(); Assert.AreEqual("failure." + index, record["event_name"]!.GetValue<string>()); Assert.AreEqual("preserved", record["message"]!.GetValue<string>()); Assert.AreEqual(actual["pid"]!.GetValue<long>(), record["attributes"]!["producer_pid"]!.GetValue<long>()); }
    }
}
