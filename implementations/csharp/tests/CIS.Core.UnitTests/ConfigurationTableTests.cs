using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Tests.DiscoveryFixture;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConfigurationTableTests
{
    public static IEnumerable<object[]> Cases => Read()["cases"]!.AsArray().Select(row => new object[] { ConfigurationFixtureAdapter.Text(row!, "id")! });

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void SelectedConfigurationMatchesTheSharedTable(string id)
    {
        JsonObject row = ConfigurationFixtureAdapter.Case(Read(), id);
        JsonObject input = row["inputs"]!.AsObject();
        JsonNode before = input.DeepClone();
        JsonObject discovery = input["discovery"]!.AsObject();
        var application = new ApplicationIdentity(ConfigurationFixtureAdapter.Text(discovery, "applicationName"), ConfigurationFixtureAdapter.Text(discovery, "buildVersion")!, null, null);
        var source = new EnvironmentVariableStub();
        foreach ((string key, JsonNode? value) in input["environment"]!.AsObject())
        {
            source.Values[key] = value?.GetValue<string>();
        }
        EnvironmentConfigurationInput setup = ConfigurationFixtureAdapter.Setup(input);
        ConfigurationResolution resolution = ConfigurationResolver.Resolve(setup, application, CapturedEnvironment.Capture(source));
        JsonObject actual = ConfigurationFixtureAdapter.Project(resolution);
        Assert.IsTrue(JsonNode.DeepEquals(row["expected"], actual), $"{id}: {actual.ToJsonString()}");
        Assert.IsTrue(JsonNode.DeepEquals(before, input), "Fixture caller and host inputs changed.");
        Assert.AreEqual(21, source.Reads.Count);
        Assert.AreEqual(21, source.Reads.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(18, actual["settings"]!.AsArray().Count);
    }

    [TestMethod]
    public void SharedTableHasCompleteSettingsSourcesAndAcceptanceCoverage()
    {
        JsonObject document = Read();
        JsonArray rows = document["cases"]!.AsArray();
        Assert.AreEqual(1, document["schemaVersion"]!.GetValue<int>());
        Assert.AreEqual(26, rows.Count);
        Assert.AreEqual(rows.Count, document["caseCount"]!.GetValue<int>());
        Assert.AreEqual(rows.Count, rows.Select(row => ConfigurationFixtureAdapter.Text(row!, "id")).Distinct(StringComparer.Ordinal).Count());
        string[] keys = ConfigurationSettings.All.Select(setting => setting.Key).ToArray();
        foreach (JsonNode? row in rows)
        {
            string[] actual = row!["expected"]!["settings"]!.AsArray().Select(setting => ConfigurationFixtureAdapter.Text(setting!, "key")!).ToArray();
            CollectionAssert.AreEqual(keys, actual);
        }
        string[] tags = rows.SelectMany(row => row!["tags"]!.AsArray().Select(tag => tag!.GetValue<string>())).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string required in new[] { "defaults", "setup", "all-settings", "aliases", "core-over-alias", "core-over-setup", "alias-over-setup", "discovery", "default", "independent-levels", "disabled-console", "disabled-file", "absent-file", "host-label", "host-unchanged", "host-state", "explicit-format", "explicit-stream", "selected-invalid" })
        {
            Assert.IsTrue(tags.Contains(required, StringComparer.Ordinal), required);
        }
    }

    private static JsonObject Read() => ConfigurationFixtureAdapter.ReadDocument(ConfigurationFixtureAdapter.FindFixture());
}
