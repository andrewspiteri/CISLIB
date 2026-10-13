using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleStartupFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    private sealed class ProbeStream : MemoryStream
    {
        internal int Disposes, AsyncDisposes, CanWriteCalls;
        internal bool ThrowOnCheck;
        internal Action? Checking;
        internal ProbeStream() { }
        internal ProbeStream(byte[] data) : base(data, writable: false) { }
        public override bool CanWrite
        {
            get
            {
                CanWriteCalls++; Checking?.Invoke();
                if (ThrowOnCheck) { ThrowOnCheck = false; throw new IOException("private-startup-path-and-payload"); }
                return base.CanWrite;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { Disposes++; } base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { AsyncDisposes++; return base.DisposeAsync(); }
    }
    internal static byte[] Marker(string name) => Encoding.UTF8.GetBytes("{\"application\":\"" + name + "-startup-preserved\"}\n");
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string mode = row["mode"]!.GetValue<string>(), profile = row["profile"]!.GetValue<string>(); bool physical = row["physical"]!.GetValue<bool>();
        bool console = profile is "console" or "both", fileEnabled = profile is "file" or "both"; int selections = 0, launches = 0;
        byte[] retained = mode == "read-only" ? Encoding.UTF8.GetBytes("existing-console-data") : [];
        var probe = mode == "read-only" ? new ProbeStream(retained) : new ProbeStream(); using var file = new MemoryStream();
        Stream chosen = physical ? Console.OpenStandardInput() : probe;
        if (mode == "closed") { probe.Dispose(); }
        var queues = new List<OutputEventQueue>(); var tasks = new List<Task>(); var suppressed = new List<bool>(); JsonArray? beforeChecks = null;
        bool trackCheck = false; probe.Checking = () => { if (trackCheck) { beforeChecks = new JsonArray(tasks.Select(task => (JsonNode)JsonValue.Create(task.IsCompleted)!).ToArray()); } };
        CoreLogRuntime? existing = null, created = null; bool existingRunning = false; JsonObject? error = null; string? successOutcome = null;
        Task Launch(Func<Task> work)
        {
            launches++; if (mode == "launch-console" && launches == 1 || mode == "launch-file" && launches == 2) { throw new IOException("private-worker-startup-path-and-payload"); }
            suppressed.Add(ExecutionContext.IsFlowSuppressed()); var queue = (OutputEventQueue)work.Target!;
            Task task = Task.Run(work); queues.Add(queue); tasks.Add(task); return task;
        }
        Stream Select(ConsoleOutputStream _)
        {
            selections++; if (mode == "select-throw") { throw new IOException("private-selector-startup-path-and-payload"); }
            return mode == "select-null" ? null! : chosen;
        }
        try
        {
            if (mode == "existing")
            {
                existing = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleFormat = LogOutputFormat.JsonLines }, timeProvider: new Clock(), acquireConsole: _ => probe);
            }
            probe.ThrowOnCheck = mode is "canwrite-throw" or "existing"; trackCheck = true;
            var options = new LoggingOptions { ConsoleLevel = console ? CoreLogLevel.Information : CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, QueueCapacity = 2, FilePath = fileEnabled ? "controlled-startup.jsonl" : null };
            if (row["stream"]!.GetValue<int>() == 1) { options.ConsoleStream = ConsoleOutputStream.StandardOutput; }
            try
            {
                created = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, fileEnabled ? item => file.WriteAsync(item.Event.JsonLine) : null, new Clock(), Select, Launch);
            }
            catch (CoreLogInitializationException failure)
            {
                error = new JsonObject
                {
                    ["type"] = failure.GetType().FullName,
                    ["output"] = failure.OutputName,
                    ["code"] = failure.ErrorCode,
                    ["message"] = failure.Message,
                    ["inner"] = failure.InnerException is not null,
                    ["dataCount"] = failure.Data.Count
                };
            }
            int initializationChecks = probe.CanWriteCalls; trackCheck = false;
            // These are actual scheduled worker tasks. On failure they must already be joined before throw.
            var joinedAtReturn = new JsonArray(tasks.Select(task => (JsonNode)JsonValue.Create(task.IsCompletedSuccessfully)!).ToArray());
            if (created is not null)
            {
                successOutcome = created.CreateLogger("Startup").Write(CoreLogLevel.Information, new LogEventData { EventName = "startup.file", Message = "file-only-remains-independent" }).Outcome.ToString();
                created.CloseAdmission(); await created.QueuedOutputs!.Completion.WaitAsync(Limit);
            }
            if (existing is not null)
            {
                existingRunning = existing.GetDiagnostics().State == CoreLogState.Running && !existing.QueuedOutputs!.Completion.IsCompleted;
                LogWriteResult value = existing.CreateLogger("StartupExisting").Write(CoreLogLevel.Information, new LogEventData { EventName = "startup.survived", Message = "old-runtime-still-usable" });
                if (value.Outcome != LogWriteOutcome.Accepted) { throw new InvalidDataException("Existing runtime lost admission."); }
                existing.CloseAdmission(); await existing.QueuedOutputs!.Completion.WaitAsync(Limit);
            }
            int canWriteCalls = initializationChecks; bool canRead = chosen.CanRead;
            byte[] beforeApplication = physical ? [] : probe.ToArray();
            if (physical) { Console.OpenStandardOutput().Write(Marker("stdout")); Console.OpenStandardError().Write(Marker("stderr")); }
            else if (mode is not ("read-only" or "closed")) { probe.Write(Marker("selected")); }
            var final = new JsonArray();
            foreach (OutputEventQueue queue in queues)
            {
                OutputAccounting stats = queue.CaptureAccounting(); LogOutputWriteStatus after = queue.TryAdmit(new EncodedEvent(Encoding.UTF8.GetBytes("{}\n"), false), out long? ticket);
                final.Add(new JsonObject
                {
                    ["state"] = stats.State.ToString(),
                    ["accepted"] = stats.Accepted,
                    ["written"] = stats.Written,
                    ["pending"] = stats.Pending,
                    ["failed"] = stats.Failed,
                    ["abandoned"] = stats.Abandoned,
                    ["fault"] = stats.LastFaultCode,
                    ["after"] = after.ToString(),
                    ["ticket"] = ticket
                });
            }
            return new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["error"] = error,
                ["successOutcome"] = successOutcome,
                ["selectionCalls"] = selections,
                ["launchCalls"] = launches,
                ["workers"] = tasks.Count,
                ["joinedAtReturn"] = joinedAtReturn,
                ["flowSuppressed"] = new JsonArray(suppressed.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
                ["beforeChecks"] = beforeChecks,
                ["canWriteCalls"] = canWriteCalls,
                ["canRead"] = canRead,
                ["disposes"] = probe.Disposes,
                ["asyncDisposes"] = probe.AsyncDisposes,
                ["existingRunning"] = existingRunning,
                ["beforeApplication"] = Convert.ToBase64String(beforeApplication),
                ["selectedBytes"] = Convert.ToBase64String(probe.ToArray()),
                ["fileBytes"] = Convert.ToBase64String(file.ToArray()),
                ["queues"] = final,
                ["stdout"] = "",
                ["stderr"] = ""
            };
        }
        finally
        {
            trackCheck = false;
            created?.CloseAdmission(); existing?.CloseAdmission(); foreach (OutputEventQueue queue in queues) { queue.Complete(); }
            try
            {
                if (created is not null) { await created.QueuedOutputs!.Completion.WaitAsync(Limit); }
                if (existing is not null) { await existing.QueuedOutputs!.Completion.WaitAsync(Limit); }
                await Task.WhenAll(tasks).WaitAsync(Limit);
            }
            finally { if (probe.Disposes == 0) { probe.Dispose(); } if (physical) { chosen.Dispose(); } }
        }
    }
    internal static int Run(string path, string id)
    {
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(path))!.AsObject(), row = catalog["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); ConsoleStartupExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ConsoleStartupFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["inputsSha256"] = Hash(path)
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
