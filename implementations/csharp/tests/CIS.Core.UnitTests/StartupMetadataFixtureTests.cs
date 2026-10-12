using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class StartupMetadataFixtureTests
{
    public static IEnumerable<object[]> MetadataRows => Document()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(MetadataRows))]
    public void SharedMetadataVectorsExecuteAgainstActualOwnedCoreSnapshots(string id)
    {
        JsonObject row = Row(id);
        EnvironmentStoreTestData.AssignValidFacts();
        Assign(row["values"]!.AsObject());
        ConfigurationFixtureAdapter.ApplyOverrides(key => row["environment"]?[key]?.GetValue<string>());
        if (row["expectedIssue"] is { } issue)
        {
            CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentStoreTestData.Values.CaptureMetadata());
            Assert.AreEqual(issue["code"]!.GetValue<string>(), error.Issues.Single().Code);
            Assert.AreEqual(issue["key"]!.GetValue<string>(), error.Issues.Single().Key);
            return;
        }
        EventMetadata captured = EnvironmentStoreTestData.Values.CaptureMetadata();
        JsonObject before = JsonSerializer.SerializeToNode(captured)!.AsObject();
        AssertSubset(row["expected"]!.AsObject(), before);
        if (row["mutation"] is JsonObject mutation)
        {
            Assign(mutation);
            EnvironmentStoreTestData.Values.Logging.QueueCapacity = 1;
            Assert.IsTrue(JsonNode.DeepEquals(before, JsonSerializer.SerializeToNode(captured)));
            EventMetadata later = EnvironmentStoreTestData.Values.CaptureMetadata();
            AssertSubset(mutation, JsonSerializer.SerializeToNode(later)!.AsObject());
            Assert.IsFalse(JsonNode.DeepEquals(before, JsonSerializer.SerializeToNode(later)));
            Assert.IsNull(before["Logging"]);
            Assert.AreEqual(1, EnvironmentStoreTestData.Values.Logging.QueueCapacity);
        }
    }

    [TestMethod]
    [DataRow("META-TELEMETRY-RELEASE", "BuildVersion", "release-2026-10")]
    [DataRow("META-OPTIONAL-UNAVAILABLE", "HostName", "invented-host")]
    public void MetadataComparatorRejectsSemanticSubstitution(string id, string property, string replacement)
    {
        JsonObject expected = Row(id)["expected"]!.AsObject();
        JsonObject altered = expected.DeepClone().AsObject();
        altered[property] = replacement;
        Assert.ThrowsExactly<InvalidDataException>(() => AssertSubset(expected, altered));
    }

    private static void Assign(JsonObject values)
    {
        foreach ((string name, JsonNode? value) in values)
        {
            System.Reflection.PropertyInfo property = typeof(FixtureEnvironmentValues).GetProperty(name)!;
            property.SetValue(EnvironmentStoreTestData.Values, value?.Deserialize(property.PropertyType));
        }
    }
    private static void AssertSubset(JsonObject expected, JsonObject actual)
    {
        foreach ((string key, JsonNode? value) in expected)
        {
            bool matches;
            if (value is not null && key.EndsWith("AtUtc", StringComparison.Ordinal))
            {
                matches = DateTimeOffset.Parse(value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) == actual[key]!.GetValue<DateTimeOffset>();
            }
            else { matches = JsonNode.DeepEquals(value, actual[key]); }
            if (!matches) { throw new InvalidDataException("Metadata differs: " + key); }
        }
    }
    private static JsonObject Row(string id) => Document()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Document() => ConfigurationFixtureAdapter.ReadDocument(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "passive-environment-cases.json"));
}
