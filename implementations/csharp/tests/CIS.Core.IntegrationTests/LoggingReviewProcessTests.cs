using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class LoggingReviewProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    [TestMethod]
    [DataRow("recovery-1")]
    [DataRow("recovery-10")]
    [DataRow("recovery-1000")]
    [DataRow("recovery-intent")]
    [DataRow("recovery-retire-one")]
    [DataRow("recovery-retire-two")]
    [DataRow("recovery-retired")]
    [DataRow("recovery-renamed")]
    [DataRow("recovery-published")]
    [DataRow("recovery-fresh")]
    [DataRow("rotation-intent")]
    [DataRow("rotation-retire-one")]
    [DataRow("rotation-retire-two")]
    [DataRow("rotation-retired")]
    [DataRow("rotation-renamed")]
    [DataRow("rotation-published")]
    [DataRow("rotation-fresh")]
    [DataRow("recovery-denied")]
    [DataRow("rotation-denied")]
    [DataRow("cleanup-denied")]
    public async Task PublicConsumersRecoverExactPlansAndBoundPrivateArtifacts(string mode)
    {
        using var root = new TemporaryDirectory();
        (JsonObject report, _) = await FixtureProcessChecks.RunAsync(["logging-review", mode, root.Path], root.Path, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: TimeSpan.FromSeconds(30), flatReport: true);
        Assert.AreEqual(mode, (string?)report["mode"]);
        if (mode == "cleanup-denied")
        {
            Assert.AreEqual(1, Directory.EnumerateFileSystemEntries(root.Path, "*.candidate.tmp").Count());
            Assert.AreEqual("preserve", File.ReadAllText(Path.Combine(root.Path, "metadata.json.foreign.tmp")));
            return;
        }
        Assert.IsTrue(report["publicCreate"]!.GetValue<bool>());
        string path = report["path"]!.GetValue<string>();
        byte[] bytes = File.ReadAllBytes(path); Assert.AreEqual((byte)'\n', bytes[^1]);
        using JsonDocument record = JsonDocument.Parse(bytes);
        Assert.AreEqual("review.append", record.RootElement.GetProperty("event_name").GetString());
        Assert.AreEqual(report["pid"]!.GetValue<int>(), record.RootElement.GetProperty("resource").GetProperty("process_id").GetInt32());
        JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Assert.IsTrue(JsonNode.DeepEquals(inventory, report["inventory"]));
        Assert.AreEqual(report["limit"]!.GetValue<int>() + 1, inventory["segments"]!.AsArray().Count);
        Assert.AreEqual(report["originalSha256"]!.GetValue<string>(), Hash(report["archived"]!.GetValue<string>()));
        Assert.AreEqual("foreign evidence", File.ReadAllText(Path.Combine(root.Path, "foreign.closed")));
        Assert.IsFalse(File.Exists(path + ".cis.transition.json"));
        using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
