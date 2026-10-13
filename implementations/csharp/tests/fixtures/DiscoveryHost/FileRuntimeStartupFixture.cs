using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRuntimeStartupFixture
{
    internal static readonly string[] Modes = ["jsonl", "restart", "file-display-loss", "human", "mixed-file-human", "mixed-console-human", "file-only", "console-only", "disabled", "truncated-file", "truncated-both", "frozen", "held-console", "existing-owner", "file-full", "rollback-missing", "rollback-format", "rollback-invalid-json", "rollback-over-limit", "rollback-parent", "rollback-meta-directory", "rollback-retained", "rollback-console-readonly", "rollback-console-throw", "rollback-launch-console", "rollback-launch-file", "physical-console"];
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    private sealed class Borrowed(bool readOnly, bool throws, bool held) : MemoryStream
    {
        internal int Disposes;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanWrite => throws ? throw new IOException("private-console-payload") : !readOnly && base.CanWrite;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (held) { Started.TrySetResult(); await Release.Task.WaitAsync(Limit); }
            await base.WriteAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); }
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown file startup mode.", nameof(mode)); }
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.jsonl"), redirected = Path.Combine(root, "redirected", "must-not-exist.log");
        bool console = mode is not ("file-only" or "truncated-file" or "disabled"), file = mode is not ("console-only" or "disabled");
        var options = new LoggingOptions
        {
            ConsoleLevel = console ? CoreLogLevel.Information : CoreLogLevel.None,
            FileLevel = file ? CoreLogLevel.Information : CoreLogLevel.None,
            FilePath = path,
            ConsoleFormat = mode is "human" or "mixed-console-human" ? LogOutputFormat.Human : LogOutputFormat.JsonLines,
            FileFormat = mode is "human" or "mixed-file-human" or "file-display-loss" ? LogOutputFormat.Human : LogOutputFormat.JsonLines,
            HumanDetail = HumanOutputDetail.Verbose,
            MaxEntryBytes = mode == "file-display-loss" ? 32768 : 16384,
            FileMaxBytes = 131072,
            QueueCapacity = 8,
            FileRetainedFiles = 1
        };
        options.Prepare(root);
        var probe = new Borrowed(mode == "rollback-console-readonly", mode == "rollback-console-throw", mode == "held-console");
        CoreLogRuntime? created = null, existing = null; var tasks = new List<Task>(); var queues = new List<OutputEventQueue>(); var suppressed = new List<bool>();
        int selections = 0, launches = 0, factories = 0; JsonObject? error = null; JsonObject? held = null; bool existingRunning = false, ownershipReleased = true;
        JsonObject? result = null; JsonObject? diagnostics = null;
        try
        {
            if (mode is "rollback-format" or "rollback-invalid-json" or "rollback-over-limit" or "rollback-retained")
            {
                using (FileOutputOwner.Initialize(path, mode == "rollback-format" ? LogOutputFormat.Human : LogOutputFormat.JsonLines)) { }
                File.WriteAllText(path, mode == "rollback-invalid-json" ? "{\"prior\":\n" : "{\"prior\":true}\n", new UTF8Encoding(false));
                if (mode == "rollback-over-limit") { File.WriteAllText(path, new string('x', 131072) + "\n"); }
                if (mode is "rollback-retained" or "rollback-format")
                {
                    JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject();
                    string id = inventory["groupId"]!.GetValue<string>(); var entries = inventory["segments"]!.AsArray(); entries[0]!["id"] = 2;
                    entries.Insert(0, new JsonObject { ["id"] = 1, ["fileName"] = ".cis-log-" + id + "-00000000000000000001.closed", ["state"] = "closed" });
                    File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString());
                    if (mode == "rollback-format") { File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, ".cis-log-" + id + "-00000000000000000001.closed"), "retained-must-survive\n"); }
                }
            }
            if (mode == "rollback-missing") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "{\"prior\":true}\n"); }
            if (mode == "rollback-parent") { File.WriteAllText(Path.Combine(root, "logs"), "keep-parent"); }
            if (mode == "rollback-meta-directory") { Directory.CreateDirectory(path + ".cis.inventory.json"); File.WriteAllText(Path.Combine(path + ".cis.inventory.json", "keep.txt"), "keep"); }
            if (mode is "existing-owner" or "file-full" or "restart")
            {
                var previous = new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FilePath = path, ConsoleFormat = LogOutputFormat.JsonLines, MaxEntryBytes = mode == "file-full" ? 65536 : 16384, FileMaxBytes = 131072 };
                previous.Prepare(root); existing = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), previous, new Clock());
                existing.CreateLogger("File.Component").Write(CoreLogLevel.Information, new LogEventData { EventName = "file.prior", Message = mode == "file-full" ? new string('a', 100000) : "prior record" });
                await Until(() => existing.GetDiagnostics().Outputs.Single().Written == 1);
                if (mode == "restart") { existing.CloseAdmission(); await existing.QueuedOutputs!.Completion.WaitAsync(Limit); existing = null; }
                else if (mode == "file-full")
                {
                    existing.CloseAdmission(); await existing.QueuedOutputs!.Completion.WaitAsync(Limit); existing = null;
                    // A valid JSONL preimage reaches the exact file limit using insignificant JSON whitespace.
                    byte[] seed = ReadShared(path), full = Enumerable.Repeat((byte)' ', 65536).ToArray(); seed.AsSpan(0, seed.Length - 1).CopyTo(full); full[^1] = 10; File.WriteAllBytes(path, full);
                    options.FileMaxBytes = 65536;
                }
            }
            Dictionary<string, byte[]> before = Snapshot(root);
            Task Launch(Func<Task> work)
            {
                launches++;
                if (mode == "rollback-launch-console" && launches == 1 || mode == "rollback-launch-file" && launches == 2) { throw new IOException("private-launch-payload"); }
                suppressed.Add(ExecutionContext.IsFlowSuppressed()); queues.Add((OutputEventQueue)work.Target!); Task task = Task.Run(work); tasks.Add(task); return task;
            }
            Stream Select(ConsoleOutputStream _)
            {
                selections++;
                if (mode == "frozen") { options.FilePath = redirected; options.FileFormat = LogOutputFormat.Human; options.FileLevel = CoreLogLevel.None; options.MaxEntryBytes = 65536; options.QueueCapacity = 0; options.ConsoleFormat = LogOutputFormat.Human; }
                return probe;
            }
            try { created = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock(), mode == "physical-console" ? null : Select, Launch); }
            catch (CoreLogInitializationException failure)
            { error = new JsonObject { ["output"] = failure.OutputName, ["code"] = failure.ErrorCode, ["message"] = failure.Message, ["inner"] = failure.InnerException is not null, ["dataCount"] = failure.Data.Count }; }
            JsonArray joined = new(tasks.Select(task => (JsonNode)JsonValue.Create(task.IsCompleted)!).ToArray());
            Dictionary<string, byte[]> afterStartup = Snapshot(root); bool preserved = before.All(pair => afterStartup.TryGetValue(pair.Key, out byte[]? value) && pair.Value.AsSpan().SequenceEqual(value));
            if (error is null)
            {
                string message = mode == "file-display-loss" ? new string('\u200d', 8192) : mode is "truncated-file" or "truncated-both" or "frozen" ? new string('z', 30000) : "owned \u03a9 \U0001f642";
                CoreLogger logger = created!.CreateLogger("File.Component");
                LogWriteResult written = logger.WriteLazy(CoreLogLevel.Information, () => { factories++; return new LogEventData { EventName = "file.event", Message = message }; });
                result = new JsonObject
                {
                    ["outcome"] = written.Outcome.ToString(),
                    ["truncated"] = written.IsTruncated,
                    ["outputs"] = new JsonArray(written.Outputs.Select(output => (JsonNode)new JsonObject { ["name"] = output.OutputName, ["status"] = output.Status.ToString(), ["ticket"] = output.AcceptedSequence }).ToArray())
                };
                if (mode == "held-console")
                {
                    await probe.Started.Task.WaitAsync(Limit); await Until(() => created.GetDiagnostics().Outputs.Single(output => output.OutputName == "file").Written == 1);
                    CoreLogDiagnostics live = created.GetDiagnostics(); held = new JsonObject
                    {
                        ["consolePending"] = live.Outputs.Single(output => output.OutputName == "console").Pending,
                        ["fileWritten"] = live.Outputs.Single(output => output.OutputName == "file").Written,
                        ["fileBytes"] = Convert.ToBase64String(ReadShared(path))
                    }; probe.Release.TrySetResult();
                }
                created.CloseAdmission();
                await created.QueuedOutputs!.Completion.WaitAsync(Limit);
                diagnostics = Diagnostics(created.GetDiagnostics());
                LogWriteResult stopped = logger.WriteLazy(CoreLogLevel.Information, () => { factories++; return new LogEventData { EventName = "should.not.run", Message = "closed" }; });
                result["afterClose"] = stopped.Outcome.ToString();
            }
            if (existing is not null)
            {
                existingRunning = !existing.QueuedOutputs!.Completion.IsCompleted;
                existing.CreateLogger("File.Component").Write(CoreLogLevel.Information, new LogEventData { EventName = "file.after.failure", Message = "existing writer survives" });
                existing.CloseAdmission(); await existing.QueuedOutputs.Completion.WaitAsync(Limit);
            }
            if (File.Exists(path + ".cis.lock")) { using var released = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); ownershipReleased = released.CanWrite; }
            bool standardWritable = true;
            if (mode == "physical-console") { Stream standard = Console.OpenStandardError(); standardWritable = standard.CanWrite; standard.Write(Encoding.UTF8.GetBytes("CIS_BORROWED_CONSOLE_SURVIVES\n")); }
            return new JsonObject
            {
                ["mode"] = mode,
                ["error"] = error,
                ["result"] = result,
                ["diagnostics"] = diagnostics,
                ["selections"] = selections,
                ["launches"] = launches,
                ["workers"] = tasks.Count,
                ["joinedAtReturn"] = joined,
                ["joinedAtEnd"] = tasks.All(task => task.IsCompleted),
                ["queues"] = new JsonArray(queues.Select(queue => { OutputAccounting counts = queue.CaptureAccounting(); return (JsonNode)new JsonObject { ["state"] = counts.State.ToString(), ["accepted"] = counts.Accepted, ["pending"] = counts.Pending }; }).ToArray()),
                ["flowSuppressed"] = new JsonArray(suppressed.Select(flag => (JsonNode)JsonValue.Create(flag)!).ToArray()),
                ["borrowedDisposes"] = probe.Disposes,
                ["factories"] = factories,
                ["preservedAtStartup"] = preserved,
                ["ownershipReleased"] = ownershipReleased,
                ["existingRunning"] = existingRunning,
                ["held"] = held,
                ["closedBytes"] = mode == "file-full" ? Convert.ToBase64String(ReadShared(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.closed").Single())) : null,
                ["fileBytes"] = File.Exists(path) ? Convert.ToBase64String(ReadShared(path)) : "",
                ["consoleBytes"] = Convert.ToBase64String(probe.ToArray()),
                ["fileExists"] = File.Exists(path),
                ["inventoryExists"] = File.Exists(path + ".cis.inventory.json"),
                ["redirectedExists"] = Directory.Exists(Path.GetDirectoryName(redirected)),
                ["standardWritable"] = standardWritable
            };
        }
        finally
        {
            probe.Release.TrySetResult(); created?.CloseAdmission(); existing?.CloseAdmission();
            foreach (OutputEventQueue queue in queues) { queue.Complete(); }
            await Task.WhenAll(tasks.Concat(new[] { created?.QueuedOutputs?.Completion ?? Task.CompletedTask, existing?.QueuedOutputs?.Completion ?? Task.CompletedTask })).WaitAsync(Limit);
            probe.Dispose();
        }
    }
    private static async Task Until(Func<bool> condition)
    { using var stop = new CancellationTokenSource(Limit); while (!condition()) { await Task.Delay(1, stop.Token); } }
    private static byte[] ReadShared(string path)
    { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray(); }
    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith(".cis.lock", StringComparison.Ordinal)).ToDictionary(path => Path.GetRelativePath(root, path), ReadShared, StringComparer.Ordinal);
    private static JsonObject Diagnostics(CoreLogDiagnostics snapshot) => new()
    {
        ["state"] = snapshot.State.ToString(),
        ["truncated"] = snapshot.TruncatedEventCount,
        ["outputs"] = new JsonArray(snapshot.Outputs.Select(output => (JsonNode)new JsonObject
        {
            ["name"] = output.OutputName,
            ["state"] = output.State.ToString(),
            ["accepted"] = output.Accepted,
            ["written"] = output.Written,
            ["failed"] = output.Failed,
            ["abandoned"] = output.Abandoned,
            ["pending"] = output.Pending,
            ["fault"] = output.LastFaultCode
        }).ToArray())
    };
    internal static int Run(string mode, string root)
    {
        JsonObject report = ExecuteAsync(mode, root).GetAwaiter().GetResult();
        string host = typeof(FileRuntimeStartupFixture).Assembly.Location, core = typeof(CoreLogRuntime).Assembly.Location;
        report["observed"] = new JsonObject
        {
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(host),
            ["coreSha256"] = Hash(core)
        };
        Console.WriteLine(report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false })); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
