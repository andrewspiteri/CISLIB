using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class LoggerQualificationArchitectureTests
{
    [TestMethod]
    [DataRow("WP04-S05-AC01")]
    [DataRow("WP04-S05-AC02")]
    [DataRow("WP04-S05-AC03")]
    [DataRow("WP04-S05-AC04")]
    public void MapPreservesOriginalCriteriaAndRealIoRemainsPending(string id)
    {
        string root = Root();
        JsonObject map = Read("conformance/logging/logger-qualification-map.json");
        Assert.AreEqual("logger-admission-seam", map["implementationPhase"]!.GetValue<string>());
        Assert.AreEqual("Pending real output integration", map["fullStoryAcceptanceStatus"]!.GetValue<string>());
        Assert.AreEqual(4, map["criteria"]!.AsArray().Count);
        JsonObject criterion = map["criteria"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject original = Find(Read("planning/logging/sources/core-library-backlog-v1.json"), id)!;
        Assert.AreEqual(original["description"]!.GetValue<string>(), criterion["sourceDescription"]!.GetValue<string>());
        var caseIds = Read("conformance/logging/logger-outcome-cases.json")["cases"]!.AsArray().Select(row => row!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var mappedIds = map["criteria"]!.AsArray().SelectMany(item => item!["caseIds"]!.AsArray()).Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(caseIds.SetEquals(mappedIds), "Every current outcome case must be mapped to an acceptance criterion.");
        Assert.IsTrue(criterion["caseIds"]!.AsArray().Count > 0);
        foreach (JsonNode? item in criterion["caseIds"]!.AsArray()) { Assert.IsTrue(caseIds.Contains(item!.GetValue<string>())); }
        JsonArray inventory = Read("conformance/logging/case-inventory.json")["cases"]!.AsArray();
        foreach (JsonNode? binding in criterion["bindings"]!.AsArray().Concat(criterion["priorBindings"]!.AsArray()))
        {
            JsonObject row = inventory.Single(item => item!["id"]!.GetValue<string>() == binding!.GetValue<string>())!.AsObject();
            Assert.AreEqual("Implemented", row["implementation"]!.GetValue<string>());
            Assert.IsTrue(row["minimumExecutions"]!.GetValue<int>() > 0);
        }
        if (criterion["priorCaseIds"] is JsonArray priorCases)
        {
            string path = criterion["priorCatalog"]!.GetValue<string>();
            var priorIds = Read(path)["cases"]!.AsArray().Select(row => row!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(priorCases.Count > 0);
            foreach (JsonNode? item in priorCases) { Assert.IsTrue(priorIds.Contains(item!.GetValue<string>())); }
        }
        Assert.AreEqual(id == "WP04-S05-AC04" ? "Partially qualified" : "Qualified at logger seam", criterion["status"]!.GetValue<string>());
        if (id == "WP04-S05-AC04")
        {
            JsonArray parts = criterion["parts"]!.AsArray();
            Assert.AreEqual(3, parts.Count);
            Assert.AreEqual("Qualified at logger seam", parts[0]!["status"]!.GetValue<string>());
            Assert.AreEqual("Qualified at logger seam", parts[1]!["status"]!.GetValue<string>());
            Assert.AreEqual("Pending real output integration", parts[2]!["status"]!.GetValue<string>());
            Assert.AreEqual("WP05", parts[2]!["owner"]!.GetValue<string>());
        }
        Assert.IsTrue(File.Exists(Path.Combine(root, "docs/modules/logging/logger-outcome-qualification.md")));
    }
    private static JsonObject? Find(JsonNode node, string id)
    {
        if (node is JsonObject obj)
        {
            if (obj["id"]?.GetValue<string>() == id) { return obj; }
            foreach ((string _, JsonNode? child) in obj) { if (child is not null && Find(child, id) is { } result) { return result; } }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array) { if (child is not null && Find(child, id) is { } result) { return result; } }
        }
        return null;
    }
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root(), path)))!.AsObject();
    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "conformance", "logging"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find canonical conformance inputs.");
    }
}
