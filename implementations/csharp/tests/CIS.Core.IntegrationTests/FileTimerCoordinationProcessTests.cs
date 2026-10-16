using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileTimerCoordinationProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => FileTimerCoordinationExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFileTimerCoordinationRunsInAnExactRuntimeChild(string mode)
    { using var root = new TemporaryDirectory(); await Run(mode, root.Path); }

    [TestMethod]
    public void NativeTimerModesMatchTheUnchangedCatalog()
    {
        JsonArray rows = JsonNode.Parse(File.ReadAllText(FixtureProcessChecks.Find("file-timer-coordination-cases.json")))!["cases"]!.AsArray();
        Assert.AreEqual(FileTimerCoordinationExpectations.Modes.Length, rows.Count); Assert.AreEqual(rows.Count, rows.Select(row => (string)row!["id"]!).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEqual(FileTimerCoordinationExpectations.Modes, rows.Select(row => (string)row!["id"]!).ToArray());
    }


    public static IEnumerable<object[]> Controls => FileTimerReaderControls.Coordination.Select(c => new object[] { c.Id });
    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task OriginalTimerReaderCorruptionsRejectAfterPhysicalPositive(string key)
    {
        FileTimerReaderControls.Control control = FileTimerReaderControls.Coordination.Single(c => c.Id == key);
        using var root = new TemporaryDirectory(); JsonObject report = await Run(control.Mode, root.Path); int pid = report["pid"]!.GetValue<int>();
        control.Mutate(report);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileTimerCoordinationExpectations.Validate(control.Mode, report, pid));
        Assert.AreEqual(control.Reason, error.Message); TestContext.WriteLine("Rejected original coordination timer control: " + key);
    }

    private async Task<JsonObject> Run(string mode, string root)
    {
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("CORE_", StringComparison.Ordinal))
            .ToDictionary(k => k, _ => (string?)null, StringComparer.Ordinal);
        environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null;

        string mapping = FixtureProcessChecks.Find("file-timer-coordination-cases.json"), before = Hash(mapping);
        (JsonObject report, int pid) = await FixtureProcessChecks.RunAsync(["file-timer-coordination", mode, root], root, TestContext, environment: environment, timeout: TimeSpan.FromSeconds(60), flatReport: true);
        Assert.AreEqual(before, Hash(mapping)); TestContext.WriteLine("Timer case catalog SHA-256: " + before);
        FileTimerCoordinationExpectations.Validate(mode, report, pid);

        return report;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
