using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class FileRotationTests
{
    [TestMethod]
    [TestCategory("ComponentIntegration")]
    [DataRow(999)]
    [DataRow(1000)]
    public async Task MaximumInventoryRotatesWithinTheReadableBound(int closedCount)
    {
        using var root = new TemporaryDirectory(); string path = Path.Combine(root.Path, "active.log"); byte[] seed = Record(LogOutputFormat.JsonLines, "first"), retained = Encoding.UTF8.GetBytes("{\"retained\":true}\n");
        using (FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines)) { }
        JsonObject inventory = ReadInventory(path); string group = inventory["groupId"]!.GetValue<string>(); JsonArray segments = inventory["segments"]!.AsArray(); segments.Clear();
        for (int id = 1; id <= closedCount; id++)
        {
            string name = ".cis-log-" + group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".closed";
            File.WriteAllBytes(Path.Combine(root.Path, name), retained); segments.Add(new JsonObject { ["id"] = id, ["fileName"] = name, ["state"] = "closed" });
        }
        segments.Add(new JsonObject { ["id"] = closedCount + 1, ["fileName"] = "active.log", ["state"] = "active" }); File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString()); File.WriteAllBytes(path, seed);
        byte[] next = Record(LogOutputFormat.JsonLines, "next");
        using (var writer = new FileOutputWriter(path, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384, seed.Length, 1000)) { writer.Initialize(); await Write(writer, next); }
        JsonArray updated = ReadInventory(path)["segments"]!.AsArray(); Assert.AreEqual(1001, updated.Count); Assert.AreEqual((long)closedCount + 2, updated[^1]!["id"]!.GetValue<long>());
        Assert.AreEqual(closedCount == 1000 ? 2L : 1L, updated[0]!["id"]!.GetValue<long>());
        CollectionAssert.AreEqual(next, File.ReadAllBytes(path));
        for (int index = 0; index < updated.Count - 1; index++)
        {
            string closed = Path.Combine(root.Path, updated[index]!["fileName"]!.GetValue<string>());
            CollectionAssert.AreEqual(index == updated.Count - 2 ? seed : retained, File.ReadAllBytes(closed));
        }
        Assert.AreEqual(1000, Directory.EnumerateFiles(root.Path, "*.closed").Count()); Assert.AreEqual(0, Directory.EnumerateFiles(root.Path, "*.tmp").Count());
        using var restarted = FileOutputOwner.Initialize(path, LogOutputFormat.JsonLines); Assert.AreEqual(1001, restarted.Inventory!.Segments.Count);
    }
}
