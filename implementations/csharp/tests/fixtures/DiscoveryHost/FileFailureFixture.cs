using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileFailureFixture
{
    internal static readonly string[] Modes = ["delete-jsonl-directory", "delete-human-directory", "render-empty", "render-active", "delete-jsonl-damaged-directory"];
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    private sealed class Borrowed : MemoryStream
    {
        internal int Disposes;
        protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); }
    }
    internal static int Run(string mode, string root) { Console.WriteLine(ExecuteAsync(mode, root, externalRelease: mode.EndsWith("permission", StringComparison.Ordinal)).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root, bool externalRelease = false)
    {
        bool render = mode.StartsWith("render", StringComparison.Ordinal), human = render || mode.StartsWith("delete-human", StringComparison.Ordinal), empty = mode == "render-empty", damaged = mode.Contains("-damaged-", StringComparison.Ordinal);
        if (!Modes.Contains(mode, StringComparer.Ordinal) && mode is not ("delete-jsonl-permission" or "delete-human-permission" or "delete-jsonl-damaged-permission")) { throw new ArgumentException("Unknown file failure mode.", nameof(mode)); }
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.log"); LogOutputFormat format = human ? LogOutputFormat.Human : LogOutputFormat.JsonLines;
        using (FileOutputOwner.Initialize(path, format)) { }
        JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(path + ".cis.inventory.json"))!.AsObject(); string group = inventory["groupId"]!.GetValue<string>(), directory = Path.GetDirectoryName(path)!;
        string victim = Path.Combine(directory, ".cis-log-" + group + "-00000000000000000001." + (damaged ? "damaged" : "closed"));
        if (!empty)
        {
            inventory["segments"]![0]!["id"] = 2; inventory["segments"]!.AsArray().Insert(0, new JsonObject { ["id"] = 1, ["fileName"] = Path.GetFileName(victim), ["state"] = damaged ? "damaged" : "closed" });
            File.WriteAllText(path + ".cis.inventory.json", inventory.ToJsonString()); File.WriteAllText(victim, human ? "retained=true\n" : "{\"retained\":true}\n", new UTF8Encoding(false));
            string seed = render ? "seed=\"prior\"\n" : human ? "seed=\"" + new string('a', 65528) + "\"\n" : "{\"seed\":\"" + new string('a', 65524) + "\"}\n";
            File.WriteAllText(path, seed, new UTF8Encoding(false));
        }
        string foreign = Path.Combine(directory, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"), unlisted = Path.Combine(directory, ".cis-log-" + group + "-00000000000000000050.closed"), outside = Path.Combine(root, "archive", "keep.log"), other = Path.Combine(directory, "other.log");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!); foreach (string name in new[] { foreign, unlisted, outside, other }) { File.WriteAllText(name, "untouched neighbor\n", new UTF8Encoding(false)); }
        var options = new LoggingOptions { FilePath = path, FileFormat = format, ConsoleFormat = LogOutputFormat.JsonLines, HumanDetail = HumanOutputDetail.Verbose, FileMaxBytes = 65536, FileRetainedFiles = 1, MaxEntryBytes = 16384, QueueCapacity = 4 }; options.Prepare(root);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int launches = 0, factories = 0;
        Task Launch(Func<Task> work)
        {
            launches++;
            return launches == 1 ? Task.Run(work) : Task.Run(async () => { await release.Task.WaitAsync(Limit); await work(); });
        }
        using var borrowed = new Borrowed(); CoreLogRuntime runtime; byte[]? attemptedFileBytes = null; int attempts = 0;
        if (!render) { runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock(), _ => borrowed, Launch); }
        else
        {
            // Real producer/rendering/queues/physical writer, with a deliberately smaller
            // trusted internal segment to qualify the otherwise defensive Human guard.
            var gate = new object(); var console = new ConsoleOutputWriter(borrowed, LogOutputFormat.JsonLines, HumanOutputDetail.Verbose, 16384); var file = new FileOutputWriter(path, format, HumanOutputDetail.Verbose, 16384, 16, 1);
            ValueTask Attempt(AdmittedEvent admitted) { attempts++; attemptedFileBytes = admitted.PreparedLine?.ToArray() ?? throw new InvalidOperationException("Missing prepared Human bytes."); return file.WriteAsync(admitted); }
            var queues = new OutputQueues(4, true, true, console.WriteAsync, Attempt, gate, Launch, file);
            try { console.Initialize(); file.Initialize(); runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), options, queues.Console, queues.File, new Clock(), gate, queues, console, file); }
            catch { release.TrySetResult(); queues.Complete(); await queues.Completion.WaitAsync(Limit); file.Dispose(); throw; }
        }
        try
        {
            if (mode.EndsWith("directory", StringComparison.Ordinal)) { File.Delete(victim); Directory.CreateDirectory(victim); File.WriteAllText(Path.Combine(victim, "keep.txt"), "blocked deletion\n"); }
            var protectedPaths = new List<string> { path, path + ".cis.inventory.json", foreign, unlisted, outside, other };
            if (!empty) { protectedPaths.Add(mode.EndsWith("directory", StringComparison.Ordinal) ? Path.Combine(victim, "keep.txt") : victim); }
            JsonObject Snapshot() => new(protectedPaths.Select(name => new KeyValuePair<string, JsonNode?>(Path.GetRelativePath(root, name).Replace('\\', '/'), JsonValue.Create(Convert.ToBase64String(Read(name))))));
            string[] Names() => Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
            JsonObject before = Snapshot(); string[] namesBefore = Names(); var results = new JsonArray(); CoreLogger logger = runtime.CreateLogger("Failure");
            LogWriteResult Write(int index) => logger.WriteLazy(CoreLogLevel.Information, () => { factories++; return new LogEventData { EventName = "failure." + index, Message = "preserved", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } }; });
            for (int index = 0; index < 3; index++) { results.Add(Result(Write(index))); }
            if (externalRelease)
            {
                Console.WriteLine(new JsonObject { ["stage"] = "prepared", ["mode"] = mode, ["pid"] = Environment.ProcessId, ["path"] = path, ["victim"] = victim }.ToJsonString()); Console.Out.Flush();
                if (Console.ReadLine() != "continue") { throw new InvalidOperationException("Missing owned permission release."); }
            }
            release.TrySetResult(); FileOutputWriteException error;
            try { await runtime.QueuedOutputs!.File!.Completion.WaitAsync(Limit); throw new InvalidOperationException("Expected terminal file failure."); }
            catch (FileOutputWriteException observed) { error = observed; }
            // Probe inside the producer while the independent console runtime is still alive.
            bool leaseReleased = Released(path + ".cis.lock"), activeReleased = Released(path); CoreLogDiagnostics fault = runtime.GetDiagnostics();
            results.Add(Result(Write(3))); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Console!.Completion.WaitAsync(Limit); CoreLogDiagnostics final = runtime.GetDiagnostics();
            LogWriteResult stopped = logger.WriteLazy(CoreLogLevel.Information, () => throw new InvalidOperationException("Stopped factory ran."));
            return new JsonObject
            {
                ["segmentMaxBytes"] = render ? 16 : 65536,
                ["attemptedFileBytes"] = attemptedFileBytes is null ? null : Convert.ToBase64String(attemptedFileBytes),
                ["writeAttempts"] = render ? attempts : null,
                ["mode"] = mode,
                ["composition"] = render ? "trusted-internal-small-segment" : "configured-runtime",
                ["pid"] = Environment.ProcessId,
                ["path"] = path,
                ["victim"] = empty ? null : victim,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
                ["fixtureSha256"] = Hash(typeof(FileFailureFixture).Assembly.Location),
                ["error"] = new JsonObject { ["code"] = error.FaultCode, ["message"] = error.Message, ["inner"] = error.InnerException is not null, ["dataCount"] = error.Data.Count },
                ["fault"] = Diagnostics(fault),
                ["final"] = Diagnostics(final),
                ["results"] = results,
                ["stopped"] = stopped.Outcome.ToString(),
                ["factories"] = factories,
                ["launches"] = launches,
                ["borrowedDisposes"] = borrowed.Disposes,
                ["leaseReleased"] = leaseReleased,
                ["activeReleased"] = activeReleased,
                ["before"] = before,
                ["after"] = Snapshot(),
                ["namesBefore"] = new JsonArray(namesBefore.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["namesAfter"] = new JsonArray(Names().Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["consoleBytes"] = Convert.ToBase64String(borrowed.ToArray())
            };
        }
        finally
        {
            release.TrySetResult(); runtime.CloseAdmission();
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (FileOutputWriteException) { }
        }
    }
    private static JsonObject Result(LogWriteResult value) => new() { ["outcome"] = value.Outcome.ToString(), ["truncated"] = value.IsTruncated, ["outputs"] = new JsonArray(value.Outputs.Select(x => (JsonNode)new JsonObject { ["name"] = x.OutputName, ["status"] = x.Status.ToString(), ["sequence"] = x.AcceptedSequence }).ToArray()) };
    private static JsonObject Diagnostics(CoreLogDiagnostics value) => new() { ["state"] = value.State.ToString(), ["invalid"] = value.InvalidEventCount, ["truncated"] = value.TruncatedEventCount, ["outputs"] = new JsonArray(value.Outputs.Select(x => (JsonNode)new JsonObject { ["name"] = x.OutputName, ["state"] = x.State.ToString(), ["accepted"] = x.Accepted, ["written"] = x.Written, ["failed"] = x.Failed, ["abandoned"] = x.Abandoned, ["pending"] = x.Pending, ["unavailable"] = x.Unavailable, ["faultCode"] = x.LastFaultCode, ["faultAt"] = x.LastFaultAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) }).ToArray()) };
    private static bool Released(string path) { try { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static byte[] Read(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray(); }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
