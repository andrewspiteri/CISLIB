using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ExceptionQualificationArchitectureTests
{
    [TestMethod]
    public void SharedMapPreservesAllOriginalAcceptanceCriteriaAndRealNativeBindings()
    {
        JsonObject map = Read("conformance/logging/exception-qualification-map.json");
        JsonObject source = Read("planning/logging/sources/core-library-backlog-v1.json");
        JsonNode story = source["stories"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == "WP04-S02")!;
        JsonArray criteria = map["criteria"]!.AsArray();
        Assert.AreEqual(4, criteria.Count);
        JsonArray groups = Read("conformance/logging/case-inventory.json")["cases"]!.AsArray();
        foreach (JsonNode? criterion in criteria)
        {
            JsonNode original = story["acceptance_criteria"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == criterion!["id"]!.GetValue<string>())!;
            Assert.AreEqual(original["description"]!.GetValue<string>(), criterion!["sourceDescription"]!.GetValue<string>());
            foreach (JsonNode? id in criterion["nativeCaseGroups"]!.AsArray())
            {
                JsonNode group = groups.Single(row => row!["id"]!.GetValue<string>() == id!.GetValue<string>())!;
                Assert.AreEqual("Implemented", group["implementation"]!.GetValue<string>());
                Assert.IsTrue(group["minimumExecutions"]!.GetValue<int>() > 0);
                Assert.IsNotNull(group["method"]);
            }
        }
        Assert.IsTrue(map["pending"]!.AsArray().Any(row => row!.GetValue<string>().Contains("encoded-entry", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SharedCatalogDeclaresRuntimeCapabilitiesAndCompleteDeterministicRows()
    {
        JsonObject catalog = Read("conformance/logging/exception-qualification-cases.json");
        JsonArray rows = catalog["cases"]!.AsArray();
        Assert.AreEqual(68, rows.Count);
        Assert.AreEqual(68, catalog["caseCount"]!.GetValue<int>());
        Assert.AreEqual(68, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(12, rows.Count(row => row!["requires"]?.GetValue<string>() == "utf16-code-unit-input"));
        Assert.AreEqual(8, rows.Count(row => !row!["expected"]!["valid"]!.GetValue<bool>()));
        Assert.AreEqual("value", catalog["adapterObservations"]!["csharp"]!["argument-invalid"]!["parameter"]!.GetValue<string>());
        foreach (JsonNode? row in rows)
        {
            Assert.IsFalse(row!["expected"]!.AsObject().ContainsKey("parameter"));
            if (!row["expected"]!["valid"]!.GetValue<bool>())
            {
                Assert.IsFalse(row["expected"]!.AsObject().ContainsKey("reads"));
                Assert.IsNotNull(row["nativeExpectedReads"]!["csharp"]);
            }
        }
        foreach (string id in new[] { "EXCEPTION-AGGREGATE-ORDER", "EXCEPTION-NESTED-AGGREGATE", "EXCEPTION-NODES-1000", "EXCEPTION-DEPTH-64", "EXCEPTION-LAST-BRANCH-LOSS", "EXCEPTION-LATER-MESSAGE-UTF16-HIGH", "EXCEPTION-LATER-STACK-UTF16-LOW", "EXCEPTION-MESSAGE-SPLIT-COMBINING", "EXCEPTION-STACK-SPLIT-COMBINING", "EXCEPTION-EMPTY-AGGREGATE-AT-DEPTH-4", "EXCEPTION-UNICODE-NONCHARACTER" })
        {
            Assert.IsTrue(rows.Any(row => row!["id"]!.GetValue<string>() == id));
        }
    }

    private static JsonObject Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate)) { return JsonNode.Parse(File.ReadAllText(candidate))!.AsObject(); }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Shared exception input unavailable.", path);
    }
}
