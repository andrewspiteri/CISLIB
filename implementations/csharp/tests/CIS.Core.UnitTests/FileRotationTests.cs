using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileRotationTests
{
    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines, -1)]
    [DataRow(LogOutputFormat.JsonLines, 0)]
    [DataRow(LogOutputFormat.JsonLines, 1)]
    [DataRow(LogOutputFormat.Human, -1)]
    [DataRow(LogOutputFormat.Human, 0)]
    [DataRow(LogOutputFormat.Human, 1)]
    public async Task EncodedUtf8AndLfDetermineExactSegmentBoundary(LogOutputFormat format, int room)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        byte[] first = Record(format, "first"), second = Record(format, "\u03a9\U0001f642e\u0301");
        Assert.IsTrue(second.Length > Encoding.UTF8.GetString(second).Length);
        long limit = first.Length + second.Length + room;
        using var writer = new FileOutputWriter(path, format, HumanOutputDetail.Verbose, 16384, limit); writer.Initialize();
        await Write(writer, first); await Write(writer, second);
        JsonObject inventory = ReadInventory(path); JsonArray segments = inventory["segments"]!.AsArray();
        Assert.AreEqual(room < 0 ? 2 : 1, segments.Count);
        if (room < 0)
        {
            Assert.AreEqual(2L, segments[1]!["id"]!.GetValue<long>());
            CollectionAssert.AreEqual(first, File.ReadAllBytes(ClosedPath(path, inventory)));
            CollectionAssert.AreEqual(second, ReadShared(path));
        }
        else { CollectionAssert.AreEqual(first.Concat(second).ToArray(), ReadShared(path)); }
        Assert.IsTrue(new FileInfo(path).Length <= limit);
        // An exact fit remains active until the following whole record arrives.
        if (room == 0)
        {
            await Write(writer, first);
            inventory = ReadInventory(path); Assert.AreEqual(2, inventory["segments"]!.AsArray().Count);
            CollectionAssert.AreEqual(first.Concat(second).ToArray(), File.ReadAllBytes(ClosedPath(path, inventory)));
            CollectionAssert.AreEqual(first, ReadShared(path));
        }
    }

    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines)]
    [DataRow(LogOutputFormat.Human)]
    public async Task ExistingFullSegmentRotatesBeforeFirstIncomingRecord(LogOutputFormat format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); byte[] seed = Record(format, "seed"), next = Record(format, "next");
        using (FileOutputOwner.Initialize(path, format)) { }
        File.WriteAllBytes(path, seed); string id = ReadInventory(path)["groupId"]!.GetValue<string>();
        using var writer = new FileOutputWriter(path, format, HumanOutputDetail.Normal, 16384, seed.Length); writer.Initialize(); await Write(writer, next);
        JsonObject inventory = ReadInventory(path); Assert.AreEqual(id, inventory["groupId"]!.GetValue<string>());
        CollectionAssert.AreEqual(seed, File.ReadAllBytes(ClosedPath(path, inventory))); CollectionAssert.AreEqual(next, ReadShared(path));
    }

    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines)]
    [DataRow(LogOutputFormat.Human)]
    public async Task RecordLargerThanAnEmptySegmentNeverCreatesEmptyRotations(LogOutputFormat format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        using var writer = new FileOutputWriter(path, format, HumanOutputDetail.Normal, 16384, 16); writer.Initialize();
        byte[] metadata = File.ReadAllBytes(path + ".cis.inventory.json"), record = Record(format, new string('x', 30));
        for (int attempt = 0; attempt < 2; attempt++) { await Assert.ThrowsAsync<IOException>(() => Write(writer, record)); }
        Assert.AreEqual(0L, new FileInfo(path).Length); Assert.AreEqual(0, Directory.EnumerateFiles(root.Path, "*.closed").Count());
        CollectionAssert.AreEqual(metadata, File.ReadAllBytes(path + ".cis.inventory.json"));
    }

    [TestMethod]
    public async Task ClosedNameCollisionPreservesBothFilesAndInventory()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); byte[] seed = Record(LogOutputFormat.JsonLines, "first"), next = Record(LogOutputFormat.JsonLines, "next");
        using var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, seed.Length); writer.Initialize(); await Write(writer, seed);
        JsonObject inventory = ReadInventory(path); string closed = Path.Combine(root.Path, ".cis-log-" + inventory["groupId"]!.GetValue<string>() + "-00000000000000000001.closed");
        byte[] collision = Encoding.UTF8.GetBytes("unrelated collision\n"), metadata = File.ReadAllBytes(path + ".cis.inventory.json"); File.WriteAllBytes(closed, collision);
        await Assert.ThrowsAsync<IOException>(() => Write(writer, next));
        CollectionAssert.AreEqual(seed, File.ReadAllBytes(path)); CollectionAssert.AreEqual(collision, File.ReadAllBytes(closed)); CollectionAssert.AreEqual(metadata, File.ReadAllBytes(path + ".cis.inventory.json"));
        Assert.Throws<IOException>(() => { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
    }

    [TestMethod]
    public async Task FailedMetadataPublicationPreservesClosedBytesAndLeavesNoAppendableActivePath()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); byte[] seed = Record(LogOutputFormat.JsonLines, "first");
        using (var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, seed.Length))
        {
            writer.Initialize(); await Write(writer, seed); string metadata = path + ".cis.inventory.json";
            File.Delete(metadata); Directory.CreateDirectory(metadata); File.WriteAllText(Path.Combine(metadata, "keep.txt"), "keep");
            try { await Write(writer, Record(LogOutputFormat.JsonLines, "next")); Assert.Fail("Metadata publication unexpectedly succeeded."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Assert.IsFalse(File.Exists(path)); CollectionAssert.AreEqual(seed, File.ReadAllBytes(Directory.EnumerateFiles(root.Path, "*.closed").Single()));
        }
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(path + ".cis.inventory.json", "keep.txt")));
        Assert.Throws<CoreLogInitializationException>(() => { using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); }); Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task ExhaustedSegmentIdentityFailsBeforeRenamingActiveData()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); byte[] seed = Record(LogOutputFormat.JsonLines, "first");
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        JsonObject inventory = ReadInventory(path); inventory["segments"]![0]!["id"] = long.MaxValue; File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString()); File.WriteAllBytes(path, seed);
        byte[] metadata = File.ReadAllBytes(path + ".cis.inventory.json"); using var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, seed.Length); writer.Initialize();
        await Assert.ThrowsAsync<IOException>(() => Write(writer, Record(LogOutputFormat.JsonLines, "next")));
        CollectionAssert.AreEqual(seed, ReadShared(path)); CollectionAssert.AreEqual(metadata, File.ReadAllBytes(path + ".cis.inventory.json")); Assert.AreEqual(0, Directory.EnumerateFiles(root.Path, "*.closed").Count());
    }

    [TestMethod]
    public async Task FullInventoryFailsBeforeCreatingAnUnreadableNextIdentity()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); byte[] seed = Record(LogOutputFormat.JsonLines, "first"), retained = Encoding.UTF8.GetBytes("{\"retained\":true}\n");
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        JsonObject inventory = ReadInventory(path); string group = inventory["groupId"]!.GetValue<string>(); JsonArray segments = inventory["segments"]!.AsArray(); segments.Clear();
        for (int id = 1; id <= 1000; id++)
        {
            string name = ".cis-log-" + group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".closed";
            File.WriteAllBytes(Path.Combine(root.Path, name), retained); segments.Add(new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = "closed" });
        }
        segments.Add(new JsonObject { ["id"] = 1001, ["fileName"] = "active.log", ["state"] = "active" }); File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString()); File.WriteAllBytes(path, seed);
        byte[] metadata = File.ReadAllBytes(path + ".cis.inventory.json"); using var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, seed.Length); writer.Initialize();
        await Assert.ThrowsAsync<IOException>(() => Write(writer, Record(LogOutputFormat.JsonLines, "next")));
        CollectionAssert.AreEqual(seed, ReadShared(path)); CollectionAssert.AreEqual(metadata, File.ReadAllBytes(path + ".cis.inventory.json"));
        string[] files = Directory.EnumerateFiles(root.Path, "*.closed").ToArray(); Assert.AreEqual(1000, files.Length); foreach (string closed in files) { CollectionAssert.AreEqual(retained, File.ReadAllBytes(closed)); }
        Assert.AreEqual(0, Directory.EnumerateFiles(root.Path, "*.tmp").Count());
    }

    private static Task Write(FileOutputWriter writer, byte[] bytes) => writer.WriteAsync(new AdmittedEvent(new EncodedEvent(bytes, false), 1, bytes)).AsTask();
    private static byte[] Record(LogOutputFormat format, string message) => Encoding.UTF8.GetBytes(format == LogOutputFormat.JsonLines ? "{\"message\":\"" + message + "\"}\n" : "message=\"" + message + "\"\n");
    private static JsonObject ReadInventory(string path) => JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
    private static string ClosedPath(string path, JsonObject inventory) => Path.Combine(Path.GetDirectoryName(path)!, inventory["segments"]![0]!["fileName"]!.GetValue<string>());
    private static byte[] ReadShared(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var memory = new MemoryStream(); file.CopyTo(memory); return memory.ToArray(); }
}
