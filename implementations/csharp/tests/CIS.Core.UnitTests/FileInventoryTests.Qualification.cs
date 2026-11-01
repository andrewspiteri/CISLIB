using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class FileInventoryTests
{
    [TestMethod]
    [TestCategory("ComponentIntegration")]
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
}
