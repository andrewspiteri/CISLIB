using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StartupMetadataFixtureTests
{
    public static IEnumerable<object[]> MetadataRows => Document()["cases"]!.AsArray()
        .Select(row => new object[] { row!["id"]!.GetValue<string>() });

    [TestMethod]
    [DynamicData(nameof(MetadataRows))]
    public void SharedMetadataVectorsExecuteAgainstActualOwnedCoreSnapshots(string id)
    {
        JsonObject row = Document()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        using var owned = new TemporaryDirectory();
        JsonObject actual = StartupMetadataFixtureAdapter.Execute(row, owned.Path);
        StartupMetadataFixtureAdapter.Validate(row, actual);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    [DataRow("META-TELEMETRY-RELEASE", "BuildVersion", "release-2026-10")]
    [DataRow("META-OPTIONAL-UNAVAILABLE", "HostName", "invented-host")]
    public void MetadataComparatorRejectsSemanticSubstitution(string id, string property, string replacement)
    {
        JsonObject row = Document()["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        using var owned = new TemporaryDirectory();
        JsonObject actual = StartupMetadataFixtureAdapter.Execute(row, owned.Path);
        actual["snapshot"]![property] = replacement;
        Assert.ThrowsExactly<InvalidDataException>(() => StartupMetadataFixtureAdapter.Validate(row, actual));
    }

    private static JsonObject Document() => ConfigurationFixtureAdapter.ReadDocument(FindDocument());

    private static string FindDocument()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", "metadata-cases.json");
            if (File.Exists(path))
            {
                return path;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find the shared metadata vectors.");
    }
}
