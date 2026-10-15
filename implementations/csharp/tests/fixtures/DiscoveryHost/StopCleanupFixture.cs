using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class StopCleanupFixture
{
    internal static readonly string[] Modes = ["normal", "write-resource-failure", "flush-resource-failure", "write-timer-resource-failure", "resource-failure", "timer-failure", "callback-write-failure", "callback-held", "ownership-held", "deadline-cleanup-failure", "deadline-cleanup-held", "post-stop-flush", "primitive-close"];
    internal static int Run(string mode, string root)
    {
        _ = root;
        Console.WriteLine(ExecuteAsync(mode).GetAwaiter().GetResult().ToJsonString()); return 0;
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal));
        bool primaryWrite = mode is "write-resource-failure" or "write-timer-resource-failure" or "callback-write-failure";
        bool primaryFlush = mode == "flush-resource-failure";
        bool earlyFault = primaryFlush || primaryWrite && mode != "callback-write-failure";
        bool deadline = mode is "callback-write-failure" or "callback-held" or "ownership-held" or "deadline-cleanup-failure" or "deadline-cleanup-held";
        var gate = new object(); var clock = new Clock(mode); using var console = new MemoryStream();
        using var writer = new Writer(primaryWrite, primaryFlush, mode); var owner = new Owner(writer, gate, mode);
        var options = new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.Information, FilePath = Path.Combine(Path.GetTempPath(), "unused-kernel.jsonl"), QueueCapacity = 3, MaxEntryBytes = 16384, ShutdownTimeoutMilliseconds = 10 };
        var queues = new OutputQueues(3, true, true, item => console.WriteAsync(item.Event.JsonLine), item => writer.WriteAsync(item.Event.JsonLine, CancellationToken.None), gate,
            fileResource: owner, consoleFlush: () => new ValueTask(console.FlushAsync()), fileFlush: () => new ValueTask(writer.FlushAsync(CancellationToken.None)),
            fileCancellableWrite: (item, token) => writer.WriteAsync(item.Event.JsonLine, token), fileCancellableFlush: token => new ValueTask(writer.FlushAsync(token)), timeProvider: clock);
        var runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), options, queues.Console, queues.File, clock, gate, queues);
        queues.File!.StartFileFlushTimer(clock, TimeSpan.FromMilliseconds(1000));
        OutputFlushRegistration? originalFileFlush = null;
        int postFlushes = 0; bool postFlushSucceeded = true, inactiveFlushResolved = false, lateFlushCompleted = false; Task<LogFlushResult>? lateFlush = null; bool heldOwned = false, heldCallback = false; Task<LogStopResult>? stop = null;
        try
        {
            Accepted(runtime.CreateLogger("Cleanup").Write(CoreLogLevel.Information, Data("one")));
            await writer.WriteEntered.Task.WaitAsync(Guard);
            if (earlyFault)
            {
                writer.WriteRelease.TrySetResult();
                if (primaryFlush)
                {
                    await UntilAsync(() => queues.File.CaptureAccounting().Written == 1);
                    _ = queues.File.RegisterFlushBoundary(); await writer.FlushEntered.Task.WaitAsync(Guard); writer.FlushRelease.TrySetResult();
                }
                await Settled(queues.File.Completion);
                LogWriteResult healthy = runtime.CreateLogger("Cleanup").Write(CoreLogLevel.Information, Data("healthy"));
                Require(healthy.Outcome == LogWriteOutcome.PartiallyAccepted);
            }
            if (mode == "primitive-close")
            {
                runtime.CloseAdmission(); writer.WriteRelease.TrySetResult(); await Settled(queues.Completion);
            }
            stop = runtime.StopAsync(CancellationToken.None);
            if (deadline)
            {
                Require(owner.Calls == 0 && writer.InFlight);
                await queues.Console!.Completion.WaitAsync(Guard);
                if (mode == "deadline-cleanup-held") { originalFileFlush = Registrations(queues.File).Single(); Require(!originalFileFlush.WriterFlushCompletion.IsCompleted); }
                clock.Base.Advance(TimeSpan.FromMilliseconds(10));
            }
            else { writer.WriteRelease.TrySetResult(); writer.FlushRelease.TrySetResult(); }
            LogStopResult result = await stop.WaitAsync(Guard); string frozen = StopJoinFixture.Json(result).ToJsonString();
            if (deadline)
            {
                heldOwned = owner.Calls == 0 && !queues.File.Completion.IsCompleted; Require(heldOwned);
                if (mode == "deadline-cleanup-held")
                {
                    await clock.DeadlineDisposeEntered.Task.WaitAsync(Guard); clock.DeadlineDisposeRelease.TrySetResult();
                    await UntilAsync(() => runtime.GetDiagnostics().Outputs.All(o => o.LastFaultCode == "ShutdownTimerCleanupFailed"));
                    inactiveFlushResolved = originalFileFlush!.WriterFlushCompletion.IsCompletedSuccessfully && !originalFileFlush.WriterFlushCompletion.Result && queues.File.RegisteredFlushCount == 1; Require(inactiveFlushResolved);
                    Require(owner.Calls == 0 && writer.InFlight);
                    lateFlush = runtime.FlushAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                }
                if (mode is "callback-write-failure" or "callback-held") { await writer.CallbackEntered.Task.WaitAsync(Guard); }
                writer.WriteRelease.TrySetResult(); writer.FlushRelease.TrySetResult();
                if (mode == "callback-held")
                {
                    await writer.WriteFinished.Task.WaitAsync(Guard);
                    heldCallback = !queues.File.Completion.IsCompleted && owner.Calls == 0 && writer.CallbackInFlight; Require(heldCallback);
                    writer.CallbackRelease.TrySetResult();
                }
            }
            await Settled(queues.Completion);
            if (lateFlush is not null)
            {
                LogFlushResult later = await lateFlush.WaitAsync(Guard); postFlushes++; postFlushSucceeded = later.Succeeded;
                lateFlushCompleted = later.Completion == LogCompletion.Completed && later.Outputs.All(o => o.WriterFlushStatus == LogWriterFlushStatus.Failed);
                Require(lateFlushCompleted && !postFlushSucceeded);
            }
            if (mode == "deadline-cleanup-failure")
            {
                await clock.DeadlineDisposeEntered.Task.WaitAsync(Guard);
                Require(runtime.GetDiagnostics().Outputs.All(o => o.LastFaultCode is null));
                clock.DeadlineDisposeRelease.TrySetResult();
                await UntilAsync(() => runtime.GetDiagnostics().Outputs.All(o => o.LastFaultCode == "ShutdownTimerCleanupFailed"));
            }
            if (mode == "post-stop-flush")
            {
                for (int i = 0; i < 4; i++)
                {
                    LogFlushResult later = await runtime.FlushAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None).WaitAsync(Guard);
                    postFlushes++; postFlushSucceeded &= later.Succeeded;
                }
                Require(!postFlushSucceeded);
            }
            Task[] joins = Enumerable.Range(0, 64).Select(_ => runtime.DisposeAsync().AsTask()).ToArray();
            Require(joins.All(task => ReferenceEquals(task, stop))); await Task.WhenAll(joins).WaitAsync(Guard);
            Require(ReferenceEquals(result, runtime.GetDiagnostics().LastStopResult) && frozen == StopJoinFixture.Json(result).ToJsonString());
            await UntilAsync(() => clock.Base.Active == 0 && clock.DeadlineDisposals == (mode == "post-stop-flush" ? 5 : mode == "deadline-cleanup-held" ? 2 : 1));
            Exception[] errors = queues.File.Completion.Exception?.Flatten().InnerExceptions.ToArray() ?? [];
            Exception? primary = primaryWrite ? writer.WriteError : primaryFlush ? writer.FlushError : null;
            Exception[] expectedCleanup = new[] { owner.Throw ? owner.Error : null, clock.ThrowPeriodic ? clock.TimerError : null, mode == "callback-write-failure" ? writer.CallbackError : null }.OfType<Exception>().ToArray();
            Require(primary is null || errors.Contains(primary) && ReferenceEquals(errors[0], primary)); Require(expectedCleanup.All(errors.Contains));
            Require(owner.Calls == 1 && owner.GateFree && !owner.ActiveIo && !owner.ActiveCallback);
            JsonObject report = Common(mode); report["result"] = StopJoinFixture.Json(result); report["live"] = Diagnostics(runtime);
            report["file"] = writer.Report(); report["consoleBytes"] = Convert.ToBase64String(console.ToArray()); report["owner"] = owner.Report();
            report["timers"] = clock.Base.Report(); report["timerDisposeCalls"] = clock.PeriodicDisposals; report["deadlineDisposeCalls"] = clock.DeadlineDisposals;
            report["primaryPreserved"] = primary is null ? null : errors.Contains(primary); report["primaryFirst"] = primary is null ? null : ReferenceEquals(errors[0], primary); report["borrowedConsoleOpen"] = console.CanWrite; report["cleanupPreserved"] = expectedCleanup.All(errors.Contains);
            report["inactiveFlushResolved"] = inactiveFlushResolved; report["lateFlushCompleted"] = lateFlushCompleted; report["workerErrors"] = errors.Length; report["heldOwned"] = heldOwned; report["heldCallback"] = heldCallback;
            report["sameTask"] = true; report["immutable"] = true; report["disposeJoiners"] = joins.Length; report["postFlushes"] = postFlushes; report["postFlushSucceeded"] = postFlushSucceeded;
            Require(!report.ToJsonString().Contains("private-failure-details", StringComparison.Ordinal)); return report;
        }
        finally
        {
            writer.ReleaseAll(); clock.DeadlineDisposeRelease.TrySetResult(); queues.Complete(); await Settled(queues.Completion);
        }
    }
    private static async Task Settled(Task task)
    {
        try { await task.WaitAsync(Guard); }
        catch (Exception) when (task.IsFaulted || task.IsCanceled) { }
    }
    private sealed class Writer(bool failWrite, bool failFlush, string mode) : IDisposable
    {
        private readonly MemoryStream bytes = new(); private CancellationTokenRegistration registration;
        internal readonly TaskCompletionSource WriteEntered = Signal(), WriteRelease = Signal(), WriteFinished = Signal(), FlushEntered = Signal(), FlushRelease = Signal(), CallbackEntered = Signal(), CallbackRelease = Signal();
        internal readonly IOException WriteError = new("private-failure-details"), FlushError = new("private-failure-details"), CallbackError = new("private-failure-details");
        internal volatile bool InFlight, CallbackInFlight;
        private int writes, written, flushes, flushed, closed;
        internal async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            Interlocked.Increment(ref writes); InFlight = true;
            if (mode is "callback-held" or "callback-write-failure")
            {
                registration = token.Register(() =>
                {
                    CallbackInFlight = true; CallbackEntered.TrySetResult();
                    try { if (mode == "callback-held") { Require(CallbackRelease.Task.Wait(Guard)); } else { throw CallbackError; } }
                    finally { CallbackInFlight = false; }
                });
            }
            WriteEntered.TrySetResult();
            try { await WriteRelease.Task; if (failWrite) { throw WriteError; } await bytes.WriteAsync(data, CancellationToken.None); Interlocked.Increment(ref written); }
            finally { InFlight = false; WriteFinished.TrySetResult(); }
        }
        internal async Task FlushAsync(CancellationToken token)
        {
            _ = token; Interlocked.Increment(ref flushes); InFlight = true; FlushEntered.TrySetResult();
            try { await FlushRelease.Task; if (failFlush) { throw FlushError; } await bytes.FlushAsync(); Interlocked.Increment(ref flushed); }
            finally { InFlight = false; }
        }
        internal void CloseOwned() { Interlocked.Increment(ref closed); bytes.Dispose(); }
        internal void ReleaseAll() { WriteRelease.TrySetResult(); FlushRelease.TrySetResult(); CallbackRelease.TrySetResult(); }
        internal JsonObject Report() => new() { ["writes"] = writes, ["written"] = written, ["flushes"] = flushes, ["flushed"] = flushed, ["closed"] = closed, ["bytes"] = Convert.ToBase64String(bytes.ToArray()) };
        public void Dispose() { registration.Dispose(); bytes.Dispose(); }
    }
    private sealed class Owner(Writer writer, object gate, string mode) : IDisposable
    {
        internal int Calls; internal bool GateFree = true, ActiveIo, ActiveCallback;
        internal bool Throw => mode is "write-resource-failure" or "flush-resource-failure" or "write-timer-resource-failure" or "resource-failure";
        internal readonly IOException Error = new("private-failure-details");
        public void Dispose()
        {
            Calls++; GateFree &= !Monitor.IsEntered(gate); ActiveIo |= writer.InFlight; ActiveCallback |= writer.CallbackInFlight;
            writer.CloseOwned(); if (Throw) { throw Error; }
        }
        internal JsonObject Report() => new() { ["calls"] = Calls, ["gateFree"] = GateFree, ["activeIo"] = ActiveIo, ["activeCallback"] = ActiveCallback };
    }
    private sealed class Clock(string mode) : TimeProvider
    {
        internal readonly FileTimerCoordinationFixture.Clock Base = new();
        internal readonly IOException TimerError = new("private-failure-details"), DeadlineError = new("private-failure-details");
        internal readonly TaskCompletionSource DeadlineDisposeEntered = Signal(), DeadlineDisposeRelease = Signal();
        internal int PeriodicDisposals, DeadlineDisposals;
        internal bool ThrowPeriodic => mode is "write-timer-resource-failure" or "timer-failure";
        internal bool HoldDeadline => mode is "deadline-cleanup-failure" or "deadline-cleanup-held";
        public override long TimestampFrequency => Base.TimestampFrequency;
        public override long GetTimestamp() => Base.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => Base.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer(this, Base.CreateTimer(callback, state, dueTime, period));
        private sealed class Timer(Clock clock, ITimer inner) : ITimer
        {
            private bool periodic;
            public bool Change(TimeSpan dueTime, TimeSpan period) { periodic = period > TimeSpan.Zero; return inner.Change(dueTime, period); }
            public void Dispose() => inner.Dispose();
            public async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync();
                if (periodic) { Interlocked.Increment(ref clock.PeriodicDisposals); if (clock.ThrowPeriodic) { throw clock.TimerError; } }
                else
                {
                    int attempt = Interlocked.Increment(ref clock.DeadlineDisposals);
                    if (clock.HoldDeadline && attempt == 1)
                    {
                        clock.DeadlineDisposeEntered.TrySetResult(); await clock.DeadlineDisposeRelease.Task; throw clock.DeadlineError;
                    }
                }
            }
        }
    }
}
