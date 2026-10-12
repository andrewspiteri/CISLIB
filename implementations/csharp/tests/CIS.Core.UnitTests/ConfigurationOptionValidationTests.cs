using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationOptionValidationTests
{
    public static IEnumerable<object[]> TypedBounds => ConfigurationReadExpectations.Catalog()["settings"]!.AsArray()
        .Where(row => row!["kind"]!.GetValue<string>() == "integer")
        .SelectMany(row => new[] { row!["minimum"]!.GetValue<long>() - 1, row["minimum"]!.GetValue<long>(), row["maximum"]!.GetValue<long>(), row["maximum"]!.GetValue<long>() + 1 }
            .Select(value => new object[] { row["key"]!.GetValue<string>(), value, value >= row["minimum"]!.GetValue<long>() && value <= row["maximum"]!.GetValue<long>() }));
    public static IEnumerable<object[]> SharedCrossOptions => Read()["crossOptionCases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(TypedBounds))]
    public void TypedSetupUsesTheSameInclusiveNumericBounds(string key, long value, bool valid)
    {
        string field = ConfigurationReadExpectations.Catalog()["settings"]!.AsArray().Single(row => row!["key"]!.GetValue<string>() == key)!["property"]!.GetValue<string>().Replace("Logging.", "", StringComparison.Ordinal);
        var logging = new LoggingOptions();
        System.Reflection.PropertyInfo property = typeof(LoggingOptions).GetProperty(field)!;
        if (property.PropertyType == typeof(long))
        {
            property.SetValue(logging, value);
        }
        else
        {
            property.SetValue(logging, checked((int)value));
        }
        if (valid)
        {
            logging.Prepare(Path.GetTempPath());
        }
        else
        {
            CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => logging.Prepare(Path.GetTempPath()));
            Assert.AreEqual(key == "CORE_LOG_SCHEMA_VERSION" ? ConfigurationIssueCodes.UnsupportedSchemaVersion : ConfigurationIssueCodes.OutOfRange, error.Issues.Single().Code);
            Assert.AreEqual("LoggingOptions." + field, error.Issues.Single().Key);
        }
    }

    [TestMethod]
    [DynamicData(nameof(SharedCrossOptions))]
    public void SharedCrossOptionVectorsValidateWithoutOutputSideEffects(string id)
    {
        JsonObject row = Read()["crossOptionCases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject raw = row["inputs"]!.AsObject();
        var input = new JsonObject
        {
            ["environment"] = raw.DeepClone(),
            ["setup"] = new JsonObject { ["EnvironmentName"] = raw["hostEnvironment"]?.DeepClone() },
            ["discovery"] = new JsonObject { ["applicationName"] = "Fixture.App", ["buildVersion"] = "fixture-build" }
        };
        using var directory = new TemporaryDirectory();
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, () => directory.Path);
        string[] expected = row["expectedIssues"] is JsonArray many
            ? many.Select(issue => issue!["code"]!.GetValue<string>() + ":" + issue["key"]!.GetValue<string>()).ToArray()
            : row["expectedIssue"] is { } one ? [one["code"]!.GetValue<string>() + ":" + one["key"]!.GetValue<string>()] : [];
        if (id == "CFG-FILE-ENTRY-SIZE-CONSISTENCY")
        {
            expected = ["OutOfRange:LoggingOptions.FileMaxBytes"];
        }
        CollectionAssert.AreEquivalent(expected, error?.Issues.Select(issue => issue.Code + ":" + issue.Key).ToArray() ?? []);
        if (expected.Length == 0)
        {
            Assert.IsNotNull(snapshot);
            if (id == "CFG-INDEPENDENT-LEVELS")
            {
                Assert.AreEqual(CoreLogLevel.Information, snapshot.Logging.Level);
                Assert.AreEqual(CoreLogLevel.Warning, snapshot.Logging.ConsoleLevel);
                Assert.AreEqual(CoreLogLevel.Debug, snapshot.Logging.FileLevel);
                Assert.IsTrue(snapshot.Logging.FileEnabled);
            }
            else if (id == "CFG-HOST-ENVIRONMENT-ISOLATION")
            {
                Assert.AreEqual("live", snapshot.EnvironmentName);
                Assert.AreEqual("Production", input["setup"]!["EnvironmentName"]!.GetValue<string>());
            }
            else if (id == "CFG-FILE-DISABLED-ENTRY-SIZE")
            {
                Assert.AreEqual(131072, snapshot.Logging.MaxEntryBytes);
                Assert.IsFalse(snapshot.Logging.FileEnabled);
            }
        }
        else
        {
            Assert.IsNull(snapshot);
        }
        foreach (JsonNode? excluded in row["diagnosticsMustExclude"]?.AsArray() ?? [])
        {
            string diagnostics = error?.ToString() + string.Join("|", error?.Issues.Select(issue => issue.Code + issue.Key + issue.Explanation) ?? []);
            Assert.IsFalse(diagnostics.Contains(excluded!.GetValue<string>(), StringComparison.Ordinal));
        }
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(directory.Path).Count());
    }

    [TestMethod]
    [DataRow(true, 65536L, 131072, 1)]
    [DataRow(true, 131072L, 131072, 0)]
    [DataRow(true, 262144L, 131072, 0)]
    [DataRow(false, 65536L, 131072, 0)]
    [DataRow(true, 1L, 131072, 1)]
    [DataRow(true, 65536L, 16777217, 1)]
    public void EnabledFileSizeRelationshipAvoidsDuplicateRangeErrors(bool enabled, long fileBytes, int entryBytes, int count)
    {
        var logging = new LoggingOptions { FilePath = "log.jsonl", FileLevel = enabled ? CoreLogLevel.Information : CoreLogLevel.None, FileMaxBytes = fileBytes, MaxEntryBytes = entryBytes };
        if (count == 0)
        {
            logging.Prepare(Path.GetTempPath());
            Assert.AreEqual(enabled, logging.FileEnabled);
        }
        else
        {
            CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => logging.Prepare(Path.GetTempPath()));
            Assert.AreEqual(count, error.Issues.Count);
            Assert.AreEqual(ConfigurationIssueCodes.OutOfRange, error.Issues.Single().Code);
        }
    }

    private static JsonObject Read() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "configuration-cases.json"));
}
