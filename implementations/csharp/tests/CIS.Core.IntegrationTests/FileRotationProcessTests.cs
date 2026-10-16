using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileRotationProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("jsonl-exact")]
    [DataRow("jsonl-over")]
    [DataRow("human-exact")]
    [DataRow("human-over")]
    public async Task ActualPreparedRecordBytesRotateOnlyWhenTheCompleteRecordExceedsTheLimit(string mode)
    {
        using var root = new TemporaryDirectory(); JsonObject report = await Run(mode, root.Path);
        Check(mode, report);
    }

    [TestMethod]
    [DataRow("activeBytes")]
    [DataRow("inventory")]
    [DataRow("outputs")]
    [DataRow("borrowedDisposes")]
    [DataRow("recordBytes")]
    public async Task OriginalRotationReaderCorruptionsAreRejectedAfterPhysicalPositive(string field)
    {
        using var root = new TemporaryDirectory(); JsonObject report = await Run("jsonl-over", root.Path); Check("jsonl-over", report);
        switch (field)
        {
            case "activeBytes": report[field] = Convert.ToBase64String([.. Bytes(report, "seedBytes"), .. Bytes(report, "recordBytes")]); break;
            case "inventory": report[field]!["segments"]!.AsArray()[^1]!["id"] = 1; break;
            case "outputs": report[field]![1]!["written"] = 0; break;
            case "borrowedDisposes": report[field] = 1; break;
            case "recordBytes": report[field] = Convert.ToBase64String(Bytes(report, field)[..^1]); break;
        }
        string reason = field switch { "activeBytes" => "active bytes", "inventory" => "next inventory", "outputs" => "output accounting", "borrowedDisposes" => "borrowed disposal", _ => "record bounds" };
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => Check("jsonl-over", report)).Message);
        TestContext.WriteLine("Rejected original rotation control: " + field);
    }

    private async Task<JsonObject> Run(string mode, string root)
    {
        (JsonObject report, _) = await FixtureProcessChecks.RunAsync(["file-rotation", mode, root], root, TestContext,
            environment: new Dictionary<string, string?> { ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null }, timeout: TimeSpan.FromSeconds(30), flatReport: true);
        string path = Path.Combine(root, "logs", "Ω-🙂.log");
        Assert.AreEqual(path, (string?)report["path"]);
        CollectionAssert.AreEqual(Bytes(report, "activeBytes"), File.ReadAllBytes(path));
        CollectionAssert.AreEqual(Bytes(report, "recordBytes"), File.ReadAllBytes(Path.Combine(root, "reference.log")));
        Assert.IsTrue(JsonNode.DeepEquals(report["inventory"], JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))));
        Assert.AreEqual(report["closed"]!.AsArray().Count, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.closed").Length);
        foreach (JsonNode? item in report["closed"]!.AsArray())
        { CollectionAssert.AreEqual(Convert.FromBase64String((string)item!["bytes"]!), File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, (string)item["name"]!))); }
        return report;
    }

    private static void Check(string mode, JsonObject report)
    {
        bool human = mode.StartsWith("human", StringComparison.Ordinal), over = mode.EndsWith("over", StringComparison.Ordinal);
        Assert.AreEqual(mode, (string?)report["mode"]); Assert.AreEqual("Accepted", (string?)report["result"]);
        Assert.IsFalse(report["truncated"]!.GetValue<bool>()); Assert.AreEqual("Stopped", (string?)report["state"]);
        Require(report["borrowedDisposes"]!.GetValue<int>() == 0, "borrowed disposal");
        var tickets = new JsonArray(); var outputs = new JsonArray();
        foreach (string name in new[] { "console", "file" })
        {
            tickets.Add(new JsonObject { ["name"] = name, ["status"] = "Accepted", ["sequence"] = 1 });
            outputs.Add(new JsonObject { ["name"] = name, ["state"] = "Stopped", ["accepted"] = 1, ["written"] = 1, ["failed"] = 0, ["pending"] = 0, ["abandoned"] = 0 });
        }
        Assert.IsTrue(JsonNode.DeepEquals(tickets, report["tickets"])); Require(JsonNode.DeepEquals(outputs, report["outputs"]), "output accounting");
        byte[] line = Bytes(report, "recordBytes"), seed = Bytes(report, "seedBytes"), active = Bytes(report, "activeBytes");
        Record(line, human); Record(Bytes(report, "consoleBytes"), false);
        Assert.AreEqual(65536 + (over ? 1 : 0), seed.Length + line.Length);
        string prefix = human ? "seed=\"" : "{\"seed\":\"", suffix = human ? "\"\n" : "\"}\n";
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(prefix + new string('a', seed.Length - prefix.Length - suffix.Length) + suffix), seed);
        string group = (string)report["oldInventory"]!["groupId"]!;
        Assert.IsTrue(Guid.TryParseExact(group, "N", out Guid id) && id != Guid.Empty); Assert.AreEqual(id.ToString("N"), group);
        JsonObject Segment(int value, string name, string state) => new() { ["id"] = value, ["fileName"] = name, ["state"] = state };
        string closed = ".cis-log-" + group + "-00000000000000000001.closed", fileName = "Ω-🙂.log";
        var old = new JsonObject { ["schema"] = 1, ["groupId"] = group, ["format"] = human ? "human" : "jsonl", ["encoding"] = "utf-8", ["bom"] = false, ["newline"] = "lf", ["segments"] = new JsonArray(Segment(1, fileName, "active")) };
        Assert.IsTrue(JsonNode.DeepEquals(old, report["oldInventory"]));
        var next = (JsonObject)old.DeepClone();
        if (over) { next["segments"] = new JsonArray(Segment(1, closed, "closed"), Segment(2, fileName, "active")); }
        Require(JsonNode.DeepEquals(next, report["inventory"]), "next inventory");
        Require(active.AsSpan().SequenceEqual(over ? line : [.. seed, .. line]), "active bytes"); Assert.IsTrue(active.Length <= 65536);
        JsonArray expectedClosed = over ? new JsonArray(new JsonObject { ["name"] = closed, ["bytes"] = (string?)report["seedBytes"] }) : [];
        Assert.IsTrue(JsonNode.DeepEquals(expectedClosed, report["closed"]));
    }

    private static void Record(byte[] raw, bool human)
    {
        Require(raw.Length is > 0 and <= 16384 && raw[^1] == 10, "record bounds");
        Assert.IsFalse(raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        string text = new UTF8Encoding(false, true).GetString(raw);
        Assert.AreEqual(1, text.Count(c => c == '\n')); Assert.IsFalse(text.Contains('\r'));
        Dictionary<string, JsonElement> fields;
        if (human) { fields = HumanRenderingExpectations.Read(raw, 16384); }
        else
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            fields = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
        foreach ((string key, string value) in new[] { ("event_name", "rotation.event"), ("message", "Ω 🙂 é"), ("component", "Rotation"), (human ? "severity" : "status", "info"), ("service", "Console.App"), ("env", "test"), ("version", "1.2.3"), ("timestamp", "2026-10-13T10:11:12.0000000Z") })
        { Assert.AreEqual(value, fields[key].GetString(), key); }
        Assert.IsFalse(fields.ContainsKey("truncated"));
        if (!human) { Assert.AreEqual(1, fields["schema_version"].GetInt32()); Assert.AreEqual("11111111-1111-1111-1111-111111111111", fields["resource"].GetProperty("instance_id").GetString()); }
    }

    private static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static byte[] Bytes(JsonObject report, string key) => Convert.FromBase64String((string)report[key]!);
}
