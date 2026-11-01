using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class CandidateLoggingProcessTests
{
    private static CandidatePackageFixture fixture = null!;
    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        fixture = await CandidatePackageFixture.Prepare(); context.WriteLine("Exact package consumer evidence: " + fixture.Root);
    }
    private static JsonObject Output(JsonObject report, string name, string section = "diagnostics") => report[section]!["outputs"]!.AsArray().Single(o => (string?)o!["outputName"] == name)!.AsObject();
    private static long Count(JsonObject value, string key) => value[key]!.GetValue<long>();
    private static string PathFor(string root) => Path.Combine(root, "Ω-🙂.log");
    private static byte[] ReadLive(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream(); input.CopyTo(copy); return copy.ToArray();
    }
    private static JsonObject Inventory(string path) => JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
    private static JsonObject[] Records(byte[] bytes)
    {
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        Assert.IsFalse(bytes.Contains((byte)13)); Assert.IsFalse(bytes.Contains((byte)27));
        if (bytes.Length == 0) { return []; }
        Assert.AreEqual((byte)10, bytes[^1]); string[] lines = new UTF8Encoding(false, true).GetString(bytes).Split('\n');
        Assert.IsTrue(lines.Take(lines.Length - 1).All(line => line.Length > 0));
        return lines.Take(lines.Length - 1).Select(line => { Assert.IsTrue(Encoding.UTF8.GetByteCount(line) + 1 <= 16384); return JsonNode.Parse(line)!.AsObject(); }).ToArray();
    }
    private static JsonObject[] Retained(string path, JsonObject inventory) => inventory["segments"]!.AsArray().SelectMany(segment => Records(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, segment!["fileName"]!.GetValue<string>())))).ToArray();
    private static void SuccessfulStop(JsonObject report)
    {
        Assert.AreEqual("stopped", (string?)report["stage"]); Assert.AreEqual("Completed", (string?)report["stop"]!["completion"]);
        Assert.IsTrue(report["stop"]!["succeeded"]!.GetValue<bool>()); Assert.IsTrue(report["sameStopTask"]!.GetValue<bool>());
        Assert.AreEqual("Stopped", (string?)report["stoppedOutcome"]);
        foreach (JsonNode? output in report["diagnostics"]!["outputs"]!.AsArray())
        { Assert.AreEqual(0L, output!["pending"]!.GetValue<long>()); Assert.AreEqual(output["accepted"]!.GetValue<long>(), output["written"]!.GetValue<long>()); }
    }

    [TestMethod]
    public async Task PackedProcessingPoliciesAndConvenienceCallsUseTheSameWriterPipeline()
    {
        using var root = new TemporaryDirectory(); var value = await fixture.Run("processing", root.Path);
        SuccessfulStop(value.Report);
        var records = Records(File.ReadAllBytes(PathFor(root.Path)));
        CollectionAssert.AreEqual(new[] { "log.message", "typed", "lazy" }, records.Select(row => (string?)row["event_name"]).ToArray());
        Assert.IsTrue(records.All(row => (string?)row["message"] == "safe" && !row.ContainsKey("exception")));
        Assert.AreEqual(42, records[1]["attributes"]!["customer"]!.GetValue<int>());
        string application = string.Concat(value.Report["applicationLines"]!.AsArray().Select(line => line!.GetValue<string>()));
        Assert.AreEqual(File.ReadAllText(PathFor(root.Path)), application);
        Assert.AreEqual(1, value.Report["applicationDisposals"]!.GetValue<int>());
        JsonObject incoming = value.Report["stop"]!["incoming"]!.AsObject();
        Assert.AreEqual(6L, Count(incoming, "accepted")); Assert.AreEqual(6L, Count(incoming, "processed"));
        Assert.AreEqual(3L, Count(incoming, "suppressed")); Assert.AreEqual(1L, Count(incoming, "duplicateSuppressed"));
        Assert.AreEqual(0L, Count(incoming, "failed")); Assert.AreEqual(0L, Count(incoming, "pending"));
    }

    [TestMethod]
    [DataRow("human", "normal", "stderr")]
    [DataRow("human", "verbose", "stderr")]
    [DataRow("jsonl", "normal", "stderr")]
    [DataRow("jsonl", "normal", "stdout")]
    public async Task PackedConsumerSelectsPresentationStreamsAndIndependentThresholds(string format, string detail, string stream)
    {
        using var root = new TemporaryDirectory();
        var value = await fixture.Run("emit", root.Path, new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_LEVEL"] = "Warning", ["CORE_LOG_CONSOLE_FORMAT"] = format, ["CORE_LOG_HUMAN_DETAIL"] = detail, ["CORE_LOG_CONSOLE_STREAM"] = stream });
        SuccessfulStop(value.Report); Assert.AreEqual(1, value.Report["factories"]!.GetValue<int>());
        JsonObject[] file = Records(File.ReadAllBytes(PathFor(root.Path)));
        CollectionAssert.AreEqual(new[] { "event.info", "event.error", "event.debug" }, file.Select(row => (string?)row["event_name"]).ToArray());
        foreach (JsonObject row in file)
        {
            Assert.AreEqual("candidate.operation", (string?)row["operation_id"]); Assert.AreEqual("Candidate.App", (string?)row["service"]);
            Assert.AreEqual("acceptance", (string?)row["env"]); Assert.AreEqual("2.3.4", (string?)row["version"]);
            Assert.AreEqual((string?)value.Report["instanceId"], (string?)row["resource"]!["instance_id"]); Assert.AreEqual(value.Report["pid"]!.GetValue<int>(), row["resource"]!["process_id"]!.GetValue<int>());
        }
        string console = stream == "stderr" ? value.Stderr : value.Stdout;
        byte[] consoleBytes = (stream == "stderr" ? value.StderrBytes : value.StdoutBytes).ToArray();
        Assert.IsFalse(consoleBytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }));
        if (format == "jsonl")
        {
            Assert.AreEqual("event.error", (string?)Records(consoleBytes).Single()["event_name"]);
            Assert.ThrowsExactly<AssertFailedException>(() => Records([239, 187, 191, .. consoleBytes]));
        }
        else
        {
            StringAssert.Contains(console, "event.error"); Assert.IsFalse(console.Contains('\u001b')); Assert.AreEqual(1, console.Count(c => c == '\n'));
            if (detail == "verbose") { StringAssert.Contains(console, "candidate failure"); StringAssert.Contains(console, "Candidate.App"); StringAssert.Contains(console, "candidate.operation"); }
        }
        if (stream == "stderr") { Assert.AreEqual("ok", (string?)JsonNode.Parse(value.Stdout)!["command"]); }
        else { Assert.AreEqual(0, value.StderrBytes.Length); }
    }

    [TestMethod]
    public async Task PackedMicrosoftProviderMapsScopesAndLeavesItsRuntimeUsableAfterDisposal()
    {
        using var root = new TemporaryDirectory(); var value = await fixture.Run("provider", root.Path); SuccessfulStop(value.Report);
        JsonObject[] rows = Records(File.ReadAllBytes(PathFor(root.Path))); Assert.AreEqual(2, rows.Length);
        Assert.AreEqual("Candidate.Provider", (string?)rows[0]["component"]); Assert.AreEqual("provider.event", (string?)rows[0]["event_name"]);
        Assert.AreEqual("provider.operation", (string?)rows[0]["operation_id"]); Assert.AreEqual(42, rows[0]["attributes"]!["Code"]!.GetValue<int>());
        Assert.AreEqual("provider failure", (string?)rows[0]["exception"]!["message"]); Assert.AreEqual(32, rows[0]["trace_id"]!.GetValue<string>().Length); Assert.AreEqual(16, rows[0]["span_id"]!.GetValue<string>().Length);
        Assert.AreEqual("after.provider", (string?)rows[1]["event_name"]);
    }

    [TestMethod]
    public async Task CandidateOriginGuardRejectsAlteredAssemblyAndRuntimeEvidence()
    {
        using var root = new TemporaryDirectory(); var value = await fixture.Run("disabled", root.Path, new Dictionary<string, string?> { ["CORE_LOG_FILE_LEVEL"] = "None" });
        foreach (string name in new[] { "core", "logging", "di" })
        {
            JsonObject changed = value.Report.DeepClone().AsObject(); changed[name]!["sha256"] = new string('0', 64);
            Assert.ThrowsExactly<AssertFailedException>(() => fixture.Origin(changed));
        }
        JsonObject wrongRuntime = value.Report.DeepClone().AsObject(); wrongRuntime["runtime"] = ".NET 0.0.0";
        Assert.ThrowsExactly<AssertFailedException>(() => fixture.Origin(wrongRuntime));
    }

    [TestMethod]
    public async Task PackedConsumerWithDisabledOutputsSkipsFactoriesAndStopsIdempotently()
    {
        using var root = new TemporaryDirectory(); var value = await fixture.Run("disabled", root.Path, new Dictionary<string, string?> { ["CORE_LOG_FILE_LEVEL"] = "None" });
        SuccessfulStop(value.Report); Assert.AreEqual(0, value.Report["factories"]!.GetValue<int>()); Assert.AreEqual("Filtered", (string?)value.Report["receipts"]![0]!["outcome"]);
        Assert.AreEqual(0, value.StdoutBytes.Length); Assert.AreEqual(0, value.StderrBytes.Length); Assert.IsFalse(File.Exists(PathFor(root.Path)));
    }

    [TestMethod]
    public async Task PackedConsumersEnforceExclusiveGroupsReleaseOnTerminationAndKeepDistinctGroupsIndependent()
    {
        using var root = new TemporaryDirectory(); string path = PathFor(root.Path);
        await using var owner = fixture.Start("interactive", root.Path); JsonObject ready = await owner.Read();
        await owner.Command("{\"name\":\"owner\",\"count\":1,\"payload\":0}"); byte[] prior = ReadLive(path), inventory = File.ReadAllBytes(path + ".cis.inventory.json");
        var blocked = await fixture.Run("produce", root.Path); Assert.AreEqual("FileOwnershipUnavailable", (string?)blocked.Report["code"]);
        CollectionAssert.AreEqual(prior, ReadLive(path)); CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json"));
        var independent = await fixture.Run("produce", root.Path, new Dictionary<string, string?> { ["CORE_LOG_FILE_PATH"] = Path.Combine(root.Path, "other.log") }); SuccessfulStop(independent.Report);
        CollectionAssert.AreEqual(prior, ReadLive(path));
        owner.Process.Kill(entireProcessTree: true); await owner.Process.WaitForExitAsync().WaitAsync(CandidatePackageFixture.Guard);
        var next = await fixture.Run("produce", root.Path); SuccessfulStop(next.Report);
        CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json"));
        JsonObject[] rows = Records(ReadLive(path)); Assert.AreEqual(2, rows.Length);
        Assert.AreEqual((string?)ready["instanceId"], (string?)rows[0]["resource"]!["instance_id"]); Assert.AreEqual((string?)next.Report["instanceId"], (string?)rows[1]["resource"]!["instance_id"]);
        Assert.AreNotEqual((string?)ready["instanceId"], (string?)next.Report["instanceId"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PackedStartupRejectsUnsafeExistingOutputWithoutChangingBytesOrLeakingItsLease(bool incompatibleFormat)
    {
        using var root = new TemporaryDirectory(); string path = PathFor(root.Path);
        if (incompatibleFormat) { SuccessfulStop((await fixture.Run("produce", root.Path)).Report); }
        else { File.WriteAllText(path, "{\"existing\":true}\n"); }
        byte[] before = ReadLive(path); byte[]? inventory = File.Exists(path + ".cis.inventory.json") ? File.ReadAllBytes(path + ".cis.inventory.json") : null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var failed = await fixture.Run("produce", root.Path, incompatibleFormat ? new Dictionary<string, string?> { ["CORE_LOG_FILE_FORMAT"] = "human" } : null);
            Assert.AreEqual("initialization-failed", (string?)failed.Report["stage"]); Assert.AreEqual(incompatibleFormat ? "FileInventoryIncompatible" : "FileInventoryMissing", (string?)failed.Report["code"]);
            CollectionAssert.AreEqual(before, ReadLive(path));
            if (inventory is not null) { CollectionAssert.AreEqual(inventory, File.ReadAllBytes(path + ".cis.inventory.json")); }
            using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); Assert.IsTrue(lease.CanWrite);
        }
    }

    [TestMethod]
    public async Task PackedRestartContinuesRotationIdentitiesAndRetainsOnlyOwnedNewestSegments()
    {
        using var root = new TemporaryDirectory(); string path = PathFor(root.Path), neighbor = Path.Combine(root.Path, "foreign.closed"); File.WriteAllText(neighbor, "preserve");
        var first = await fixture.Run("produce", root.Path, count: 12); SuccessfulStop(first.Report); JsonObject initial = Inventory(path);
        Assert.AreEqual(3, initial["segments"]!.AsArray().Count); Assert.IsTrue(Retained(path, initial).All(row => (string?)row["resource"]!["instance_id"] == (string?)first.Report["instanceId"]));
        var next = await fixture.Run("produce", root.Path, count: 12); SuccessfulStop(next.Report); JsonObject final = Inventory(path);
        Assert.IsTrue(JsonNode.DeepEquals(initial, next.Report["before"])); Assert.AreEqual((string?)initial["groupId"], (string?)final["groupId"]);
        Assert.AreNotEqual((string?)first.Report["instanceId"], (string?)next.Report["instanceId"]); Assert.AreEqual(3, final["segments"]!.AsArray().Count);
        long previous = initial["segments"]!.AsArray().Last()!["id"]!.GetValue<long>();
        Assert.IsTrue(final["segments"]!.AsArray().All(segment => segment!["id"]!.GetValue<long>() > previous));
        JsonObject[] rows = Retained(path, final); Assert.AreEqual(12, rows.Length); Assert.IsTrue(rows.All(row => (string?)row["resource"]!["instance_id"] == (string?)next.Report["instanceId"]));
        CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(i => "producer." + i).ToArray(), rows.Select(row => (string?)row["event_name"]).ToArray());
        foreach (JsonNode? segment in initial["segments"]!.AsArray().Take(2)) { Assert.IsFalse(File.Exists(Path.Combine(root.Path, segment!["fileName"]!.GetValue<string>()))); }
        Assert.AreEqual("preserve", File.ReadAllText(neighbor)); Assert.AreEqual(2, Directory.EnumerateFiles(root.Path, ".cis-log-*.closed").Count());
        foreach (JsonNode? segment in final["segments"]!.AsArray()) { Assert.IsTrue(new FileInfo(Path.Combine(root.Path, segment!["fileName"]!.GetValue<string>())).Length <= 65536); }
    }

    [TestMethod]
    [DataRow("intent")]
    [DataRow("renamed")]
    [DataRow("published")]
    [DataRow("partial")]
    public async Task PackedStartupReconcilesPreparedInterruptedRotationAndPreservesOriginalBytes(string stage)
    {
        using var root = new TemporaryDirectory(); string path = PathFor(root.Path); SuccessfulStop((await fixture.Run("produce", root.Path)).Report);
        JsonObject previous = Inventory(path); byte[] original = ReadLive(path); string group = previous["groupId"]!.GetValue<string>();
        string archived = ".cis-log-" + group + "-" + 1.ToString("D20", CultureInfo.InvariantCulture) + (stage == "partial" ? ".damaged" : ".closed");
        if (stage == "partial")
        {
            File.AppendAllText(path, "{\"partial\":"); original = ReadLive(path);
        }
        else
        {
            JsonObject next = previous.DeepClone().AsObject(); next["segments"] = new JsonArray(new JsonObject { ["id"] = 1, ["fileName"] = archived, ["state"] = "closed" }, new JsonObject { ["id"] = 2, ["fileName"] = Path.GetFileName(path), ["state"] = "active" });
            File.WriteAllText(path + ".cis.transition.json", new JsonObject { ["schema"] = 1, ["recovery"] = false, ["retainedLimit"] = 2, ["originalBytes"] = original.Length, ["originalSha256"] = Convert.ToHexStringLower(SHA256.HashData(original)), ["previous"] = previous.DeepClone(), ["next"] = next.DeepClone() }.ToJsonString());
            if (stage != "intent") { File.Move(path, Path.Combine(root.Path, archived)); }
            if (stage == "published") { File.WriteAllBytes(path, []); File.WriteAllText(path + ".cis.inventory.json", next.ToJsonString()); }
        }
        var resumed = await fixture.Run("produce", root.Path); SuccessfulStop(resumed.Report); CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(root.Path, archived)));
        JsonObject inventory = Inventory(path); Assert.AreEqual(group, (string?)inventory["groupId"]); Assert.AreEqual(2L, inventory["segments"]![1]!["id"]!.GetValue<long>());
        Assert.AreEqual(stage == "partial" ? "damaged" : "closed", (string?)inventory["segments"]![0]!["state"]); Assert.IsFalse(File.Exists(path + ".cis.transition.json"));
        Assert.AreEqual((string?)resumed.Report["instanceId"], (string?)Records(ReadLive(path)).Single()["resource"]!["instance_id"]);
        Assert.AreEqual(stage == "partial", Output(resumed.Report, "file")["recoveredPartialSegment"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task PackedRotationCollisionFaultsOnlyFileAndKeepsConsoleAvailable()
    {
        using var root = new TemporaryDirectory(); string path = PathFor(root.Path);
        await using var child = fixture.Start("interactive", root.Path, new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_LEVEL"] = "Information" }); await child.Read();
        JsonObject initial = await child.Command("{\"name\":\"initial\",\"count\":4,\"payload\":14000}"); Assert.IsTrue(initial["flush"]!["succeeded"]!.GetValue<bool>());
        byte[] before = ReadLive(path); JsonObject inventory = Inventory(path); string collision = Path.Combine(root.Path, ".cis-log-" + inventory["groupId"]!.GetValue<string>() + "-" + 1.ToString("D20", CultureInfo.InvariantCulture) + ".closed"); File.WriteAllText(collision, "do not replace");
        JsonObject failed = await child.Command("{\"name\":\"trigger\",\"count\":1,\"payload\":14000}"); Assert.AreEqual("Faulted", (string?)Output(failed, "file")["state"]); Assert.AreEqual(1L, Count(Output(failed, "file"), "failed"));
        JsonObject after = await child.Command("{\"name\":\"after\",\"count\":1,\"payload\":0}"); Assert.AreEqual(1L, Count(Output(after, "file"), "unavailable"));
        JsonObject stop = await child.Command("stop"); Assert.IsTrue(stop["stop"]!["lossObserved"]!.GetValue<bool>()); Assert.IsFalse(stop["stop"]!["succeeded"]!.GetValue<bool>());
        _ = await child.Finish(); CollectionAssert.AreEqual(new[] { "initial.0", "initial.1", "initial.2", "initial.3", "trigger.0", "after.0" }, Records(child.RawStderr!).Select(row => (string?)row["event_name"]).ToArray());
        CollectionAssert.AreEqual(before, ReadLive(path)); Assert.AreEqual("do not replace", File.ReadAllText(collision)); Assert.AreEqual(0L, Count(Output(stop, "console"), "failed"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PackedPipePressureIsBoundedAndStopReportsActualDeadlineBeforeLateDrain(bool deadline)
    {
        using var root = new TemporaryDirectory();
        await using var child = fixture.Start(deadline ? "deadline" : "burst", root.Path, new Dictionary<string, string?> { ["CORE_LOG_CONSOLE_LEVEL"] = "Information", ["CORE_LOG_CONSOLE_STREAM"] = "stdout", ["CORE_LOG_FILE_LEVEL"] = "None", ["CORE_LOG_QUEUE_CAPACITY"] = "1", ["CORE_LOG_SHUTDOWN_TIMEOUT_MS"] = deadline ? "100" : "5000" });
        JsonObject blocked = await child.Read(); Assert.AreEqual("blocked", (string?)blocked["stage"]); JsonObject held = Output(blocked, "console");
        Assert.IsTrue(Count(held, "queueFull") > 0); Assert.IsTrue(Count(held, "pending") is >= 1 and <= 2); Assert.AreEqual(Count(held, "accepted"), Count(held, "written") + Count(held, "pending"));
        JsonObject? expired = deadline ? await child.Read() : null;
        if (expired is not null)
        {
            Assert.AreEqual("TimedOut", (string?)expired["stop"]!["completion"]); Assert.IsFalse(expired["stop"]!["succeeded"]!.GetValue<bool>());
            Assert.IsTrue(Count(Output(expired, "console", "stop"), "pending") > 0);
        }
        _ = child.BeginDrain(); JsonObject final = await child.Command("drain"); _ = await child.Finish(); byte[] bytes = child.RawStdout!;
        if (!deadline)
        {
            Assert.AreEqual("stopped", (string?)final["stage"]);
            Assert.AreEqual("Completed", (string?)final["stop"]!["completion"]);
            Assert.IsFalse(final["stop"]!["succeeded"]!.GetValue<bool>(), "Writer rejections lost events already accepted by the incoming stage.");
            Assert.IsTrue(final["stop"]!["lossObserved"]!.GetValue<bool>());
            Assert.IsTrue(final["sameStopTask"]!.GetValue<bool>()); Assert.AreEqual("Stopped", (string?)final["stoppedOutcome"]);
            JsonObject incoming = final["stop"]!["incoming"]!.AsObject();
            Assert.IsTrue(Count(incoming, "rejectedByWriters") > 0);
            Assert.AreEqual(Count(incoming, "accepted"), Count(incoming, "consumed") + Count(incoming, "evicted") + Count(incoming, "abandoned") + Count(incoming, "waiting"));
            Assert.AreEqual(Count(incoming, "consumed"), Count(incoming, "processed") + Count(incoming, "failed"));
            Assert.AreEqual(0L, Count(incoming, "waiting")); Assert.AreEqual(0L, Count(incoming, "activePublishers"));
            Assert.IsTrue(incoming["isExact"]!.GetValue<bool>());
            Assert.AreEqual(Count(incoming, "processed"), Count(Output(final, "console"), "written") + Count(incoming, "rejectedByWriters"));
            Assert.AreEqual(0L, Count(Output(final, "console"), "pending"));
            Assert.AreEqual(Count(Output(final, "console"), "accepted"), Count(Output(final, "console"), "written"));
            Assert.AreEqual(Count(Output(final, "console"), "written"), (long)Records(bytes).Length);
        }
        else
        {
            Assert.AreEqual("settled", (string?)final["stage"]); Assert.IsTrue(final["sameFrozenStop"]!.GetValue<bool>()); Assert.AreEqual(0L, Count(Output(final, "console"), "pending"));
            JsonObject observed = Output(final, "console"); Assert.AreEqual(Count(observed, "accepted"), Count(observed, "written") + Count(observed, "failed") + Count(observed, "abandoned"));
            Assert.IsTrue(JsonNode.DeepEquals(expired!["stop"], final["diagnostics"]!["lastStopResult"]), "The originally returned deadline observation must remain frozen.");
            int prefixLength = Array.LastIndexOf(bytes, (byte)10) + 1;
            long complete = Records(bytes[..prefixLength]).LongLength;
            long uncertain = complete - Count(observed, "written");
            Assert.IsTrue(uncertain >= 0 && uncertain <= Count(observed, "failed"), "Complete but unconfirmed bytes can belong only to failed writes.");
            int tailBytes = bytes.Length - prefixLength;
            Assert.IsTrue(tailBytes < 16384);
            Assert.IsTrue(uncertain + (tailBytes == 0 ? 0 : 1) <= Count(observed, "failed"), "Any partial tail must belong to an unconfirmed failed write.");
        }
    }

    [TestMethod]
    public async Task PackedAlreadyCancelledStopWaiterDoesNotCancelSharedDrainOrDispose()
    {
        using var root = new TemporaryDirectory(); var value = await fixture.Run("cancel", root.Path); SuccessfulStop(value.Report);
        Assert.AreEqual(16, Records(File.ReadAllBytes(PathFor(root.Path))).Length); Assert.IsFalse(value.Report["stop"]!["lossObserved"]!.GetValue<bool>());
        using var lease = new FileStream(PathFor(root.Path) + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); Assert.IsTrue(lease.CanWrite);
    }
}
