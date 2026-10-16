using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileTimerLifecycleProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => FileTimerLifecycleExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFileTimerLifecycleRunsInAnExactRuntimeChild(string mode)
    { using var root = new TemporaryDirectory(); await Run(mode, root.Path); }

    [TestMethod]
    public void NativeTimerModesMatchTheUnchangedCatalog()
    {
        JsonArray rows = JsonNode.Parse(File.ReadAllText(FixtureProcessChecks.Find("file-timer-lifecycle-cases.json")))!["cases"]!.AsArray();
        Assert.AreEqual(FileTimerLifecycleExpectations.Modes.Length, rows.Count); Assert.AreEqual(rows.Count, rows.Select(row => (string)row!["id"]!).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEqual(FileTimerLifecycleExpectations.Modes, rows.Select(row => (string)row!["id"]!).ToArray());
    }


    public static IEnumerable<object[]> Controls => FileTimerReaderControls.Lifecycle.Select(c => new object[] { c.Id });
    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task OriginalTimerReaderCorruptionsRejectAfterPhysicalPositive(string key)
    {
        FileTimerReaderControls.Control control = FileTimerReaderControls.Lifecycle.Single(c => c.Id == key);
        using var root = new TemporaryDirectory(); JsonObject report = await Run(control.Mode, root.Path); int pid = report["pid"]!.GetValue<int>();
        control.Mutate(report);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileTimerLifecycleExpectations.Validate(control.Mode, report, pid));
        Assert.AreEqual(control.Reason, error.Message); TestContext.WriteLine("Rejected original lifecycle timer control: " + key);
    }

    private async Task<JsonObject> Run(string mode, string root)
    {
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("CORE_", StringComparison.Ordinal))
            .ToDictionary(k => k, _ => (string?)null, StringComparer.Ordinal);
        environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null;

        string mapping = FixtureProcessChecks.Find("file-timer-lifecycle-cases.json"), before = Hash(mapping);
        (JsonObject report, int pid) = await FixtureProcessChecks.RunAsync(["file-timer-lifecycle", mode, root], root, TestContext, environment: environment, timeout: TimeSpan.FromSeconds(60), flatReport: true);
        Assert.AreEqual(before, Hash(mapping)); TestContext.WriteLine("Timer case catalog SHA-256: " + before);
        FileTimerLifecycleExpectations.Validate(mode, report, pid);
        if (mode == "close-callback") { CollectionAssert.AreEqual(Convert.FromBase64String((string)report["fileBytes"]!), File.ReadAllBytes(Path.Combine(root, "owned", "events.jsonl"))); }
        return report;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
