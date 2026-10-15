using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileReviewRegressionTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(10)]
    [DataRow(1000)]
    public async Task PartialRecoveryAtFullRetentionPreservesDamageAndWritesFreshJsonl(int limit)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        Guid group = Seed(path, limit, partial: true);
        string neighbor = Path.Combine(root.Path, ".cis-log-foreign.closed");
        File.WriteAllText(neighbor, "neighbor evidence");
        byte[] partial = File.ReadAllBytes(path);
        byte[] record = Encoding.UTF8.GetBytes("{\"fresh\":true}\n");
        using (var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 65536, 1048576, limit))
        {
            writer.Initialize();
            Assert.IsTrue(writer.RecoveredPartialSegment);
            await writer.WriteAsync(new AdmittedEvent(new EncodedEvent(record, false), 1, record));
            await writer.FlushAsync();
        }
        CollectionAssert.AreEqual(record, File.ReadAllBytes(path));
        string damaged = Path.Combine(root.Path, FileGroupInventory.ClosedName(group, limit + 1, FileSegmentState.Damaged));
        CollectionAssert.AreEqual(partial, File.ReadAllBytes(damaged));
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Closed))));
        Assert.AreEqual("neighbor evidence", File.ReadAllText(neighbor));
        using var restarted = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines, limit);
        Assert.AreEqual(limit + 1, restarted.Inventory!.Segments.Count);
        Assert.AreEqual(limit + 2L, restarted.Inventory.Active.Id);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    public void PublishedIntentRecoversBeforeAndBetweenExactRetirements(bool recovery, int deleted)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, 3, recovery);
        byte[] original = File.ReadAllBytes(path);
        FileGroupInventory next;
        using (FileOutputOwner owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            next = recovery ? owner.Inventory!.AfterPartialRecovery(path, 1) : owner.Inventory!.AfterRotation(path, 1);
            FileGroupTransition.Begin(owner, next, 1, recovery);
            for (int index = 0; index < deleted; index++)
            {
                File.Delete(Path.Combine(root.Path, owner.Inventory!.Segments[index].FileName));
            }
        }
        using (FileOutputOwner resumed = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines, 1))
        {
            Assert.IsTrue(resumed.Inventory!.HasSameIdentity(next));
            Assert.AreEqual(recovery, resumed.RecoveredPartialSegment);
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(root.Path, next.Segments[^2].FileName)));
        Assert.IsFalse(File.Exists(FileGroupTransition.PendingPath(path)));
        Assert.AreEqual(1, Directory.EnumerateFiles(root.Path, ".cis-log-*").Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IntentPublicationCollisionCannotDeleteAnyRetainedMember(bool recovery)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, 1, recovery);
        string candidate = FileMetadataPublication.CandidatePath(FileGroupTransition.PendingPath(path));
        File.WriteAllText(candidate, "foreign occupied publication slot");
        var before = Directory.EnumerateFiles(root.Path).ToDictionary(name => name, File.ReadAllBytes);
        using FileOutputOwner owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines);
        CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() =>
        {
            if (recovery) { owner.RecoverPartialSegment(1); }
            else { owner.Rotate(1); }
        });
        Assert.AreEqual("FileMetadataCandidateUnavailable", error.ErrorCode);
        owner.Dispose();
        foreach ((string name, byte[] bytes) in before) { CollectionAssert.AreEqual(bytes, File.ReadAllBytes(name)); }
        Assert.IsFalse(File.Exists(FileGroupTransition.PendingPath(path)));
    }

    [TestMethod]
    public void FailedPrivatePublicationsDoNotAccumulateOrRemoveForeignCandidates()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "inventory.json");
        string neighbor = path + ".foreign.tmp";
        File.WriteAllText(neighbor, "preserve");
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Assert.ThrowsExactly<IOException>(() => FileMetadataPublication.Publish(path, writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("attempt", attempt);
                throw new IOException("controlled failure after candidate creation");
            }, overwrite: false));
            Assert.IsFalse(File.Exists(FileMetadataPublication.CandidatePath(path)));
        }
        Assert.AreEqual(1, Directory.EnumerateFiles(root.Path).Count());
        FileMetadataPublication.Publish(path, writer => { writer.WriteStartObject(); writer.WriteBoolean("ready", true); writer.WriteEndObject(); }, overwrite: false);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.IsTrue(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.AreEqual("preserve", File.ReadAllText(neighbor));
    }

    [TestMethod]
    public void RetirementDeleteFailureKeepsThePublishedPlanAndDoesNotGrowTheGroup()
    {
        if (!OperatingSystem.IsWindows()) { return; } // Native Unix denied-directory cases use the integration lane.
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, 1, partial: false);
        string retired;
        using (FileOutputOwner owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            retired = Path.Combine(root.Path, owner.Inventory!.Segments[0].FileName);
            using var held = new FileStream(retired, FileMode.Open, FileAccess.Read, FileShare.Read);
            FileOutputWriteException error = Assert.ThrowsExactly<FileOutputWriteException>(() => owner.Rotate(1));
            Assert.AreEqual("RetentionDeleteFailed", error.FaultCode);
            Assert.IsTrue(File.Exists(FileGroupTransition.PendingPath(path)));
            Assert.AreEqual(1, Directory.EnumerateFiles(root.Path, ".cis-log-*").Count());
            Assert.IsTrue(File.Exists(path));
        }
        using FileOutputOwner resumed = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines, 1);
        Assert.AreEqual(2, resumed.Inventory!.Segments.Count);
        Assert.IsFalse(File.Exists(retired));
    }

    [TestMethod]
    public void LegacyIntentStillRecoversItsAlreadyRetiredExactPrefix()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, 2, partial: false);
        using (FileOutputOwner owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            FileGroupInventory next = owner.Inventory!.AfterRotation(path, 1);
            FileGroupTransition intent = FileGroupTransition.Begin(owner, next, 1, recovery: false);
            intent.Retire(path);
        }
        string pending = FileGroupTransition.PendingPath(path);
        JsonObject legacy = JsonNode.Parse(File.ReadAllBytes(pending))!.AsObject();
        legacy["schema"] = 1; legacy.Remove("retirements"); File.WriteAllText(pending, legacy.ToJsonString());
        using FileOutputOwner resumed = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines, 1);
        Assert.AreEqual(2, resumed.Inventory!.Segments.Count); Assert.AreEqual(4L, resumed.Inventory.Active.Id);
        Assert.IsFalse(File.Exists(pending));
    }

    private static Guid Seed(string path, int retained, bool partial)
    {
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Guid group = Guid.ParseExact(inventory["groupId"]!.GetValue<string>(), "N");
        JsonArray segments = inventory["segments"]!.AsArray();
        segments.Clear();
        for (int id = 1; id <= retained; id++)
        {
            string name = FileGroupInventory.ClosedName(group, id, FileSegmentState.Closed);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, name), "{\"previous\":" + id + "}\n");
            segments.Add(new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = "closed" });
        }
        segments.Add(new JsonObject { ["id"] = retained + 1, ["fileName"] = Path.GetFileName(path), ["state"] = "active" });
        File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString());
        File.WriteAllText(path, partial ? "{\"partial\":" : "{\"complete\":true}\n");
        return group;
    }
}
