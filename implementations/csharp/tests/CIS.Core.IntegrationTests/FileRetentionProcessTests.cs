using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRetentionProcessTests
{
    [TestMethod]
    [DataRow("jsonl-1")]
    [DataRow("jsonl-3")]
    [DataRow("human-1")]
    [DataRow("human-3")]
    public async Task SeparateProducersKeepTheSameGroupAndNewestBoundedSegments(string mode)
    {
        using var root = new TemporaryDirectory(); string host = Path.Combine(AppContext.BaseDirectory, "discovery-fixture", "CIS.Core.DiscoveryFixture.dll");
        string directory = Path.Combine(root.Path, "logs"); Directory.CreateDirectory(directory); string neighbor = Path.Combine(directory, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"); File.WriteAllText(neighbor, "foreign group");
        JsonObject? previous = null; var origins = new List<JsonObject>();
        foreach (string phase in new[] { "start", "resume" })
        {
            ProcessResult process = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", host, "file-retention", mode, phase, root.Path], root.Path, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
            Assert.AreEqual(0, process.ExitCode, process.StandardError); Assert.AreEqual("", process.StandardError); JsonObject actual = JsonNode.Parse(process.StandardOutput)!.AsObject(); origins.Add(actual);
            Assert.AreEqual(mode, actual["mode"]!.GetValue<string>()); Assert.AreEqual(phase, actual["phase"]!.GetValue<string>()); Assert.AreEqual(".NET 10.0.12", actual["runtime"]!.GetValue<string>()); Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", actual["os"]!.GetValue<string>());
            Assert.AreEqual(Hash(host), actual["fixtureSha256"]!.GetValue<string>()); Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll")), actual["coreSha256"]!.GetValue<string>());
            if (previous is null) { Assert.IsNull(actual["before"]); } else { Assert.IsTrue(JsonNode.DeepEquals(previous["inventory"], actual["before"])); Assert.AreNotEqual(previous["instanceId"]!.GetValue<string>(), actual["instanceId"]!.GetValue<string>()); }
            int count = phase == "start" ? 20 : 12, last = phase == "start" ? 5 : 8, retained = mode.EndsWith('1') ? 1 : 3;
            Assert.AreEqual("Stopped", actual["state"]!.GetValue<string>()); JsonObject output = actual["output"]!.AsObject(); Assert.AreEqual("file", output["name"]!.GetValue<string>()); Assert.AreEqual("Stopped", output["state"]!.GetValue<string>()); Assert.AreEqual((long)count, output["accepted"]!.GetValue<long>()); Assert.AreEqual((long)count, output["written"]!.GetValue<long>()); foreach (string key in new[] { "failed", "pending", "abandoned" }) { Assert.AreEqual(0L, output[key]!.GetValue<long>()); }
            JsonArray results = actual["results"]!.AsArray(); Assert.AreEqual(count, results.Count); for (int index = 0; index < count; index++) { Assert.AreEqual("Accepted", results[index]!["outcome"]!.GetValue<string>()); Assert.IsFalse(results[index]!["truncated"]!.GetValue<bool>()); Assert.AreEqual((long)index + 1, results[index]!["sequence"]!.GetValue<long>()); }
            string path = actual["path"]!.GetValue<string>(); Assert.AreEqual(Path.Combine(directory, "\u03a9-\U0001f642.log"), path); JsonObject inventory = actual["inventory"]!.AsObject(); Assert.IsTrue(JsonNode.DeepEquals(inventory, JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json")))); string group = inventory["groupId"]!.GetValue<string>();
            if (previous is not null) { Assert.AreEqual(previous["inventory"]!["groupId"]!.GetValue<string>(), group); }
            JsonArray segments = inventory["segments"]!.AsArray(), files = actual["files"]!.AsArray(); Assert.AreEqual(retained + 1, segments.Count); Assert.AreEqual(segments.Count, files.Count);
            for (int index = 0; index < segments.Count; index++)
            {
                int id = last - retained + index; bool active = id == last; string name = active ? "\u03a9-\U0001f642.log" : ".cis-log-" + group + "-" + id.ToString("D20", CultureInfo.InvariantCulture) + ".closed";
                Assert.AreEqual((long)id, segments[index]!["id"]!.GetValue<long>()); Assert.AreEqual(name, segments[index]!["fileName"]!.GetValue<string>()); Assert.AreEqual(active ? "active" : "closed", segments[index]!["state"]!.GetValue<string>()); Assert.AreEqual(name, files[index]!["name"]!.GetValue<string>());
                string member = Path.Combine(directory, name); byte[] bytes = File.ReadAllBytes(member); Assert.IsTrue(bytes.Length <= 65536 && bytes[^1] == 10); Assert.AreEqual((long)bytes.Length, files[index]!["bytes"]!.GetValue<long>()); Assert.AreEqual(Hash(member), files[index]!["sha256"]!.GetValue<string>());
                string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n'); Assert.AreEqual(5, lines.Length); Assert.AreEqual("", lines[^1]);
                for (int line = 0; line < 4; line++)
                {
                    byte[] raw = Encoding.UTF8.GetBytes(lines[line] + "\n"); Assert.IsTrue(raw.Length is > 13107 and <= 16384); JsonObject value;
                    if (mode.StartsWith("human", StringComparison.Ordinal)) { value = new JsonObject(); foreach (var field in HumanRenderingExpectations.Read(raw, 16384)) { value[field.Key] = JsonNode.Parse(field.Value.GetRawText()); } } else { value = JsonNode.Parse(raw)!.AsObject(); }
                    int entry = (id - 1) * 4 + line; JsonObject origin = origins[entry < 20 ? 0 : 1];
                    Assert.AreEqual(entry, value["attributes"]!["entry_index"]!.GetValue<int>()); Assert.AreEqual(new string('y', 7000), value["attributes"]!["payload"]!.GetValue<string>()); Assert.AreEqual("retention.event", value["event_name"]!.GetValue<string>()); Assert.AreEqual(new string('x', 7000) + "\u03a9\U0001f642", value["message"]!.GetValue<string>()); Assert.IsFalse(value.ContainsKey("truncated"));
                    string instance = mode.StartsWith("human", StringComparison.Ordinal) ? value["resource.instance_id"]!.GetValue<string>() : value["resource"]!["instance_id"]!.GetValue<string>(); long pid = mode.StartsWith("human", StringComparison.Ordinal) ? value["resource.process_id"]!.GetValue<long>() : value["resource"]!["process_id"]!.GetValue<long>();
                    Assert.AreEqual(origin["instanceId"]!.GetValue<string>(), instance); Assert.AreEqual(origin["pid"]!.GetValue<long>(), pid);
                }
            }
            Assert.AreEqual(retained + 1, Directory.EnumerateFiles(directory, "*.closed").Count()); Assert.AreEqual("foreign group", File.ReadAllText(neighbor)); previous = actual;
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
