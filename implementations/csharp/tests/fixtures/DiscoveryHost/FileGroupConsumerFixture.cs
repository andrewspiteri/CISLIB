using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

// This test-only protocol owns one real runtime and permits the parent to observe live ownership.
internal static class FileGroupConsumerFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    private sealed class Borrowed : MemoryStream
    {
        internal int Disposes;
        protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); }
    }
    internal static int Run(string mode, string path) => Execute(mode, path).GetAwaiter().GetResult();
    private static async Task<int> Execute(string mode, string path)
    {
        if (mode is not ("probe-jsonl" or "probe-human" or "hold-jsonl" or "hold-human" or "capture-jsonl")) { throw new ArgumentException("Unknown group consumer mode.", nameof(mode)); }
        var options = new LoggingOptions
        {
            FilePath = path,
            ConsoleFormat = LogOutputFormat.JsonLines,
            FileFormat = mode.EndsWith("human", StringComparison.Ordinal) ? LogOutputFormat.Human : LogOutputFormat.JsonLines,
            HumanDetail = HumanOutputDetail.Verbose,
            MaxEntryBytes = 16384,
            FileMaxBytes = 131072,
            QueueCapacity = 8
        };
        options.Prepare(Environment.CurrentDirectory);
        string capturedPath = options.FilePath!;
        using var borrowed = new Borrowed(); var tasks = new List<Task>(); CoreLogRuntime? runtime = null; JsonObject? error = null;
        int factories = 0; bool? leaseReleased = null, activeReleased = null;
        try
        {
            if (mode == "capture-jsonl")
            {
                Report("prepared", includeBytes: false);
                string change = await Console.In.ReadLineAsync().WaitAsync(Limit) ?? throw new InvalidDataException("Missing captured-directory command.");
                if (!change.StartsWith("start:", StringComparison.Ordinal)) { throw new InvalidDataException("Unknown captured-directory command."); }
                Environment.CurrentDirectory = change[6..];
            }
            try
            {
                runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock(), _ => borrowed,
                    work => { Task task = Task.Run(work); tasks.Add(task); return task; });
            }
            catch (CoreLogInitializationException failure)
            {
                error = new JsonObject { ["output"] = failure.OutputName, ["code"] = failure.ErrorCode, ["message"] = failure.Message, ["inner"] = failure.InnerException is not null, ["dataCount"] = failure.Data.Count };
                // Observe owned-handle release in this process, before OS process-exit cleanup.
                // A contender never acquires the lease; denied/nonexistent paths cannot be probed.
                if (failure.ErrorCode != "FileOwnershipUnavailable") { leaseReleased = ProbeReleased(capturedPath + ".cis.lock"); activeReleased = ProbeReleased(capturedPath); }
                Report("failed", includeBytes: false); return 0;
            }
            await Write("group.initial");
            if (mode.StartsWith("hold-", StringComparison.Ordinal))
            {
                Report("ready", includeBytes: true);
                while (true)
                {
                    string command = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)) ?? throw new InvalidDataException("Missing release command.");
                    if (command == "release") { break; }
                    if (command != "append") { throw new InvalidDataException("Unknown live-group command."); }
                    await Write("group.after.contender"); Report("appended", includeBytes: true);
                }
            }
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit);
            Report(mode.StartsWith("hold-", StringComparison.Ordinal) ? "released" : "completed", includeBytes: true); return 0;
        }
        finally
        {
            runtime?.CloseAdmission(); await Task.WhenAll(tasks).WaitAsync(Limit);
        }
        async Task Write(string eventName)
        {
            LogWriteResult result = runtime!.CreateLogger("Group").WriteLazy(CoreLogLevel.Information, () =>
            {
                factories++; return new LogEventData { EventName = eventName, Message = Path.GetFileName(capturedPath), Attributes = new Dictionary<string, LogValue> { ["process_id"] = LogValue.Integer(Environment.ProcessId) } };
            });
            if (result.Outcome != LogWriteOutcome.Accepted || result.Outputs.Any(output => output.Status != LogOutputWriteStatus.Accepted || output.AcceptedSequence != factories)) { throw new InvalidDataException("Group write admission differs."); }
            using var deadline = new CancellationTokenSource(Limit);
            while (runtime.GetDiagnostics().Outputs.Any(output => output.Written != factories)) { await Task.Delay(1, deadline.Token); }
        }
        void Report(string status, bool includeBytes)
        {
            CoreLogDiagnostics? state = runtime?.GetDiagnostics();
            var report = new JsonObject
            {
                ["status"] = status,
                ["error"] = error,
                ["capturedPath"] = capturedPath,
                ["cwd"] = Environment.CurrentDirectory,
                ["pid"] = Environment.ProcessId,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["workers"] = tasks.Count,
                ["joined"] = tasks.All(task => task.IsCompleted),
                ["borrowedDisposes"] = borrowed.Disposes,
                ["leaseReleased"] = leaseReleased,
                ["activeReleased"] = activeReleased,
                ["factories"] = factories,
                ["state"] = state?.State.ToString(),
                ["outputs"] = state is null ? null : new JsonArray(state.Outputs.Select(output => (JsonNode)new JsonObject { ["name"] = output.OutputName, ["state"] = output.State.ToString(), ["accepted"] = output.Accepted, ["written"] = output.Written, ["failed"] = output.Failed, ["abandoned"] = output.Abandoned, ["pending"] = output.Pending }).ToArray()),
                ["consoleBytes"] = Convert.ToBase64String(borrowed.ToArray()),
                ["fileBytes"] = includeBytes ? Convert.ToBase64String(ReadShared(capturedPath)) : "",
                ["inventorySha256"] = includeBytes ? Hash(capturedPath + ".cis.inventory.json") : null,
                ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
                ["fixtureSha256"] = Hash(typeof(FileGroupConsumerFixture).Assembly.Location)
            };
            Console.WriteLine(report.ToJsonString()); Console.Out.Flush();
        }
    }
    private static bool? ProbeReleased(string path)
    {
        if (!File.Exists(path)) { return null; }
        try { using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return false; }
    }
    private static byte[] ReadShared(string path) { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray(); }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
