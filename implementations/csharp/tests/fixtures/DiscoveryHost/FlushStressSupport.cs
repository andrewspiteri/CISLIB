using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushStressSupport
{
    internal static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value) { if (!value) { throw new InvalidOperationException("Flush stress invariant failed."); } }
    internal static async Task UntilAsync(Func<bool> predicate)
    {
        using var guard = new CancellationTokenSource(Guard);
        while (!predicate()) { await Task.Delay(1, guard.Token); }
    }
    internal static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
    internal static void Accepted(LogWriteResult result) => Require(result.Outcome == LogWriteOutcome.Accepted);
    internal static LogEventData Data(string name) => FlushResultFixture.Data(name);
    internal static JsonObject Result(LogFlushResult result) => FlushResultFixture.Serialize(result);
    internal static JsonObject Common(string mode) => new()
    {
        ["mode"] = mode,
        ["pid"] = Environment.ProcessId,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
        ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
        ["fixtureSha256"] = Hash(typeof(FlushStressSupport).Assembly.Location)
    };
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static OutputFlushRegistration[] Registrations(OutputEventQueue queue)
    {
        object gate = Field(queue, "admissionGate")!;
        lock (gate) { return ((IDictionary)Field(queue, "flushRequests")!).Values.Cast<OutputFlushRegistration>().ToArray(); }
    }
    internal static Task? Progress(OutputEventQueue queue)
    {
        object gate = Field(queue, "admissionGate")!;
        lock (gate) { return (Field(queue, "dispositionChanged") as TaskCompletionSource)?.Task; }
    }
    internal static object? Field(object instance, string name) => (instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Pinned inspection field unavailable: " + name)).GetValue(instance);
    internal static JsonObject QueueGauge(OutputEventQueue queue)
    {
        OutputFlushRegistration[] controls = Registrations(queue);
        return new JsonObject
        {
            ["controls"] = controls.Length,
            ["attached"] = controls.Sum(c => FlushCoordinationFixture.Continuations(c.WriterFlushCompletion)),
            ["slots"] = controls.Sum(c => FlushCoordinationFixture.ContinuationSlots(c.WriterFlushCompletion)),
            ["buffered"] = queue.BufferedCount,
            ["targets"] = new JsonArray(controls.Select(c => (JsonNode)JsonValue.Create(Number(c.AcceptedSequenceBoundary))!).ToArray())
        };
    }
    internal static JsonArray Diagnostics(CoreLogRuntime runtime) => new(runtime.GetDiagnostics().Outputs.Select(output => (JsonNode)new JsonObject
    {
        ["name"] = output.OutputName,
        ["accepted"] = Number(output.Accepted),
        ["written"] = Number(output.Written),
        ["failed"] = Number(output.Failed),
        ["abandoned"] = Number(output.Abandoned),
        ["pending"] = Number(output.Pending),
        ["full"] = Number(output.QueueFull),
        ["unavailable"] = Number(output.Unavailable),
        ["filtered"] = Number(output.Filtered),
        ["fault"] = output.LastFaultCode
    }).ToArray());
    internal sealed class Writer(bool holdFirstWrite = true, bool holdFirstFlush = false, bool holdSecondWrite = false, bool failFirstFlush = false) : IDisposable
    {
        internal readonly TaskCompletionSource FirstWrite = Signal(), SecondWrite = Signal(), FirstFlush = Signal(),
            ReleaseFirst = Signal(), ReleaseSecond = Signal(), ReleaseFlush = Signal();
        internal readonly MemoryStream Bytes = new();
        internal readonly List<string> Order = [];
        private int active;
        private long written;
        internal int Maximum, Writes, Flushes;
        internal async ValueTask WriteAsync(AdmittedEvent item)
        {
            OutputQueueFixture.RecordMaximum(ref Maximum, Interlocked.Increment(ref active));
            try
            {
                int attempt = ++Writes;
                if (attempt == 1) { FirstWrite.TrySetResult(); if (holdFirstWrite) { await ReleaseFirst.Task; } }
                if (attempt == 2) { SecondWrite.TrySetResult(); if (holdSecondWrite) { await ReleaseSecond.Task; } }
                await Bytes.WriteAsync(item.Event.JsonLine); written = item.AcceptedSequence;
                Order.Add("write." + Number(written));
            }
            finally { Interlocked.Decrement(ref active); }
        }
        internal async ValueTask FlushAsync()
        {
            OutputQueueFixture.RecordMaximum(ref Maximum, Interlocked.Increment(ref active));
            try
            {
                if (++Flushes == 1)
                {
                    FirstFlush.TrySetResult(); if (holdFirstFlush) { await ReleaseFlush.Task; }
                    if (failFirstFlush) { throw new IOException("private controlled flush failure"); }
                }
                await Bytes.FlushAsync(); Order.Add("flush." + Number(written));
            }
            finally { Interlocked.Decrement(ref active); }
        }
        internal JsonObject Report() => new()
        {
            ["maximumIo"] = Maximum,
            ["writes"] = Writes,
            ["flushes"] = Flushes,
            ["bytes"] = Convert.ToBase64String(Bytes.ToArray()),
            ["order"] = new JsonArray(Order.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray())
        };
        internal void ReleaseAll() { ReleaseFirst.TrySetResult(); ReleaseSecond.TrySetResult(); ReleaseFlush.TrySetResult(); }
        public void Dispose() { ReleaseAll(); Bytes.Dispose(); }
    }
    // Nonshipping deterministic clock: real Core timer callbacks, one-shot arms and asynchronous callback joins.
    internal sealed class Clock : TimeProvider
    {
        [ThreadStatic] internal static string? CallerKey;
        private readonly object gate = new();
        private readonly List<Timer> timers = [];
        private long ticks;
        private DateTimeOffset utc = new(2026, 10, 13, 19, 20, 21, TimeSpan.Zero);
        internal int Created, Disposed, Fires, MaximumActive, ActiveCallbacks, PendingJoins;
        internal bool SampleOwners;
        private int sampledCalls;
        internal readonly List<WeakReference> Samples = [];
        internal readonly List<JsonObject> Captures = [];
        internal readonly TaskCompletionSource DisposalStarted = Signal();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) { return ticks; } }
        public override DateTimeOffset GetUtcNow() { lock (gate) { return utc; } }
        internal void ShiftUtc(int years) { lock (gate) { utc = utc.AddYears(years); } }
        internal int Active { get { lock (gate) { return timers.Count; } } }
        internal object Owner { get { lock (gate) { return timers.Single().State!; } } }
        internal long[] Due { get { lock (gate) { return timers.Select(t => t.Due).Order().ToArray(); } } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            if (CallerKey is { } key)
            {
                var observe = (Func<LogFlushResult>)Field(state!, "observe")!;
                JsonObject initial = Result(observe()); initial["caller"] = key;
                lock (gate) { Captures.Add(initial); }
            }
            lock (gate)
            {
                timers.Add(timer); Created++; MaximumActive = Math.Max(MaximumActive, timers.Count);
                if (SampleOwners && sampledCalls++ % 1000 is 0 or 1) { Samples.Add(new WeakReference(state)); }
            }
            timer.Change(dueTime, period); return timer;
        }
        internal void Advance(TimeSpan elapsed, bool early = false)
        {
            (Timer timer, object? state)[] ready;
            lock (gate)
            {
                ticks += elapsed.Ticks;
                ready = timers.Where(t => early || t.Due <= ticks).Select(t => (t, t.State)).ToArray();
                foreach ((Timer timer, _) in ready) { timer.Due = long.MaxValue; timer.Running++; ActiveCallbacks++; Fires++; }
            }
            foreach ((Timer timer, object? state) in ready)
            {
                try { timer.Callback(state); }
                finally
                {
                    lock (gate)
                    {
                        timer.Running--; ActiveCallbacks--;
                        if (timer.Disposed && timer.Running == 0) { timer.Join.TrySetResult(); }
                    }
                }
            }
        }
        internal JsonObject Report() { lock (gate) { return new JsonObject { ["created"] = Created, ["disposed"] = Disposed, ["fires"] = Fires, ["active"] = timers.Count, ["maximumActive"] = MaximumActive, ["callbacks"] = ActiveCallbacks, ["pendingJoins"] = PendingJoins, ["dueTicks"] = new JsonArray(timers.Select(t => t.Due).Order().Select(d => (JsonNode)JsonValue.Create(Number(d))!).ToArray()) }; } }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal object? State = state;
            internal long Due = long.MaxValue;
            internal int Running;
            internal bool Disposed;
            internal readonly TaskCompletionSource Join = Signal();
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Require(period == Timeout.InfiniteTimeSpan);
                lock (clock.gate)
                {
                    if (Disposed) { return false; }
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks; return true;
                }
            }
            private void BeginDispose()
            {
                if (Disposed) { return; }
                Disposed = true; clock.timers.Remove(this); clock.Disposed++; State = null;
                if (Running == 0) { Join.TrySetResult(); }
            }
            public void Dispose() { lock (clock.gate) { BeginDispose(); } }
            public async ValueTask DisposeAsync()
            {
                bool pending;
                lock (clock.gate)
                {
                    BeginDispose(); pending = !Join.Task.IsCompleted;
                    if (pending) { clock.PendingJoins++; }
                    clock.DisposalStarted.TrySetResult();
                }
                try { await Join.Task.ConfigureAwait(false); }
                finally { if (pending) { lock (clock.gate) { clock.PendingJoins--; } } }
            }
        }
    }
    internal static async Task Cleanup(CoreLogRuntime runtime, params Writer[] writers)
    {
        foreach (Writer writer in writers) { writer.ReleaseAll(); }
        runtime.CloseAdmission();
        try { await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); } catch (IOException) { }
    }
}
