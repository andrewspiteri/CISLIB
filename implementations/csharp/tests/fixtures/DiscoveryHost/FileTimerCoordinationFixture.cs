using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileTimerCoordinationFixture
{
    internal static readonly string[] Modes = ["blocked-write", "blocked-flush", "continuous", "duplicate-start", "closed-start", "detach-before-arm", "detach-system-before-arm", "arm-false", "arm-disposed"];
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static Task<JsonObject> ExecuteAsync(string mode)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        Require(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12");
        return mode == "continuous" ? ContinuousAsync(mode) : mode.StartsWith("blocked-", StringComparison.Ordinal) ? BlockedAsync(mode) : StartAsync(mode);
    }
    private static CoreLogRuntime Runtime(Clock clock, Writer writer) =>
        CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.Information, FilePath = "controlled", QueueCapacity = 3, MaxEntryBytes = 16384 },
            null, writer.WriteAsync, clock, fileFlush: writer.FlushAsync);

    private static async Task<JsonObject> BlockedAsync(string mode)
    {
        bool flush = mode == "blocked-flush";
        var clock = new Clock();
        using var writer = new Writer(holdFirstWrite: !flush, holdFirstFlush: flush);
        CoreLogRuntime runtime = Runtime(clock, writer);
        OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        Task<LogFlushResult>? live = null;
        try
        {
            queue.StartFileFlushTimer(clock, Interval);
            Accepted(runtime.CreateLogger("FileTimer").Write(CoreLogLevel.Information, Data("record.1")));
            await writer.FirstWrite.Task.WaitAsync(Guard);
            if (flush) { await UntilAsync(() => queue.CaptureAccounting().Written == 1); }
            lock (Field(queue, "admissionGate")!)
            {
                clock.Advance(Interval);
                Require(queue.RegisteredFlushCount == 1);
            }
            if (flush) { await writer.FirstFlush.Task.WaitAsync(Guard); }
            for (int index = 2; index <= 4; index++)
            {
                Accepted(runtime.CreateLogger("FileTimer").Write(CoreLogLevel.Information, Data("record." + index)));
                lock (Field(queue, "admissionGate")!)
                {
                    clock.Advance(Interval);
                    Require(queue.RegisteredFlushCount == index);
                }
            }
            Require(runtime.CreateLogger("FileTimer").Write(CoreLogLevel.Information, Data("overflow")).Outcome == LogWriteOutcome.Rejected);
            JsonObject before = QueueGauge(queue); OutputFlushRegistration[] beforeControls = Registrations(queue); int beforeCreated = clock.Created;
            int workerId = queue.Completion.Id;
            lock (Field(queue, "admissionGate")!)
            {
                clock.Advance(TimeSpan.FromSeconds(30));
                Require(queue.RegisteredFlushCount == 4 && clock.Created == beforeCreated);
            }
            JsonObject after = QueueGauge(queue); int afterCreated = clock.Created;
            bool controlsIdentical = Registrations(queue).SequenceEqual(beforeControls, ReferenceEqualityComparer.Instance);
            Require(controlsIdentical);
            Require(before.ToJsonString() == after.ToJsonString() && clock.PeriodicFires == 3004);
            JsonObject heldWriter = writer.Report();
            Require(writer.Writes == 1 && writer.Flushes == (flush ? 1 : 0) && writer.Maximum == 1);
            OutputFlushRegistration last = Registrations(queue).Single(c => c.AcceptedSequenceBoundary == 4);
            live = runtime.FlushAsync(TimeSpan.FromMinutes(10), CancellationToken.None);
            Require(ReferenceEquals(last, Registrations(queue).Single(c => c.AcceptedSequenceBoundary == 4)));
            Require(!live.IsCompleted && clock.Active == 2);
            int peakAttached = 0, peakSlots = 0, cancelled = 0;
            for (int index = 0; index < 500; index++)
            {
                using var caller = new CancellationTokenSource();
                Task<LogFlushResult> wait = runtime.FlushAsync(TimeSpan.FromMinutes(10), caller.Token);
                Require(FlushCoordinationFixture.Continuations(last.WriterFlushCompletion) == 2);
                peakAttached = Math.Max(peakAttached, FlushCoordinationFixture.Continuations(last.WriterFlushCompletion));
                peakSlots = Math.Max(peakSlots, FlushCoordinationFixture.ContinuationSlots(last.WriterFlushCompletion));
                caller.Cancel();
                try { await wait.WaitAsync(Guard); throw new InvalidOperationException("Cancelled caller completed."); }
                catch (OperationCanceledException error) { Require(error.CancellationToken == caller.Token); cancelled++; }
                Require(FlushCoordinationFixture.Continuations(last.WriterFlushCompletion) == 1 && clock.Active == 2);
            }
            Require(peakAttached == 2 && peakSlots <= 4 && queue.Completion.Id == workerId);
            JsonObject detached = QueueGauge(queue);
            writer.ReleaseAll();
            LogFlushResult result = await live.WaitAsync(Guard);
            Require(result.Completion == LogCompletion.Completed && result.Succeeded && result.LossObserved);
            Require(result.Outputs.Single().AcceptedSequenceBoundary == 4 && result.Outputs.Single().Written == 4 && result.Outputs.Single().Rejections.QueueFull == 1);
            await Cleanup(runtime, writer);
            Require(queue.RegisteredFlushCount == 0 && clock.Active == 0);
            JsonObject report = Common(mode);
            report["controlsIdentical"] = controlsIdentical; report["beforePulses"] = before; report["afterPulses"] = after; report["afterDetach"] = detached; report["heldWriter"] = heldWriter;
            report["repeated"] = new JsonObject
            {
                ["ticks"] = 3000,
                ["cancelled"] = cancelled,
                ["peakAttached"] = peakAttached,
                ["peakSlots"] = peakSlots,
                ["createdBeforeTicks"] = beforeCreated,
                ["createdAfterTicks"] = afterCreated,
                ["workerIdBefore"] = workerId,
                ["workerIdAfter"] = queue.Completion.Id
            };
            report["result"] = Result(result); report["writer"] = writer.Report(); report["timers"] = clock.Report();
            report["diagnostics"] = Diagnostics(runtime); report["workerCompleted"] = queue.Completion.IsCompletedSuccessfully; report["finalControls"] = queue.RegisteredFlushCount;
            return report;
        }
        finally { await Cleanup(runtime, writer); if (live is not null) { await live.WaitAsync(Guard); } }
    }
    private static async Task<JsonObject> ContinuousAsync(string mode)
    {
        var clock = new Clock(); using var writer = new Writer(holdFirstWrite: false);
        CoreLogRuntime runtime = Runtime(clock, writer); OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        var boundaries = new JsonArray(); int pulseCreations = 0; LogFlushResult? last = null;
        try
        {
            queue.StartFileFlushTimer(clock, Interval);
            for (int index = 1; index <= 128; index++)
            {
                Accepted(runtime.CreateLogger("FileTimer").Write(CoreLogLevel.Information, Data("record." + index)));
                await UntilAsync(() => queue.CaptureAccounting().Written == index);
                Task<LogFlushResult> caller;
                lock (Field(queue, "admissionGate")!)
                {
                    // Test-only barrier encloses fixture clock creation; production does not hold this gate for a public caller.
                    int before = clock.Created; clock.Advance(Interval); pulseCreations += clock.Created - before;
                    OutputFlushRegistration existing = Registrations(queue).Single();
                    Require(existing.AcceptedSequenceBoundary == index && !existing.WriterFlushCompletion.IsCompleted);
                    caller = runtime.FlushAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
                    Require(ReferenceEquals(existing, Registrations(queue).Single()));
                }
                last = await caller.WaitAsync(Guard);
                Require(last.Completion == LogCompletion.Completed && last.Succeeded && !last.LossObserved && last.Outputs.Single().AcceptedSequenceBoundary == index);
                Require(writer.Flushes == index && queue.RegisteredFlushCount == 0 && clock.Active == 1);
                boundaries.Add(Number(last.Outputs.Single().AcceptedSequenceBoundary));
            }
            await Cleanup(runtime, writer); Require(pulseCreations == 0);
            JsonObject report = Common(mode); report["boundaries"] = boundaries; report["pulseCreations"] = pulseCreations;
            report["result"] = Result(last!); report["writer"] = writer.Report();
            report["timers"] = clock.Report(); report["diagnostics"] = Diagnostics(runtime); report["workerCompleted"] = queue.Completion.IsCompletedSuccessfully; report["finalControls"] = queue.RegisteredFlushCount;
            return report;
        }
        finally { await Cleanup(runtime, writer); }
    }
    private static string? Error(Action action)
    { try { action(); return null; } catch (Exception error) { return error.GetType().Name; } }
    private static async Task<JsonObject> StartAsync(string mode)
    {
        var clock = new Clock { HoldArm = mode.StartsWith("detach-", StringComparison.Ordinal), ReturnFalse = mode == "arm-false", UseSystemTimer = mode == "detach-system-before-arm", ThrowOwned = mode == "arm-disposed" };
        using var writer = new Writer(holdFirstWrite: false);
        CoreLogRuntime runtime = Runtime(clock, writer); OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        string? error = null; Task? starter = null; JsonObject? held = null;
        try
        {
            if (mode == "duplicate-start")
            {
                queue.StartFileFlushTimer(clock, Interval);
                error = Error(() => queue.StartFileFlushTimer(clock, Interval));
                Require(error == "InvalidOperationException" && clock.Created == 2 && clock.Disposed == 1 && clock.Active == 1);
                OutputFlushRegistration actual;
                lock (Field(queue, "admissionGate")!) { clock.Advance(Interval); actual = Registrations(queue).Single(); Require(actual.AcceptedSequenceBoundary == 0); }
                Require(await actual.WriterFlushCompletion.WaitAsync(Guard) && writer.Flushes == 1);
            }
            else if (mode == "closed-start")
            {
                await Cleanup(runtime, writer);
                error = Error(() => queue.StartFileFlushTimer(clock, Interval));
                Require(error is null && clock.Created == 1 && clock.Disposed == 1 && clock.Active == 0);
                clock.Advance(Interval); Require(clock.PeriodicFires == 0 && writer.Flushes == 0);
            }
            else if (mode.StartsWith("detach-", StringComparison.Ordinal))
            {
                starter = Task.Run(() => queue.StartFileFlushTimer(clock, Interval));
                await clock.ArmEntered.Task.WaitAsync(Guard);
                await Cleanup(runtime, writer);
                Require(clock.Created == 1 && clock.Disposed == 1 && Field(queue, "fileFlushTimer") is null);
                held = clock.Report(); clock.ArmRelease.TrySetResult();
                await starter.WaitAsync(Guard); Require(writer.Flushes == 0 && clock.Active == 0);
            }
            else
            {
                error = Error(() => queue.StartFileFlushTimer(clock, Interval));
                Require(error == (mode == "arm-disposed" ? "ObjectDisposedException" : "InvalidOperationException") && clock.Created == 1 && clock.Disposed == 1 && clock.Active == 0);
            }
            held ??= clock.Report(); await Cleanup(runtime, writer);
            Require(clock.SystemCreated == (mode == "detach-system-before-arm" ? 1 : 0) && clock.SystemFalseChanges == clock.SystemCreated);
            JsonObject report = Common(mode); report["startError"] = error; report["heldTimers"] = held;
            report["timers"] = clock.Report(); report["writer"] = writer.Report(); report["diagnostics"] = Diagnostics(runtime);
            report["workerCompleted"] = queue.Completion.IsCompletedSuccessfully; report["finalControls"] = queue.RegisteredFlushCount;
            return report;
        }
        finally { clock.ArmRelease.TrySetResult(); if (starter is not null) { await starter.WaitAsync(Guard); } await Cleanup(runtime, writer); }
    }
    // Nonshipping clock for periodic and caller-deadline requests. No active-callback join qualification is claimed.
    private sealed class Clock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<Timer> timers = [];
        private long ticks;
        private DateTimeOffset utc = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        internal readonly TaskCompletionSource ArmEntered = Signal(), ArmRelease = Signal();
        internal bool HoldArm, ReturnFalse, UseSystemTimer, ThrowOwned, PeriodicSuppressed = true;
        internal int Created, Disposed, PeriodicFires, DeadlineFires, MaximumActive, SystemCreated, SystemFalseChanges;
        internal int Active { get { lock (gate) { return timers.Count; } } }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) { return ticks; } }
        public override DateTimeOffset GetUtcNow() { lock (gate) { return utc; } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer? actual = UseSystemTimer ? TimeProvider.System.CreateTimer(callback, state, dueTime, period) : null;
            if (actual is not null) { SystemCreated++; }
            var timer = new Timer(this, callback, state, ExecutionContext.IsFlowSuppressed(), actual);
            lock (gate) { timers.Add(timer); Created++; MaximumActive = Math.Max(MaximumActive, timers.Count); }
            if (dueTime != Timeout.InfiniteTimeSpan || period != Timeout.InfiniteTimeSpan) { timer.Change(dueTime, period); }
            return timer;
        }
        internal void Advance(TimeSpan elapsed)
        {
            Require(elapsed >= TimeSpan.Zero);
            long target; lock (gate) { target = checked(ticks + elapsed.Ticks); }
            while (true)
            {
                Timer[] ready;
                lock (gate)
                {
                    long next = timers.Select(t => t.Due).DefaultIfEmpty(long.MaxValue).Min();
                    long destination = Math.Min(next, target);
                    utc += TimeSpan.FromTicks(destination - ticks); ticks = destination;
                    ready = timers.Where(t => t.Due <= ticks).ToArray();
                    foreach (Timer timer in ready)
                    {
                        if (timer.Period > 0) { timer.Due = checked(timer.Due + timer.Period); PeriodicFires++; }
                        else { timer.Due = long.MaxValue; DeadlineFires++; }
                    }
                    if (ready.Length == 0) { return; }
                }
                foreach (Timer timer in ready) { timer.Callback(timer.State); }
            }
        }
        internal JsonObject Report()
        {
            lock (gate)
            {
                return new JsonObject
                {
                    ["created"] = Created,
                    ["disposed"] = Disposed,
                    ["periodicFires"] = PeriodicFires,
                    ["deadlineFires"] = DeadlineFires,
                    ["active"] = timers.Count,
                    ["activePeriodic"] = timers.Count(t => t.Period > 0),
                    ["maximumActive"] = MaximumActive,
                    ["periodicSuppressed"] = PeriodicSuppressed,
                    ["systemCreated"] = SystemCreated,
                    ["systemFalseChanges"] = SystemFalseChanges
                };
            }
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state, bool suppressed, ITimer? actual) : ITimer
        {
            internal readonly TimerCallback Callback = callback; internal readonly object? State = state;
            internal long Due = long.MaxValue, Period; private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (clock.HoldArm && period > TimeSpan.Zero) { clock.ArmEntered.TrySetResult(); Require(clock.ArmRelease.Task.Wait(Guard)); }
                lock (clock.gate)
                {
                    if (actual is not null)
                    {
                        bool changed = actual.Change(dueTime, period);
                        if (!changed) { clock.SystemFalseChanges++; }
                        return changed;
                    }
                    if (!disposed && clock.ThrowOwned && period > TimeSpan.Zero) { throw new ObjectDisposedException("controlled timer"); }
                    if (disposed || clock.ReturnFalse && period > TimeSpan.Zero) { return false; }
                    Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(clock.ticks + dueTime.Ticks);
                    if (Period > 0) { clock.PeriodicSuppressed &= suppressed; }
                    return true;
                }
            }
            public void Dispose()
            {
                lock (clock.gate) { if (!disposed) { disposed = true; clock.timers.Remove(this); clock.Disposed++; } }
                actual?.Dispose();
            }
            public ValueTask DisposeAsync() { Dispose(); return actual?.DisposeAsync() ?? ValueTask.CompletedTask; }
        }
    }
}
