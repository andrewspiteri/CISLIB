using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class FileIntervalProcessTests
{
#if !CIS_MUTATION_TESTS
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> ReaderPositives = new(StringComparer.Ordinal);
#endif
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Modes => FileIntervalExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFileIntervalRunsInAnExactRuntimeChild(string mode)
    { using var root = new TemporaryDirectory(); await Run(mode, root.Path); }

    [TestMethod]
    public void NativeTimerModesMatchTheUnchangedCatalog()
    {
        JsonArray rows = JsonNode.Parse(File.ReadAllText(FixtureProcessChecks.Find("file-interval-cases.json")))!["cases"]!.AsArray();
        Assert.AreEqual(FileIntervalExpectations.Modes.Length, rows.Count); Assert.AreEqual(rows.Count, rows.Select(row => (string)row!["id"]!).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEqual(FileIntervalExpectations.Modes, rows.Select(row => (string)row!["id"]!).ToArray());
    }


    public static IEnumerable<object[]> Controls => FileTimerReaderControls.Interval.Select(c => new object[] { c.Id });
    [TestMethod]
    [DynamicData(nameof(Controls))]
    public async Task OriginalTimerReaderCorruptionsRejectAfterPhysicalPositive(string key)
    {
        FileTimerReaderControls.Control control = FileTimerReaderControls.Interval.Single(c => c.Id == key);
        JsonObject report = await ReadPositive(control.Mode); int pid = report["pid"]!.GetValue<int>();
        FileIntervalExpectations.Validate(control.Mode, report, pid);
        control.Mutate(report);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileIntervalExpectations.Validate(control.Mode, report, pid));
        Assert.AreEqual(control.Reason, error.Message); TestContext.WriteLine("Rejected original interval timer control: " + key);
    }

    private async Task<JsonObject> Run(string mode, string root)
    {
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("CORE_", StringComparison.Ordinal))
            .ToDictionary(k => k, _ => (string?)null, StringComparer.Ordinal);
        environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = null;
        if (mode == "env") { environment["CORE_LOG_CONSOLE_LEVEL"] = "NONE"; environment["CORE_LOG_FILE_LEVEL"] = "INFO"; environment["CORE_LOG_FILE_PATH"] = Path.Combine(root, "owned", "events.jsonl"); environment["CORE_LOG_FLUSH_INTERVAL_MS"] = "250"; }
        string mapping = FixtureProcessChecks.Find("file-interval-cases.json"), before = Hash(mapping);
        (JsonObject report, int pid) = await FixtureProcessChecks.RunAsync(["file-interval", mode, root], root, TestContext, environment: environment, timeout: TimeSpan.FromSeconds(60), flatReport: true);
        Assert.AreEqual(before, Hash(mapping)); TestContext.WriteLine("Timer case catalog SHA-256: " + before);
        FileIntervalExpectations.Validate(mode, report, pid);
        if (mode is "configured" or "env") { CollectionAssert.AreEqual(Convert.FromBase64String((string)report["bytes"]!), File.ReadAllBytes(Path.Combine(root, "owned", "events.jsonl"))); }
        return report;
    }
    private async Task<JsonObject> ReadPositive(string mode)
    {
#if CIS_MUTATION_TESTS
        using var root = new TemporaryDirectory();
        return await Run(mode, root.Path);
#else
        string text = await ReaderPositives.GetOrAdd(mode, value => new Lazy<Task<string>>(() => CapturePositive(value))).Value;
        return JsonNode.Parse(text)!.AsObject();
#endif
    }

#if !CIS_MUTATION_TESTS
    private async Task<string> CapturePositive(string mode)
    {
        using var root = new TemporaryDirectory();
        return (await Run(mode, root.Path)).ToJsonString();
    }
#endif
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
