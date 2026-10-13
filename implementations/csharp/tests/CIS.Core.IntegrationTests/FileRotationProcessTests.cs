using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRotationProcessTests
{
    [TestMethod]
    [DataRow("jsonl-exact")]
    [DataRow("jsonl-over")]
    [DataRow("human-exact")]
    [DataRow("human-over")]
    public async Task ActualPreparedRecordBytesRotateOnlyWhenTheCompleteRecordExceedsTheLimit(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-rotation", mode, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
        Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual("", process.StandardError); JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject();
        Assert.AreEqual(mode, actual["mode"]!.GetValue<string>()); Assert.AreEqual(".NET 10.0.12", actual["runtime"]!.GetValue<string>());
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["os"]!.GetValue<string>()); Assert.AreEqual(Hash(host), actual["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["coreSha256"]!.GetValue<string>());
        byte[] record = Bytes(actual, "recordBytes"), seed = Bytes(actual, "seedBytes"), active = Bytes(actual, "activeBytes"); bool over = mode.EndsWith("over", StringComparison.Ordinal);
        Assert.AreEqual(65536 + (over ? 1 : 0), seed.Length + record.Length); Assert.AreEqual("Accepted", actual["result"]!.GetValue<string>()); Assert.IsFalse(actual["truncated"]!.GetValue<bool>()); Assert.AreEqual("Stopped", actual["state"]!.GetValue<string>()); Assert.AreEqual(0, actual["borrowedDisposes"]!.GetValue<int>());
        foreach (JsonNode? output in actual["outputs"]!.AsArray()) { Assert.AreEqual("Stopped", output!["state"]!.GetValue<string>()); Assert.AreEqual(1L, output["accepted"]!.GetValue<long>()); Assert.AreEqual(1L, output["written"]!.GetValue<long>()); foreach (string field in new[] { "failed", "pending", "abandoned" }) { Assert.AreEqual(0L, output[field]!.GetValue<long>()); } }
        string path = actual["path"]!.GetValue<string>(); CollectionAssert.AreEqual(File.ReadAllBytes(path), active);
        JsonObject old = actual["oldInventory"]!.AsObject(), inventory = actual["inventory"]!.AsObject(); Assert.AreEqual(old["groupId"]!.GetValue<string>(), inventory["groupId"]!.GetValue<string>());
        JsonArray closed = actual["closed"]!.AsArray(); Assert.AreEqual(over ? 1 : 0, closed.Count); Assert.AreEqual(over ? 2 : 1, inventory["segments"]!.AsArray().Count);
        if (over)
        {
            CollectionAssert.AreEqual(record, active); CollectionAssert.AreEqual(seed, Convert.FromBase64String(closed[0]!["bytes"]!.GetValue<string>()));
            string name = ".cis-log-" + inventory["groupId"]!.GetValue<string>() + "-00000000000000000001.closed"; Assert.AreEqual(name, closed[0]!["name"]!.GetValue<string>()); Assert.AreEqual(2L, inventory["segments"]![1]!["id"]!.GetValue<long>());
            CollectionAssert.AreEqual(seed, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, name)));
        }
        else { CollectionAssert.AreEqual(seed.Concat(record).ToArray(), active); Assert.IsTrue(JsonNode.DeepEquals(old, inventory)); }
        Assert.IsTrue(active.Length <= 65536); Assert.IsTrue(record[^1] == 10 && seed[^1] == 10);
        JsonObject console = JsonNode.Parse(Bytes(actual, "consoleBytes"))!.AsObject(); Assert.AreEqual("rotation.event", console["event_name"]!.GetValue<string>()); Assert.AreEqual("\u03a9 \U0001f642 e\u0301", console["message"]!.GetValue<string>());
    }
    private static byte[] Bytes(JsonObject value, string key) => Convert.FromBase64String(value[key]!.GetValue<string>());
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
