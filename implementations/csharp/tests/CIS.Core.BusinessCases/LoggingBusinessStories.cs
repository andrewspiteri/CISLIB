using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases;

[TestClass]
public sealed class LoggingBusinessStories
{
    private sealed record Observation(JsonObject Report, string StdoutLogs, string Stderr);
    private static string Host => Path.Combine(AppContext.BaseDirectory, "logging-fixture", "CIS.Core.DiscoveryFixture.dll");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static Dictionary<string, string?> Variables(string root, Dictionary<string, string?>? changes = null)
    {
        var env = System.Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(k => k.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) || k is "DD_SERVICE" or "DD_ENV" or "DD_VERSION")
            .ToDictionary(k => k, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in new Dictionary<string, string?>
        {
            ["CORE_SERVICE_NAME"] = "Business.App",
            ["CORE_ENVIRONMENT"] = "acceptance",
            ["CORE_SERVICE_VERSION"] = "1.2.3",
            ["CORE_LOG_CONSOLE_LEVEL"] = "None",
            ["CORE_LOG_CONSOLE_FORMAT"] = "jsonl",
            ["CORE_LOG_CONSOLE_STREAM"] = "stderr",
            ["CORE_LOG_FILE_PATH"] = Path.Combine(root, "events.log"),
            ["CORE_LOG_FILE_LEVEL"] = "Information",
            ["CORE_LOG_FILE_FORMAT"] = "jsonl",
            ["CORE_LOG_MAX_ENTRY_BYTES"] = "16384",
            ["CORE_LOG_QUEUE_CAPACITY"] = "64",
            ["CORE_LOG_SHUTDOWN_TIMEOUT_MS"] = "5000",
            ["UNRELATED_BUSINESS_SECRET"] = "must-not-appear"
        }) { env[pair.Key] = pair.Value; }
        if (changes is not null) { foreach (var pair in changes) { env[pair.Key] = pair.Value; } }
        return env;
    }
    private static async Task<Observation> Run(string mode, Dictionary<string, string?>? changes = null, string command = "logging-business")
    {
        using var root = new TemporaryDirectory();
        return await RunAt(command, mode, root.Path, changes);
    }
    private static async Task<Observation> RunAt(string command, string mode, string root, Dictionary<string, string?>? changes = null, string? phase = null)
    {
        string[] args = phase is null ? [command, mode, root] : [command, mode, phase, root];
        ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, .. args],
            root, TimeSpan.FromSeconds(30), Variables(root, changes), new UTF8Encoding(false, true));
        Assert.AreEqual(0, child.ExitCode, child.StandardError);
        int start = child.StandardOutput.LastIndexOf('\n', child.StandardOutput.Length - 2) + 1;
        JsonObject report = JsonNode.Parse(child.StandardOutput[start..])!.AsObject();
        Assert.AreEqual(Hash(Host), (string?)report["fixtureSha256"]);
        Assert.AreEqual(Hash(Path.Combine(Path.GetDirectoryName(Host)!, "CIS.Core.dll")), (string?)report["coreSha256"]);
        Assert.AreEqual(".NET 10.0.12", (string?)report["runtime"]);
        return new(report, child.StandardOutput[..start], child.StandardError);
    }
    private static JsonObject[] Records(string bytes)
    {
        byte[] raw = Convert.FromBase64String(bytes);
        Assert.IsFalse(raw.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        Assert.IsTrue(raw.Length == 0 || raw[^1] == 10);
        Assert.IsFalse(raw.Contains((byte)13)); Assert.IsFalse(raw.Contains((byte)27));
        if (raw.Length == 0) { return []; }
        string[] lines = new UTF8Encoding(false, true).GetString(raw).Split('\n');
        Assert.IsTrue(lines.Take(lines.Length - 1).All(line => line.Length != 0), "Every JSONL line must contain a record.");
        return lines.Take(lines.Length - 1).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
    }
    private static JsonObject[] RetentionRecords(Observation value) => value.Report["files"]!.AsArray().SelectMany(file =>
    {
        string path = Path.Combine(Path.GetDirectoryName(value.Report["path"]!.GetValue<string>())!, file!["name"]!.GetValue<string>());
        Assert.AreEqual(file["sha256"]!.GetValue<string>(), Hash(path));
        return Records(Convert.ToBase64String(File.ReadAllBytes(path)));
    }).ToArray();
    private static JsonObject[] FileRecords(Observation value) => Records(value.Report["file"]!.GetValue<string>());
    private static JsonObject[] StreamRecords(string value) => Records(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));

    [TestMethod]
    public async Task GivenVerboseHumanWhenOperationFailsThenIdentityExceptionAndContextAreVisible()
    {
        Observation value = await Run("verbose", new() { ["CORE_LOG_CONSOLE_LEVEL"] = "Error", ["CORE_LOG_CONSOLE_FORMAT"] = "human", ["CORE_LOG_HUMAN_DETAIL"] = "verbose", ["CORE_LOG_FILE_LEVEL"] = "None" });
        Assert.AreEqual("", value.StdoutLogs);
        foreach (string required in new[] { "Business.App", "acceptance", "1.2.3", "9.8.7-rc.2", "operation failed", "controlled failure", "InvalidOperationException", "business.operation", "experiment" })
        { StringAssert.Contains(value.Stderr, required); }
        Assert.IsFalse(value.Stderr.Contains('\u001b')); Assert.IsTrue(value.Report["succeeded"]!.GetValue<bool>());
    }
    [TestMethod]
    public async Task GivenJsonDiagnosticsWhenCliRunsThenStdoutResultAndStderrRecordsAreSeparate()
    {
        Observation value = await Run("configured", new() { ["CORE_LOG_CONSOLE_LEVEL"] = "Information", ["CORE_LOG_FILE_LEVEL"] = "None" });
        Assert.AreEqual("", value.StdoutLogs); Assert.AreEqual("configured", (string?)value.Report["mode"]);
        JsonObject row = StreamRecords(value.Stderr).Single(); Assert.AreEqual("configured", (string?)row["event_name"]);
        Assert.AreEqual(value.Report["instanceId"]!.GetValue<string>(), (string?)row["resource"]!["instance_id"]);
    }
    [TestMethod]
    public async Task GivenSameBuildWhenEnvironmentSelectsStreamFileAndFormatThenMeaningIsPreserved()
    {
        Observation console = await Run("configured", new() { ["CORE_LOG_CONSOLE_LEVEL"] = "Information", ["CORE_LOG_CONSOLE_STREAM"] = "stdout", ["CORE_LOG_FILE_LEVEL"] = "None" });
        Observation file = await Run("configured");
        Observation human = await Run("configured", new() { ["CORE_LOG_CONSOLE_LEVEL"] = "Information", ["CORE_LOG_CONSOLE_FORMAT"] = "human", ["CORE_LOG_FILE_LEVEL"] = "None" });
        Assert.AreEqual(console.Report["fixtureSha256"]!.GetValue<string>(), (string?)file.Report["fixtureSha256"]);
        Assert.AreEqual((string?)file.Report["fixtureSha256"], (string?)human.Report["fixtureSha256"]);
        JsonObject a = StreamRecords(console.StdoutLogs).Single(), b = FileRecords(file).Single();
        foreach (string key in new[] { "event_name", "message", "service", "env", "version", "attributes" }) { Assert.IsTrue(JsonNode.DeepEquals(a[key], b[key]), key); }
        Assert.AreEqual("", console.Stderr); Assert.AreEqual("", file.Stderr); StringAssert.Contains(human.Stderr, "configured");
        Assert.IsFalse(human.Stderr.TrimStart().StartsWith('{')); Assert.IsFalse(human.Report["fileExists"]!.GetValue<bool>());
    }
    [TestMethod]
    public async Task GivenWarningConsoleAndDebugFileWhenWritingThenUnionInterestControlsLazyWork()
    {
        Observation value = await Run("levels", new() { ["CORE_LOG_CONSOLE_LEVEL"] = "Warning", ["CORE_LOG_FILE_LEVEL"] = "Debug" });
        Assert.AreEqual(2, value.Report["factories"]!.GetValue<int>());
        CollectionAssert.AreEqual(new[] { "debug", "warning" }, FileRecords(value).Select(x => x["event_name"]!.GetValue<string>()).ToArray());
        Assert.AreEqual("warning", (string?)StreamRecords(value.Stderr).Single()["event_name"]);
        Assert.AreEqual("Filtered", (string?)value.Report["receipts"]![0]!["outcome"]);
    }
    [TestMethod]
    public async Task GivenConcurrentExperimentsWhenAwaitingThenOperationsStayIsolatedWithSharedIdentity()
    {
        Observation value = await Run("concurrent"); JsonObject[] rows = FileRecords(value); Assert.AreEqual(3, rows.Length);
        foreach (string id in new[] { "A", "B" })
        {
            JsonObject row = rows.Single(r => (string?)r["event_name"] == "experiment." + id);
            Assert.AreEqual("operation." + id, (string?)row["operation_id"]); Assert.AreEqual(id, (string?)row["attributes"]!["experiment"]);
        }
        Assert.AreEqual(1, rows.Select(r => (string?)r["resource"]!["instance_id"]).Distinct().Count());
        Assert.IsNull(rows.Single(r => (string?)r["event_name"] == "after")["operation_id"]);
    }
    [TestMethod]
    public async Task GivenInvalidSettingWhenStartingThenKeyAndAcceptedValuesAreSafeAndActionable()
    {
        foreach ((string key, string invalid, string accepted) in new[]
        {
            ("CORE_LOG_LEVEL", "not-a-level", "Information"),
            ("CORE_LOG_CONSOLE_FORMAT", "not-a-format", "jsonl"),
            ("CORE_LOG_FILE_FORMAT", "not-a-format", "human"),
            ("CORE_LOG_CONSOLE_STREAM", "not-a-stream", "stderr"),
            ("CORE_LOG_HUMAN_DETAIL", "not-a-detail", "verbose"),
            ("CORE_LOG_QUEUE_CAPACITY", "invalid-number", "1"),
            ("CORE_LOG_MAX_ENTRY_BYTES", "0", "16384"),
            ("CORE_LOG_SCHEMA_VERSION", "2", "1")
        })
        {
            Observation value = await Run("configured", new() { [key] = invalid });
            Assert.IsTrue(value.Report["configurationError"]!.GetValue<bool>());
            JsonObject issue = value.Report["issues"]!.AsArray().Select(n => n!.AsObject()).Single(row => (string?)row["key"] == key);
            StringAssert.Contains(issue["explanation"]!.GetValue<string>(), accepted);
            string actualError = value.Report["message"]!.GetValue<string>() + value.Report["exceptionText"]!.GetValue<string>();
            Assert.IsFalse(actualError.Contains("must-not-appear", StringComparison.Ordinal));
            if (invalid.StartsWith("not-", StringComparison.Ordinal) || invalid == "invalid-number") { Assert.IsFalse(actualError.Contains(invalid, StringComparison.Ordinal)); }
            Assert.AreEqual("", value.Stderr);
        }
    }
    [TestMethod]
    public async Task GivenQueuePressureOrWriterFaultWhenLoggingThenBoundsAndIndependentOutputRemainTruthful()
    {
        Observation full = await Run("full", command: "provider-behavior"); JsonNode queue = full.Report["outputs"]![0]!;
        Assert.AreEqual("1", (string?)queue["full"]); Assert.AreEqual("2", (string?)queue["accepted"]); Assert.AreEqual("2", (string?)queue["written"]);
        Observation fault = await Run("partial-fault", command: "provider-behavior");
        Assert.AreEqual("1", (string?)fault.Report["outputs"]![0]!["failed"]); Assert.AreEqual("2", (string?)fault.Report["outputs"]![1]!["written"]);
        Assert.AreEqual(2, Records(fault.Report["fileBytes"]!.GetValue<string>()).Length);
        Assert.AreEqual(1, FileRecords(await Run("configured")).Length);
    }
    [TestMethod]
    public async Task GivenQueuedWorkWhenStoppingThenCompletionOrUnresolvedDeadlineIsExplicit()
    {
        Observation settled = await Run("configured"); Assert.AreEqual("Completed", (string?)settled.Report["completion"]); Assert.IsTrue(settled.Report["succeeded"]!.GetValue<bool>());
        Observation held = await Run("held-write", command: "stop-lifecycle"); JsonNode output = held.Report["result"]!["outputs"]![0]!;
        Assert.AreEqual("TimedOut", (string?)held.Report["result"]!["completion"]); Assert.AreEqual("1", (string?)output["pending"]); Assert.AreEqual("5", (string?)output["abandoned"]);
    }
    [TestMethod]
    public async Task GivenRestartedProducerWhenRotatingThenIdentityChangesAndOwnedRetentionPreservesNeighbors()
    {
        using var root = new TemporaryDirectory(); string folder = Path.Combine(root.Path, "logs"); Directory.CreateDirectory(folder);
        string neighbor = Path.Combine(folder, "unrelated.txt"); File.WriteAllText(neighbor, "preserve");
        Observation first = await RunAt("file-retention", "jsonl-1", root.Path, phase: "start");
        JsonObject[] firstRecords = RetentionRecords(first);
        Assert.IsTrue(firstRecords.Length > 0);
        Assert.IsTrue(firstRecords.All(row => (string?)row["resource"]!["instance_id"] == (string?)first.Report["instanceId"]));
        Observation second = await RunAt("file-retention", "jsonl-1", root.Path, phase: "resume");
        JsonObject[] secondRecords = RetentionRecords(second);
        Assert.IsTrue(secondRecords.Length > 0);
        Assert.IsTrue(secondRecords.All(row => (string?)row["resource"]!["instance_id"] == (string?)second.Report["instanceId"]));
        Assert.AreNotEqual((string?)first.Report["instanceId"], (string?)second.Report["instanceId"]);
        Assert.AreEqual((string?)first.Report["inventory"]!["groupId"], (string?)second.Report["inventory"]!["groupId"]);
        Assert.AreEqual(2, second.Report["inventory"]!["segments"]!.AsArray().Count); Assert.AreEqual("preserve", File.ReadAllText(neighbor));
        Assert.AreEqual(12L, second.Report["output"]!["written"]!.GetValue<long>());
    }
    [TestMethod]
    public async Task GivenCallerMutationsAfterWriteThenCapturedAttributesAndMessageStayOwned()
    {
        JsonObject row = FileRecords(await Run("mutation")).Single(); Assert.AreEqual("before", (string?)row["message"]);
        Assert.AreEqual("before", (string?)row["attributes"]!["owner"]); Assert.AreEqual("before", (string?)row["attributes"]!["nested"]!["value"]);
    }
    [TestMethod]
    public async Task GivenMicrosoftScopesWhenLoggingThenMappingCorrelationAndNonowningDisposalHold()
    {
        Observation value = await Run("provider"); JsonObject[] rows = FileRecords(value); Assert.AreEqual(2, rows.Length); JsonObject row = rows[0];
        Assert.AreEqual("Business.Provider", (string?)row["component"]); Assert.AreEqual("provider.failed", (string?)row["event_name"]);
        Assert.AreEqual(42, row["attributes"]!["Code"]!.GetValue<int>()); Assert.AreEqual("provider failure", (string?)row["exception"]!["message"]);
        Assert.AreEqual("provider.operation", (string?)row["operation_id"]); Assert.AreEqual((string?)value.Report["trace"], (string?)row["trace_id"]); Assert.AreEqual((string?)value.Report["span"], (string?)row["span_id"]);
        Assert.AreEqual("after-provider", (string?)rows[1]["event_name"]);
    }
    [TestMethod]
    public async Task GivenCollectorOrientedJsonThenIdentityAndEscapedRecordsNeedNoHostedService()
    {
        Observation value = await Run("configured"); JsonObject row = FileRecords(value).Single();
        Assert.AreEqual("Business.App", (string?)row["service"]); Assert.AreEqual("acceptance", (string?)row["env"]); Assert.AreEqual("1.2.3", (string?)row["version"]);
        Assert.AreEqual(value.Report["pid"]!.GetValue<int>(), row["resource"]!["process_id"]!.GetValue<int>());
        Assert.IsTrue(Guid.TryParseExact((string?)row["resource"]!["instance_id"], "D", out _));
        Assert.IsTrue(DateTimeOffset.TryParse((string?)row["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
        Assert.AreEqual("\u03a9\U0001f642 \"quoted\"\nnext\tline\u001b", (string?)row["message"]);
    }
    [TestMethod]
    public async Task GivenUnicodeDecimalAndUnsupportedValuesThenValidOversizeTruncatesAndInvalidIsCounted()
    {
        Observation value = await Run("bounded"); JsonObject[] rows = FileRecords(value); Assert.AreEqual(2, rows.Length);
        Assert.AreEqual("1.2300", (string?)rows[0]["attributes"]!["decimal"]); Assert.AreEqual("\u03a9\U0001f642", (string?)rows[0]["message"]);
        Assert.IsTrue(rows[1]["truncated"]!.GetValue<bool>()); Assert.AreEqual("oversized", (string?)rows[1]["event_name"]);
        Assert.AreEqual("Business.App", (string?)rows[1]["service"]); Assert.AreEqual("acceptance", (string?)rows[1]["env"]); Assert.AreEqual("1.2.3", (string?)rows[1]["version"]);
        Assert.AreEqual((string?)value.Report["instanceId"], (string?)rows[1]["resource"]!["instance_id"]); Assert.AreEqual("bounded.operation", (string?)rows[1]["operation_id"]);
        Assert.AreEqual(2L, value.Report["invalid"]!.GetValue<long>()); Assert.AreEqual(1L, value.Report["truncated"]!.GetValue<long>()); Assert.IsTrue(value.Report["loss"]!.GetValue<bool>());
        foreach (string line in Encoding.UTF8.GetString(Convert.FromBase64String(value.Report["file"]!.GetValue<string>())).Split('\n', StringSplitOptions.RemoveEmptyEntries)) { Assert.IsTrue(Encoding.UTF8.GetByteCount(line) + 1 <= 16384); }
    }
    [TestMethod]
    public async Task GivenDisabledOutputsWhenWritingAndStoppingThenNoFactoriesOrOrdinaryRecordsAppear()
    {
        Observation value = await Run("disabled", new() { ["CORE_LOG_FILE_LEVEL"] = "None", ["CORE_LOG_CONSOLE_LEVEL"] = "None" });
        Assert.AreEqual(0, value.Report["factories"]!.GetValue<int>()); Assert.AreEqual("", value.Stderr); Assert.AreEqual("", value.StdoutLogs); Assert.IsFalse(value.Report["fileExists"]!.GetValue<bool>());
        Assert.IsTrue(value.Report["receipts"]!.AsArray().All(x => (string?)x!["outcome"] == "Filtered")); Assert.AreEqual("Stopped", (string?)value.Report["stoppedOutcome"]);
    }
}
