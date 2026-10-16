using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class FileRecoveryQualificationTests
{
    [TestMethod]
    public void RecoveryQualificationPreservesAllFourOriginalCriteriaAndConcreteLaneBindings()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "conformance", "logging", "file-recovery-qualification-map.json"))) { root = root.Parent; }
        Assert.IsNotNull(root); string folder = root.FullName; JsonObject map = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "conformance/logging/file-recovery-qualification-map.json")))!.AsObject();
        Assert.AreEqual("WP05-S07", map["story"]!.GetValue<string>()); Assert.AreEqual("WP05-S07-T04", map["task"]!.GetValue<string>()); JsonArray criteria = map["criteria"]!.AsArray(); Assert.AreEqual(4, criteria.Count);
        string plan = File.ReadAllText(Path.Combine(folder, "planning/logging/sources/core-library-implementation-plan-v1.md"));
        Dictionary<string, string> original = Regex.Matches(plan, @"^- \*\*(WP05-S07-AC0[1-4])\*\* — (.+)$", RegexOptions.Multiline).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.TrimEnd('\r'), StringComparer.Ordinal);
        Assert.AreEqual(4, original.Count); CollectionAssert.AreEquivalent(original.Keys.ToArray(), criteria.Select(x => x!["id"]!.GetValue<string>()).ToArray());
        string fixture = File.ReadAllText(Path.Combine(folder, "implementations/csharp/tests/fixtures/DiscoveryHost/FileRestartFixture.cs"));
        Match modeDeclaration = Regex.Match(fixture, @"Modes = \[(.*?)\];", RegexOptions.Singleline); Assert.IsTrue(modeDeclaration.Success);
        string[] restartModes = Regex.Matches(modeDeclaration.Groups[1].Value, "\\\"([^\\\"]+)\\\"").Select(match => match.Groups[1].Value).ToArray(); Assert.AreEqual(15, restartModes.Length);
        var boundRestartModes = new HashSet<string>(StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(new[] { "restart", "failure", "boundary", "corrective" }, map["lanes"]!.AsObject().Select(lane => lane.Key).ToArray());
        var literalLaneModes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> lane in map["lanes"]!.AsObject())
        {
            Assert.IsTrue(File.Exists(Path.Combine(folder, lane.Value!["verifier"]!.GetValue<string>())));
            bool native = lane.Value!["verifier"]!.GetValue<string>().EndsWith(".cs", StringComparison.Ordinal);
            if (lane.Key != "restart")
            {
                string reader = File.ReadAllText(Path.Combine(folder, lane.Value[native ? "caseSource" : "verifier"]!.GetValue<string>()));
                Match modes = Regex.Match(reader, native ? @"Modes = \[(.*?)\];" : @"(?m)^MODES=\[(.*?)\]", RegexOptions.Singleline);
                if (native && !modes.Success)
                {
                    string[] rows = Regex.Matches(reader, @"\[DataRow\(""([^""]+)""\)\]").Select(match => match.Groups[1].Value).ToArray();
                    Assert.IsTrue(rows.Length > 0); Assert.AreEqual("vstest.trx/2010", lane.Value["reportSchema"]!.GetValue<string>()); literalLaneModes.Add(lane.Key, rows); continue;
                }
                Assert.IsTrue(modes.Success);
                literalLaneModes.Add(lane.Key, Regex.Matches(modes.Groups[1].Value, native ? "\"([^\"]+)\"" : "'([^']+)'").Select(match => match.Groups[1].Value).ToArray());
            }
            if (native) { Assert.AreEqual("vstest.trx/2010", lane.Value["reportSchema"]!.GetValue<string>()); }
            else { Assert.IsTrue(File.ReadAllText(Path.Combine(folder, lane.Value["verifier"]!.GetValue<string>())).Contains("schema='" + lane.Value["reportSchema"]!.GetValue<string>() + "'", StringComparison.Ordinal)); }

        }
        foreach (JsonNode? node in criteria)
        {
            JsonObject criterion = node!.AsObject(); Assert.AreEqual(original[criterion["id"]!.GetValue<string>()], criterion["original"]!.GetValue<string>());
            CollectionAssert.AreEquivalent(new[] { "windows", "linux" }, criterion["platforms"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
            Assert.IsTrue(criterion["nativeLanes"]!.AsObject().Count > 0); Assert.AreEqual(map["scope"]!.GetValue<string>(), criterion["limits"]!.GetValue<string>());
            foreach (KeyValuePair<string, JsonNode?> lane in criterion["nativeLanes"]!.AsObject())
            {
                Assert.IsTrue(map["lanes"]!.AsObject().ContainsKey(lane.Key));
                foreach (JsonNode? identity in lane.Value!.AsArray())
                {
                    string value = identity!.GetValue<string>();
                    if (lane.Key == "restart") { CollectionAssert.Contains(restartModes, value); boundRestartModes.Add(value); }
                    else { CollectionAssert.Contains(literalLaneModes[lane.Key], value); }
                }
            }
            foreach (JsonNode? test in criterion["additionalExecutedTests"]?.AsArray() ?? [])
            {
                string[] name = test!.GetValue<string>().Split('.');
                string testSource = File.ReadAllText(Path.Combine(folder, "implementations/csharp/tests/CIS.Core.UnitTests", name[^2] + ".cs"));
                Assert.IsTrue(testSource.Contains("void " + name[^1] + "(", StringComparison.Ordinal));
            }
        }
        CollectionAssert.AreEquivalent(restartModes, boundRestartModes.ToArray());
    }
}
