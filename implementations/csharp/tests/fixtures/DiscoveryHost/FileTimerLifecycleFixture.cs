using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

// Nonshipping barriers exercise real Core queues; no public stop/result substitute.
internal static class FileTimerLifecycleFixture
{
    internal static readonly string[] Modes = ["close-write", "close-flush", "close-callback", "flush-failure", "flush-failure-callback", "admission-fault", "detached-arm-throw"];
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    private static readonly AsyncLocal<string?> CallerFact = new();
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        Require(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription == ".NET 10.0.12");
        return mode == "detached-arm-throw" ? DetachedArmAsync(mode) : mode == "close-callback" ? PhysicalJoinAsync(mode, root) : ControlledAsync(mode);
    }
    private static JsonArray ObserveDiagnostics(CoreLogRuntime runtime)
    {
        var array = new JsonArray();
        foreach (LogOutputDiagnostics output in runtime.GetDiagnostics().Outputs)
        {
            array.Add(new JsonObject
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
                ["fault"] = output.LastFaultCode,
                ["faultAt"] = output.LastFaultAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["state"] = output.State.ToString()
            });
        }
        return array;
    }
    private static void CloseUnderOuterGate(CoreLogRuntime runtime, Clock clock)
    {
        lock (Field(runtime.QueuedOutputs!.File!, "admissionGate")!)
        {
            Volatile.Write(ref clock.CloserInside, 1);
            CallerFact.Value = "caller-private";
            try { runtime.CloseAdmission(); }
            finally { CallerFact.Value = null; Volatile.Write(ref clock.CloserInside, 0); }
        }
    }
    private static async Task<JsonObject> DetachedArmAsync(string mode)
    {
        var clock = new Clock { HoldArm = true };
        using var writer = new Writer(holdFirstWrite: false);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.Information, FilePath = "controlled", QueueCapacity = 3, MaxEntryBytes = 16384 },
            null, writer.WriteAsync, clock, fileFlush: writer.FlushAsync);
        OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        Task completion = runtime.QueuedOutputs.Completion;
        Task starter = Task.Run(() => queue.StartFileFlushTimer(clock, Interval));
        try
        {
            await clock.ArmEntered.Task.WaitAsync(Guard);
            CloseUnderOuterGate(runtime, clock);
            await clock.DisposalStarted.Task.WaitAsync(Guard);
            Task disposal = Disposal(queue)!;
            await completion.WaitAsync(Guard); Require(clock.PeriodicDisposals == 1);
            JsonObject held = new() { ["timerDetached"] = Field(queue, "fileFlushTimer") is null, ["timers"] = clock.Report(), ["sharedPending"] = !completion.IsCompleted, ["workerPending"] = !queue.Completion.IsCompleted };
            clock.ArmRelease.TrySetResult();
            string? error = null;
            try { await starter.WaitAsync(Guard); }
            catch (ObjectDisposedException caught) { error = caught.GetType().Name; }
            Require(error == "ObjectDisposedException" && clock.SyncDisposeCalls == 0 && clock.PeriodicDisposals == 1);
            await CloseRepeatedAsync(runtime, clock, completion, disposal);
            JsonObject report = Common(mode); report["held"] = held; report["timers"] = clock.Report(); report["startError"] = error;
            report["sameSharedCompletion"] = ReferenceEquals(runtime.QueuedOutputs.Completion, completion);
            report["sameDisposal"] = ReferenceEquals(Disposal(queue), disposal); report["sharedJoined"] = completion.IsCompletedSuccessfully;
            report["repeatedClosures"] = 64; report["finalControls"] = queue.RegisteredFlushCount; report["writer"] = writer.Report();
            return report;
        }
        finally
        {
            clock.ArmRelease.TrySetResult(); runtime.CloseAdmission();
            try { await starter.WaitAsync(Guard); } catch (ObjectDisposedException) { }
            await completion.WaitAsync(Guard);
        }
    }
    private static Task? Disposal(OutputEventQueue queue) => (Task?)Field(queue, "fileFlushTimerDisposal");
    private static async Task CloseRepeatedAsync(CoreLogRuntime runtime, Clock clock, Task completion, Task disposal)
    {
        CallerFact.Value = "caller-private";
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(runtime.CloseAdmission))).WaitAsync(Guard);
            Require(ReferenceEquals(runtime.QueuedOutputs!.Completion, completion));
            Require(ReferenceEquals(Disposal(runtime.QueuedOutputs.File!), disposal));
            Require(clock.PeriodicDisposals == 1 && clock.SuppressedCreation && clock.DisposalOutsideGate && clock.DisposalWithoutCaller);
        }
        finally { CallerFact.Value = null; }
    }
    private static async Task ObserveSharedCompletion(Task completion, bool fault)
    {
        if (fault)
        {
            try { await completion.WaitAsync(Guard); throw new InvalidOperationException("Faulted worker succeeded."); }
            catch (IOException) { }
            Require(completion.IsFaulted);
        }
        else { await completion.WaitAsync(Guard); Require(completion.IsCompletedSuccessfully); }
    }
    private static async Task<JsonObject> ControlledAsync(string mode)
    {
        bool closing = mode.StartsWith("close-", StringComparison.Ordinal);
        bool writeHeld = mode == "close-write", admission = mode == "admission-fault";
        bool holdCallback = mode == "flush-failure-callback";
        var clock = new Clock();
        using var file = new Writer(holdFirstWrite: writeHeld, holdFirstFlush: !writeHeld, failFirstFlush: !closing && !admission);
        using var console = new Writer(holdFirstWrite: false);
        var options = new LoggingOptions { ConsoleLevel = closing ? CoreLogLevel.None : CoreLogLevel.Information, FileLevel = CoreLogLevel.Information, FilePath = "controlled", QueueCapacity = 3, MaxEntryBytes = 16384 };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, closing ? null : console.WriteAsync, file.WriteAsync, clock,
            consoleFlush: closing ? null : console.FlushAsync, fileFlush: file.FlushAsync);
        OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        Task completion = runtime.QueuedOutputs.Completion;
        Task? dispatch = null; LogFlushResult? result = null; JsonObject? heldJoin = null;
        try
        {
            queue.StartFileFlushTimer(clock, Interval);
            Accepted(runtime.CreateLogger("TimerLifecycle").Write(CoreLogLevel.Information, Data("record.1")));
            await file.FirstWrite.Task.WaitAsync(Guard);
            if (!writeHeld) { await UntilAsync(() => queue.CaptureAccounting().Written == 1); }
            OutputFlushRegistration original;
            lock (Field(queue, "admissionGate")!)
            {
                clock.Advance(Interval);
                original = Registrations(queue).Single();
                Require(original.AcceptedSequenceBoundary == 1 && !original.WriterFlushCompletion.IsCompleted);
            }
            if (!writeHeld) { await file.FirstFlush.Task.WaitAsync(Guard); }
            if (!closing)
            {
                Accepted(runtime.CreateLogger("TimerLifecycle").Write(CoreLogLevel.Information, Data("record.2")));
                Accepted(runtime.CreateLogger("TimerLifecycle").Write(CoreLogLevel.Information, Data("record.3")));
                await UntilAsync(() => runtime.QueuedOutputs.Console!.CaptureAccounting().Written == 3);
            }
            if (holdCallback)
            {
                clock.HoldCallback = true;
                dispatch = Task.Run(() => clock.Advance(Interval));
                await clock.CallbackEntered.Task.WaitAsync(Guard);
            }
            if (closing)
            {
                CloseUnderOuterGate(runtime, clock);
            }
            else if (admission)
            {
                // Seed only the internal ticket-exhaustion trigger; three physical accepts remain counted.
                typeof(OutputEventQueue).GetField("lastAcceptedSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue);
                lock (Field(queue, "admissionGate")!)
                {
                    Volatile.Write(ref clock.CloserInside, 1); CallerFact.Value = "caller-private";
                    try
                    {
                        LogWriteResult write = runtime.CreateLogger("TimerLifecycle").Write(CoreLogLevel.Information, Data("console.4"));
                        Require(write.Outcome == LogWriteOutcome.PartiallyAccepted && write.Outputs.Single(o => o.OutputName == "file").Status == LogOutputWriteStatus.Faulted);
                    }
                    finally { CallerFact.Value = null; Volatile.Write(ref clock.CloserInside, 0); }
                }
            }
            else { file.ReleaseFlush.TrySetResult(); }
            await clock.DisposalStarted.Task.WaitAsync(Guard);
            Task disposal = Disposal(queue)!;
            Require(disposal is not null && Field(queue, "fileFlushTimer") is null);
            Require(clock.PeriodicDisposals == 1 && clock.DisposalOutsideGate && clock.DisposalWithoutCaller && clock.PublishedAtDisposal);
            if (holdCallback)
            {
                Require(!disposal.IsCompleted && !queue.Completion.IsCompleted && clock.Running == 1 && clock.PendingJoins == 1);
                heldJoin = clock.Report();
            }
            JsonObject held = new()
            {
                ["sharedPending"] = !completion.IsCompleted,
                ["workerPending"] = !queue.Completion.IsCompleted,
                ["flushPending"] = !original.WriterFlushCompletion.IsCompleted,
                ["timerDetached"] = Field(queue, "fileFlushTimer") is null,
                ["controls"] = queue.RegisteredFlushCount,
                ["writer"] = file.Report(),
                ["timers"] = clock.Report(),
                ["diagnostics"] = ObserveDiagnostics(runtime)
            };
            if (closing || admission) { Require(!completion.IsCompleted && !original.WriterFlushCompletion.IsCompleted); }
            if (closing) { await CloseRepeatedAsync(runtime, clock, completion, disposal); }
            int fires = clock.Fires, calls = file.Flushes;
            clock.Advance(TimeSpan.FromMinutes(1));
            Require(clock.Fires == fires && file.Flushes == calls); // cancelled scheduling while old I/O stays held
            if (!closing)
            {
                LogOutputDiagnostics diagnosis = runtime.GetDiagnostics().Outputs.Single(o => o.OutputName == "file");
                Require(diagnosis.LastFaultCode == (admission ? "AdmissionExhausted" : "FlushFailed") && diagnosis.LastFaultAtUtc is not null);
                if (!admission)
                {
                    LogWriteResult continued = runtime.CreateLogger("TimerLifecycle").Write(CoreLogLevel.Information, Data("console.4"));
                    Require(continued.Outcome == LogWriteOutcome.PartiallyAccepted && continued.Outputs.Single(o => o.OutputName == "file").Status == LogOutputWriteStatus.Faulted);
                }
                await UntilAsync(() => runtime.QueuedOutputs.Console!.CaptureAccounting().Written == 4);
                OutputFlushBoundary healthy = runtime.QueuedOutputs.Console!.RegisterFlushBoundary();
                Require(await healthy.Registration.WriterFlushCompletion.WaitAsync(Guard));
                Require(!completion.IsCompleted); // healthy console remains live until global closure
                if (holdCallback) { clock.CallbackRelease.TrySetResult(); await dispatch!.WaitAsync(Guard); }
            }
            file.ReleaseAll();
            if (closing) { Require(await original.WriterFlushCompletion.WaitAsync(Guard)); }
            else { Require(await original.WriterFlushCompletion.WaitAsync(Guard) == admission); }
            await disposal.WaitAsync(Guard);
            await UntilAsync(() => queue.Completion.IsCompleted);
            if (!closing && !admission)
            {
                result = await runtime.FlushAsync(TimeSpan.FromMinutes(1), CancellationToken.None).WaitAsync(Guard);
                Require(!result.Succeeded && result.LossObserved && result.Completion == LogCompletion.Completed);
            }
            if (!closing) { await CloseRepeatedAsync(runtime, clock, completion, disposal); }
            await ObserveSharedCompletion(completion, fault: !closing && !admission);
            Require(queue.RegisteredFlushCount == 0 && clock.Active == 0);
            JsonObject report = Common(mode);
            report["held"] = held; report["heldJoin"] = heldJoin; report["result"] = result is null ? null : Result(result);
            report["writer"] = file.Report(); report["console"] = console.Report(); report["timers"] = clock.Report();
            report["diagnostics"] = ObserveDiagnostics(runtime); report["workerFaulted"] = queue.Completion.IsFaulted;
            report["sameSharedCompletion"] = ReferenceEquals(runtime.QueuedOutputs.Completion, completion);
            report["sameDisposal"] = ReferenceEquals(Disposal(queue), disposal); report["repeatedClosures"] = 64;
            report["sharedJoined"] = completion.IsCompleted; report["finalControls"] = queue.RegisteredFlushCount;
            return report;
        }
        finally
        {
            clock.CallbackRelease.TrySetResult(); file.ReleaseAll(); console.ReleaseAll(); runtime.CloseAdmission();
            if (dispatch is not null) { await dispatch.WaitAsync(Guard); }
            try { await completion.WaitAsync(Guard); } catch (IOException) { }
        }
    }
    private static bool LeaseHeld(string path)
    {
        Require(File.Exists(path + ".cis.lock"));
        try { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
    }
    private static async Task<JsonObject> PhysicalJoinAsync(string mode, string root)
    {
        string path = Path.Combine(root, "owned", "events.jsonl");
        var clock = new Clock { HoldCallback = true };
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.Information, FilePath = path, QueueCapacity = 3, MaxEntryBytes = 16384, FlushIntervalMilliseconds = 10 }, clock);
        OutputEventQueue queue = runtime.QueuedOutputs!.File!;
        Task completion = runtime.QueuedOutputs.Completion; Task? dispatch = null;
        try
        {
            Accepted(runtime.CreateLogger("TimerLifecycle").Write(CoreLogLevel.Information, Data("record.1")));
            await UntilAsync(() => queue.CaptureAccounting().Written == 1);
            dispatch = Task.Run(() => clock.Advance(Interval)); await clock.CallbackEntered.Task.WaitAsync(Guard);
            CloseUnderOuterGate(runtime, clock);
            await clock.DisposalStarted.Task.WaitAsync(Guard);
            Task disposal = Disposal(queue)!;
            Require(!disposal.IsCompleted && !completion.IsCompleted && clock.Running == 1 && clock.PendingJoins == 1);
            Require(LeaseHeld(path));
            JsonObject held = new()
            {
                ["sharedPending"] = !completion.IsCompleted,
                ["workerPending"] = !queue.Completion.IsCompleted,
                ["timerDetached"] = Field(queue, "fileFlushTimer") is null,
                ["ownershipHeld"] = LeaseHeld(path),
                ["controls"] = queue.RegisteredFlushCount,
                ["timers"] = clock.Report()
            };
            await CloseRepeatedAsync(runtime, clock, completion, disposal);
            clock.Advance(TimeSpan.FromMinutes(1)); Require(clock.Fires == 1);
            clock.CallbackRelease.TrySetResult(); await dispatch.WaitAsync(Guard); await completion.WaitAsync(Guard);
            Require(disposal.IsCompletedSuccessfully && !LeaseHeld(path) && queue.RegisteredFlushCount == 0);
            using (var active = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { Require(active.Length > 0); }
            JsonObject report = Common(mode); report["held"] = held; report["timers"] = clock.Report();
            report["fileBytes"] = Convert.ToBase64String(File.ReadAllBytes(path)); report["diagnostics"] = ObserveDiagnostics(runtime);
            report["ownershipReleased"] = !LeaseHeld(path); report["sameSharedCompletion"] = ReferenceEquals(runtime.QueuedOutputs.Completion, completion);
            report["sameDisposal"] = ReferenceEquals(Disposal(queue), disposal); report["repeatedClosures"] = 64;
            report["sharedJoined"] = completion.IsCompletedSuccessfully; report["finalControls"] = queue.RegisteredFlushCount;
            return report;
        }
        finally
        {
            clock.CallbackRelease.TrySetResult(); runtime.CloseAdmission();
            if (dispatch is not null) { await dispatch.WaitAsync(Guard); }
            await completion.WaitAsync(Guard);
        }
    }
    // Periodic callback has a real join, with separate dispatch and disposal barriers.
    private sealed class Clock : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<Timer> timers = [];
        private long ticks;
        internal bool HoldCallback, HoldArm, DisposalAfterCloser = true, SuppressedCreation = true, DisposalOutsideGate = true, DisposalWithoutCaller = true, PublishedAtDisposal = true;
        internal int Created, Disposals, PeriodicDisposals, Fires, Running, PendingJoins, SyncDisposeCalls, CloserInside;
        internal readonly TaskCompletionSource ArmEntered = Signal(), ArmRelease = Signal();
        internal readonly TaskCompletionSource CallbackEntered = Signal(), CallbackRelease = Signal(), DisposalStarted = Signal();
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (gate) { return ticks; } }
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 19, 20, 21, TimeSpan.Zero);
        internal int Active { get { lock (gate) { return timers.Count(t => !t.Disposed); } } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); lock (gate) { timers.Add(timer); Created++; }
            if (period != Timeout.InfiniteTimeSpan) { SuppressedCreation &= ExecutionContext.IsFlowSuppressed(); }
            // Core first creates a disarmed timer; its actual period is selected in Change.
            else if (state is OutputEventQueue) { SuppressedCreation &= ExecutionContext.IsFlowSuppressed(); }
            timer.Change(dueTime, period); return timer;
        }
        internal void Advance(TimeSpan elapsed)
        {
            Timer[] ready;
            lock (gate)
            {
                ticks += elapsed.Ticks; ready = timers.Where(t => !t.Disposed && t.Due <= ticks).ToArray();
                foreach (Timer timer in ready) { timer.Running++; Running++; Fires++; timer.Due = timer.Period == long.MaxValue ? long.MaxValue : ticks + timer.Period; }
            }
            foreach (Timer timer in ready)
            {
                try
                {
                    if (HoldCallback && timer.Period != long.MaxValue)
                    { CallbackEntered.TrySetResult(); Require(CallbackRelease.Task.Wait(Guard)); }
                    timer.Callback(timer.State);
                }
                finally
                {
                    lock (gate)
                    {
                        timer.Running--; Running--;
                        if (timer.Disposed && timer.Running == 0) { timer.Join.TrySetResult(); }
                    }
                }
            }
        }
        internal JsonObject Report()
        {
            lock (gate) { return new JsonObject { ["created"] = Created, ["disposed"] = Disposals, ["periodicDisposed"] = PeriodicDisposals, ["fires"] = Fires, ["active"] = Active, ["callbacks"] = Running, ["pendingJoins"] = PendingJoins, ["suppressedCreation"] = SuppressedCreation, ["outsideGate"] = DisposalOutsideGate, ["withoutCaller"] = DisposalWithoutCaller, ["published"] = PublishedAtDisposal, ["afterCloser"] = DisposalAfterCloser, ["syncDisposes"] = SyncDisposeCalls }; }
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal readonly object? State = state;
            internal long Due = long.MaxValue, Period = long.MaxValue;
            internal bool Disposed;
            internal int Running;
            internal readonly TaskCompletionSource Join = Signal();
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (clock.HoldArm && dueTime != Timeout.InfiniteTimeSpan)
                {
                    clock.ArmEntered.TrySetResult(); Require(clock.ArmRelease.Task.Wait(Guard));
                    lock (clock.gate) { if (Disposed) { throw new ObjectDisposedException(nameof(Timer)); } }
                }
                lock (clock.gate)
                {
                    if (Disposed) { return false; }
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks;
                    Period = period == Timeout.InfiniteTimeSpan ? long.MaxValue : period.Ticks; return true;
                }
            }
            public void Dispose()
            {
                lock (clock.gate) { clock.SyncDisposeCalls++; BeginDispose(); }
            }
            private void BeginDispose()
            {
                lock (clock.gate)
                {
                    if (Disposed) { return; }
                    Disposed = true; clock.Disposals++;
                    if (State is OutputEventQueue) { clock.PeriodicDisposals++; }
                    if (Running == 0) { Join.TrySetResult(); }
                }
            }
            public async ValueTask DisposeAsync()
            {
                if (State is OutputEventQueue queue)
                {
                    clock.DisposalAfterCloser &= Volatile.Read(ref clock.CloserInside) == 0;
                    clock.DisposalOutsideGate &= !Monitor.IsEntered(Field(queue, "admissionGate")!);
                    clock.DisposalWithoutCaller &= CallerFact.Value is null;
                    clock.PublishedAtDisposal &= (bool)Field(queue, "admissionClosed")! && queue.CaptureAccounting().State is LogOutputState.Stopping or LogOutputState.Faulted;
                }
                bool pending;
                lock (clock.gate)
                {
                    BeginDispose(); pending = !Join.Task.IsCompleted;
                    if (pending) { clock.PendingJoins++; }
                    if (State is OutputEventQueue) { clock.DisposalStarted.TrySetResult(); }
                }
                try { await Join.Task.ConfigureAwait(false); }
                finally { if (pending) { lock (clock.gate) { clock.PendingJoins--; } } }
            }
        }
    }
}
