using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingLifecycleTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static CoreLogRuntime Create(Writer writer, TimeProvider? clock = null, Task? start = null,
        int capacity = 3, LogProcessingOptions? processing = null, int timeout = 100)
    {
        Task Launch(Func<Task> worker)
        {
            bool input = worker.Target is IncomingEventQueue;
            return Task.Run(async () => { if (input && start is not null) { await start; } await worker(); });
        }
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.None,
            FileLevel = CoreLogLevel.None,
            QueueCapacity = capacity,
            ShutdownTimeoutMilliseconds = timeout,
            IncomingScanPercentage = 100,
            IncomingTraceScanPercentage = null,
            IncomingDebugScanPercentage = null,
            IncomingInformationScanPercentage = null,
            IncomingWarningScanPercentage = null,
            IncomingErrorScanPercentage = null,
            IncomingCriticalScanPercentage = null
        }, clock, launchWorker: Launch, writers: [new("writer", writer, ownsWriter: true)], incomingPipeline: true, processing: processing);
    }
    private static IncomingEventQueue Queue(CoreLogRuntime runtime) => (IncomingEventQueue)typeof(CoreLogRuntime)
        .GetField("incoming", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "publishers")]
    private static extern ref long PublisherCounter(IncomingRingBuffer ring);

    [TestMethod]
    public async Task IdleLongPollCannotDelayFlushOrStopControls()
    {
        var writer = new Writer();
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.None,
            FileLevel = CoreLogLevel.None,
            IncomingPollIntervalMilliseconds = 60000,
            ShutdownTimeoutMilliseconds = 100
        }, writers: [new("writer", writer, ownsWriter: true)], incomingPipeline: true,
            // Each empty worker reaches its first asynchronous wait before creation returns.
            launchWorker: worker => worker());
        LogFlushResult flushed = await runtime.FlushAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None).WaitAsync(Limit);
        Assert.AreEqual(LogCompletion.Completed, flushed.Completion);
        Assert.IsTrue(flushed.Succeeded); Assert.AreEqual(0L, flushed.FrontendBatch!.TargetCount);
        LogStopResult stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
        Assert.AreEqual(LogCompletion.Completed, stopped.Completion); Assert.IsTrue(stopped.Succeeded);
        await runtime.Completion.WaitAsync(Limit); Assert.AreEqual(1, writer.Disposals);
    }

    [TestMethod]
    public async Task RejectedEnrollmentAfterFinalPassCannotTurnCompletedStopIntoTimeout()
    {
        var writer = new Writer(); var runtime = Create(writer, timeout: 30000);
        IncomingEventQueue queue = Queue(runtime);
        var ring = (IncomingRingBuffer)Field(queue, "ring")!;
        using var clock = new DrainCaptureClock(() => (bool)Field(queue, "drained")!);
        typeof(IncomingEventQueue).GetField("clock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, clock);
        bool registered = false;
        try
        {
            Assert.IsFalse(ring.IsClosed, "The staged attempt observed Running before pausing ahead of its increment.");
            Task<LogStopResult> stopping = runtime.StopAsync(CancellationToken.None);
            Assert.IsTrue(clock.Entered.Wait(Limit), "The consumer reached its closed/zero/final-pass frontier.");
            // Resume only the registration increment of that earlier attempt. Its failed
            // closed-state recheck cannot touch slots. The actual counter remains observable.
            Interlocked.Increment(ref PublisherCounter(ring)); registered = true;
            Assert.IsTrue(ring.IsClosed); Assert.AreEqual(1L, ring.ActivePublishers);
            clock.Release.Set();
            LogStopResult result = await stopping.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.Completed, result.Completion);
            Assert.IsTrue(result.Succeeded); Assert.IsNull(result.ErrorCode);
            Assert.AreEqual(1L, result.Incoming!.ActivePublishers, "Do not conceal the independently observed registration gauge.");
            Assert.AreEqual(0L, result.Incoming.Admitted); Assert.AreEqual(0L, result.Incoming.Waiting);
            await runtime.Completion.WaitAsync(Limit); Assert.AreEqual(1, writer.Disposals);
            ring.ExitPublisher(); registered = false;
            Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.ActivePublishers);
            Assert.AreEqual(1L, result.Incoming.ActivePublishers, "The returned observation remains frozen.");
        }
        finally { if (registered) { ring.ExitPublisher(); } clock.Release.Set(); await runtime.DisposeAsync(); }
    }

    private sealed class DrainCaptureClock(Func<bool> drained) : TimeProvider, IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new();
        internal ManualResetEventSlim Release { get; } = new();
        private int held;
        public override long GetTimestamp()
        {
            if (drained() && Interlocked.Exchange(ref held, 1) == 0)
            { Entered.Set(); if (!Release.Wait(Limit)) { throw new TimeoutException("Drain capture was not released."); } }
            return base.GetTimestamp();
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }

    [TestMethod]
    public async Task NoCaptureTimeoutIsDistinctFromCapturedEmptyBatchAndStaysFrozen()
    {
        var start = Signal(); var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new Writer(); var runtime = Create(writer, clock, start.Task);
        try
        {
            runtime.CreateLogger("Boundary").Info("retained");
            Task<LogFlushResult> wait = runtime.FlushAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(100)); LogFlushResult timed = await wait.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, timed.Completion); Assert.IsNull(timed.FrontendBatch);
            Assert.AreEqual(LogFrontendCaptureStatus.NoBatchCaptured, timed.FrontendCaptureStatus);
            Assert.HasCount(0, timed.Outputs); Assert.IsNull(Field(Queue(runtime), "pending"));
            Assert.AreEqual(1L, timed.Incoming!.Waiting);
            start.TrySetResult(); LogFlushResult drained = await runtime.FlushAsync(Limit, CancellationToken.None).WaitAsync(Limit);
            Assert.IsTrue(drained.Succeeded);
            LogFlushResult empty = await runtime.FlushAsync(Limit, CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(LogFrontendCaptureStatus.Captured, empty.FrontendCaptureStatus);
            Assert.AreEqual(0L, empty.FrontendBatch!.TargetCount); Assert.IsTrue(empty.FrontendBatch.IsLossless);
            Assert.IsNull(timed.FrontendBatch); Assert.AreEqual(1L, timed.Incoming.Waiting);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task FixedBatchReportsStaleTargetsWithoutDoubleCountingAndLaterTrafficCannotExtendIt()
    {
        var start = Signal(); var entered = Signal(); var release = Signal();
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); var writer = new Writer();
        var runtime = Create(writer, clock, start.Task, processing: new()
        {
            Interceptors = [value =>
            { if (value.Message == "one") { entered.TrySetResult(); release.Task.WaitAsync(Limit).GetAwaiter().GetResult(); } return new(); }]
        });
        try
        {
            var logger = runtime.CreateLogger("Batch"); logger.Info("one"); logger.Info("two"); logger.Info("three");
            Task<LogFlushResult> shortWait = runtime.FlushAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            Task<LogFlushResult> fullWait = runtime.FlushAsync(Limit, CancellationToken.None);
            start.TrySetResult(); await entered.Task.WaitAsync(Limit);
            logger.Info("four"); logger.Info("five");
            clock.Advance(TimeSpan.FromMilliseconds(100)); LogFlushResult partial = await shortWait.WaitAsync(Limit);
            Assert.AreEqual(3L, partial.FrontendBatch!.TargetCount); Assert.AreEqual(1L, partial.FrontendBatch.Consumed);
            Assert.AreEqual(2L, partial.FrontendBatch.Pending);
            Assert.AreEqual(0L, partial.FrontendBatch.Evicted, "A stale target remains pending until classified.");
            release.TrySetResult(); LogFlushResult full = await fullWait.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.Completed, full.Completion); Assert.IsFalse(full.Succeeded); Assert.IsTrue(full.LossObserved);
            Assert.AreEqual(3L, full.FrontendBatch!.TargetCount); Assert.AreEqual(2L, full.FrontendBatch.Consumed);
            Assert.AreEqual(1L, full.FrontendBatch.Evicted); Assert.AreEqual(0L, full.FrontendBatch.Pending);
            Assert.AreEqual(2L, full.Outputs.Single().AcceptedSequenceBoundary, "Later batches cannot extend writer coverage.");
            Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.Evicted, "Stale classification adds no lifetime eviction.");
            Assert.AreEqual(2L, partial.FrontendBatch.Pending, "The timeout snapshot stays immutable.");
            LogFlushResult later = await runtime.FlushAsync(Limit, CancellationToken.None).WaitAsync(Limit);
            Assert.IsTrue(later.Succeeded); Assert.IsTrue(later.LossObserved);
        }
        finally { start.TrySetResult(); release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task CanceledCoalescedWaiterDetachesWithoutCancelingTheOtherWaiters()
    {
        var start = Signal(); var writer = new Writer(); var runtime = Create(writer, start: start.Task);
        using var cancellation = new CancellationTokenSource();
        try
        {
            runtime.CreateLogger("Shared").Info("one");
            Task<LogFlushResult> first = runtime.FlushAsync(Limit, CancellationToken.None);
            Task<LogFlushResult> canceled = runtime.FlushAsync(Limit, cancellation.Token);
            Task<LogFlushResult> second = runtime.FlushAsync(Limit, CancellationToken.None);
            cancellation.Cancel(); LogFlushResult detached = await canceled.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.Canceled, detached.Completion); Assert.AreEqual("FlushCanceled", detached.ErrorCode);
            Assert.IsFalse(first.IsCompleted); Assert.IsFalse(second.IsCompleted);
            start.TrySetResult(); LogFlushResult one = await first.WaitAsync(Limit), two = await second.WaitAsync(Limit);
            Assert.IsTrue(one.Succeeded); Assert.IsTrue(two.Succeeded);
            Assert.AreEqual(one.FrontendBatch!.TargetCount, two.FrontendBatch!.TargetCount);
            Assert.AreEqual(1L, one.FrontendBatch.TargetCount); Assert.AreEqual(1, writer.Lines.Count);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task RepeatedExpiredRequestsRetireTheirPendingGroupWithoutPayloadSnapshots()
    {
        var start = Signal(); var runtime = Create(new Writer(), start: start.Task);
        try
        {
            runtime.CreateLogger("Retirement").Info("retained");
            for (int index = 0; index < 100; index++)
            {
                LogFlushResult result = await runtime.FlushAsync(TimeSpan.FromTicks(1), CancellationToken.None);
                Assert.AreEqual(LogCompletion.TimedOut, result.Completion); Assert.IsNull(result.FrontendBatch);
                Assert.IsNull(Field(Queue(runtime), "pending"));
            }
            Assert.IsNull(Field(Queue(runtime), "current")); Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.Waiting);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task EvictedOccurrenceDoesNotReserveADuplicateKeyBeforeItsLaterRetry()
    {
        var start = Signal(); var writer = new Writer(); var keys = new ConcurrentQueue<string>();
        var runtime = Create(writer, start: start.Task, capacity: 1, timeout: 5000, processing: new()
        { DuplicateSuppression = new(value => { keys.Enqueue(value.EventName); return value.EventName; }, TimeSpan.FromMinutes(1)) });
        try
        {
            var logger = runtime.CreateLogger("Duplicate");
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("retry", "first occurrence").Outcome);
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("survivor", "replacement").Outcome);
            Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.Evicted);
            Assert.AreEqual(0, keys.Count, "The paused consumer has not run any duplicate policy.");
            start.TrySetResult();
            Assert.IsTrue((await runtime.FlushAsync(Limit, CancellationToken.None).WaitAsync(Limit)).Succeeded);
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("retry", "second occurrence").Outcome);
            Assert.IsTrue((await runtime.FlushAsync(Limit, CancellationToken.None).WaitAsync(Limit)).Succeeded);
            CollectionAssert.AreEqual(new[] { "survivor", "retry" }, keys.ToArray());
            Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.DuplicateSuppressed);
            Assert.AreEqual(2, writer.Lines.Count);
            Assert.IsTrue(writer.Lines.Last().Contains("second occurrence", StringComparison.Ordinal));
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task FirstCanceledStopStillClosesAndTimeoutDoesNotInventAbandonmentBeforeCleanup()
    {
        var start = Signal(); var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); var writer = new Writer();
        int keys = 0;
        var runtime = Create(writer, clock, start.Task, processing: new()
        { DuplicateSuppression = new(value => { keys++; return value.EventName; }, TimeSpan.FromMinutes(1)) });
        try
        {
            var logger = runtime.CreateLogger("Close"); logger.Info("waiting");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            LogStopResult detached = await runtime.StopAsync(canceled.Token);
            Assert.AreEqual(LogCompletion.Canceled, detached.Completion);
            Assert.AreEqual(LogWriteOutcome.Stopped, logger.Info("closed").Outcome);
            Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None);
            Assert.AreSame(shared, runtime.StopAsync(CancellationToken.None));
            Assert.AreSame(shared, runtime.DisposeAsync().AsTask());
            clock.Advance(TimeSpan.FromMilliseconds(200)); LogStopResult timed = await shared.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, timed.Completion); Assert.AreEqual(0L, timed.Incoming!.Abandoned);
            Assert.AreEqual(1L, timed.Incoming.Waiting); Assert.IsFalse(runtime.Completion.IsCompleted);
            start.TrySetResult(); await runtime.Completion.WaitAsync(Limit);
            Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.Abandoned); Assert.AreEqual(0, writer.Lines.Count);
            Assert.AreEqual(0, keys, "An abandoned occurrence never reaches duplicate-key selection or reservation.");
            Assert.AreSame(timed, await runtime.StopAsync(CancellationToken.None)); Assert.AreSame(timed, runtime.GetDiagnostics().LastStopResult);
            Assert.AreEqual(0L, timed.Incoming.Abandoned);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task PublisherPausedPastStopTimeoutCanPublishLateAndIsSafelyAbandoned()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); var writer = new Writer();
        var runtime = Create(writer, clock);
        var ring = (IncomingRingBuffer)Field(Queue(runtime), "ring")!;
        CapturedEventFacts facts = EventProjection.Capture(new EventMetadata(), new OperationScopeStore().Capture(), "Late",
            CoreLogLevel.Information, new LogEventData { EventName = "late", Message = "owned" }, clock)!;
        var item = new IncomingEvent(42, CoreLogLevel.Information, facts);
        Assert.IsTrue(ring.TryEnterPublisher()); bool enrolled = true;
        int slot = ring.SelectSlot(item, out int probes);
        try
        {
            Task<LogStopResult> stop = runtime.StopAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(200)); LogStopResult timed = await stop.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, timed.Completion); Assert.AreEqual(1L, timed.Incoming!.ActivePublishers);
            Assert.AreEqual(0L, timed.Incoming.Admitted); Assert.AreEqual(0L, timed.Incoming.Abandoned);
            Assert.IsFalse(runtime.Completion.IsCompleted); Assert.AreEqual(0, writer.Disposals);
            ring.Exchange(slot, item, probes); ring.ExitPublisher(); enrolled = false;
            await runtime.Completion.WaitAsync(Limit);
            LogIncomingSnapshot settled = runtime.GetDiagnostics().Incoming!;
            Assert.AreEqual(1L, settled.Admitted); Assert.AreEqual(1L, settled.Abandoned);
            Assert.AreEqual(0L, settled.Consumed); Assert.AreEqual(0L, settled.ActivePublishers);
            Assert.AreEqual(0, writer.Lines.Count); Assert.AreEqual(1, writer.Disposals);
        }
        finally { if (enrolled) { ring.ExitPublisher(); } await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task ActiveBackendPreparationIsNotAbandonedAndOwnedWritersSurviveUntilItFinishes()
    {
        var entered = Signal(); var release = Signal(); var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var writer = new Writer(); var runtime = Create(writer, clock, processing: new()
        { Interceptors = [_ => { entered.TrySetResult(); release.Task.WaitAsync(Limit).GetAwaiter().GetResult(); return new(); }] });
        try
        {
            runtime.CreateLogger("Active").Info("owned"); await entered.Task.WaitAsync(Limit);
            Task<LogStopResult> stop = runtime.StopAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(200)); LogStopResult timed = await stop.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, timed.Completion); Assert.AreEqual(1L, timed.Incoming!.Consumed);
            Assert.AreEqual(0L, timed.Incoming.Abandoned); Assert.AreEqual(1L, timed.Incoming.Pending);
            Assert.AreEqual(0, writer.Disposals); Assert.IsFalse(runtime.Completion.IsCompleted);
            release.TrySetResult(); await runtime.Completion.WaitAsync(Limit);
            Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.Abandoned); Assert.AreEqual(1, writer.Disposals);
            Assert.AreEqual(0, writer.Lines.Count); Assert.AreEqual(1L, timed.Incoming.Pending);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task InvalidTimeoutAndTimerFailureProduceExplicitUnfaultedResults()
    {
        var runtime = Create(new Writer(), new BadTimerClock());
        try
        {
            LogFlushResult invalid = await runtime.FlushAsync(TimeSpan.Zero, CancellationToken.None);
            Assert.AreEqual(LogCompletion.Failed, invalid.Completion); Assert.AreEqual("InvalidFlushTimeout", invalid.ErrorCode);
            LogFlushResult failed = await runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.AreEqual(LogCompletion.Failed, failed.Completion); Assert.AreEqual("FlushFailed", failed.ErrorCode);
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.Failed, stop.Completion); Assert.AreEqual("StopFailed", stop.ErrorCode);
            await runtime.Completion.WaitAsync(Limit);
        }
        finally { await runtime.DisposeAsync(); }
    }

    private sealed class BadTimerClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new InvalidOperationException("private timer detail");
    }

    [TestMethod]
    public async Task StopWaitsForPublisherBookkeepingEvenAfterItsOccurrenceWasRemoved()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); var writer = new Writer();
        var runtime = Create(writer, clock);
        var ring = (IncomingRingBuffer)Field(Queue(runtime), "ring")!;
        CapturedEventFacts facts = EventProjection.Capture(new EventMetadata(), new OperationScopeStore().Capture(), "Bookkeeping",
            CoreLogLevel.Information, new LogEventData { EventName = "owned", Message = "owned" }, clock)!;
        var item = new IncomingEvent(42, CoreLogLevel.Information, facts);
        using var exchanged = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Task producer = Task.Run(() =>
        {
            Assert.IsTrue(ring.TryEnterPublisher());
            try
            {
                int slot = ring.SelectSlot(item, out int probes);
                ring.Exchange(slot, item, probes); exchanged.Set();
                Assert.IsTrue(release.Wait(Limit));
            }
            finally { ring.ExitPublisher(); }
        });
        try
        {
            Assert.IsTrue(exchanged.Wait(Limit));
            await writer.Written.Task.WaitAsync(Limit);
            Task<LogStopResult> stopping = runtime.StopAsync(CancellationToken.None);
            Assert.IsFalse(stopping.IsCompleted); Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.ActivePublishers);
            Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.Waiting);
            release.Set(); await producer.WaitAsync(Limit);
            LogStopResult result = await stopping.WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.Completed, result.Completion); Assert.AreEqual(0L, result.Incoming!.ActivePublishers);
            Assert.AreEqual(1L, result.Incoming.Admitted); Assert.AreEqual(1L, result.Incoming.Consumed);
        }
        finally { release.Set(); await producer.WaitAsync(Limit); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task WorkerFailureClosesPublicInterestAndIsVisibleInLiveDiagnostics()
    {
        using var clock = new ToggleClock(); var runtime = Create(new Writer(), clock);
        try
        {
            var logger = runtime.CreateLogger("FaultState");
            Assert.IsTrue(clock.FirstTimestamp.Wait(Limit), "The idle backend starts before injecting the frontend clock fault.");
            clock.Broken = true;
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("owned").Outcome);
            await Queue(runtime).Completion.WaitAsync(Limit);
            clock.Broken = false;
            CoreLogDiagnostics diagnostics = runtime.GetDiagnostics();
            Assert.IsTrue(diagnostics.AdmissionClosed); Assert.AreEqual(CoreLogState.Stopping, diagnostics.State);
            Assert.AreEqual("IncomingWorkerFailed", diagnostics.Incoming!.ErrorCode);
            Assert.IsFalse(logger.IsEnabled(CoreLogLevel.Information)); Assert.AreEqual(LogWriteOutcome.Stopped, logger.Info("closed").Outcome);
            LogStopResult stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.Failed, stopped.Completion);
            Assert.AreEqual("IncomingWorkerFailed", stopped.ErrorCode);
        }
        finally { clock.Broken = false; await runtime.DisposeAsync(); }
    }

    private sealed class ToggleClock : TimeProvider, IDisposable
    {
        private int broken;
        internal ManualResetEventSlim FirstTimestamp { get; } = new();
        internal bool Broken { set => Volatile.Write(ref broken, value ? 1 : 0); }
        public override long GetTimestamp()
        {
            bool fail = Volatile.Read(ref broken) != 0;
            FirstTimestamp.Set();
            return fail ? throw new InvalidOperationException("private clock detail") : base.GetTimestamp();
        }
        public void Dispose() => FirstTimestamp.Dispose();
    }

    [TestMethod]
    public async Task ExpiredBackendRejectsLateHandoffEvenWithAStalePreExpirySample()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        int writes = 0;
        var queue = new OutputEventQueue(1, _ => { writes++; return ValueTask.CompletedTask; }, timeProvider: clock);
        try
        {
            CapturedEventFacts facts = EventProjection.Capture(new EventMetadata(), new OperationScopeStore().Capture(), "LateHandoff",
                CoreLogLevel.Information, new LogEventData { EventName = "late", Message = "owned" }, clock)!;
            EncodedEvent encoded = EventEncoder.Encode(facts, 65536);
            long beforeExpiry = clock.GetTimestamp();
            queue.SetStopBudget(new(beforeExpiry, TimeSpan.FromMilliseconds(100), clock.TimestampFrequency));
            clock.Advance(TimeSpan.FromMilliseconds(200)); queue.AbandonUnstarted(clock.GetTimestamp());
            Assert.AreEqual(LogOutputWriteStatus.Faulted, queue.TryAdmit(encoded, null, out long? ticket, beforeExpiry));
            Assert.IsNull(ticket); Assert.AreEqual(0L, queue.CaptureAccounting().Accepted);
            Assert.AreEqual(1L, queue.CaptureAccounting().Unavailable);
        }
        finally { queue.Complete(); await queue.Completion.WaitAsync(Limit); }
        Assert.AreEqual(0, writes);
    }
    private sealed class Writer : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        internal TaskCompletionSource Written { get; } = Signal();
        internal int Disposals;
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        { Lines.Enqueue(jsonLine); Written.TrySetResult(); return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
}
