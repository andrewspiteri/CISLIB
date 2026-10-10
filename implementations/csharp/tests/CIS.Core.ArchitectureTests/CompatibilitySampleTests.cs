using System.Text.Json;
using CIS.Tests.ArchitectureTests.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class CompatibilitySampleTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string SampleRoot = Path.Combine(RepositoryRoot, "conformance", "logging", "compatibility", "samples");
    private static readonly JsonDocument Catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(SampleRoot, "catalog.json")));

    public static IEnumerable<object[]> WireCases => Catalog.RootElement.GetProperty("cases").EnumerateArray().Select(item => new object[] { item.GetProperty("id").GetString()! });

    [TestMethod]
    [DynamicData(nameof(WireCases))]
    public void WireSamplesDistinguishValidityFromPreservedMeaning(string id)
    {
        JsonElement sample = Catalog.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "contracts", "logging", "v1", "event.schema.json")));
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "contracts", "logging", "v1", "event-contract.json")));
        var comparer = new WireSampleComparer(schema.RootElement, contract.RootElement);
        byte[] baseline = File.ReadAllBytes(Path.Combine(SampleRoot, Catalog.RootElement.GetProperty("baseline").GetString()!));
        byte[] candidate = File.ReadAllBytes(Path.Combine(SampleRoot, sample.GetProperty("file").GetString()!));
        Assert.IsTrue(comparer.IsValid(baseline), "The positive oracle must be valid.");
        Assert.AreEqual(sample.GetProperty("expectedValid").GetBoolean(), comparer.IsValid(candidate), id + ": wire validity");
        Assert.AreEqual(sample.GetProperty("expectedCompatible").GetBoolean(), comparer.Preserves(baseline, candidate), id + ": preserved meaning");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the wire sample checks from the CisLib checkout.");
    }
}
