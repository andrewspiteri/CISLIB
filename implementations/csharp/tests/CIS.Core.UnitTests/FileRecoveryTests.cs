using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileRecoveryTests
{
    [TestMethod]
    [DataRow("missing-lf")]
    [DataRow("partial-json")]
    [DataRow("partial-utf8")]
    [DataRow("garbage")]
    [DataRow("earlier-record")]
    public void RecoveryPreservesTheWholePartialSegmentUnderItsPersistentIdentity(string mode)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        byte[] seed = mode switch
        {
            "missing-lf" => Encoding.UTF8.GetBytes("{\"a\":1}"),
            "partial-json" => Encoding.UTF8.GetBytes("{\"a\":"),
            "partial-utf8" => [.. Encoding.UTF8.GetBytes("{\"a\":\""), 0xf0, 0x9f],
            "garbage" => [0xff],
            _ => Encoding.UTF8.GetBytes("{\"earlier\":true}\n{\"next\":")
        };
        Seed(path, seed);
        Guid group;
        string damaged;
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            group = owner.Inventory!.GroupId;
            owner.RecoverPartialSegment(2);
            Assert.AreEqual(group, owner.Inventory.GroupId);
            Assert.AreEqual(0L, owner.ActiveFile.Length);
            Assert.AreEqual(0L, owner.ActiveFile.Position);
            Assert.AreEqual(2L, owner.Inventory.Active.Id);
            Assert.AreEqual(FileSegmentState.Damaged, owner.Inventory.Segments[0].State);
            damaged = Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged));
            Assert.AreEqual(Path.GetFileName(damaged), owner.Inventory.Segments[0].FileName);
            Assert.AreEqual("FileOwnershipUnavailable", Assert.ThrowsExactly<CoreLogInitializationException>(() => { using var second = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); }).ErrorCode);
        }
        CollectionAssert.AreEqual(seed, File.ReadAllBytes(damaged));
        Assert.AreEqual(0L, new FileInfo(path).Length);
        using var restarted = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines);
        Assert.AreEqual(group, restarted.Inventory!.GroupId);
        Assert.AreEqual(FileSegmentState.Damaged, restarted.Inventory.Segments[0].State);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("complete")]
    [DataRow("human")]
    [DataRow("raw")]
    public void IneligibleOwnersCannotRecoverOrChangeExistingBytes(string mode)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.log");
        byte[] seed = mode == "empty" ? [] : Encoding.UTF8.GetBytes("{\"a\":1}\n");
        using (mode == "raw" ? FileOutputOwner.Initialize(path) : FileOutputOwner.Initialize(path, mode == "human" ? LogOutputFormat.Human : LogOutputFormat.JsonLines)) { }
        File.WriteAllBytes(path, seed);
        Dictionary<string, string> before = Snapshot(root.Path);
        using (var owner = mode == "raw" ? FileOutputOwner.Initialize(path) : FileOutputOwner.Initialize(path, mode == "human" ? LogOutputFormat.Human : LogOutputFormat.JsonLines))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => owner.RecoverPartialSegment(2));
        }
        AssertSnapshots(before, Snapshot(root.Path));
    }

    [TestMethod]
    [DataRow("collision-file", "FileRecoveryCollision")]
    [DataRow("collision-directory", "FileRecoveryCollision")]
    [DataRow("over-bound", "FileRetentionLimitExceeded")]
    [DataRow("id-exhausted", "FileRecoveryIdentityExhausted")]
    public void PredictableRecoveryFailuresPreserveAllPreimagesAndReleaseOwnership(string mode, string code)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, Encoding.UTF8.GetBytes("{\"a\":"));
        JsonObject metadata = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
        Guid group = Guid.ParseExact(metadata["groupId"]!.GetValue<string>(), "N");
        JsonArray segments = metadata["segments"]!.AsArray();
        if (mode is "full" or "over-bound")
        {
            int count = mode == "full" ? 1 : 2;
            segments[0]!["id"] = count + 1;
            for (int id = count; id >= 1; id--)
            {
                string name = FileGroupInventory.ClosedName(group, id, FileSegmentState.Closed);
                segments.Insert(0, new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = "closed" });
                File.WriteAllText(Path.Combine(root.Path, name), "prior evidence\n");
            }
        }
        if (mode == "id-exhausted") { segments[0]!["id"] = long.MaxValue; }
        File.WriteAllText(path + ".cis.inventory.json", metadata.ToJsonString());
        string collision = Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged));
        if (mode == "collision-file") { File.WriteAllText(collision, "reserved neighbor\n"); }
        if (mode == "collision-directory") { Directory.CreateDirectory(collision); File.WriteAllText(Path.Combine(collision, "keep.txt"), "reserved neighbor\n"); }
        Dictionary<string, string> before = Snapshot(root.Path);
        using (var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, 65536, mode is "full" or "over-bound" ? 1 : 10))
        {
            Assert.AreEqual(code, Assert.ThrowsExactly<CoreLogInitializationException>(writer.Initialize).ErrorCode);
        }
        AssertSnapshots(before, Snapshot(root.Path));
        using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var active = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [TestMethod]
    public void SuccessiveRecoveriesUseDistinctIncreasingDamagedIdsWithoutOverwritingEarlierDamage()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        byte[] first = Encoding.UTF8.GetBytes("{\"first\":"), second = [.. Encoding.UTF8.GetBytes("{\"ok\":true}\n"), 0xf0, 0x9f];
        Seed(path, first);
        Guid group;
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { group = owner.Inventory!.GroupId; owner.RecoverPartialSegment(2); }
        File.WriteAllBytes(path, second);
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            owner.RecoverPartialSegment(2);
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, owner.Inventory!.Segments.Select(x => x.Id).ToArray());
            Assert.IsTrue(owner.Inventory.Segments.Take(2).All(x => x.State == FileSegmentState.Damaged));
            Assert.AreEqual(group, owner.Inventory.GroupId);
        }
        CollectionAssert.AreEqual(first, File.ReadAllBytes(Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 1, FileSegmentState.Damaged))));
        CollectionAssert.AreEqual(second, File.ReadAllBytes(Path.Combine(root.Path, FileGroupInventory.ClosedName(group, 2, FileSegmentState.Damaged))));
        Assert.AreEqual(0L, new FileInfo(path).Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StartupRecoveryDiagnosticIsOwnedStickyAndDoesNotInventDeliveryFailures(bool console)
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.jsonl");
        Seed(path, Encoding.UTF8.GetBytes("{\"prior\":"));
        var options = new LoggingOptions { FilePath = path, ConsoleLevel = console ? CoreLogLevel.Information : CoreLogLevel.None };
        options.Prepare(root.Path);
        using var borrowed = new MemoryStream();
        var runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => borrowed);
        CoreLogDiagnostics held = runtime.GetDiagnostics();
        try
        {
            LogOutputDiagnostics file = held.Outputs.Single(x => x.OutputName == "file");
            Assert.IsTrue(file.RecoveredPartialSegment);
            Assert.AreEqual(LogOutputState.Running, file.State);
            Assert.AreEqual(0L, file.Accepted);
            Assert.AreEqual(0L, file.Failed);
            Assert.IsNull(file.LastFaultCode);
            Assert.IsNull(file.LastFaultAtUtc);
            if (console) { Assert.IsFalse(held.Outputs.Single(x => x.OutputName == "console").RecoveredPartialSegment); }
            Assert.AreEqual(LogWriteOutcome.Accepted, runtime.CreateLogger("Recovery").Write(CoreLogLevel.Information, new LogEventData { EventName = "fresh" }).Outcome);
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        LogOutputDiagnostics stopped = runtime.GetDiagnostics().Outputs.Single(x => x.OutputName == "file");
        Assert.IsTrue(stopped.RecoveredPartialSegment);
        Assert.AreEqual(1L, stopped.Written);
        Assert.AreEqual(0L, stopped.Failed + stopped.Abandoned + stopped.Pending);
        Assert.AreEqual(0L, held.Outputs.Single(x => x.OutputName == "file").Accepted);
        var fresh = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => borrowed);
        try { Assert.IsFalse(fresh.GetDiagnostics().Outputs.Single(x => x.OutputName == "file").RecoveredPartialSegment); }
        finally { fresh.CloseAdmission(); await fresh.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [TestMethod]
    public void HumanPartialBoundaryStillFailsUnchangedInsteadOfJsonlRecovery()
    {
        using var root = new TemporaryDirectory();
        string path = Path.Combine(root.Path, "active.log");
        using (FileOutputOwner.Initialize(path, LogOutputFormat.Human)) { }
        File.WriteAllText(path, "prior=\"unfinished");
        Dictionary<string, string> before = Snapshot(root.Path);
        using var writer = new FileOutputWriter(path, LogOutputFormat.Human, HumanOutputDetail.Normal, 16384, 65536, 10);
        Assert.AreEqual("FileRecordBoundaryInvalid", Assert.ThrowsExactly<CoreLogInitializationException>(writer.Initialize).ErrorCode);
        Assert.IsFalse(writer.RecoveredPartialSegment);
        AssertSnapshots(before, Snapshot(root.Path));
    }

    private static void Seed(string path, byte[] bytes)
    {
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        File.WriteAllBytes(path, bytes);
    }
    private static Dictionary<string, string> Snapshot(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToBase64String(File.ReadAllBytes(path)));
    private static void AssertSnapshots(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (string path in before.Keys) { Assert.AreEqual(before[path], after[path], path); }
    }
}
