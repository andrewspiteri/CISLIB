using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileRotationPolicyFixture
{
    internal static readonly string[] Modes = ["collision-jsonl-file", "collision-human-file", "collision-jsonl-directory", "collision-human-directory", "metadata-jsonl-directory", "metadata-human-directory", "write-jsonl-failure", "write-human-failure"];
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    private sealed class Borrowed : MemoryStream { internal int Disposes; protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); } }
    internal static int Run(string mode, string root) { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        bool limited = mode == "write-jsonl-os-limit", write = mode.StartsWith("write", StringComparison.Ordinal), human = mode.Contains("human", StringComparison.Ordinal), metadataFailure = mode.StartsWith("metadata", StringComparison.Ordinal);
        if (!Modes.Contains(mode, StringComparer.Ordinal) && !limited) { throw new ArgumentException("Unknown rotation policy mode.", nameof(mode)); }
        string directory = Path.Combine(root, "logs"), path = Path.Combine(directory, "\u03a9-\U0001f642.log"), metadata = path + ".cis.inventory.json"; bool directoryInitiallyMissing = !Directory.Exists(directory); LogOutputFormat format = human ? LogOutputFormat.Human : LogOutputFormat.JsonLines;
        using (FileOutputOwner.Initialize(path, format)) { }
        JsonObject inventory = JsonNode.Parse(File.ReadAllBytes(metadata))!.AsObject(); string group = inventory["groupId"]!.GetValue<string>();
        string Name(int id) => ".cis-log-" + group + "-" + id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + ".closed";
        string oldest = Path.Combine(directory, Name(1)), closing = Path.Combine(directory, Name(2));
        if (!write)
        {
            inventory["segments"]![0]!["id"] = 2L; inventory["segments"]!.AsArray().Insert(0, new JsonObject { ["id"] = 1L, ["fileName"] = Name(1), ["state"] = "closed" });
            File.WriteAllText(metadata, inventory.ToJsonString()); File.WriteAllText(oldest, human ? "retained=true\n" : "{\"retained\":true}\n", new UTF8Encoding(false));
            File.WriteAllText(path, human ? "seed=\"" + new string('a', 65528) + "\"\n" : "{\"seed\":\"" + new string('a', 65524) + "\"}\n", new UTF8Encoding(false));
        }
        string outside = Path.Combine(root, "archive", "keep.log"); Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        foreach (string neighbor in new[] { outside, Path.Combine(directory, "other.log"), Path.Combine(directory, ".cis-log-ffffffffffffffffffffffffffffffff-00000000000000000001.closed"), Path.Combine(directory, ".cis-log-" + group + "-00000000000000000050.closed") }) { File.WriteAllText(neighbor, "untouched neighbor\n", new UTF8Encoding(false)); }
        var options = new LoggingOptions { FilePath = path, FileFormat = format, ConsoleFormat = LogOutputFormat.JsonLines, HumanDetail = HumanOutputDetail.Verbose, FileMaxBytes = 65536, FileRetainedFiles = 1, MaxEntryBytes = 16384, QueueCapacity = 4 }; options.Prepare(root);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int launches = 0, attempts = 0, factories = 0; byte[]? firstRecord = null;
        Task Launch(Func<Task> work) { launches++; return launches == 1 ? Task.Run(work) : Task.Run(async () => { await release.Task.WaitAsync(Limit); await work(); }); }
        using var borrowed = new Borrowed(); CoreLogRuntime runtime;
        if (!write || limited) { runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock(), _ => borrowed, Launch); }
        else
        {
            // Existing trusted worker callback: one real physical write, then a controlled
            // storage-failure IOException. This is not an actual full-disk observation.
            var gate = new object(); var console = new ConsoleOutputWriter(borrowed, LogOutputFormat.JsonLines, HumanOutputDetail.Verbose, 16384); var file = new FileOutputWriter(path, format, HumanOutputDetail.Verbose, 16384, 65536, 1);
            ValueTask Write(AdmittedEvent admitted) { attempts++; if (attempts == 2) { return ValueTask.FromException(new IOException("Controlled storage write failure.")); } firstRecord = admitted.PreparedLine?.ToArray() ?? throw new InvalidOperationException("Missing prepared bytes."); return file.WriteAsync(admitted); }
            var queues = new OutputQueues(4, true, true, console.WriteAsync, Write, gate, Launch, file);
            try { console.Initialize(); file.Initialize(); runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), options, queues.Console, queues.File, new Clock(), gate, queues, [new("console", options.EffectiveConsoleLevel, console.WriteAsync, Prepare: console.Prepare), new("file", options.EffectiveFileLevel, file.WriteAsync, Prepare: file.Prepare)], file.RecoveredPartialSegment); }
            catch { release.TrySetResult(); queues.Complete(); await queues.Completion.WaitAsync(Limit); file.Dispose(); throw; }
        }
        try
        {
            if (mode.StartsWith("collision", StringComparison.Ordinal))
            { if (mode.EndsWith("file", StringComparison.Ordinal)) { File.WriteAllText(closing, "unrelated collision\n"); } else { Directory.CreateDirectory(closing); File.WriteAllText(Path.Combine(closing, "keep.txt"), "unrelated collision\n"); } }
            if (metadataFailure) { File.Delete(metadata); Directory.CreateDirectory(metadata); File.WriteAllText(Path.Combine(metadata, "keep.txt"), "metadata collision\n"); }
            JsonObject Snapshot() => new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(name => !string.Equals(name, path + ".cis.lock", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(name => new KeyValuePair<string, JsonNode?>(Path.GetRelativePath(root, name).Replace('\\', '/'), JsonValue.Create(Convert.ToBase64String(Read(name))))));
            string[] Names() => Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
            JsonObject before = Snapshot(); string[] namesBefore = Names(); var results = new JsonArray(); CoreLogger logger = runtime.CreateLogger("Policy");
            LogWriteResult Log(int index) => logger.WriteLazy(CoreLogLevel.Information, () => { factories++; return new LogEventData { EventName = "policy." + index, Message = "\u03a9 \U0001f642 e\u0301\nline", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } }; });
            for (int index = 0; index < 3; index++) { results.Add(Result(Log(index))); }
            if (limited)
            { Console.WriteLine(new JsonObject { ["stage"] = "prepared", ["mode"] = mode, ["pid"] = Environment.ProcessId, ["path"] = path }.ToJsonString()); Console.Out.Flush(); if (Console.ReadLine() != "continue") { throw new InvalidOperationException("Missing owned capacity release."); } }
            release.TrySetResult(); string exceptionType;
            try { await runtime.QueuedOutputs!.File!.Completion.WaitAsync(Limit); throw new InvalidOperationException("Expected file failure."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException || limited && error is ArgumentOutOfRangeException { ParamName: "value" }) { exceptionType = error.GetType().Name; }
            bool leaseReleased = Released(path + ".cis.lock"), dataReleased = Released(metadataFailure ? closing : path); CoreLogDiagnostics fault = runtime.GetDiagnostics();
            string? restartError = null; try { using var owner = FileOutputOwner.Initialize(path, format); } catch (CoreLogInitializationException error) { restartError = error.ErrorCode; }
            results.Add(Result(Log(3))); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Console!.Completion.WaitAsync(Limit); CoreLogDiagnostics final = runtime.GetDiagnostics();
            LogWriteResult stopped = logger.WriteLazy(CoreLogLevel.Information, () => throw new InvalidOperationException("Stopped factory ran."));
            return new JsonObject
            {
                ["mode"] = mode,
                ["composition"] = limited ? "configured-runtime-os-capacity" : write ? "controlled-write-with-real-first-record" : "configured-runtime",
                ["directoryInitiallyMissing"] = directoryInitiallyMissing,
                ["pid"] = Environment.ProcessId,
                ["path"] = path,
                ["group"] = group,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
                ["fixtureSha256"] = Hash(typeof(FileRotationPolicyFixture).Assembly.Location),
                ["exceptionType"] = exceptionType,
                ["firstRecordBytes"] = firstRecord is null ? null : Convert.ToBase64String(firstRecord),
                ["controlledWriteAttempts"] = write && !limited ? attempts : null,
                ["fault"] = Diagnostics(fault),
                ["final"] = Diagnostics(final),
                ["results"] = results,
                ["stopped"] = stopped.Outcome.ToString(),
                ["factories"] = factories,
                ["launches"] = launches,
                ["borrowedDisposes"] = borrowed.Disposes,
                ["leaseReleased"] = leaseReleased,
                ["dataReleased"] = dataReleased,
                ["restartError"] = restartError,
                ["before"] = before,
                ["after"] = Snapshot(),
                ["namesBefore"] = new JsonArray(namesBefore.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["namesAfter"] = new JsonArray(Names().Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["consoleBytes"] = Convert.ToBase64String(borrowed.ToArray())
            };
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (Exception error) when (error is IOException or UnauthorizedAccessException || limited && error is ArgumentOutOfRangeException { ParamName: "value" }) { } }
    }
    private static byte[] Read(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray(); }
    private static bool Released(string path) { try { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonObject Result(LogWriteResult value) => new() { ["outcome"] = value.Outcome.ToString(), ["truncated"] = value.IsTruncated, ["outputs"] = new JsonArray(value.Outputs.Select(x => (JsonNode)new JsonObject { ["name"] = x.OutputName, ["status"] = x.Status.ToString(), ["sequence"] = x.AcceptedSequence }).ToArray()) };
    private static JsonObject Diagnostics(CoreLogDiagnostics value) => new() { ["state"] = value.State.ToString(), ["outputs"] = new JsonArray(value.Outputs.Select(x => (JsonNode)new JsonObject { ["name"] = x.OutputName, ["state"] = x.State.ToString(), ["accepted"] = x.Accepted, ["written"] = x.Written, ["failed"] = x.Failed, ["abandoned"] = x.Abandoned, ["pending"] = x.Pending, ["unavailable"] = x.Unavailable, ["faultCode"] = x.LastFaultCode, ["faultAt"] = x.LastFaultAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) }).ToArray()) };
}
