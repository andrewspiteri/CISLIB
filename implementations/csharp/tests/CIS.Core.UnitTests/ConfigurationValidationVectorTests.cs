using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationValidationVectorTests
{
    public static IEnumerable<object[]> CombinedCases => Read("passive-configuration-validation-cases.json")["cases"]!.AsArray().Select(row => new object[] { Text(row!, "id") });
    public static IEnumerable<object[]> PerKeyCases => Read("configuration-cases.json")["cases"]!.AsArray()
        .Where(row => row!["lanes"] is not JsonArray lanes || lanes.Any(lane => lane!.GetValue<string>() == (OperatingSystem.IsWindows() ? "windows-x64" : "linux-ubuntu2404-x64")))
        .Select(row => new object[] { Text(row!, "id") });

    [TestMethod]
    [DynamicData(nameof(CombinedCases))]
    public void CompleteValidationMatchesCombinedBoundaryPriorityAndSafeErrorFixtures(string id)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(Read("passive-configuration-validation-cases.json"), id);
        using var owned = new TemporaryDirectory();
        (PreparedConfiguration? snapshot, CoreConfigurationException? error, EnvironmentVariableStub source) = Execute(row["inputs"]!.AsObject(), owned.Path);
        JsonObject projected = ConfigurationValidationAdapter.Project(snapshot, error, owned.Path);
        JsonNode expected = row["expected"]!;
        Assert.AreEqual(Text(expected, "status"), Text(projected, "status"), id);
        AssertIssues(expected["issues"]!.AsArray(), error?.Issues ?? [], id);
        foreach (string name in new[] { "consoleEnabled", "fileEnabled" })
        {
            if (snapshot is not null && expected.AsObject().ContainsKey(name)) { Assert.IsTrue(JsonNode.DeepEquals(expected[name], projected[name]), id + ":" + name); }
        }
        foreach (string name in new[] { "values" })
        {
            if (expected[name] is JsonObject subset) { AssertSubset(subset, projected[name]!, id); }
        }
        if (error is null)
        {
            Assert.AreEqual(ExpectedPath(owned.Path, expected["filePath"]), snapshot!.Logging.FilePath, id);
        }
        foreach (JsonNode? forbidden in row["diagnosticsMustExclude"]!.AsArray())
        {
            string diagnostic = error?.ToString() + string.Join("|", error?.Issues.Select(issue => issue.Code + issue.Key + issue.Explanation) ?? []);
            Assert.IsFalse(diagnostic.Contains(forbidden!.GetValue<string>(), StringComparison.Ordinal), id);
        }
        CollectionAssert.AreEqual(ConfigurationReadExpectations.Expected(row["inputs"]!.AsObject()), source.Reads.ToArray());
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count(), id);
    }

    [TestMethod]
    [DynamicData(nameof(PerKeyCases))]
    public void EveryApplicablePerKeyVectorPassesThroughCompleteValidation(string id)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(Read("configuration-cases.json"), id);
        JsonObject input = row["inputs"]!.DeepClone().AsObject();
        // Option-only rows omit discovery facts; explicit null remains a missing identity.
        if (!input["discovery"]!.AsObject().ContainsKey("applicationName"))
        {
            input["discovery"]!["applicationName"] = "Fixture.App";
        }
        using var owned = new TemporaryDirectory();
        string initial = ConfigurationFixtureAdapter.Text(input, "workingDirectoryAtInitialization") ?? owned.Path;
        (PreparedConfiguration? snapshot, CoreConfigurationException? error, _) = Execute(input, initial);
        JsonNode expected = row["expected"]!;
        if (Text(expected, "status") == "Invalid")
        {
            Assert.IsNotNull(error, id);
            if (expected["issues"] is JsonArray issues) { AssertIssues(issues, error.Issues, id); }
            else
            {
                Assert.AreEqual(1, error.Issues.Count, id);
                Assert.AreEqual(Text(expected, "code"), error.Issues[0].Code, id);
                string key = Text(row, "key");
                string expectedKey = input["environment"]![key] is not null ? key
                    : input["setup"]!.AsObject().Single().Key.Replace("Logging.", "LoggingOptions.", StringComparison.Ordinal);
                Assert.AreEqual(CurrentKey(error.Issues[0].Code, expectedKey), error.Issues[0].Key, id);
            }
        }
        else
        {
            Assert.IsNull(error, id);
            JsonNode? actual = Text(row, "key") == "CORE_LOG_FILE_PATH" ? JsonValue.Create(snapshot!.Logging.FilePath)
                : ConfigurationFixtureAdapter.Values(snapshot!)[Read("configuration.json")["settings"]!.AsArray().Single(setting => Text(setting!, "key") == Text(row, "key"))!["property"]!.GetValue<string>()];
            JsonNode? wanted = Text(row, "key") == "CORE_LOG_FILE_PATH" ? JsonValue.Create(ExpectedPath(initial, expected["value"])) : expected["value"];
            Assert.IsTrue(JsonNode.DeepEquals(wanted, actual), id);
        }
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count(), id);
    }

    [TestMethod]
    public void ActualAggregatedIssuesRemainOwnedAndImmutableAcrossLaterValidation()
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(Read("passive-configuration-validation-cases.json"), "CFG-VALIDATE-ALL-DISABLED-COMPLETE-ERRORS");
        using var owned = new TemporaryDirectory();
        (_, CoreConfigurationException? error, _) = Execute(row["inputs"]!.AsObject(), owned.Path);
        Assert.IsNotNull(error);
        Assert.AreEqual(21, error.Issues.Count);
        ConfigurationIssue[] before = error.Issues.ToArray();
        var list = (IList<ConfigurationIssue>)error.Issues;
        Assert.ThrowsExactly<NotSupportedException>(() => list.Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => list[0] = new("other", "other", "other"));
        row["inputs"] = ConfigurationFixtureAdapter.Case(Read("passive-configuration-validation-cases.json"), "CFG-VALIDATE-DEFAULTS")["inputs"]!.DeepClone();
        (PreparedConfiguration? valid, CoreConfigurationException? nextError, _) = Execute(row["inputs"]!.AsObject(), owned.Path);
        Assert.IsNotNull(valid);
        Assert.IsNull(nextError);
        CollectionAssert.AreEqual(before, error.Issues.ToArray());
    }

    private static (PreparedConfiguration?, CoreConfigurationException?, EnvironmentVariableStub) Execute(JsonObject input, string initial)
    {
        JsonNode before = input.DeepClone();
        var source = new EnvironmentVariableStub();
        foreach ((string key, JsonNode? value) in input["environment"]!.AsObject())
        {
            source.Values[key] = value?.GetValue<string>();
        }
        int directoryReads = 0;
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input,
            () => { directoryReads++; return initial; }, source.GetValue);
        foreach (string key in source.Values.Keys.ToArray())
        {
            source.Values[key] = "replacement-after-capture";
        }
        Assert.IsTrue(JsonNode.DeepEquals(before, input));
        Assert.AreEqual(1, directoryReads);
        CollectionAssert.AreEqual(ConfigurationReadExpectations.Expected(input), source.Reads.ToArray());
        Assert.AreEqual(source.Reads.Count, source.Reads.Distinct(StringComparer.Ordinal).Count());
        return (snapshot, error, source);
    }

    private static string? ExpectedPath(string initial, JsonNode? expected)
    {
        if (expected is null) { return null; }
        if (expected is JsonValue value) { return value.GetValue<string>(); }
        string input = Text(expected, "input");
        return Path.Combine(initial, input.Replace('/', Path.DirectorySeparatorChar));
    }
    private static void AssertIssues(JsonArray expected, IReadOnlyList<ConfigurationIssue> actual, string id) =>
        CollectionAssert.AreEquivalent(expected.Select(issue => Text(issue!, "code") + ":" + CurrentKey(Text(issue!, "code"), Text(issue!, "key"))).ToArray(), actual.Select(issue => issue.Code + ":" + issue.Key).ToArray(), id);
    private static string CurrentKey(string code, string key)
    {
        if (code == ConfigurationIssueCodes.InvalidPath) { return "LoggingOptions.FilePath"; }
        if (code == ConfigurationIssueCodes.MissingApplicationName) { return "EnvironmentOptions.ApplicationName"; }
        if (key is "BuildVersion" or "CommitId" or "BuildId") { return "EnvironmentOptions." + key; }
        return key.Replace("LoggingSetup.", "LoggingOptions.", StringComparison.Ordinal).Replace("EnvironmentSetup.", "EnvironmentOptions.", StringComparison.Ordinal);
    }
    private static void AssertSubset(JsonObject expected, JsonNode actual, string id)
    {
        foreach ((string key, JsonNode? value) in expected)
        {
            if (value is JsonObject child) { AssertSubset(child, actual[key]!, id); }
            else { Assert.IsTrue(JsonNode.DeepEquals(value, actual[key]), id + ":" + key); }
        }
    }
    private static string Text(JsonNode node, string key) => node[key]!.GetValue<string>();
    private static JsonObject Read(string name) => ConfigurationFixtureAdapter.ReadDocument(name == "configuration.json" ? Path.Combine(Path.GetDirectoryName(ConfigurationValidationAdapter.FindFixture())!, "..", "..", "contracts", "logging", "v1", name) : Path.Combine(Path.GetDirectoryName(ConfigurationValidationAdapter.FindFixture())!, name));
}
