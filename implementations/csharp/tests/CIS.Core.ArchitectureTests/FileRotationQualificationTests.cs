using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class FileRotationQualificationTests
{
    [TestMethod]
    public void RotationQualificationPreservesAllFourOriginalCriteriaAndConcreteLaneBindings()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "conformance", "logging", "file-rotation-qualification-map.json"))) { root = root.Parent; }
        Assert.IsNotNull(root); string folder = root.FullName; JsonObject map = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "conformance/logging/file-rotation-qualification-map.json")))!.AsObject();
        Assert.AreEqual("WP05-S06", map["story"]!.GetValue<string>()); Assert.AreEqual("WP05-S06-T04", map["task"]!.GetValue<string>()); JsonArray criteria = map["criteria"]!.AsArray(); Assert.AreEqual(4, criteria.Count);
        string plan = File.ReadAllText(Path.Combine(folder, "planning/logging/sources/core-library-implementation-plan-v1.md"));
        Dictionary<string, string> original = Regex.Matches(plan, @"^- \*\*(WP05-S06-AC0[1-4])\*\* — (.+)$", RegexOptions.Multiline).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.TrimEnd('\r'), StringComparer.Ordinal);
        Assert.AreEqual(4, original.Count); CollectionAssert.AreEquivalent(original.Keys.ToArray(), criteria.Select(x => x!["id"]!.GetValue<string>()).ToArray());
        CollectionAssert.AreEqual(new[] { "write-jsonl-os-limit" }, map["platformSupplemental"]!["linux"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray()); Assert.AreEqual(0, map["platformSupplemental"]!["windows"]!.AsArray().Count);
        foreach (JsonNode? node in criteria)
        {
            JsonObject criterion = node!.AsObject(); Assert.AreEqual(original[criterion["id"]!.GetValue<string>()], criterion["original"]!.GetValue<string>());
            CollectionAssert.AreEquivalent(new[] { "windows", "linux" }, criterion["platforms"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
            Assert.IsTrue(criterion["nativeLanes"]!.AsObject().Count > 0 && criterion["supplemental"]!.AsArray().Count > 0); Assert.IsTrue(criterion["limits"]!.GetValue<string>().Length > 0);
        }
    }
}
