using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRestartProcessTests
{
    [TestMethod]
    [DataRow("intact")]
    [DataRow("partial-json")]
    [DataRow("partial-utf8")]
    [DataRow("rotation-intent")]
    [DataRow("rotation-renamed")]
    [DataRow("rotation-published")]
    [DataRow("rotation-created")]
    [DataRow("recovery-intent")]
    [DataRow("recovery-renamed")]
    [DataRow("recovery-published")]
    [DataRow("recovery-created")]
    [DataRow("collision-file")]
    [DataRow("collision-directory")]
    [DataRow("unproven-gap")]
    [DataRow("ambiguous-both")]
    public async Task SeparateProducerAndRestartProcessesPreserveWholeSegmentsAndSafeFailures(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        async Task<JsonObject> Run(string phase)
        {
            ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-restart", mode, phase, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
            Assert.AreEqual(0, result.ExitCode, result.StandardError); Assert.AreEqual("", result.StandardError); JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
            Assert.AreEqual(Hash(host), report["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), report["coreSha256"]!.GetValue<string>());
            Assert.AreEqual(".NET 10.0.12", report["runtime"]!.GetValue<string>()); return report;
        }
        JsonObject seed = await Run("seed"), resume = await Run("resume");
        Assert.AreNotEqual(seed["pid"]!.GetValue<int>(), resume["pid"]!.GetValue<int>());
        Assert.AreEqual(2L, seed["seedWritten"]!.GetValue<long>()); Assert.IsTrue(seed["leaseHeld"]!.GetValue<bool>()); Assert.IsTrue(resume["leaseReleased"]!.GetValue<bool>());
        Assert.IsTrue(JsonNode.DeepEquals(seed["files"], resume["before"]));
        bool failure = mode is "collision-file" or "collision-directory" or "unproven-gap" or "ambiguous-both";
        if (failure)
        {
            string code = mode.StartsWith("collision", StringComparison.Ordinal) ? "FileRecoveryCollision" : mode == "unproven-gap" ? "FileInventoryIncompatible" : "FileTransitionIncompatible";
            Assert.AreEqual(code, resume["error"]!["code"]!.GetValue<string>()); Assert.IsFalse(resume["error"]!["inner"]!.GetValue<bool>());
            Assert.IsTrue(JsonNode.DeepEquals(resume["before"], resume["after"])); Assert.IsTrue(JsonNode.DeepEquals(resume["namesBefore"], resume["namesAfter"])); Assert.IsNull(resume["final"]);
        }
        else
        {
            Assert.IsNull(resume["error"]); Assert.AreEqual("Accepted", resume["outcome"]!.GetValue<string>());
            bool recovered = mode.StartsWith("partial", StringComparison.Ordinal) || mode.StartsWith("recovery", StringComparison.Ordinal);
            JsonObject final = resume["final"]!.AsObject(); Assert.AreEqual(recovered, final["recovered"]!.GetValue<bool>()); Assert.AreEqual(1L, final["written"]!.GetValue<long>());
            Assert.AreEqual(0L, final["failed"]!.GetValue<long>() + final["abandoned"]!.GetValue<long>() + final["pending"]!.GetValue<long>());
            const string active = "logs/\u03a9-\U0001f642.jsonl"; JsonObject files = resume["after"]!.AsObject();
            Assert.IsFalse(files.ContainsKey(active + ".cis.transition.json"));
            JsonObject metadata = JsonNode.Parse(Convert.FromBase64String(files[active + ".cis.inventory.json"]!.GetValue<string>()))!.AsObject(); Assert.AreEqual(seed["group"]!.GetValue<string>(), metadata["groupId"]!.GetValue<string>());
            JsonArray members = metadata["segments"]!.AsArray(); Assert.AreEqual(mode == "intact" ? 1L : mode.StartsWith("partial", StringComparison.Ordinal) ? 2L : 4L, members[^1]!["id"]!.GetValue<long>());
            if (mode != "intact")
            {
                byte[] first = Convert.FromBase64String(seed["seedRecords"]![0]!.GetValue<string>()), second = Convert.FromBase64String(seed["seedRecords"]![1]!.GetValue<string>());
                byte[] suffix = mode == "partial-json" ? Encoding.UTF8.GetBytes("{\"partial\":") : [.. Encoding.UTF8.GetBytes("{\"partial\":\""), 0xf0, 0x9f];
                byte[] original = mode.StartsWith("partial", StringComparison.Ordinal) ? [.. first, .. second, .. suffix] : mode.StartsWith("recovery", StringComparison.Ordinal) ? [.. second, .. suffix] : second;
                string archive = "logs/" + members[^2]!["fileName"]!.GetValue<string>();
                CollectionAssert.AreEqual(original, Convert.FromBase64String(files[archive]!.GetValue<string>()));
            }
            foreach (JsonNode? member in members.Take(members.Count - 1))
            {
                string key = "logs/" + member!["fileName"]!.GetValue<string>();
                if (seed["files"]!.AsObject().ContainsKey(key)) { Assert.IsTrue(JsonNode.DeepEquals(seed["files"]![key], files[key]), key); }
            }
            KeyValuePair<string, JsonNode?>[] neighbors = seed["files"]!.AsObject().Where(entry => entry.Key == "neighbor.bin" || entry.Key.EndsWith("00000000000000000050.closed", StringComparison.Ordinal) || entry.Key.Contains("ffffffffffffffffffffffffffffffff", StringComparison.Ordinal) || entry.Key.EndsWith("unlisted.tmp", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(4, neighbors.Length);
            foreach (KeyValuePair<string, JsonNode?> neighbor in neighbors) { Assert.IsTrue(JsonNode.DeepEquals(neighbor.Value, files[neighbor.Key])); }
        }
        foreach (KeyValuePair<string, JsonNode?> entry in resume["after"]!.AsObject()) { CollectionAssert.AreEqual(Convert.FromBase64String(entry.Value!.GetValue<string>()), File.ReadAllBytes(Path.Combine(root.Path, entry.Key))); }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
