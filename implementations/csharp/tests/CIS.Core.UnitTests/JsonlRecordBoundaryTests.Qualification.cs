using System.Text;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class JsonlRecordBoundaryTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    public void AbsoluteRecordCapIncludesLfAndAllowsHistoricalRecordsAboveTheNewCap(bool previousRecord, int extra)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"); using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        byte[] prefix = Encoding.UTF8.GetBytes("{\"message\":\""), suffix = Encoding.UTF8.GetBytes("\"}\n"), record = [.. prefix, .. Enumerable.Repeat((byte)'x', LoggingOptions.MaximumMaxEntryBytes + extra - prefix.Length - suffix.Length), .. suffix];
        File.WriteAllBytes(path, previousRecord ? [.. Encoding.UTF8.GetBytes("{\"prior\":true}\n"), .. record] : record);
        using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            JsonlRecordBoundary boundary = JsonlRecordBoundary.Inspect(owner); Assert.AreEqual(extra == 0 ? JsonlBoundaryStatus.Complete : JsonlBoundaryStatus.Invalid, boundary.Status); Assert.AreEqual(new FileInfo(path).Length, boundary.Length);
        }
        byte[] before = File.ReadAllBytes(path), inventory = File.ReadAllBytes(path + ".cis.inventory.json");
        using (var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, 2L * LoggingOptions.MaximumMaxEntryBytes, 10))
        {
            if (extra == 0) { writer.Initialize(); }
            else { Assert.AreEqual("FileRecordBoundaryInvalid", Assert.ThrowsExactly<CoreLogInitializationException>(writer.Initialize).ErrorCode); }
        }
        Assert.IsTrue(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "The complete original record bytes and length must remain unchanged.");
        Assert.IsTrue(inventory.AsSpan().SequenceEqual(File.ReadAllBytes(path + ".cis.inventory.json")), "The complete inventory bytes and length must remain unchanged.");
    }
}
