using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class FileIntervalFixture
{
    internal static readonly string[] Modes = ["idle-min", "idle-default", "idle-max", "configured", "console-only", "file-none", "create-failure", "arm-failure", "below", "above", "env"];
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        Require(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12");
        if (mode.StartsWith("idle-", StringComparison.Ordinal)) { return await IdleAsync(mode); }
        return await ConfiguredAsync(mode, root);
    }
    private static async Task<JsonObject> IdleAsync(string mode)
    {
        int milliseconds = mode == "idle-min" ? 10 : mode == "idle-max" ? 60000 : 1000;
        var clock = new Clock(); using var writer = new Writer(holdFirstWrite: false);
        var options = new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.Information, FilePath = "controlled", QueueCapacity = 3, MaxEntryBytes = 16384 };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, null, writer.WriteAsync, clock, fileFlush: writer.FlushAsync);
        OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        var targets = new JsonArray();
        try
        {
            queue.StartFileFlushTimer(clock, TimeSpan.FromMilliseconds(milliseconds));
            Require(runtime.CreateLogger("FileInterval").Write(CoreLogLevel.Information, Data("one")).Outcome == LogWriteOutcome.Accepted);
            await UntilAsync(() => queue.CaptureAccounting().Written == 1);
            Require(writer.Flushes == 0);
            clock.Advance(TimeSpan.FromMilliseconds(milliseconds) - TimeSpan.FromTicks(1));
            Require(clock.Fires == 0 && writer.Flushes == 0 && queue.RegisteredFlushCount == 0);
            for (int index = 0; index < 2; index++)
            {
                OutputFlushBoundary observation;
                lock (Field(queue, "admissionGate")!)
                {
                    clock.Advance(index == 0 ? TimeSpan.FromTicks(1) : TimeSpan.FromMilliseconds(milliseconds));
                    Require(queue.RegisteredFlushCount == 1);
                    observation = queue.RegisterFlushBoundary();
                    Require(observation.AcceptedSequenceBoundary == 1 && !observation.Registration.WriterFlushCompletion.IsCompleted);
                }
                Require(await observation.Registration.WriterFlushCompletion.WaitAsync(Guard));
                targets.Add(Number(observation.AcceptedSequenceBoundary));
                Require(writer.Flushes == index + 1 && queue.CaptureAccounting().Written == 1);
            }
            lock (Field(queue, "admissionGate")!)
            {
                runtime.CloseAdmission();
                Require(!(bool)Field(queue, "workerExiting")! && clock.Report()["active"]!.GetValue<int>() == 1);
                clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
                Require(queue.RegisteredFlushCount == 0);
            }
            await Cleanup(runtime, writer);
            Require(writer.Flushes == 2);
            JsonObject report = Common(mode); report["interval"] = milliseconds; report["targets"] = targets;
            report["beforeDue"] = true; report["idleNoNewEvents"] = true;
            report["writer"] = writer.Report(); report["timers"] = clock.Report(); report["diagnostics"] = Diagnostics(runtime); return report;
        }
        finally { await Cleanup(runtime, writer); }
    }
    private static async Task<JsonObject> ConfiguredAsync(string mode, string root)
    {
        var clock = new Clock { FailCreate = mode == "create-failure", FailArm = mode == "arm-failure" };
        using var borrowed = new MemoryStream(); var release = Signal(); var workers = new List<Task>();
        string path = Path.Combine(root, "owned", "events.jsonl");
        bool disabled = mode is "console-only" or "file-none", invalid = mode is "below" or "above", failure = mode is "create-failure" or "arm-failure";
        var options = mode == "env" ? EnvironmentOptions.Logging : new LoggingOptions
        {
            ConsoleLevel = disabled || failure || invalid ? CoreLogLevel.Information : CoreLogLevel.None,
            FileLevel = mode == "file-none" ? CoreLogLevel.None : CoreLogLevel.Information,
            FilePath = mode == "console-only" ? null : path,
            ConsoleFormat = LogOutputFormat.JsonLines,
            QueueCapacity = 3,
            MaxEntryBytes = 16384,
            FlushIntervalMilliseconds = 500
        };
        if (mode == "env") { Require(options.FlushIntervalMilliseconds == 250); options.Prepare(root); path = options.FilePath!; }
        if (mode == "below") { options.FlushIntervalMilliseconds = 9; }
        if (mode == "above") { options.FlushIntervalMilliseconds = 60001; }
        if (disabled) { options.FlushIntervalMilliseconds = int.MinValue; }
        int selections = 0;
        Task Launch(Func<Task> process)
        {
            Task worker = Task.Run(async () => { await release.Task; await process(); }); workers.Add(worker); return worker;
        }
        CoreLogRuntime? runtime = null;
        try
        {
            if (invalid || failure)
            {
                // Failed composition joins its idle workers; don't hold those workers for this branch.
                release.TrySetResult();
                Exception? error = null;
                try { runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, clock, _ => { selections++; return borrowed; }, Launch); }
                catch (Exception caught) { error = caught; }
                Require(error is not null && runtime is null);
                if (invalid) { Require(error is ArgumentOutOfRangeException && selections == 0 && workers.Count == 0 && !Directory.Exists(Path.GetDirectoryName(path))); }
                else
                {
                    Require(error is CoreLogInitializationException safe && safe.OutputName == "file" && safe.ErrorCode == "FileFlushTimerInitializationFailed" && safe.InnerException is null);
                    Require(workers.Count == 2 && workers.All(w => w.IsCompletedSuccessfully) && borrowed.CanWrite);
                    using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    using var active = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    Require(active.Length == 0 && lease.Length == 0);
                }
                JsonObject failed = Common(mode); failed["safeCode"] = (error as CoreLogInitializationException)?.ErrorCode;
                failed["errorType"] = error.GetType().Name; failed["selections"] = selections; failed["workers"] = workers.Count;
                failed["joined"] = workers.All(w => w.IsCompletedSuccessfully); failed["borrowedOpen"] = borrowed.CanWrite;
                failed["timers"] = clock.Report(); failed["noOutputBeforeFailure"] = true;
                failed["ownershipReleased"] = failure;
                failed["fileBytes"] = failure ? Convert.ToBase64String(ReadLiveFile(path)) : ""; return failed;
            }
            runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, clock, _ => { selections++; return borrowed; }, Launch);
            int original = options.FlushIntervalMilliseconds;
            options.FlushIntervalMilliseconds = 60000; // Runtime timer must keep the captured value.
            Require(runtime.CreateLogger("FileInterval").Write(CoreLogLevel.Information, Data("one")).Outcome == LogWriteOutcome.Accepted);
            var targets = new JsonArray();
            if (disabled)
            {
                clock.Advance(TimeSpan.FromSeconds(10)); Require(clock.Created == 0 && !File.Exists(path));
                release.TrySetResult(); await UntilAsync(() => runtime.GetDiagnostics().Outputs[0].Written == 1);
            }
            else
            {
                Require(clock.Created == 1 && clock.PeriodMilliseconds == original && clock.Suppressed);
                OutputEventQueue queue = runtime.QueuedOutputs!.File!;
                clock.Advance(TimeSpan.FromMilliseconds(original) - TimeSpan.FromTicks(1));
                Require(clock.Fires == 0 && queue.RegisteredFlushCount == 0);
                clock.Advance(TimeSpan.FromTicks(1));
                Require(queue.RegisteredFlushCount == 1 && queue.CaptureAccounting().Written == 0);
                OutputFlushRegistration[] existing = Registrations(queue);
                Require(existing.Length == 1);
                OutputFlushBoundary first = queue.RegisterFlushBoundary();
                Require(ReferenceEquals(first.Registration, existing[0]) && queue.RegisteredFlushCount == 1);
                targets.Add(Number(first.AcceptedSequenceBoundary));
                release.TrySetResult(); Require(await first.Registration.WriterFlushCompletion.WaitAsync(Guard));
                Require(queue.CaptureAccounting().Written == 1);
                OutputFlushBoundary idle;
                lock (Field(queue, "admissionGate")!)
                {
                    clock.SetUtc(15); clock.SetUtc(-30);
                    clock.Advance(TimeSpan.FromMilliseconds(original));
                    Require(queue.RegisteredFlushCount == 1);
                    idle = queue.RegisterFlushBoundary();
                    Require(!idle.Registration.WriterFlushCompletion.IsCompleted && idle.AcceptedSequenceBoundary == 1);
                }
                Require(await idle.Registration.WriterFlushCompletion.WaitAsync(Guard));
                targets.Add(Number(idle.AcceptedSequenceBoundary));
            }
            byte[] bytes = disabled ? borrowed.ToArray() : ReadLiveFile(path);
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            if (!disabled)
            {
                using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var active = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            JsonObject report = Common(mode); report["interval"] = disabled ? (JsonNode?)null : original;
            report["targets"] = targets; report["bytes"] = Convert.ToBase64String(bytes);
            report["timers"] = clock.Report(); report["diagnostics"] = Diagnostics(runtime);
            report["borrowedOpen"] = borrowed.CanWrite; report["ownershipReleased"] = true;
            report["optionMutationIgnored"] = true; report["beforeDue"] = true; report["idleNoNewEvents"] = true; return report;
        }
        finally
        {
            release.TrySetResult();
            if (runtime is not null) { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); }
        }
    }
    private static byte[] ReadLiveFile(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
    }
    private sealed class Clock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<Timer> timers = [];
        private long ticks; private DateTimeOffset utc = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        internal bool FailCreate, FailArm, Suppressed = true;
        internal int Attempts, Created, Disposed, Fires, PeriodMilliseconds;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) { return ticks; } }
        public override DateTimeOffset GetUtcNow() { lock (gate) { return utc; } }
        internal void SetUtc(int years) { lock (gate) { utc = utc.AddYears(years); } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Attempts++; Suppressed &= ExecutionContext.IsFlowSuppressed();
            Require(dueTime == Timeout.InfiniteTimeSpan && period == Timeout.InfiniteTimeSpan);
            if (FailCreate) { throw new IOException("private timer creation details"); }
            var timer = new Timer(this, callback, state); lock (gate) { timers.Add(timer); Created++; }
            return timer;
        }
        internal void Advance(TimeSpan elapsed)
        {
            long target; lock (gate) { target = checked(ticks + elapsed.Ticks); }
            while (true)
            {
                Timer[] ready;
                lock (gate)
                {
                    long next = timers.Select(t => t.Due).DefaultIfEmpty(long.MaxValue).Min();
                    long destination = Math.Min(next, target); utc += TimeSpan.FromTicks(destination - ticks); ticks = destination;
                    ready = timers.Where(t => t.Due <= ticks).ToArray();
                    foreach (Timer timer in ready) { timer.Due += timer.Period; Fires++; }
                    if (ready.Length == 0) { return; }
                }
                foreach (Timer timer in ready) { timer.Callback(timer.State); }
            }
        }
        internal JsonObject Report() { lock (gate) { return new JsonObject { ["attempts"] = Attempts, ["created"] = Created, ["disposed"] = Disposed, ["fires"] = Fires, ["active"] = timers.Count, ["period"] = PeriodMilliseconds, ["flowSuppressed"] = Suppressed }; } }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback; internal readonly object? State = state;
            internal long Due = long.MaxValue, Period; private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (clock.FailArm) { throw new IOException("private timer arm details"); }
                lock (clock.gate)
                {
                    if (disposed) { return false; }
                    Require(dueTime == period && dueTime > TimeSpan.Zero);
                    Due = clock.ticks + dueTime.Ticks; Period = period.Ticks;
                    clock.PeriodMilliseconds = (int)period.TotalMilliseconds; return true;
                }
            }
            public void Dispose() { lock (clock.gate) { if (!disposed) { disposed = true; clock.timers.Remove(this); clock.Disposed++; } } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
