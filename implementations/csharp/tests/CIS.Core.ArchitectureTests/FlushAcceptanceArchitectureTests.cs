using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class FlushAcceptanceArchitectureTests
{
    [TestMethod]
    public void DevelopmentFlushCasesPreserveAllFourOriginalAcceptanceCriteria() => Validate(Read());

    [TestMethod]
    [DataRow("missing")]
    [DataRow("rewritten")]
    [DataRow("unknown-case")]
    [DataRow("missing-reverse")]
    public void AcceptanceMappingRejectsMissingRewrittenOrUnboundRequirements(string mutation)
    {
        JsonObject mapping = Read();
        switch (mutation)
        {
            case "missing": mapping["criteria"]!.AsArray().RemoveAt(3); break;
            case "rewritten": mapping["criteria"]![3]!["text"] = "A weaker rule."; break;
            case "unknown-case": mapping["criteria"]![0]!["cases"]!.AsArray().Add("unknown"); break;
            case "missing-reverse": mapping["cases"]![0]!["criteria"]!.AsArray().Clear(); break;
        }
        Assert.ThrowsExactly<AssertFailedException>(() => Validate(mapping));
    }
    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx"))) { return directory.FullName; }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Run the acceptance checks from the CisLib checkout.");
        }
    }
    private static JsonObject Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "conformance/logging/flush-stress-cases.json")))!.AsObject();
    private static void Validate(JsonObject mapping)
    {
        string plan = File.ReadAllText(Path.Combine(Root, mapping["sourcePlan"]!.GetValue<string>()));
        int start = plan.IndexOf("### WP05-S08", StringComparison.Ordinal);
        int end = plan.IndexOf("#### WP05-S09", start, StringComparison.Ordinal);
        var original = plan[start..end].Split('\n').Where(l => l.StartsWith("- **WP05-S08-AC", StringComparison.Ordinal))
            .Select(l => (id: l.Split("**")[1], text: l.Split(" — ", 2, StringSplitOptions.None)[1].TrimEnd('\r'))).ToDictionary(x => x.id, x => x.text, StringComparer.Ordinal);
        JsonArray criteria = mapping["criteria"]!.AsArray(), cases = mapping["cases"]!.AsArray();
        CollectionAssert.AreEquivalent(original.Keys.ToArray(), criteria.Select(c => c!["id"]!.GetValue<string>()).ToArray());
        CollectionAssert.AreEquivalent(new[] { "repeat-write", "repeat-flush", "repeat-progress", "saturation", "cohort", "failed-flush", "concurrent", "deadline-race" }, cases.Select(c => c!["id"]!.GetValue<string>()).ToArray());
        foreach (JsonNode? criterion in criteria)
        {
            string id = criterion!["id"]!.GetValue<string>(); Assert.AreEqual(original[id], criterion["text"]!.GetValue<string>());
            string[] bindings = criterion["cases"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray(); Assert.IsTrue(bindings.Length > 0);
            CollectionAssert.AreEquivalent(cases.Where(c => c!["criteria"]!.AsArray().Any(a => a!.GetValue<string>() == id)).Select(c => c!["id"]!.GetValue<string>()).ToArray(), bindings);
        }
        foreach (JsonNode? scenario in cases)
        {
            string[] bindings = scenario!["criteria"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
            Assert.IsTrue(bindings.Length > 0 && bindings.All(original.ContainsKey));
        }
    }
}
