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
    private const string Active = "Ω-🙂.log";
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("jsonl-1")]
    [DataRow("jsonl-3")]
    [DataRow("human-1")]
    [DataRow("human-3")]
    public async Task SeparateProducersKeepTheSameGroupAndNewestBoundedSegments(string mode)
    {
        using var root = new TemporaryDirectory(); string directory = Path.Combine(root.Path, "logs"); Directory.CreateDirectory(directory);
        string archive = Path.Combine(root.Path, "archives"); Directory.CreateDirectory(archive);
        var neighbors = new Dictionary<string, byte[]>
        {
            [Path.Combine(directory, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed")] = Encoding.UTF8.GetBytes("foreign group\n"),
            [Path.Combine(directory, "other.log")] = Encoding.UTF8.GetBytes("another active path\n"),
            [Path.Combine(archive, "keep.jsonl")] = Encoding.UTF8.GetBytes("archived outside group\n")
        };
        foreach ((string path, byte[] raw) in neighbors) { File.WriteAllBytes(path, raw); }
        JsonObject? previous = null; var origins = new List<JsonObject>();
        foreach (string phase in new[] { "start", "resume" })
        {
            (JsonObject actual, _) = await FixtureProcessChecks.RunAsync(["file-retention", mode, phase, root.Path], root.Path, TestContext,
                environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: TimeSpan.FromSeconds(30), flatReport: true);
            origins.Add(actual); Check(mode, phase, actual, previous);
            string path = Path.Combine(directory, Active); Assert.AreEqual(path, (string?)actual["path"]);
            Assert.IsTrue(JsonNode.DeepEquals(actual["inventory"], JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))));
            JsonArray segments = actual["inventory"]!["segments"]!.AsArray(), files = actual["files"]!.AsArray();
            for (int index = 0; index < segments.Count; index++)
            {
                string member = Path.Combine(directory, (string)segments[index]!["fileName"]!); byte[] raw = File.ReadAllBytes(member);
                Assert.AreEqual((long)raw.Length, files[index]!["bytes"]!.GetValue<long>()); Assert.AreEqual(Hash(member), (string?)files[index]!["sha256"]);
                Records(raw, mode.StartsWith("human", StringComparison.Ordinal), segments[index]!["id"]!.GetValue<int>(), origins);
            }
            Neighbors(neighbors); Membership(directory, actual["inventory"]!.AsObject(), neighbors); Preimages(actual, previous);
            if (phase == "start")
            {
                string unlisted = Path.Combine(directory, ".cis-log-" + (string)actual["inventory"]!["groupId"]! + "-00000000000000000050.closed");
                neighbors[unlisted] = Encoding.UTF8.GetBytes("unlisted same-group archive\n"); File.WriteAllBytes(unlisted, neighbors[unlisted]);
                foreach (JsonNode? segment in segments.Take(segments.Count - 1))
                { File.SetLastWriteTimeUtc(Path.Combine(directory, (string)segment!["fileName"]!), DateTimeOffset.FromUnixTimeSeconds(1900000000 - segment["id"]!.GetValue<long>()).UtcDateTime); }
            }
            if (mode == "jsonl-3" && phase == "resume")
            {
                Assert.IsNotNull(previous);
                Controls(actual, previous, origins, directory, neighbors);
            }
            previous = actual;
        }
    }

    private static void Check(string mode, string phase, JsonObject actual, JsonObject? previous)
    {
        int retained = mode.EndsWith('1') ? 1 : 3, count = phase == "start" ? 20 : 12, last = phase == "start" ? 5 : 8;
        Assert.AreEqual(mode, (string?)actual["mode"]); Assert.AreEqual(phase, (string?)actual["phase"]); Assert.AreEqual("Stopped", (string?)actual["state"]);
        var output = new JsonObject { ["name"] = "file", ["state"] = "Stopped", ["accepted"] = count, ["written"] = count, ["failed"] = 0, ["pending"] = 0, ["abandoned"] = 0 };
        Require(JsonNode.DeepEquals(output, actual["output"]), "output accounting");
        var results = new JsonArray(); for (int index = 1; index <= count; index++) { results.Add(new JsonObject { ["outcome"] = "Accepted", ["truncated"] = false, ["sequence"] = index }); }
        Assert.IsTrue(JsonNode.DeepEquals(results, actual["results"]));
        string group = (string)actual["inventory"]!["groupId"]!; Assert.IsTrue(Guid.TryParseExact(group, "N", out Guid id) && id != Guid.Empty); Assert.AreEqual(id.ToString("N"), group);
        if (previous is null) { Assert.IsNull(actual["before"]); }
        else
        {
            Require(JsonNode.DeepEquals(previous["inventory"], actual["before"]), "previous inventory"); Require((string?)previous["inventory"]!["groupId"] == group, "same group");
            Assert.AreNotEqual((string?)previous["instanceId"], (string?)actual["instanceId"]);
        }
        var segments = new JsonArray();
        for (int value = last - retained; value <= last; value++)
        { segments.Add(new JsonObject { ["id"] = value, ["fileName"] = value == last ? Active : ".cis-log-" + group + "-" + value.ToString("D20", CultureInfo.InvariantCulture) + ".closed", ["state"] = value == last ? "active" : "closed" }); }
        var inventory = new JsonObject { ["schema"] = 1, ["groupId"] = group, ["format"] = mode.StartsWith("human", StringComparison.Ordinal) ? "human" : "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = segments };
        Require(JsonNode.DeepEquals(inventory, actual["inventory"]), "bounded inventory");
        CollectionAssert.AreEqual(segments.Select(s => (string)s!["fileName"]!).ToArray(), actual["files"]!.AsArray().Select(f => (string)f!["name"]!).ToArray());
    }

    private static void Records(byte[] raw, bool human, int id, List<JsonObject> origins)
    {
        Assert.IsTrue(raw.Length is > 0 and <= 65536 && raw[^1] == 10);
        Assert.IsFalse(raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        string[] lines = new UTF8Encoding(false, true).GetString(raw).Split('\n'); Assert.AreEqual(5, lines.Length); Assert.AreEqual("", lines[^1]);
        for (int offset = 0; offset < 4; offset++)
        {
            byte[] line = Encoding.UTF8.GetBytes(lines[offset] + "\n"); Assert.IsTrue(line.Length is > 13107 and <= 16384); Assert.IsFalse(lines[offset].Contains('\r'));
            JsonObject value;
            if (human) { value = new JsonObject(); foreach (var field in HumanRenderingExpectations.Read(line, 16384)) { value.Add(field.Key, JsonNode.Parse(field.Value.GetRawText())); } }
            else { value = JsonNode.Parse(line)!.AsObject(); Assert.AreEqual(1, value["schema_version"]!.GetValue<int>()); }
            foreach ((string key, string wanted) in new[] { ("component", "Retention"), ("event_name", "retention.event"), ("message", new string('x', 7000) + "Ω🙂"), ("service", "Retention.App"), ("env", "test"), ("version", "1.2.3"), ("timestamp", "2026-10-13T10:11:12.0000000Z"), (human ? "severity" : "status", "info") })
            { Assert.AreEqual(wanted, (string?)value[key], key); }
            int entry = (id - 1) * 4 + offset; JsonObject origin = origins[entry < 20 ? 0 : 1];
            Require(JsonNode.DeepEquals(new JsonObject { ["entry_index"] = entry, ["payload"] = new string('y', 7000) }, value["attributes"]), "record order");
            Assert.IsFalse(value.ContainsKey("truncated"));
            JsonNode resource = human ? value : value["resource"]!; string prefix = human ? "resource." : "";
            Require((string?)origin["instanceId"] == (string?)resource[prefix + "instance_id"], "record instance");
            Require(origin["pid"]!.GetValue<long>() == resource[prefix + "process_id"]!.GetValue<long>(), "record pid");
        }
    }

    private static void Neighbors(Dictionary<string, byte[]> neighbors)
    { foreach ((string path, byte[] raw) in neighbors) { Require(raw.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "neighbor preservation"); } }

    private static void Membership(string directory, JsonObject inventory, Dictionary<string, byte[]> neighbors)
    {
        string[] expected = inventory["segments"]!.AsArray().Select(s => (string)s!["fileName"]!)
            .Concat([Active + ".cis.inventory.json", Active + ".cis.lock"]).Concat(neighbors.Keys.Where(p => Path.GetDirectoryName(p) == directory).Select(p => Path.GetFileName(p)!)).ToArray();
        Require(expected.Order(StringComparer.Ordinal).SequenceEqual(Directory.GetFileSystemEntries(directory).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal)), "exact membership");
    }

    private static void Preimages(JsonObject actual, JsonObject? previous)
    {
        if (previous is null) { return; }
        var hashes = previous["inventory"]!["segments"]!.AsArray().Select((s, index) => (Id: s!["id"]!.GetValue<int>(), Hash: (string?)previous["files"]![index]!["sha256"])).ToDictionary(p => p.Id, p => p.Hash);
        foreach ((JsonNode? segment, int index) in actual["inventory"]!["segments"]!.AsArray().Select((s, i) => (s, i)))
        { if (hashes.TryGetValue(segment!["id"]!.GetValue<int>(), out string? hash)) { Require(hash == (string?)actual["files"]![index]!["sha256"], "retained preimage"); } }
    }

    private void Controls(JsonObject sample, JsonObject prior, List<JsonObject> origins, string directory, Dictionary<string, byte[]> neighbors)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(directory, (string)sample["inventory"]!["segments"]![0]!["fileName"]!));
        foreach (string key in new[] { "bound", "id-reset", "written", "group", "record-order", "record-pid", "record-instance", "extra-member", "neighbor", "retained-preimage" })
        {
            var bad = (JsonObject)sample.DeepClone(); Action damage;
            if (key == "bound") { bad["inventory"]!["segments"]!.AsArray().Insert(0, new JsonObject { ["id"] = 1, ["fileName"] = "extra.closed", ["state"] = "closed" }); damage = () => Check("jsonl-3", "resume", bad, prior); }
            else if (key == "id-reset") { bad["inventory"]!["segments"]!.AsArray()[^1]!["id"] = 1; damage = () => Check("jsonl-3", "resume", bad, prior); }
            else if (key == "written") { bad["output"]!["written"] = 0; damage = () => Check("jsonl-3", "resume", bad, prior); }
            else if (key == "group") { bad["inventory"]!["groupId"] = new string('e', 32); damage = () => Check("jsonl-3", "resume", bad, prior); }
            else if (key == "extra-member")
            {
                string extra = Path.Combine(directory, "unexpected.closed"); File.WriteAllText(extra, "extra");
                try { Assert.AreEqual("exact membership", Assert.ThrowsExactly<InvalidDataException>(() => Membership(directory, sample["inventory"]!.AsObject(), neighbors)).Message); }
                finally { File.Delete(extra); }
                TestContext.WriteLine("Rejected original retention control: " + key); continue;
            }
            else if (key == "neighbor")
            {
                string path = neighbors.Keys.First(); File.WriteAllText(path, "changed neighbor");
                try { Assert.AreEqual("neighbor preservation", Assert.ThrowsExactly<InvalidDataException>(() => Neighbors(neighbors)).Message); }
                finally { File.WriteAllBytes(path, neighbors[path]); }
                TestContext.WriteLine("Rejected original retention control: " + key); continue;
            }
            else if (key == "retained-preimage") { var changed = (JsonObject)prior.DeepClone(); changed["files"]!.AsArray()[^1]!["sha256"] = new string('0', 64); damage = () => Preimages(sample, changed); }
            else
            {
                string[] lines = new UTF8Encoding(false, true).GetString(raw).Split('\n'); JsonObject value = JsonNode.Parse(lines[0])!.AsObject();
                if (key == "record-order") { value["attributes"]!["entry_index"] = 99; }
                else if (key == "record-pid") { value["resource"]!["process_id"] = -1; }
                else { value["resource"]!["instance_id"] = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"; }
                lines[0] = value.ToJsonString(); byte[] changed = Encoding.UTF8.GetBytes(string.Join('\n', lines)); damage = () => Records(changed, false, 5, origins);
            }
            string reason = key switch { "bound" or "id-reset" => "bounded inventory", "written" => "output accounting", "group" => "same group", "record-order" => "record order", "record-pid" => "record pid", "record-instance" => "record instance", _ => "retained preimage" };
            Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(damage).Message);
            TestContext.WriteLine("Rejected original retention control: " + key);
        }
    }

    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
