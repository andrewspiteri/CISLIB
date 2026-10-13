using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileInventoryTests
{
    private static readonly Guid Group = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N");
    private static string MetadataPath(string path) => path + ".cis.inventory.json";

    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines, false)]
    [DataRow(LogOutputFormat.JsonLines, true)]
    [DataRow(LogOutputFormat.Human, false)]
    [DataRow(LogOutputFormat.Human, true)]
    public void EmptyGroupRecordsItsFormatAndRetainsIdentityAcrossRestart(LogOutputFormat format, bool preexistingEmpty)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "nested", "\u03a9-\U0001f642.jsonl");
        if (preexistingEmpty) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, []); }
        Guid id;
        using (var first = FileOutputOwner.Initialize(path, format))
        {
            Assert.AreEqual(!preexistingEmpty, first.ActiveWasCreated);
            id = first.Inventory!.GroupId; Assert.AreNotEqual(Guid.Empty, id);
            Assert.AreEqual(format, first.Inventory.Format); Assert.AreEqual(1L, first.Inventory.Active.Id);
            Assert.AreEqual(Path.GetFileName(path), first.Inventory.Active.FileName);
            Assert.AreEqual(0L, first.ActiveFile.Length);
        }
        byte[] inventory = File.ReadAllBytes(MetadataPath(path)); Assert.AreEqual((byte)'\n', inventory[^1]);
        Assert.IsFalse(inventory.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        JsonObject stored = JsonNode.Parse(inventory)!.AsObject();
        Assert.AreEqual("utf-8", stored["encoding"]!.GetValue<string>()); Assert.IsFalse(stored["bom"]!.GetValue<bool>());
        Assert.AreEqual("lf", stored["newline"]!.GetValue<string>()); Assert.AreEqual(1, stored["schema"]!.GetValue<int>());
        Assert.AreEqual(format == LogOutputFormat.JsonLines ? "jsonl" : "human", stored["format"]!.GetValue<string>());
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("existing-active-content\n"));
        using var second = FileOutputOwner.Initialize(path, format);
        Assert.IsFalse(second.ActiveWasCreated); Assert.AreEqual(id, second.Inventory!.GroupId);
        Assert.AreEqual(1L, second.Inventory.Active.Id); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(MetadataPath(path)));
        Assert.AreEqual(24L, second.ActiveFile.Length);
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":1}\n")]
    [DataRow("INFO existing-human-record\n")]
    [DataRow("partial JSON")]
    public void NonemptyMissingInventoryFailsWithoutInferringFormatOrChangingBytes(string content)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"); byte[] before = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(path, before);
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)), "FileInventoryMissing");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path)); Assert.IsFalse(File.Exists(MetadataPath(path)));
        using var released = FileOutputOwner.Initialize(path);
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("schema-type")]
    [DataRow("schema-missing")]
    [DataRow("format")]
    [DataRow("encoding")]
    [DataRow("bom")]
    [DataRow("newline")]
    [DataRow("group-id")]
    [DataRow("group-empty")]
    [DataRow("segment-id")]
    [DataRow("id-type")]
    [DataRow("active-name")]
    [DataRow("state")]
    [DataRow("no-segments")]
    [DataRow("duplicate-active")]
    [DataRow("order")]
    [DataRow("path-escape")]
    [DataRow("missing-closed")]
    [DataRow("duplicate-member")]
    [DataRow("invalid-json")]
    [DataRow("empty-json")]
    [DataRow("oversized")]
    [DataRow("depth")]
    [DataRow("bom-prefix")]
    [DataRow("invalid-utf8")]
    public void MalformedOrIncompatibleInventoryPreservesAllBytesAndReleasesOwnership(string damage)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"), outside = Path.Combine(root.Path, "unrelated.txt");
        byte[] active = Encoding.UTF8.GetBytes("existing-active\n"); File.WriteAllBytes(path, active); File.WriteAllText(outside, "keep-unrelated");
        JsonObject doc = Baseline(path); JsonArray segments = doc["segments"]!.AsArray(); JsonObject entry = segments[0]!.AsObject();
        switch (damage)
        {
            case "schema": doc["schema"] = 2; break;
            case "schema-type": doc["schema"] = "1"; break;
            case "schema-missing": doc.Remove("schema"); break;
            case "format": doc["format"] = "human"; break;
            case "encoding": doc["encoding"] = "utf-16"; break;
            case "bom": doc["bom"] = true; break;
            case "newline": doc["newline"] = "crlf"; break;
            case "group-id": doc["groupId"] = "not-an-id"; break;
            case "group-empty": doc["groupId"] = Guid.Empty.ToString("N"); break;
            case "segment-id": entry["id"] = 0; break;
            case "id-type": entry["id"] = "1"; break;
            case "active-name": entry["fileName"] = "other.jsonl"; break;
            case "state": entry["state"] = "unknown"; break;
            case "no-segments": segments.Clear(); break;
            case "duplicate-active": var duplicate = entry.DeepClone(); duplicate["id"] = 2; segments.Add(duplicate); break;
            case "order":
                segments.Insert(0, Segment(2, FileGroupInventory.ClosedName(Group, 2, FileSegmentState.Closed), "closed"));
                File.WriteAllText(Path.Combine(root.Path, FileGroupInventory.ClosedName(Group, 2, FileSegmentState.Closed)), "keep-closed"); break;
            case "path-escape": entry["id"] = 2; segments.Insert(0, Segment(1, "../unrelated.txt", "closed")); break;
            case "missing-closed": entry["id"] = 2; segments.Insert(0, Segment(1, FileGroupInventory.ClosedName(Group, 1, FileSegmentState.Closed), "closed")); break;
            case "depth": JsonNode nested = JsonValue.Create(1)!; for (int i = 0; i < 14; i++) { nested = new JsonArray(nested); } doc["future"] = nested; break;
        }
        string text = damage switch
        {
            "invalid-json" => "{",
            "empty-json" => "",
            "oversized" => new string(' ', FileGroupInventory.MaximumBytes + 1),
            "duplicate-member" => doc.ToJsonString().Replace("\"schema\":1", "\"schema\":1,\"schema\":1", StringComparison.Ordinal),
            _ => doc.ToJsonString()
        };
        File.WriteAllText(MetadataPath(path), text, new UTF8Encoding(false));
        if (damage == "bom-prefix") { File.WriteAllBytes(MetadataPath(path), [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)]); }
        if (damage == "invalid-utf8") { File.WriteAllBytes(MetadataPath(path), [.. Encoding.UTF8.GetBytes(text[..^1] + ",\"future\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}")]); }
        byte[] metadata = File.ReadAllBytes(MetadataPath(path));
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)), "FileInventoryIncompatible");
        CollectionAssert.AreEqual(active, File.ReadAllBytes(path)); CollectionAssert.AreEqual(metadata, File.ReadAllBytes(MetadataPath(path)));
        Assert.AreEqual("keep-unrelated", File.ReadAllText(outside)); using var released = FileOutputOwner.Initialize(path);
    }

    [TestMethod]
    public void CompatibleOwnedClosedAndDamagedMembersAreInspectedWithoutModification()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"); File.WriteAllText(path, "active-content\n");
        JsonObject doc = Baseline(path); JsonArray segments = doc["segments"]!.AsArray(); segments[0]!["id"] = 3;
        foreach ((long id, FileSegmentState state) in new[] { (1L, FileSegmentState.Closed), (2L, FileSegmentState.Damaged) })
        {
            string name = FileGroupInventory.ClosedName(Group, id, state); File.WriteAllText(Path.Combine(root.Path, name), "keep-" + state);
            segments.Insert((int)id - 1, Segment(id, name, state == FileSegmentState.Closed ? "closed" : "damaged"));
        }
        doc["futureOptional"] = true; File.WriteAllText(MetadataPath(path), doc.ToJsonString()); byte[] before = File.ReadAllBytes(MetadataPath(path));
        using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines);
        Assert.AreEqual(Group, owner.Inventory!.GroupId); Assert.AreEqual(3, owner.Inventory.Segments.Count); Assert.AreEqual(3L, owner.Inventory.Active.Id);
        Assert.AreEqual(FileSegmentState.Damaged, owner.Inventory.Segments[1].State); CollectionAssert.AreEqual(before, File.ReadAllBytes(MetadataPath(path)));
        foreach (FileInventorySegment segment in owner.Inventory.Segments.Take(2)) { Assert.AreEqual("keep-" + segment.State, File.ReadAllText(Path.Combine(root.Path, segment.FileName))); }
        Assert.IsInstanceOfType<System.Collections.ObjectModel.ReadOnlyCollection<FileInventorySegment>>(owner.Inventory.Segments);
    }

    [TestMethod]
    [DataRow(LogOutputFormat.Human)]
    [DataRow(LogOutputFormat.JsonLines)]
    public void ExistingEvenEmptyGroupRequiresArchivalBeforeFormatSwitch(LogOutputFormat firstFormat)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        using (FileOutputOwner.Initialize(path, firstFormat)) { }
        byte[] before = File.ReadAllBytes(MetadataPath(path));
        LogOutputFormat other = firstFormat == LogOutputFormat.Human ? LogOutputFormat.JsonLines : LogOutputFormat.Human;
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path, other)), "FileInventoryIncompatible");
        Assert.AreEqual(0L, new FileInfo(path).Length); CollectionAssert.AreEqual(before, File.ReadAllBytes(MetadataPath(path)));
    }

    [TestMethod]
    public void MissingActiveSegmentCannotSilentlyReuseAnExistingIdentity()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "missing.jsonl");
        File.WriteAllText(MetadataPath(path), Baseline(path).ToJsonString()); byte[] before = File.ReadAllBytes(MetadataPath(path));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)), "FileInventoryIncompatible");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(MetadataPath(path))); Assert.IsFalse(File.Exists(path));
        }
    }

    [TestMethod]
    public void InvalidFormatAndDisposedOwnerCannotCreateInventory()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "missing", "active.jsonl");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FileOutputOwner.Initialize(path, (LogOutputFormat)999)); Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path)));
        var owner = FileOutputOwner.Initialize(path); owner.Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => FileGroupInventory.OpenOrCreate(owner, LogOutputFormat.JsonLines)); Assert.IsFalse(File.Exists(MetadataPath(path)));
    }

    [TestMethod]
    public void InventoryDirectoryCollisionIsPreservedAndReleasesOwnedHandles()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"); Directory.CreateDirectory(MetadataPath(path));
        string retained = Path.Combine(MetadataPath(path), "keep.txt"); File.WriteAllText(retained, "keep");
        AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)), "FileInventoryInitializationFailed");
        Assert.AreEqual("keep", File.ReadAllText(retained)); using var released = FileOutputOwner.Initialize(path);
    }

    [TestMethod]
    [DataRow(1000, true)]
    [DataRow(1001, false)]
    public void InventoryCountIsBoundedAtMaximumRetainedSegmentsPlusActive(int closedCount, bool valid)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"); File.WriteAllText(path, "active\n");
        JsonObject doc = Baseline(path); var entries = new JsonArray();
        for (int id = 1; id <= closedCount; id++)
        {
            string name = ".cis-log-0123456789abcdef0123456789abcdef-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".closed";
            File.WriteAllText(Path.Combine(root.Path, name), "retained"); entries.Add(Segment(id, name, "closed"));
        }
        entries.Add(Segment(closedCount + 1, Path.GetFileName(path), "active")); doc["segments"] = entries;
        File.WriteAllText(MetadataPath(path), doc.ToJsonString());
        if (valid) { using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); Assert.AreEqual(1001, owner.Inventory!.Segments.Count); }
        else { AssertSafe(Assert.ThrowsExactly<CoreLogInitializationException>(() => FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)), "FileInventoryIncompatible"); }
        Assert.AreEqual(closedCount, Directory.GetFiles(root.Path, "*.closed").Length);
    }

    private static JsonObject Baseline(string path) => new()
    {
        ["schema"] = 1,
        ["groupId"] = Group.ToString("N"),
        ["format"] = "jsonl",
        ["encoding"] = "utf-8",
        ["bom"] = false,
        ["newline"] = "lf",
        ["segments"] = new JsonArray(Segment(1, Path.GetFileName(path), "active"))
    };
    private static JsonObject Segment(long id, string name, string state) => new() { ["id"] = id, ["fileName"] = name, ["state"] = state };
    private static void AssertSafe(CoreLogInitializationException error, string code)
    {
        Assert.AreEqual("file", error.OutputName); Assert.AreEqual(code, error.ErrorCode); Assert.IsNull(error.InnerException); Assert.AreEqual(0, error.Data.Count);
        Assert.AreEqual($"Logging output 'file' could not initialize ({code}).", error.Message);
    }
}
