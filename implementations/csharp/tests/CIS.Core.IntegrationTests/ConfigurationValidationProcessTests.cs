using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ConfigurationValidationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> SharedCases => FixtureProcessChecks.Cases("passive-configuration-validation-cases.json", 24).Select(row => new object[] { (string)row!["id"]! });

    [TestMethod]
    [DynamicData(nameof(SharedCases))]
    public async Task CompleteValidationRemainsSafeAndStableAcrossActualChildDirectoryChanges(string id)
    {
        string inputs = FixtureProcessChecks.Find("passive-configuration-validation-cases.json");
        JsonNode row = FixtureProcessChecks.Cases("passive-configuration-validation-cases.json", 24).Single(row => (string?)row!["id"] == id)!;
        using var owned = new TemporaryDirectory();
        string before = Path.Combine(owned.Path, "before"), after = Path.Combine(owned.Path, "after");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        (JsonObject result, _) = await FixtureProcessChecks.RunAsync(["configuration-validation", inputs, id, after], before, TestContext, inputs);
        JsonNode expected = row["expected"]!;
        bool valid = (string?)expected["status"] == "Valid";
        Assert.AreEqual((string?)expected["status"], (string?)result["status"]);
        CollectionAssert.AreEquivalent(FixtureProcessChecks.Issues(expected["issues"]!), FixtureProcessChecks.Issues(result["issues"]!));
        Assert.IsTrue(result["issues"]!.AsArray().All(issue => !string.IsNullOrWhiteSpace((string?)issue!["explanation"])));
        JsonNode observed = result["observed"]!;
        Assert.AreEqual(id, (string?)observed["caseId"]);
        Assert.AreEqual(before, (string?)observed["initialActualDirectory"]);
        Assert.AreEqual(after, (string?)observed["afterActualDirectory"]);
        Assert.AreEqual(1, observed["directoryReads"]!.GetValue<int>());
        Assert.IsTrue(observed["storedProjectionStable"]!.GetValue<bool>());
        CollectionAssert.AreEqual(ConfigurationReadExpectations.Expected(row["inputs"]!.AsObject()), observed["recognizedReads"]!.AsArray().Select(name => (string)name!).ToArray());
        if (valid)
        {
            string? relative = (string?)expected["filePath"]?["input"];
            Assert.AreEqual(relative is null ? null : Path.GetFullPath(relative, before), (string?)result["filePath"]);
            Assert.AreEqual(before, (string?)result["workingDirectory"]);
            Assert.IsNull(result["message"]);
            Assert.IsNull(result["exceptionText"]);
            Assert.IsTrue(result.ContainsKey("values"));
            foreach (string name in new[] { "consoleEnabled", "fileEnabled" })
            {
                if (expected.AsObject().ContainsKey(name)) { Assert.IsTrue(JsonNode.DeepEquals(expected[name], result[name])); }
            }
        }
        else
        {
            Assert.IsFalse(result.ContainsKey("values"));
            Assert.IsNull(result["filePath"]);
            Assert.IsNull(result["workingDirectory"]);
            Assert.AreEqual("Core configuration is invalid. Inspect Issues for details.", (string?)result["message"]);
            StringAssert.StartsWith((string)result["exceptionText"]!, "CIS.Configuration.CoreConfigurationException:");
        }
        if (expected["values"] is JsonObject subset) { FixtureProcessChecks.Subset(subset, result["values"]!); }
        string diagnostic = (string?)result["message"] + (string?)result["exceptionText"] + string.Join("|", result["issues"]!.AsArray().Select(issue => (string?)issue!["explanation"]));
        foreach (JsonNode? forbidden in row["diagnosticsMustExclude"]!.AsArray())
        {
            Assert.IsFalse(result.ToJsonString().Contains((string)forbidden!, StringComparison.Ordinal));
            Assert.IsFalse(diagnostic.Contains((string)forbidden!, StringComparison.Ordinal));
        }
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(before).Count());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(after).Count());
        Assert.AreEqual(2, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }
}
