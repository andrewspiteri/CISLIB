using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigurationTableTests
{
    private static readonly Lazy<LoggingFixtureInputs> Inputs = new(() => new("configuration-resolution-cases.json"));
    private static readonly Lazy<Dictionary<string, string>> PropertyByKey = new(() => Contract()["settings"]!.AsArray()
        .ToDictionary(setting => setting!["key"]!.GetValue<string>(), setting => setting!["property"]!.GetValue<string>(), StringComparer.Ordinal));

    public static IEnumerable<object[]> Cases => Inputs.Value.Ids.Select(id => new object[] { id });

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void SelectedConfigurationMatchesTheSharedTable(string id)
    {
        JsonObject row = Inputs.Value.Row(id);
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode before = input.DeepClone();
        using var directory = new TemporaryDirectory();
        (PreparedConfiguration? snapshot, CoreConfigurationException? error) = ConfigurationValidationAdapter.Execute(input, () => directory.Path);
        JsonNode expected = row["expected"]!;
        JsonArray settings = expected["settings"]!.AsArray();
        if (expected["hasParseErrors"]!.GetValue<bool>())
        {
            Assert.IsNull(snapshot);
            Assert.IsNotNull(error);
            CollectionAssert.AreEqual(settings.Where(setting => setting!["state"]!.GetValue<string>() == "InvalidToken").Select(setting => setting!["key"]!.GetValue<string>()).ToArray(), error.Issues.Select(issue => issue.Key).ToArray());
            Assert.IsTrue(error.Issues.All(issue => issue.Code == ConfigurationIssueCodes.InvalidToken));
        }
        else
        {
            Assert.IsNull(error, id);
            Assert.AreEqual(expected["buildVersion"]!.GetValue<string>(), snapshot!.BuildVersion);
            Assert.AreEqual(expected["consoleEnabled"]!.GetValue<bool>(), snapshot.Logging.ConsoleEnabled);
            Assert.AreEqual(expected["fileEnabled"]!.GetValue<bool>(), snapshot.Logging.FileEnabled);
            JsonObject values = ConfigurationFixtureAdapter.Values(snapshot);
            foreach (JsonNode? setting in settings)
            {
                string key = setting!["key"]!.GetValue<string>();
                JsonNode? wanted = setting["value"];
                string property = PropertyByKey.Value[key];
                if (key == "CORE_LOG_FILE_PATH" && wanted is not null)
                {
                    wanted = JsonValue.Create(Path.GetFullPath(wanted.GetValue<string>(), directory.Path));
                }
                Assert.IsTrue(JsonNode.DeepEquals(wanted, values[property]), id + ":" + key);
            }
        }
        Assert.IsTrue(JsonNode.DeepEquals(before, input));
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(directory.Path).Count());
    }

    [TestMethod]
    public void SharedTableHasCompleteSettingsSourcesAndAcceptanceCoverage()
    {
        JsonObject document = Read();
        JsonArray rows = document["cases"]!.AsArray();
        Assert.AreEqual(26, rows.Count);
        Assert.AreEqual(rows.Count, document["caseCount"]!.GetValue<int>());
        Assert.AreEqual(rows.Count, rows.Select(row => ConfigurationFixtureAdapter.Text(row!, "id")).Distinct(StringComparer.Ordinal).Count());
        string[] keys = Contract()["settings"]!.AsArray().Select(setting => setting!["key"]!.GetValue<string>()).ToArray();
        Assert.AreEqual(18, keys.Length);
        foreach (JsonNode? row in rows)
        {
            CollectionAssert.AreEqual(keys, row!["expected"]!["settings"]!.AsArray().Select(setting => setting!["key"]!.GetValue<string>()).ToArray());
        }
    }

    private static JsonObject Read() => ConfigurationFixtureAdapter.ReadDocument(ConfigurationFixtureAdapter.FindFixture());
    private static JsonObject Contract() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "..", "..", "contracts", "logging", "v1", "configuration.json"));
}
