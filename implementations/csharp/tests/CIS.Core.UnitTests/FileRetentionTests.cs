using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileRetentionTests
{
    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines, 1)]
    [DataRow(LogOutputFormat.JsonLines, 2)]
    [DataRow(LogOutputFormat.JsonLines, 10)]
    [DataRow(LogOutputFormat.Human, 1)]
    [DataRow(LogOutputFormat.Human, 2)]
    [DataRow(LogOutputFormat.Human, 10)]
    public async Task NewestSegmentIdsAndBytesSurviveRepeatedRotationAndRestart(LogOutputFormat format, int retained)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        int total = retained + 6, restart = retained + 2; string? group = null;
        for (int phase = 0; phase < 2; phase++)
        {
            byte[]? previous = File.Exists(path + ".cis.inventory.json") ? File.ReadAllBytes(path + ".cis.inventory.json") : null;
            using var writer = Writer(path, format, retained); writer.Initialize();
            if (previous is not null) { CollectionAssert.AreEqual(previous, File.ReadAllBytes(path + ".cis.inventory.json")); }
            for (int id = phase == 0 ? 1 : restart + 1; id <= (phase == 0 ? restart : total); id++)
            {
                await Write(writer, Record(format, id)); JsonObject inventory = Inventory(path); group ??= inventory["groupId"]!.GetValue<string>(); Assert.AreEqual(group, inventory["groupId"]!.GetValue<string>());
                JsonArray segments = inventory["segments"]!.AsArray(); int first = Math.Max(1, id - retained); Assert.AreEqual(id - first + 1, segments.Count);
                for (int index = 0; index < segments.Count; index++)
                {
                    int expectedId = first + index; bool active = expectedId == id; string name = active ? "active.log" : Name(group, expectedId, "closed");
                    Assert.AreEqual((long)expectedId, segments[index]!["id"]!.GetValue<long>()); Assert.AreEqual(name, segments[index]!["fileName"]!.GetValue<string>()); Assert.AreEqual(active ? "active" : "closed", segments[index]!["state"]!.GetValue<string>());
                    CollectionAssert.AreEqual(Record(format, expectedId), Read(Path.Combine(root.Path, name)));
                    if (!active) { File.SetLastWriteTimeUtc(Path.Combine(root.Path, name), new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-expectedId)); }
                }
                Assert.AreEqual(segments.Count - 1, Directory.EnumerateFiles(root.Path, "*.closed").Count()); Assert.IsTrue(segments.Count - 1 <= retained);
            }
        }
    }

    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines)]
    [DataRow(LogOutputFormat.Human)]
    public async Task LowerStartupLimitPreservesAllDataAndReleasesAcquiredHandles(LogOutputFormat format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        using (var writer = Writer(path, format, 3)) { writer.Initialize(); for (int id = 1; id <= 5; id++) { await Write(writer, Record(format, id)); } }
        Dictionary<string, byte[]> before = Directory.EnumerateFiles(root.Path).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllBytes, StringComparer.Ordinal)!;
        using (var rejected = Writer(path, format, 1))
        {
            CoreLogInitializationException error = Assert.Throws<CoreLogInitializationException>(rejected.Initialize); Assert.AreEqual("file", error.OutputName); Assert.AreEqual("FileRetentionLimitExceeded", error.ErrorCode); Assert.IsNull(error.InnerException);
        }
        Assert.AreEqual(before.Count, Directory.EnumerateFiles(root.Path).Count()); foreach ((string name, byte[] bytes) in before) { CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(root.Path, name))); }
        using var retry = Writer(path, format, 3); retry.Initialize(); await Write(retry, Record(format, 6)); Assert.AreEqual(6L, Inventory(path)["segments"]!.AsArray()[^1]!["id"]!.GetValue<long>());
    }

    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines)]
    [DataRow(LogOutputFormat.Human)]
    public async Task ListedIdentityOrderCountsDamagedSegmentsAndPreservesUnlistedNeighbors(LogOutputFormat format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        using (FileOutputOwner.Initialize(path, format)) { }
        JsonObject inventory = Inventory(path); string group = inventory["groupId"]!.GetValue<string>(); string damaged = Name(group, 3, "damaged"), closed = Name(group, 7, "closed"), unlisted = Name(group, 50, "closed"), foreign = Name("ffffffffffffffffffffffffffffffff", 1, "closed");
        File.WriteAllText(Path.Combine(root.Path, damaged), "partial"); File.WriteAllBytes(Path.Combine(root.Path, closed), Record(format, 7)); File.WriteAllText(Path.Combine(root.Path, unlisted), "unlisted archive"); File.WriteAllText(Path.Combine(root.Path, foreign), "foreign group");
        inventory["segments"] = new JsonArray(Segment(3, damaged, "damaged"), Segment(7, closed, "closed"), Segment(20, "active.log", "active")); File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString()); File.WriteAllBytes(path, Record(format, 20));
        using (var writer = Writer(path, format, 2)) { writer.Initialize(); await Write(writer, Record(format, 21)); }
        JsonArray updated = Inventory(path)["segments"]!.AsArray(); CollectionAssert.AreEqual(new long[] { 7, 20, 21 }, updated.Select(x => x!["id"]!.GetValue<long>()).ToArray()); Assert.AreEqual(group, Inventory(path)["groupId"]!.GetValue<string>());
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, damaged))); CollectionAssert.AreEqual(Record(format, 7), File.ReadAllBytes(Path.Combine(root.Path, closed))); CollectionAssert.AreEqual(Record(format, 20), File.ReadAllBytes(Path.Combine(root.Path, Name(group, 20, "closed")))); CollectionAssert.AreEqual(Record(format, 21), File.ReadAllBytes(path));
        Assert.AreEqual("unlisted archive", File.ReadAllText(Path.Combine(root.Path, unlisted))); Assert.AreEqual("foreign group", File.ReadAllText(Path.Combine(root.Path, foreign)));
        using var restarted = FileOutputOwner.Initialize(path, format); Assert.AreEqual(21L, restarted.Inventory!.Active.Id);
    }

    [TestMethod]
    [DataRow(LogOutputFormat.JsonLines)]
    [DataRow(LogOutputFormat.Human)]
    public async Task ConfiguredStartupFailureJoinsWorkersWithoutPruningExistingData(LogOutputFormat format)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log");
        using (var writer = Writer(path, format, 3)) { writer.Initialize(); for (int id = 1; id <= 5; id++) { await Write(writer, Record(format, id)); } }
        Dictionary<string, byte[]> before = Directory.EnumerateFiles(root.Path).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllBytes, StringComparer.Ordinal);
        var options = new LoggingOptions { FilePath = path, FileFormat = format, FileRetainedFiles = 1 }; options.Prepare(root.Path);
        var metadata = new EventMetadata("Retention.App", "build-1", "1.2.3", "test", EnvironmentKind.Console, "11111111-1111-1111-1111-111111111111", null, new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero), "unknown", null, null, null, null, null, null, null, null);
        using var borrowed = new MemoryStream(); var workers = new List<Task>();
        Task Launch(Func<Task> work) { Task task = Task.Run(work); workers.Add(task); return task; }
        CoreLogInitializationException error = Assert.Throws<CoreLogInitializationException>(() => CoreLogRuntime.CreateConfiguredQueued(metadata, options, acquireConsole: _ => borrowed, launchWorker: Launch));
        Assert.AreEqual("FileRetentionLimitExceeded", error.ErrorCode); Assert.AreEqual(2, workers.Count); Assert.IsTrue(workers.All(x => x.IsCompletedSuccessfully)); Assert.IsTrue(borrowed.CanWrite);
        Assert.AreEqual(before.Count, Directory.EnumerateFiles(root.Path).Count()); foreach ((string name, byte[] bytes) in before) { CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(root.Path, name))); }
        using var retry = FileOutputOwner.Initialize(path, format); Assert.AreEqual(5L, retry.Inventory!.Active.Id);
    }

    private static FileOutputWriter Writer(string path, LogOutputFormat format, int retained) => new(path, format, HumanOutputDetail.Normal, 16384, Record(format, 1).Length, retained);
    private static Task Write(FileOutputWriter writer, byte[] bytes) => writer.WriteAsync(new AdmittedEvent(new EncodedEvent(bytes, false), 1, bytes)).AsTask();
    private static byte[] Record(LogOutputFormat format, int id) => Encoding.UTF8.GetBytes(format == LogOutputFormat.JsonLines ? "{\"id\":\"" + id.ToString("D3", CultureInfo.InvariantCulture) + "\"}\n" : "id=\"" + id.ToString("D3", CultureInfo.InvariantCulture) + "\"\n");
    private static JsonObject Inventory(string path) => JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
    private static JsonObject Segment(long id, string name, string state) => new() { ["id"] = id, ["fileName"] = name, ["state"] = state };
    private static string Name(string group, int id, string state) => ".cis-log-" + group + "-" + id.ToString("D20", CultureInfo.InvariantCulture) + "." + state;
    private static byte[] Read(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var memory = new MemoryStream(); file.CopyTo(memory); return memory.ToArray(); }
}
