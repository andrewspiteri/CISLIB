using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushBoundaryFixture
{
    internal static readonly string[] Modes = ["empty", "both", "saturated", "faulted", "closed", "concurrent", "physical"];
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 15, 16, 17, TimeSpan.Zero); }
    private sealed class Writer : IDisposable
    {
        internal readonly TaskCompletionSource FirstStarted = Signal(), SecondStarted = Signal(), ReleaseFirst = Signal(), ReleaseSecond = Signal();
        internal readonly MemoryStream Bytes = new();
        internal readonly List<string> Order = [];
        internal bool HoldSecond, FailFirst;
        private int active;
        internal int Maximum, Attempts;
        private long lastWritten;
        internal async ValueTask WriteAsync(AdmittedEvent admitted)
        {
            int current = Interlocked.Increment(ref active); OutputQueueFixture.RecordMaximum(ref Maximum, current);
            try
            {
                int attempt = ++Attempts;
                if (attempt == 1) { FirstStarted.TrySetResult(); await ReleaseFirst.Task; }
                if (attempt == 2 && HoldSecond) { SecondStarted.TrySetResult(); await ReleaseSecond.Task; }
                if (attempt == 1 && FailFirst) { throw new IOException("controlled private write failure"); }
                await Bytes.WriteAsync(admitted.Event.JsonLine);
                lastWritten = admitted.AcceptedSequence;
                Order.Add("write." + lastWritten.ToString(CultureInfo.InvariantCulture));
            }
            finally { Interlocked.Decrement(ref active); }
        }
        internal async ValueTask FlushAsync()
        {
            int current = Interlocked.Increment(ref active); OutputQueueFixture.RecordMaximum(ref Maximum, current);
            try { await Bytes.FlushAsync(); Order.Add("flush." + lastWritten.ToString(CultureInfo.InvariantCulture)); }
            finally { Interlocked.Decrement(ref active); }
        }
        public void Dispose() { ReleaseFirst.TrySetResult(); ReleaseSecond.TrySetResult(); Bytes.Dispose(); }
    }
    internal static int Run(string mode, string root)
    {
        Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0;
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown flush-boundary mode.", nameof(mode)); }
        if (mode == "physical") { return await PhysicalAsync(mode, root); }
        bool both = mode is "both" or "saturated" or "concurrent";
        using var console = new Writer { HoldSecond = mode == "saturated", FailFirst = mode == "faulted" };
        using var file = new Writer { HoldSecond = mode == "saturated" };
        var options = new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.Information,
            FileLevel = mode == "both" ? CoreLogLevel.Error : CoreLogLevel.Information,
            FilePath = both ? Path.Combine(root, "unused.jsonl") : null,
            MaxEntryBytes = 16384,
            QueueCapacity = mode == "saturated" ? 1 : mode == "concurrent" ? 128 : 8
        };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, console.WriteAsync,
            both ? file.WriteAsync : null, new Clock(), console.FlushAsync, both ? file.FlushAsync : null);
        CoreLogger logger = runtime.CreateLogger("FlushBoundary");
        var boundaries = new List<RuntimeFlushBoundary>(); var captures = new List<JsonObject>(); var results = new JsonArray();
        bool? coalesced = null; int heldConsole = 0, heldFile = 0;
        void Write(CoreLogLevel level, string name, bool large = false)
        { results.Add(logger.Write(level, Data(name, large)).Outcome.ToString()); }
        void Capture()
        {
            RuntimeFlushBoundary boundary = runtime.RegisterFlushBoundary(); boundaries.Add(boundary); captures.Add(Serialize(boundary));
        }
        try
        {
            if (mode == "empty") { Capture(); }
            else if (mode == "closed")
            {
                console.ReleaseFirst.TrySetResult(); Write(CoreLogLevel.Information, "flush.0");
                runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); Capture();
            }
            else if (mode == "both")
            {
                Write(CoreLogLevel.Information, "flush.0"); await console.FirstStarted.Task.WaitAsync(Limit);
                Write(CoreLogLevel.Error, "flush.1"); await file.FirstStarted.Task.WaitAsync(Limit);
                Write(CoreLogLevel.Trace, "filtered"); Write(CoreLogLevel.Information, ""); Write(CoreLogLevel.Information, "flush.2", large: true);
                Capture();
                Write(CoreLogLevel.Error, "flush.3"); Write(CoreLogLevel.Error, ""); Write(CoreLogLevel.Trace, "filtered.later"); Write(CoreLogLevel.Error, "flush.4", large: true);
            }
            else if (mode == "saturated")
            {
                Write(CoreLogLevel.Warning, "flush.0"); await Task.WhenAll(console.FirstStarted.Task, file.FirstStarted.Task).WaitAsync(Limit);
                Write(CoreLogLevel.Warning, "flush.1"); Write(CoreLogLevel.Warning, "rejected");
                Write(CoreLogLevel.Warning, ""); Write(CoreLogLevel.Warning, "rejected.large", large: true); Write(CoreLogLevel.None, "filtered");
                Capture(); Write(CoreLogLevel.Warning, ""); Capture();
                coalesced = ReferenceEquals(boundaries[0].Console!.Value.Registration, boundaries[1].Console!.Value.Registration)
                    && ReferenceEquals(boundaries[0].File!.Value.Registration, boundaries[1].File!.Value.Registration);
                console.ReleaseFirst.TrySetResult(); file.ReleaseFirst.TrySetResult();
                await Task.WhenAll(console.SecondStarted.Task, file.SecondStarted.Task).WaitAsync(Limit);
                Write(CoreLogLevel.Warning, "flush.3"); Write(CoreLogLevel.Warning, "");
            }
            else if (mode == "faulted")
            {
                Write(CoreLogLevel.Information, "flush.0"); await console.FirstStarted.Task.WaitAsync(Limit);
                Write(CoreLogLevel.Information, "flush.1"); Capture(); console.ReleaseFirst.TrySetResult();
                try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) { }
                Write(CoreLogLevel.Information, "rejected.unavailable"); Capture();
            }
            else
            {
                Write(CoreLogLevel.Warning, "flush.0"); await Task.WhenAll(console.FirstStarted.Task, file.FirstStarted.Task).WaitAsync(Limit); Capture();
                var go = Signal();
                Task[] producers = Enumerable.Range(0, 16).Select(producer => Task.Run(async () =>
                {
                    await go.Task;
                    for (int item = 0; item < 4; item++)
                    {
                        LogWriteResult written = logger.Write(CoreLogLevel.Warning, Data("flush.producer." + producer + "." + item));
                        if (written.Outcome != LogWriteOutcome.Accepted) { throw new InvalidOperationException("Concurrent fixture admission unexpectedly rejected."); }
                    }
                    if (logger.Write(CoreLogLevel.Warning, Data("")).Outcome != LogWriteOutcome.InvalidEvent) { throw new InvalidOperationException("Invalid fixture event was not rejected."); }
                })).ToArray();
                go.TrySetResult();
                for (int index = 0; index < 32; index++) { Capture(); await Task.Yield(); }
                await Task.WhenAll(producers).WaitAsync(Limit); Capture();
                coalesced = boundaries.GroupBy(boundary => boundary.Console!.Value.AcceptedSequenceBoundary).All(group =>
                    group.All(boundary => ReferenceEquals(group.First().Console!.Value.Registration, boundary.Console!.Value.Registration)
                        && ReferenceEquals(group.First().File!.Value.Registration, boundary.File!.Value.Registration)));
            }
            heldConsole = runtime.QueuedOutputs!.Console?.RegisteredFlushCount ?? 0; heldFile = runtime.QueuedOutputs.File?.RegisteredFlushCount ?? 0;
            console.ReleaseFirst.TrySetResult(); file.ReleaseFirst.TrySetResult(); console.ReleaseSecond.TrySetResult(); file.ReleaseSecond.TrySetResult();
            var flushed = new JsonArray();
            foreach (RuntimeFlushBoundary boundary in boundaries)
            {
                bool? consoleResult = boundary.Console is { } c ? await c.Registration.WriterFlushCompletion.WaitAsync(Limit) : null;
                bool? fileResult = boundary.File is { } f ? await f.Registration.WriterFlushCompletion.WaitAsync(Limit) : null;
                flushed.Add(new JsonObject { ["console"] = consoleResult, ["file"] = fileResult });
            }
            runtime.CloseAdmission(); try { await runtime.QueuedOutputs.Completion.WaitAsync(Limit); } catch (IOException) when (mode == "faulted") { }
            JsonObject report = Common(mode); report["captures"] = new JsonArray(captures.Select(capture => (JsonNode)capture).ToArray());
            report["after"] = new JsonArray(boundaries.Select(boundary => (JsonNode)Serialize(boundary)).ToArray()); report["flushResults"] = flushed;
            report["writeResults"] = results; report["coalesced"] = coalesced;
            report["heldControls"] = new JsonObject { ["console"] = heldConsole, ["file"] = heldFile };
            report["finalControls"] = new JsonObject { ["console"] = runtime.QueuedOutputs.Console?.RegisteredFlushCount ?? 0, ["file"] = runtime.QueuedOutputs.File?.RegisteredFlushCount ?? 0 };
            report["writers"] = new JsonObject { ["console"] = WriterReport(console), ["file"] = both ? WriterReport(file) : null };
            report["final"] = Diagnostics(runtime);
            report["physicalFiles"] = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count(); return report;
        }
        finally
        {
            console.ReleaseFirst.TrySetResult(); file.ReleaseFirst.TrySetResult(); console.ReleaseSecond.TrySetResult(); file.ReleaseSecond.TrySetResult(); runtime.CloseAdmission();
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) when (mode == "faulted") { }
        }
    }
    private static async Task<JsonObject> PhysicalAsync(string mode, string root)
    {
        string path = Path.Combine(root, "logs", "\u03a9-\U0001f642.jsonl");
        var options = new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FilePath = path, MaxEntryBytes = 16384, FileMaxBytes = 65536, FileFormat = LogOutputFormat.JsonLines };
        options.Prepare(root);
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new Clock());
        try
        {
            CoreLogger logger = runtime.CreateLogger("FlushBoundary");
            for (int index = 0; index < 2; index++)
            { if (logger.Write(CoreLogLevel.Information, Data("flush." + index)).Outcome != LogWriteOutcome.Accepted) { throw new InvalidOperationException("Physical fixture rejected admission."); } }
            RuntimeFlushBoundary boundary = runtime.RegisterFlushBoundary(); JsonObject initial = Serialize(boundary);
            bool flushed = await boundary.File!.Value.Registration.WriterFlushCompletion.WaitAsync(Limit);
            byte[] bytes; using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { using var copy = new MemoryStream(); await stream.CopyToAsync(copy); bytes = copy.ToArray(); }
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit);
            JsonObject result = Common(mode); result["captures"] = new JsonArray(initial); result["after"] = new JsonArray(Serialize(boundary));
            result["flushResults"] = new JsonArray(new JsonObject { ["console"] = null, ["file"] = flushed }); result["bytes"] = Convert.ToBase64String(bytes);
            result["final"] = Diagnostics(runtime); result["finalControls"] = new JsonObject { ["console"] = 0, ["file"] = runtime.QueuedOutputs.File!.RegisteredFlushCount };
            result["path"] = path; result["leaseReleased"] = Released(path + ".cis.lock"); return result;
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
    }
    private static LogEventData Data(string name, bool large = false) => new()
    { EventName = name, Message = large ? new string('x', 20000) : "\u03a9 \U0001f642", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } };
    private static string Counter(long value) => value.ToString(CultureInfo.InvariantCulture);
    internal static JsonObject Serialize(RuntimeFlushBoundary boundary) => new()
    {
        ["capturedAtUtc"] = boundary.BoundaryCapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["invalid"] = Counter(boundary.InvalidEventCount),
        ["truncated"] = Counter(boundary.TruncatedEventCount),
        ["console"] = Output(boundary.Console),
        ["file"] = Output(boundary.File)
    };
    private static JsonObject? Output(OutputFlushBoundary? value) => value is { } output ? new JsonObject
    {
        ["boundary"] = Counter(output.AcceptedSequenceBoundary),
        ["filtered"] = Counter(output.Filtered),
        ["queueFull"] = Counter(output.QueueFull),
        ["unavailable"] = Counter(output.Unavailable)
    } : null;
    private static JsonObject WriterReport(Writer writer) => new()
    { ["order"] = new JsonArray(writer.Order.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray()), ["maximum"] = writer.Maximum, ["attempts"] = writer.Attempts, ["bytes"] = Convert.ToBase64String(writer.Bytes.ToArray()) };
    private static JsonObject Diagnostics(CoreLogRuntime runtime)
    {
        CoreLogDiagnostics diagnostics = runtime.GetDiagnostics();
        return new JsonObject
        {
            ["state"] = diagnostics.State.ToString(),
            ["invalid"] = Counter(diagnostics.InvalidEventCount),
            ["truncated"] = Counter(diagnostics.TruncatedEventCount),
            ["outputs"] = new JsonArray(diagnostics.Outputs.Select(output => (JsonNode)new JsonObject
            {
                ["name"] = output.OutputName,
                ["state"] = output.State.ToString(),
                ["accepted"] = Counter(output.Accepted),
                ["written"] = Counter(output.Written),
                ["failed"] = Counter(output.Failed),
                ["abandoned"] = Counter(output.Abandoned),
                ["pending"] = Counter(output.Pending),
                ["unavailable"] = Counter(output.Unavailable),
                ["queueFull"] = Counter(output.QueueFull),
                ["filtered"] = Counter(output.Filtered),
                ["fault"] = output.LastFaultCode
            }).ToArray())
        };
    }
    private static JsonObject Common(string mode) => new()
    {
        ["mode"] = mode,
        ["composition"] = mode == "physical" ? "configured-physical-writer" : "actual-core-queue-and-memory-streams",
        ["pid"] = Environment.ProcessId,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
        ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
        ["fixtureSha256"] = Hash(typeof(FlushBoundaryFixture).Assembly.Location)
    };
    private static bool Released(string path) { try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
