using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileTransitionProcessTests
{
    [TestMethod]
    [DataRow("rotation-intent")]
    [DataRow("rotation-renamed")]
    [DataRow("rotation-published")]
    [DataRow("rotation-created")]
    [DataRow("recovery-intent")]
    [DataRow("recovery-renamed")]
    [DataRow("recovery-published")]
    [DataRow("recovery-created")]
    [DataRow("human-intent")]
    [DataRow("human-renamed")]
    [DataRow("human-published")]
    [DataRow("human-created")]
    [DataRow("ambiguous-both")]
    [DataRow("mismatch-bytes")]
    [DataRow("nonempty-fresh")]
    [DataRow("retirement-no-intent")]
    public async Task ActualProcessResumesKnownStagesOrFailsUnchanged(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-transition", mode, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError); Assert.AreEqual("", child.StandardError);
        JsonObject result = JsonNode.Parse(child.StandardOutput)!.AsObject();
        Assert.AreEqual(Hash(host), result["fixtureSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), result["coreSha256"]!.GetValue<string>());
        Assert.AreEqual(".NET 10.0.12", result["runtime"]!.GetValue<string>());
        Assert.IsTrue(result["leaseReleased"]!.GetValue<bool>() && result["dataReleased"]!.GetValue<bool>());
        bool failure = mode is "ambiguous-both" or "mismatch-bytes" or "nonempty-fresh" or "retirement-no-intent";
        if (failure)
        {
            Assert.AreEqual(mode == "retirement-no-intent" ? "FileInventoryIncompatible" : "FileTransitionIncompatible", result["error"]!["code"]!.GetValue<string>());
            Assert.IsTrue(JsonNode.DeepEquals(result["before"], result["after"]));
        }
        else
        {
            Assert.IsNull(result["error"]); Assert.AreEqual("Accepted", result["outcome"]!.GetValue<string>());
            Assert.AreEqual(mode.StartsWith("recovery", StringComparison.Ordinal), result["recovered"]!.GetValue<bool>());
            Assert.IsFalse(result["restartRecovered"]!.GetValue<bool>());

            JsonObject before = result["before"]!.AsObject(), after = result["after"]!.AsObject();
            const string active = "logs/\u03a9-\U0001f642.log";
            Assert.IsFalse(after.ContainsKey(active + ".cis.transition.json"));
            JsonObject inventory = JsonNode.Parse(Convert.FromBase64String(after[active + ".cis.inventory.json"]!.GetValue<string>()))!.AsObject();
            JsonArray members = inventory["segments"]!.AsArray(); bool recovery = mode.StartsWith("recovery", StringComparison.Ordinal);
            Assert.AreEqual(recovery ? 3L : 4L, members[^1]!["id"]!.GetValue<long>());
            string archive = "logs/" + members[^2]!["fileName"]!.GetValue<string>();
            byte[] original = Encoding.UTF8.GetBytes(recovery ? "{\"earlier\":true}\n{\"partial\":" : mode.StartsWith("human", StringComparison.Ordinal) ? "earlier=true\n" : "{\"earlier\":true}\n");
            CollectionAssert.AreEqual(original, Convert.FromBase64String(after[archive]!.GetValue<string>()));
            foreach (KeyValuePair<string, JsonNode?> neighbor in before.Where(entry => entry.Key == "neighbor.bin" || entry.Key.EndsWith("00000000000000000050.closed", StringComparison.Ordinal)))
            { Assert.IsTrue(JsonNode.DeepEquals(neighbor.Value, after[neighbor.Key])); }
            Assert.AreEqual(1L, result["final"]!["written"]!.GetValue<long>());
            Assert.AreEqual(0L, result["final"]!["failed"]!.GetValue<long>() + result["final"]!["abandoned"]!.GetValue<long>() + result["final"]!["pending"]!.GetValue<long>());
        }
        foreach (KeyValuePair<string, JsonNode?> entry in result["after"]!.AsObject())
        { CollectionAssert.AreEqual(Convert.FromBase64String(entry.Value!.GetValue<string>()), File.ReadAllBytes(Path.Combine(root.Path, entry.Key))); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
