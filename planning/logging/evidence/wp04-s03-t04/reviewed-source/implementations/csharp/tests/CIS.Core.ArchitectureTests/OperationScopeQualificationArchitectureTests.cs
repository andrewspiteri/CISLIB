using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class OperationScopeQualificationArchitectureTests
{
    [TestMethod]
    [DataRow("WP04-S03-AC01")]
    [DataRow("WP04-S03-AC02")]
    [DataRow("WP04-S03-AC03")]
    [DataRow("WP04-S03-AC04")]
    public void QualificationMapPreservesSourceCriteriaAndThePendingRuntimeBoundary(string id)
    {
        JsonObject map = Read("scope-qualification-map.json");
        Assert.AreEqual("internal-scope-state", map["implementationPhase"]!.GetValue<string>());
        Assert.AreEqual("Pending", map["publicRuntimeIntegrationStatus"]!.GetValue<string>());
        JsonObject criterion = map["criteria"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        string root = RepositoryRoot();
        JsonNode source = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "planning", "logging", "sources", "core-library-backlog-v1.json")))!;
        JsonObject original = Find(source, id)!;
        Assert.AreEqual(original["description"]!.GetValue<string>(), criterion["sourceDescription"]!.GetValue<string>());
        var caseIds = Read("scope-qualification-cases.json")["cases"]!.AsArray().Select(row => row!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (JsonNode? row in criterion["sharedCases"]!.AsArray()) { Assert.IsTrue(caseIds.Contains(row!.GetValue<string>())); }
        JsonObject inventory = Read("case-inventory.json");
        foreach (JsonNode? group in criterion["nativeCaseGroups"]!.AsArray().Concat(criterion["priorNativeCaseGroups"]!.AsArray()).Concat(map["retainedPriorNativeCaseGroups"]!.AsArray()))
        {
            JsonObject actual = inventory["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == group!.GetValue<string>())!.AsObject();
            Assert.AreEqual("Implemented", actual["implementation"]!.GetValue<string>());
            Assert.IsTrue(actual["minimumExecutions"]!.GetValue<int>() > 0);
        }
        foreach (JsonNode? path in criterion["guidance"]!.AsArray()) { Assert.IsTrue(File.Exists(Path.Combine(root, path!.GetValue<string>()))); }
        if (criterion["requiredGuidance"] is JsonObject rules)
        {
            foreach ((string path, JsonNode? requirements) in rules)
            {
                string guidance = File.ReadAllText(Path.Combine(root, path));
                foreach (JsonNode? requirement in requirements!.AsArray()) { Assert.IsTrue(guidance.Contains(requirement!.GetValue<string>(), StringComparison.Ordinal)); }
            }
        }
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

    private static JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "conformance", "logging", name)))!.AsObject();
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "conformance", "logging"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find canonical conformance inputs.");
    }
}
