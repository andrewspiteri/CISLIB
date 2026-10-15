using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileTransitionTests
{
    [TestMethod]
    [DataRow(false, "intent")]
    [DataRow(false, "renamed")]
    [DataRow(false, "published")]
    [DataRow(false, "created")]
    [DataRow(true, "intent")]
    [DataRow(true, "renamed")]
    [DataRow(true, "published")]
    [DataRow(true, "created")]
    public void KnownTransitionsResumeExactlyOnceWithoutChangingSurvivingEvidence(bool recovery, string stage)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "\u03a9-\U0001f642.jsonl");
        byte[] original = Encoding.UTF8.GetBytes(recovery ? "{\"earlier\":true}\n{\"partial\":" : "{\"earlier\":true}\n");
        FileGroupInventory next = Seed(path, original, recovery, stage);
        string archived = Path.Combine(root.Path, next.Segments[^2].FileName), neighbor = Path.Combine(root.Path, "neighbor");
        File.WriteAllBytes(neighbor, [0xff, 0xfe, 0x00]);
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines, 2, 65536))
        {
            Assert.IsTrue(owner.Inventory!.HasSameIdentity(next));
            Assert.AreEqual(recovery, owner.RecoveredPartialSegment);
            Assert.AreEqual(0L, owner.ActiveFile.Length);
            Assert.Throws<IOException>(() => { using var second = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        }
        CollectionAssert.AreEqual(original, File.ReadAllBytes(archived));
        CollectionAssert.AreEqual(new byte[] { 0xff, 0xfe, 0 }, File.ReadAllBytes(neighbor));
        Assert.IsFalse(File.Exists(FileGroupTransition.PendingPath(path)));
        using var restarted = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines);
        Assert.IsTrue(restarted.Inventory!.HasSameIdentity(next));
        Assert.IsFalse(restarted.RecoveredPartialSegment);
    }

    [TestMethod]
    [DataRow("both")]
    [DataRow("neither")]
    [DataRow("altered-original")]
    [DataRow("altered-archive")]
    [DataRow("nonempty-fresh")]
    [DataRow("missing-survivor")]
    [DataRow("retired-reappeared")]
    [DataRow("wrong-inventory")]
    [DataRow("wrong-next")]
    [DataRow("wrong-hash")]
    [DataRow("duplicate")]
    [DataRow("numeric-type")]
    [DataRow("bom")]
    [DataRow("partial-intent")]
    [DataRow("human-recovery")]
    [DataRow("exhausted")]
    [DataRow("recorded-bound")]
    [DataRow("missing-inventory")]
    [DataRow("foreign-with-lower-bound")]
    public void AmbiguousOrInvalidTransitionFailsBeforeChangingAnyExistingBytes(string mode)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        byte[] original = Encoding.UTF8.GetBytes("{\"prior\":true}\n");
        string stage = mode is "altered-archive" or "nonempty-fresh" ? "created" : "intent";
        FileGroupInventory next = Seed(path, original, recovery: false, stage);
        string archived = Path.Combine(root.Path, next.Segments[^2].FileName);
        JsonObject intent = JsonNode.Parse(File.ReadAllBytes(FileGroupTransition.PendingPath(path)))!.AsObject();
        switch (mode)
        {
            case "human-recovery":
                intent["recovery"] = true; intent["previous"]!["format"] = "human"; intent["next"]!["format"] = "human";
                JsonObject human = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject(); human["format"] = "human"; File.WriteAllText(path + ".cis.inventory.json", human.ToJsonString()); break;
            case "exhausted": intent["previous"]!["segments"]!.AsArray()[^1]!["id"] = long.MaxValue; break;
            case "recorded-bound": intent["recovery"] = true; intent["retainedLimit"] = 1; break;
            case "missing-inventory": File.Delete(path + ".cis.inventory.json"); break;
            case "foreign-with-lower-bound":
                JsonObject foreign = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject(); foreign["groupId"] = "ffffffffffffffffffffffffffffffff"; File.WriteAllText(path + ".cis.inventory.json", foreign.ToJsonString()); break;
            case "both": File.WriteAllBytes(archived, original); break;
            case "neither": File.Delete(path); break;
            case "altered-original": File.WriteAllBytes(path, Encoding.UTF8.GetBytes("{\"other\":true}\n")); break;
            case "altered-archive": File.WriteAllBytes(archived, Encoding.UTF8.GetBytes("{\"other\":true}\n")); break;
            case "nonempty-fresh": File.WriteAllText(path, "{\"fresh\":true}\n"); break;
            case "missing-survivor": File.Delete(Path.Combine(root.Path, next.Segments[0].FileName)); break;
            case "retired-reappeared":
                string retired = intent["previous"]!["segments"]![0]!["fileName"]!.GetValue<string>();
                File.WriteAllText(Path.Combine(root.Path, retired), "reappeared"); break;
            case "wrong-inventory":
                JsonObject metadata = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
                metadata["groupId"] = "ffffffffffffffffffffffffffffffff"; File.WriteAllText(path + ".cis.inventory.json", metadata.ToJsonString()); break;
            case "wrong-next": intent["next"]!["segments"]!.AsArray()[^1]!["id"] = 9L; break;
            case "wrong-hash": intent["originalSha256"] = new string('0', 64); break;
            case "numeric-type": intent["schema"] = "1"; break;
            case "partial-intent": File.WriteAllText(FileGroupTransition.PendingPath(path), "{\"schema\":"); break;
            case "duplicate": File.WriteAllText(FileGroupTransition.PendingPath(path), intent.ToJsonString().Replace("{", "{\"schema\":1,", StringComparison.Ordinal)); break;
            case "bom": File.WriteAllBytes(FileGroupTransition.PendingPath(path), [0xef, 0xbb, 0xbf, .. File.ReadAllBytes(FileGroupTransition.PendingPath(path))]); break;
        }
        if (mode is "wrong-next" or "wrong-hash" or "numeric-type" or "human-recovery" or "exhausted" or "recorded-bound") { File.WriteAllText(FileGroupTransition.PendingPath(path), intent.ToJsonString()); }
        Dictionary<string, string> before = Snapshot(root.Path);
        CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() => { using var owner = FileOutputOwner.Initialize(path, mode == "human-recovery" ? LogOutputFormat.Human : LogOutputFormat.JsonLines, mode == "foreign-with-lower-bound" ? 1 : 2, mode == "foreign-with-lower-bound" ? 1 : 65536); });
        Assert.AreEqual("FileTransitionIncompatible", error.ErrorCode);
        Assert.IsNull(error.InnerException);
        Assert.AreEqual(0, error.Data.Count);
        AssertSnapshots(before, Snapshot(root.Path));
        using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewlySelectedBoundsFailBeforeCompletingAnInterruptedTransition(bool size)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        FileGroupInventory next = Seed(path, Encoding.UTF8.GetBytes("{\"prior\":true}\n"), recovery: false, "renamed");
        Dictionary<string, string> before = Snapshot(root.Path);
        CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() => { using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines, size ? 2 : 1, size ? 1 : 65536); });
        Assert.AreEqual(size ? "FileSizeLimitExceeded" : "FileRetentionLimitExceeded", error.ErrorCode);
        AssertSnapshots(before, Snapshot(root.Path));
    }

    [TestMethod]
    public void RetirementBeforeIntentCannotBeGuessedAndFailsUnchanged()
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, Encoding.UTF8.GetBytes("{\"prior\":true}\n"), recovery: false, "intent");
        File.Delete(FileGroupTransition.PendingPath(path));
        Dictionary<string, string> before = Snapshot(root.Path);
        Assert.AreEqual("FileInventoryIncompatible", Assert.ThrowsExactly<CoreLogInitializationException>(() => { using var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); }).ErrorCode);
        AssertSnapshots(before, Snapshot(root.Path));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void NormalRotationRetiresDamagedAndClosedMembersByPersistentId(int limit)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl");
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        File.WriteAllText(path, "{\"partial\":");
        Guid group;
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { group = owner.Inventory!.GroupId; owner.RecoverPartialSegment(2); }
        string damage = Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged));
        File.WriteAllText(path, "{\"complete\":true}\n");
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { owner.Rotate(limit); Assert.AreEqual(limit + 1, owner.Inventory!.Segments.Count); Assert.AreEqual(3L, owner.Inventory.Active.Id); }
        Assert.AreEqual(limit == 2, File.Exists(damage));
        string closed = Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 2, FileSegmentState.Closed));
        Assert.IsTrue(File.Exists(closed));
        Assert.IsFalse(File.Exists(FileGroupTransition.PendingPath(path)));
    }

    private static FileGroupInventory Seed(string path, byte[] original, bool recovery, string stage)
    {
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        JsonObject metadata = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Guid group = Guid.ParseExact(metadata["groupId"]!.GetValue<string>(), "N");
        JsonArray entries = metadata["segments"]!.AsArray(); entries.Clear();
        int retained = recovery ? 1 : 2;
        for (int id = 1; id <= retained; id++)
        {
            FileSegmentState state = id == 1 ? FileSegmentState.Damaged : FileSegmentState.Closed;
            string name = FileGroupInventory.ClosedName(group, id, state);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, name), id == 1 ? "older damage" : "{\"older\":true}\n");
            entries.Add(new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = state == FileSegmentState.Damaged ? "damaged" : "closed" });
        }
        entries.Add(new JsonObject { ["id"] = retained + 1, ["fileName"] = Path.GetFileName(path), ["state"] = "active" });
        File.WriteAllText(path + ".cis.inventory.json", metadata.ToJsonString()); File.WriteAllBytes(path, original);
        FileGroupInventory next;
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            next = recovery ? owner.Inventory!.AfterPartialRecovery(path, 2) : owner.Inventory!.AfterRotation(path, 2);
            FileGroupTransition.Begin(owner, next, 2, recovery);
            if (!recovery) { File.Delete(Path.Combine(Path.GetDirectoryName(path)!, owner.Inventory!.Segments[0].FileName)); }
        }
        if (stage != "intent") { File.Move(path, Path.Combine(Path.GetDirectoryName(path)!, next.Segments[^2].FileName)); }
        if (stage is "published" or "created") { next.PublishReplacement(path); }
        if (stage == "created") { File.WriteAllBytes(path, []); }
        return next;
    }

    private static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToBase64String(File.ReadAllBytes(path)));
    private static void AssertSnapshots(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (string name in before.Keys) { Assert.AreEqual(before[name], after[name], name); }
    }
}
