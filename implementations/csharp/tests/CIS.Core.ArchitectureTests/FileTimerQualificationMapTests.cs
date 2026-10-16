using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class FileTimerQualificationMapTests
{
    private static string Root()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "conformance/logging/file-timer-qualification-map.json"))) { root = root.Parent; }
        return root?.FullName ?? throw new InvalidOperationException("Qualification source root unavailable.");
    }
    private static JsonObject Map(string root) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, "conformance/logging/file-timer-qualification-map.json")))!.AsObject();
    private static void Check(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
    private static void Validate(string root, JsonObject map)
    {
        Check(map["story"]!.GetValue<string>() == "WP05-S09" && map["task"]!.GetValue<string>() == "WP05-S09-T04", "task identity");
        string plan = File.ReadAllText(Path.Combine(root, "planning/logging/sources/core-library-implementation-plan-v1.md"));
        Dictionary<string, string> original = Regex.Matches(plan, @"^- \*\*(WP05-S09-AC0[1-4])\*\* — (.+)$", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.TrimEnd('\r'), StringComparer.Ordinal);
        JsonArray criteria = map["criteria"]!.AsArray();
        Check(criteria.Count == 4 && criteria.Select(c => c!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(original.Keys), "complete criteria");
        JsonObject lanes = map["lanes"]!.AsObject();
        Check(lanes.Select(l => l.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["interval", "timer", "lifecycle", "qualification"]), "lane identity");
        var cases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> lane in lanes)
        {
            JsonNode value = lane.Value!;
            JsonObject inventory = JsonNode.Parse(File.ReadAllText(Path.Combine(root, value["caseInventory"]!.GetValue<string>())))!.AsObject();
            cases[lane.Key] = inventory["cases"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            string fixture = File.ReadAllText(Path.Combine(root, value["fixture"]!.GetValue<string>()));
            Match declaration = Regex.Match(fixture, @"Modes = \[(.*?)\];", RegexOptions.Singleline);
            Check(declaration.Success, "fixture modes");
            var modes = Regex.Matches(declaration.Groups[1].Value, "\\\"([^\\\"]+)\\\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            Check(modes.SetEquals(cases[lane.Key]), "fixture inventory");
            string reader = File.ReadAllText(Path.Combine(root, value["verifier"]!.GetValue<string>()));
            Check(value["reportSchema"]!.GetValue<string>() == "vstest.trx/2010" && value["verifier"]!.GetValue<string>().EndsWith(".cs", StringComparison.Ordinal) && reader.Contains("OriginalTimerReaderCorruptionsRejectAfterPhysicalPositive", StringComparison.Ordinal) && File.Exists(Path.Combine(root, value["reader"]!.GetValue<string>())) && File.Exists(Path.Combine(root, value["controls"]!.GetValue<string>())), "reader schema");
        }
        foreach (JsonNode? item in criteria)
        {
            JsonObject c = item!.AsObject();
            Check(c["original"]!.GetValue<string>() == original[c["id"]!.GetValue<string>()], "original criterion");
            Check(c["limits"]!.GetValue<string>() == map["scope"]!.GetValue<string>(), "scope limits");
            Check(c["platforms"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal).SetEquals(["windows", "linux"]), "platform identity");
            Check(c["nativeLanes"]!.AsObject().Count > 0, "criterion bindings");
            if (c["id"]!.GetValue<string>() == "WP05-S09-AC03")
            {
                JsonObject contract = c["documentedContract"]!.AsObject();
                Check(contract["path"]!.GetValue<string>() == "implementations/csharp/src/CIS.Core/Logging/LoggingOptions.cs"
                    && contract["property"]!.GetValue<string>() == "FlushIntervalMilliseconds"
                    && contract["remarksPhrase"]!.GetValue<string>() == "scheduling target, not a completion or durability guarantee", "documented scheduling contract");
                string source = File.ReadAllText(Path.Combine(root, contract["path"]!.GetValue<string>()));
                Match remarks = Regex.Match(source, @"/// <remarks>(?<body>(?:(?!/// </remarks>).)*)/// </remarks>\s*public int FlushIntervalMilliseconds\b", RegexOptions.Singleline);
                Check(remarks.Success && remarks.Groups["body"].Value.Contains(contract["remarksPhrase"]!.GetValue<string>(), StringComparison.Ordinal), "documented scheduling contract");
            }

            foreach (KeyValuePair<string, JsonNode?> lane in c["nativeLanes"]!.AsObject())
            {
                Check(cases.ContainsKey(lane.Key), "bound lane");
                Check(lane.Value!.AsArray().Count > 0 && lane.Value.AsArray().All(x => cases[lane.Key].Contains(x!.GetValue<string>())), "bound case");
            }
        }
        foreach (KeyValuePair<string, JsonNode?> lane in map["supplementalCases"]!.AsObject())
        {
            Check(cases.ContainsKey(lane.Key) && lane.Value!.AsArray().All(x => cases[lane.Key].Contains(x!.GetValue<string>())), "supplemental case");
        }
        foreach (KeyValuePair<string, HashSet<string>> lane in cases)
        {
            HashSet<string> primary = criteria.SelectMany(c => c!["nativeLanes"]![lane.Key]?.AsArray() ?? []).Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            HashSet<string> supplemental = (map["supplementalCases"]![lane.Key]?.AsArray() ?? []).Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            Check(!primary.Overlaps(supplemental), "classification overlap");
            primary.UnionWith(supplemental); Check(primary.SetEquals(lane.Value), "complete classification");
        }
    }
    [TestMethod]
    public void AcceptanceMapPreservesOriginalCriteriaAndConcreteCaseBindings() => Validate(Root(), Map(Root()));
    [TestMethod]
    [DataRow("criterion")]
    [DataRow("missing")]
    [DataRow("lane")]
    [DataRow("case")]
    [DataRow("platform")]
    [DataRow("task")]
    [DataRow("unclassified")]
    [DataRow("overlap")]
    [DataRow("contract")]
    public void AcceptanceMapRejectsChangedRequirementsAndUnboundEvidence(string mutation)
    {
        string root = Root(); JsonObject map = Map(root); string reason;
        switch (mutation)
        {
            case "criterion": map["criteria"]![0]!["original"] = "weakened"; reason = "original criterion"; break;
            case "missing": map["criteria"]!.AsArray().RemoveAt(0); reason = "complete criteria"; break;
            case "lane": map["criteria"]![0]!["nativeLanes"]!["invented"] = new JsonArray("idle-min"); reason = "bound lane"; break;
            case "case": map["criteria"]![0]!["nativeLanes"]!["interval"]![0] = "invented"; reason = "bound case"; break;
            case "platform": map["criteria"]![0]!["platforms"]!.AsArray().RemoveAt(1); reason = "platform identity"; break;
            case "unclassified": map["criteria"]![0]!["nativeLanes"]!["interval"]!.AsArray().RemoveAt(2); reason = "complete classification"; break;
            case "overlap": map["supplementalCases"]!["interval"]!.AsArray().Add("idle-min"); reason = "classification overlap"; break;
            case "contract": map["criteria"]![2]!["documentedContract"]!["remarksPhrase"] = "guaranteed completion"; reason = "documented scheduling contract"; break;
            default: map["task"] = "WP05-S10-T01"; reason = "task identity"; break;
        }
        Assert.AreEqual(reason, Assert.ThrowsExactly<InvalidDataException>(() => Validate(root, map)).Message);
    }
}
