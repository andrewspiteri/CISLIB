using System.Text;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed partial class JsonlRecordBoundaryTests
{
    [TestMethod]
    [DataRow("empty", "Empty")]
    [DataRow("object", "Complete")]
    [DataRow("unicode", "Complete")]
    [DataRow("two-records", "Complete")]
    [DataRow("missing-lf", "Partial")]
    [DataRow("partial-json", "Partial")]
    [DataRow("partial-utf8", "Partial")]
    [DataRow("garbage-tail", "Partial")]
    [DataRow("invalid-json", "Invalid")]
    [DataRow("array", "Invalid")]
    [DataRow("null", "Invalid")]
    [DataRow("blank", "Invalid")]
    [DataRow("crlf", "Invalid")]
    [DataRow("invalid-utf8", "Invalid")]
    [DataRow("bom", "Invalid")]
    public void ManagedBoundaryInspectionClassifiesOnlyTheFinalRecordWithoutMutation(string mode, string expected)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.jsonl"); using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        byte[] bytes = mode switch
        {
            "empty" => [],
            "object" => Encoding.UTF8.GetBytes("{\"a\":1}\n"),
            "unicode" => Encoding.UTF8.GetBytes("{\"message\":\"\u03a9 \U0001f642 e\u0301\"}\n"),
            "two-records" => Encoding.UTF8.GetBytes("{\"first\":true}\n{\"next\":true}\n"),
            "missing-lf" => Encoding.UTF8.GetBytes("{\"a\":1}"),
            "partial-json" => Encoding.UTF8.GetBytes("{\"first\":true}\n{\"next\":"),
            "partial-utf8" => [.. Encoding.UTF8.GetBytes("{\"message\":\""), 0xf0, 0x9f],
            "garbage-tail" => [.. Encoding.UTF8.GetBytes("{\"first\":true}\n"), 0xff],
            "invalid-json" => Encoding.UTF8.GetBytes("{\"a\":\n"),
            "array" => Encoding.UTF8.GetBytes("[]\n"),
            "null" => Encoding.UTF8.GetBytes("null\n"),
            "blank" => [(byte)'\n'],
            "crlf" => Encoding.UTF8.GetBytes("{}\r\n"),
            "invalid-utf8" => [.. Encoding.UTF8.GetBytes("{\"a\":\""), 0xff, .. Encoding.UTF8.GetBytes("\"}\n")],
            _ => [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("{}\n")]
        };
        File.WriteAllBytes(path, bytes); byte[] inventory = File.ReadAllBytes(path + ".cis.inventory.json"); using (var owner = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines))
        {
            owner.ActiveFile.Position = bytes.Length == 0 ? 0 : 1; long original = owner.ActiveFile.Position; JsonlRecordBoundary result = JsonlRecordBoundary.Inspect(owner);
            Assert.AreEqual(expected, result.Status.ToString()); Assert.AreEqual(bytes.LongLength, result.Length); Assert.AreEqual(original, owner.ActiveFile.Position);
            Assert.ThrowsExactly<IOException>(() => { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        }
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path)); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json")); Assert.AreEqual(3, Directory.EnumerateFiles(root.Path).Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnvalidatedOrHumanOwnersCannotPerformJsonlInspection(bool human)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); using var owner = human ? FileOutputOwner.Initialize(path, LogOutputFormat.Human) : FileOutputOwner.Initialize(path);
        owner.ActiveFile.Position = 7; Assert.ThrowsExactly<InvalidOperationException>(() => JsonlRecordBoundary.Inspect(owner)); Assert.AreEqual(7L, owner.ActiveFile.Position); Assert.AreEqual(0L, owner.ActiveFile.Length);
    }
}
