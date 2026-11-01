using System.Text;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class FileReviewRegressionTests
{
    [TestMethod]
    [TestCategory("ComponentIntegration")]
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
}
