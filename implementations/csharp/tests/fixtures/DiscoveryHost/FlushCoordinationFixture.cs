using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushCoordinationFixture
{
    internal static readonly string[] Modes = ["write-cancel", "write-deadline", "flush-cancel", "flush-deadline", "cohort", "dual", "pre-cancel", "fault", "complete-race"];
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static int Continuations(Task task)
    {
        if (task.IsCompleted) { return 0; }
        object? current = (typeof(Task).GetField("m_continuationObject", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Pinned Task continuation inspection is unavailable.")).GetValue(task);
        if (task.IsCompleted || current is null) { return 0; }
        if (current is not IList list) { return 1; }
        lock (list) { return list.Cast<object?>().Count(item => item is not null); }
    }
    internal static int ContinuationSlots(Task task)
    {
        if (task.IsCompleted) { return 0; }
        object? current = (typeof(Task).GetField("m_continuationObject", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Pinned Task continuation inspection is unavailable.")).GetValue(task);
        if (task.IsCompleted || current is null) { return 0; }
        if (current is not List<object?> list) { return 1; }
        lock (list) { return list.Capacity; }
    }
    private sealed class EventClock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 17, 18, 19, TimeSpan.Zero); }
    internal sealed class DeadlineClock : TimeProvider
    {
        private OneShot? timer;
        internal int Fires, Active;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (timer is not null || dueTime != TimeSpan.FromSeconds(1) || period != Timeout.InfiniteTimeSpan)
            { throw new InvalidOperationException("Fixture supports one declared one-shot deadline."); }
            Active = 1; return timer = new OneShot(this, callback, state);
        }
        internal void Fire() { Fires++; (timer ?? throw new InvalidOperationException("Deadline was not armed.")).Fire(); }
        private sealed class OneShot(DeadlineClock owner, TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            internal void Fire() { if (disposed) { throw new InvalidOperationException("Disposed deadline cannot fire."); } callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("Fixture deadline is single use.");
            public void Dispose() { disposed = true; owner.Active = 0; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Writer(bool holdWrite, bool holdFlush, bool fail) : IDisposable
    {
        internal readonly TaskCompletionSource WriteEntered = Signal(), FlushEntered = Signal(), ReleaseWrite = Signal(), ReleaseFlush = Signal();
        internal readonly MemoryStream Bytes = new();
        internal readonly List<string> Order = [];
        private long written;
        private int active;
        internal int Maximum, Attempts, Flushes;
        internal async ValueTask WriteAsync(AdmittedEvent item)
        {
            OutputQueueFixture.RecordMaximum(ref Maximum, Interlocked.Increment(ref active));
            try
            {
                if (++Attempts == 1 && holdWrite) { WriteEntered.TrySetResult(); await ReleaseWrite.Task; }
                if (fail) { throw new IOException("private controlled write fault"); }
                await Bytes.WriteAsync(item.Event.JsonLine); written = item.AcceptedSequence;
                Order.Add("write." + written.ToString(CultureInfo.InvariantCulture));
            }
            finally { Interlocked.Decrement(ref active); }
        }
        internal async ValueTask FlushAsync()
        {
            OutputQueueFixture.RecordMaximum(ref Maximum, Interlocked.Increment(ref active));
            try
            {
                if (++Flushes == 1 && holdFlush) { FlushEntered.TrySetResult(); await ReleaseFlush.Task; }
                await Bytes.FlushAsync(); Order.Add("flush." + written.ToString(CultureInfo.InvariantCulture));
            }
            finally { Interlocked.Decrement(ref active); }
        }
        public void Dispose() { ReleaseWrite.TrySetResult(); ReleaseFlush.TrySetResult(); Bytes.Dispose(); }
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > Limit) { throw new TimeoutException("Fixture coordination did not advance."); }
            await Task.Delay(1);
        }
    }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown coordination mode.", nameof(mode)); }
        bool holdFlush = mode.StartsWith("flush-", StringComparison.Ordinal), both = mode is "cohort" or "dual";
        using var console = new Writer(!holdFlush, holdFlush, mode == "fault");
        using var file = new Writer(true, false, false);
        int capacity = mode is "cohort" or "dual" or "fault" ? 2 : 3;
        var options = new LoggingOptions
        {
            QueueCapacity = capacity,
            ConsoleLevel = CoreLogLevel.Information,
            FilePath = both ? Path.Combine(root, "unused.jsonl") : null,
            FileLevel = mode == "dual" ? CoreLogLevel.Error : CoreLogLevel.Information
        };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, console.WriteAsync,
            both ? file.WriteAsync : null, timeProvider: new EventClock(), consoleFlush: console.FlushAsync, fileFlush: both ? file.FlushAsync : null);
        CoreLogger logger = runtime.CreateLogger("FlushCoordination");
        var snapshots = new List<RuntimeFlushBoundary>(); var results = new JsonArray(); var callers = new List<CancellationTokenSource>();
        int cancelled = 0, succeeded = 0, failed = 0, deadlineFires = 0, attachedPeak = 0, retainedAfterDetach = 0, controlsPeak = 0, slotsPeak = 0, slotsAfterDetach = 0, fileControlsPeak = 0;
        bool shared = true, completedTokenCancelled = false; var phases = new JsonArray(); var race = new JsonArray();
        void Write(CoreLogLevel level, string name)
        { results.Add(logger.Write(level, new LogEventData { EventName = name, Message = "\u03a9 \U0001f642", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } }).Outcome.ToString()); }
        RuntimeFlushBoundary Capture()
        {
            RuntimeFlushBoundary boundary = runtime.RegisterFlushBoundary(); snapshots.Add(boundary);
            controlsPeak = Math.Max(controlsPeak, runtime.QueuedOutputs!.Console!.RegisteredFlushCount);
            fileControlsPeak = Math.Max(fileControlsPeak, runtime.QueuedOutputs.File?.RegisteredFlushCount ?? 0);
            return boundary;
        }
        int Attached(RuntimeFlushBoundary boundary, string name = "console") =>
            Continuations((name == "console" ? boundary.Console!.Value : boundary.File!.Value).Registration.WriterFlushCompletion);
        async Task CancelledAsync(Task<bool> task, CancellationToken token)
        {
            try { await task.WaitAsync(Limit); throw new InvalidOperationException("Caller was not cancelled."); }
            catch (OperationCanceledException error) { if (error.CancellationToken != token) { throw new InvalidOperationException("Cancellation token changed."); } cancelled++; }
        }
        try
        {
            RuntimeFlushBoundary boundary;
            if (holdFlush) { boundary = Capture(); await console.FlushEntered.Task.WaitAsync(Limit); }
            else
            {
                Write(CoreLogLevel.Information, "coord.0"); await console.WriteEntered.Task.WaitAsync(Limit);
                if (mode == "cohort") { await file.WriteEntered.Task.WaitAsync(Limit); }
                boundary = Capture();
            }
            if (mode.Contains('-', StringComparison.Ordinal) && mode is not ("pre-cancel" or "complete-race"))
            {
                int first = holdFlush ? 0 : 1;
                for (int index = first; index < (holdFlush ? 3 : 4); index++)
                { Write(CoreLogLevel.Information, "coord." + index); boundary = Capture(); }
                Write(CoreLogLevel.Warning, "full"); Write(CoreLogLevel.Warning, "");
                RuntimeFlushBoundary original = Capture();
                for (int index = 0; index < 3; index++) { Write(CoreLogLevel.Warning, "full.later"); }
                boundary = Capture(); shared = ReferenceEquals(original.Console!.Value.Registration, boundary.Console!.Value.Registration);
                for (int index = 0; index < 10000; index++)
                {
                    RuntimeFlushBoundary current = runtime.RegisterFlushBoundary();
                    shared &= ReferenceEquals(current.Console!.Value.Registration, boundary.Console!.Value.Registration);
                    var clock = new DeadlineClock();
                    using var lifetime = mode.EndsWith("deadline", StringComparison.Ordinal) ? new CancellationTokenSource(TimeSpan.FromSeconds(1), clock) : new CancellationTokenSource();
                    Task<bool> wait = current.WaitForWriterFlushAsync(lifetime.Token);
                    attachedPeak = Math.Max(attachedPeak, Attached(current));
                    slotsPeak = Math.Max(slotsPeak, ContinuationSlots(current.Console!.Value.Registration.WriterFlushCompletion));
                    if (mode.EndsWith("deadline", StringComparison.Ordinal)) { clock.Fire(); deadlineFires += clock.Fires; }
                    else { lifetime.Cancel(); }
                    await CancelledAsync(wait, lifetime.Token);
                    retainedAfterDetach = Math.Max(retainedAfterDetach, Attached(current));
                    slotsAfterDetach = Math.Max(slotsAfterDetach, ContinuationSlots(current.Console!.Value.Registration.WriterFlushCompletion));
                    if (clock.Active != 0 || runtime.QueuedOutputs!.Console!.RegisteredFlushCount != capacity + 1)
                    { throw new InvalidOperationException("Caller detachment changed bounded worker coordination."); }
                }
                phases.Add(new JsonObject { ["consoleControls"] = runtime.QueuedOutputs!.Console!.RegisteredFlushCount, ["fileControls"] = runtime.QueuedOutputs.File?.RegisteredFlushCount ?? 0, ["consoleAttached"] = Attached(boundary), ["consoleSlots"] = ContinuationSlots(boundary.Console!.Value.Registration.WriterFlushCompletion), ["fileAttached"] = 0 });
            }
            else if (mode == "cohort")
            {
                for (int index = 1; index < 3; index++) { Write(CoreLogLevel.Information, "coord." + index); boundary = Capture(); }
                var waits = new List<Task<bool>>(); var clocks = new List<DeadlineClock>();
                for (int index = 0; index < 64; index++)
                {
                    var clock = new DeadlineClock(); clocks.Add(clock);
                    var lifetime = index is >= 32 and < 48 ? new CancellationTokenSource(TimeSpan.FromSeconds(1), clock) : new CancellationTokenSource();
                    callers.Add(lifetime); RuntimeFlushBoundary callerBoundary = runtime.RegisterFlushBoundary();
                    shared &= ReferenceEquals(boundary.Console!.Value.Registration, callerBoundary.Console!.Value.Registration)
                        && (boundary.File is { } ownFile ? callerBoundary.File is { } otherFile && ReferenceEquals(ownFile.Registration, otherFile.Registration) : callerBoundary.File is null);
                    waits.Add(callerBoundary.WaitForWriterFlushAsync(lifetime.Token));
                }
                attachedPeak = Attached(boundary); slotsPeak = ContinuationSlots(boundary.Console!.Value.Registration.WriterFlushCompletion); phases.Add(Phase(boundary));
                for (int index = 0; index < 48; index++)
                {
                    if (index >= 32) { clocks[index].Fire(); deadlineFires += clocks[index].Fires; } else { callers[index].Cancel(); }
                    await CancelledAsync(waits[index], callers[index].Token);
                }
                if (Attached(boundary) != 16) { throw new InvalidOperationException("Exactly sixteen surviving callers must remain attached."); }
                retainedAfterDetach = Attached(boundary) - 16; phases.Add(Phase(boundary));
                console.ReleaseWrite.TrySetResult(); file.ReleaseWrite.TrySetResult();
                for (int index = 48; index < 64; index++) { if (!await waits[index].WaitAsync(Limit)) { throw new InvalidOperationException("Surviving caller did not observe flush."); } succeeded++; }
                if (clocks.Any(clock => clock.Active != 0)) { throw new InvalidOperationException("Deadline timer retained."); }
            }
            else if (mode == "dual")
            {
                Write(CoreLogLevel.Error, "coord.1"); await file.WriteEntered.Task.WaitAsync(Limit); boundary = Capture();
                using var lifetime = new CancellationTokenSource(); Task<bool> wait = boundary.WaitForWriterFlushAsync(lifetime.Token);
                attachedPeak = Attached(boundary); slotsPeak = ContinuationSlots(boundary.Console!.Value.Registration.WriterFlushCompletion); phases.Add(Phase(boundary)); console.ReleaseWrite.TrySetResult();
                await UntilAsync(() => Attached(boundary, "file") == 1); phases.Add(Phase(boundary));
                lifetime.Cancel(); await CancelledAsync(wait, lifetime.Token); phases.Add(Phase(boundary));
                Write(CoreLogLevel.Trace, "filtered"); Write(CoreLogLevel.Error, ""); RuntimeFlushBoundary later = Capture();
                shared = ReferenceEquals(boundary.File!.Value.Registration, later.File!.Value.Registration);
                Task<bool> survivor = later.WaitForWriterFlushAsync(CancellationToken.None); file.ReleaseWrite.TrySetResult();
                if (!await survivor.WaitAsync(Limit)) { throw new InvalidOperationException("Independent output flush failed."); }
                succeeded++;
            }
            else if (mode == "pre-cancel")
            {
                using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
                await CancelledAsync(boundary.WaitForWriterFlushAsync(lifetime.Token), lifetime.Token);
                phases.Add(Phase(boundary)); console.ReleaseWrite.TrySetResult();
                if (!await boundary.WaitForWriterFlushAsync(CancellationToken.None).WaitAsync(Limit)) { throw new InvalidOperationException("Shared request erased."); }
                succeeded++;
                await CancelledAsync(boundary.WaitForWriterFlushAsync(lifetime.Token), lifetime.Token); completedTokenCancelled = true;
            }
            else if (mode == "fault")
            {
                Write(CoreLogLevel.Information, "coord.1"); boundary = Capture();
                using var lifetime = new CancellationTokenSource(); Task<bool> wait = boundary.WaitForWriterFlushAsync(lifetime.Token);
                attachedPeak = Attached(boundary); slotsPeak = ContinuationSlots(boundary.Console!.Value.Registration.WriterFlushCompletion); console.ReleaseWrite.TrySetResult();
                if (await wait.WaitAsync(Limit)) { throw new InvalidOperationException("Failed writer invented success."); }
                failed++;
            }
            else
            {
                var waits = new List<Task<bool>>();
                for (int index = 0; index < 16; index++)
                {
                    var lifetime = new CancellationTokenSource(); callers.Add(lifetime); RuntimeFlushBoundary callerBoundary = runtime.RegisterFlushBoundary();
                    shared &= ReferenceEquals(boundary.Console!.Value.Registration, callerBoundary.Console!.Value.Registration)
                        && (boundary.File is { } ownFile ? callerBoundary.File is { } otherFile && ReferenceEquals(ownFile.Registration, otherFile.Registration) : callerBoundary.File is null);
                    waits.Add(callerBoundary.WaitForWriterFlushAsync(lifetime.Token));
                }
                attachedPeak = Attached(boundary); slotsPeak = ContinuationSlots(boundary.Console!.Value.Registration.WriterFlushCompletion); var go = Signal();
                Task[] cancels = callers.Select(lifetime => Task.Run(async () => { await go.Task; lifetime.Cancel(); })).ToArray();
                Task release = Task.Run(async () => { await go.Task; console.ReleaseWrite.TrySetResult(); });
                go.TrySetResult(); await Task.WhenAll(cancels.Append(release)).WaitAsync(Limit);
                foreach (Task<bool> wait in waits)
                {
                    try { if (!await wait.WaitAsync(Limit)) { throw new InvalidOperationException("Race invented failure."); } succeeded++; race.Add("succeeded"); }
                    catch (OperationCanceledException) { cancelled++; race.Add("cancelled"); }
                }
            }
            console.ReleaseWrite.TrySetResult(); file.ReleaseWrite.TrySetResult(); console.ReleaseFlush.TrySetResult(); file.ReleaseFlush.TrySetResult();
            if (mode != "fault" && mode is not ("cohort" or "dual" or "pre-cancel" or "complete-race"))
            { if (!await boundary.WaitForWriterFlushAsync(CancellationToken.None).WaitAsync(Limit)) { throw new InvalidOperationException("Cancelled callers erased flush work."); } succeeded++; }
            runtime.CloseAdmission(); try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) when (mode == "fault") { }
            CoreLogDiagnostics final = runtime.GetDiagnostics();
            return new JsonObject
            {
                ["mode"] = mode,
                ["pid"] = Environment.ProcessId,
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
                ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
                ["fixtureSha256"] = Hash(typeof(FlushCoordinationFixture).Assembly.Location),
                ["capacity"] = capacity,
                ["iterations"] = mode is "write-cancel" or "write-deadline" or "flush-cancel" or "flush-deadline" ? 10000 : 0,
                ["cancelled"] = cancelled,
                ["succeeded"] = succeeded,
                ["failed"] = failed,
                ["deadlineFires"] = deadlineFires,
                ["continuationSlotsPeak"] = slotsPeak,
                ["continuationSlotsAfterDetach"] = slotsAfterDetach,
                ["attachedPeak"] = attachedPeak,
                ["retainedAfterDetach"] = retainedAfterDetach,
                ["controlsPeak"] = controlsPeak,
                ["fileControlsPeak"] = fileControlsPeak,
                ["finalControls"] = runtime.QueuedOutputs!.Console!.RegisteredFlushCount + (runtime.QueuedOutputs.File?.RegisteredFlushCount ?? 0),
                ["shared"] = shared,
                ["completedTokenCancelled"] = completedTokenCancelled,
                ["phases"] = phases,
                ["race"] = race,
                ["writeResults"] = results,
                ["snapshots"] = new JsonArray(snapshots.Select(snapshot => (JsonNode)FlushBoundaryFixture.Serialize(snapshot)).ToArray()),
                ["writers"] = new JsonObject { ["console"] = WriterReport(console), ["file"] = both ? WriterReport(file) : null },
                ["finalInvalid"] = final.InvalidEventCount.ToString(CultureInfo.InvariantCulture),
                ["finalTruncated"] = final.TruncatedEventCount.ToString(CultureInfo.InvariantCulture),
                ["outputs"] = new JsonArray(final.Outputs.Select(output => (JsonNode)new JsonObject
                {
                    ["name"] = output.OutputName,
                    ["state"] = output.State.ToString(),
                    ["accepted"] = output.Accepted.ToString(CultureInfo.InvariantCulture),
                    ["written"] = output.Written.ToString(CultureInfo.InvariantCulture),
                    ["failed"] = output.Failed.ToString(CultureInfo.InvariantCulture),
                    ["abandoned"] = output.Abandoned.ToString(CultureInfo.InvariantCulture),
                    ["pending"] = output.Pending.ToString(CultureInfo.InvariantCulture),
                    ["filtered"] = output.Filtered.ToString(CultureInfo.InvariantCulture),
                    ["queueFull"] = output.QueueFull.ToString(CultureInfo.InvariantCulture),
                    ["unavailable"] = output.Unavailable.ToString(CultureInfo.InvariantCulture),
                    ["fault"] = output.LastFaultCode
                }).ToArray()),
                ["physicalFiles"] = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count()
            };
        }
        finally
        {
            foreach (CancellationTokenSource lifetime in callers) { lifetime.Dispose(); }
            console.ReleaseWrite.TrySetResult(); file.ReleaseWrite.TrySetResult(); console.ReleaseFlush.TrySetResult(); file.ReleaseFlush.TrySetResult(); runtime.CloseAdmission();
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) when (mode == "fault") { }
        }
        JsonObject Phase(RuntimeFlushBoundary value) => new()
        { ["consoleControls"] = runtime.QueuedOutputs!.Console!.RegisteredFlushCount, ["fileControls"] = runtime.QueuedOutputs.File?.RegisteredFlushCount ?? 0, ["consoleAttached"] = Attached(value), ["consoleSlots"] = ContinuationSlots(value.Console!.Value.Registration.WriterFlushCompletion), ["fileAttached"] = value.File is null ? 0 : Attached(value, "file") };
    }
    private static JsonObject WriterReport(Writer writer) => new()
    { ["order"] = new JsonArray(writer.Order.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()), ["maximum"] = writer.Maximum, ["attempts"] = writer.Attempts, ["flushes"] = writer.Flushes, ["bytes"] = Convert.ToBase64String(writer.Bytes.ToArray()) };
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
