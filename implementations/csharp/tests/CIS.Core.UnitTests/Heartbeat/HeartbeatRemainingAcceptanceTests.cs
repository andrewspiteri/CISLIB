using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatRemainingAcceptanceTests
{
    [TestMethod]
    public async Task OneHundredEligibleRegistrationsKeepExactlySixteenUnfinishedWrappersAndReuseOneSlotAtATime()
    {
        await using var scope = new HeartbeatTestScope(capacity: 16);
        TaskCompletionSource<bool>[] releases = Enumerable.Range(0, 100).Select(_ => scope.HoldResult()).ToArray();
        HeartbeatProbe[] subscribers = releases.Select(release => new HeartbeatProbe { Callback = _ => release.Task }).ToArray();
        HeartbeatRecordingObserver[] observations = subscribers.Select(subscriber => scope.Register(subscriber)).ToArray();
        scope.Activate(posted: 16);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.Posts == 16, "The sixteen accepted post operations did not finish publishing.");
        Assert.AreEqual(new HeartbeatSchedulerCounts(100, 100, 16, 84), scope.Runtime.Inspect());
        Assert.AreEqual(16, scope.Executor.Posts);
        Assert.AreEqual(100, scope.Owner.Holds, "Pending admission must protect owners before capacity becomes available.");
        scope.Executor.RunAvailable();
        HeartbeatTestScope.WaitUntil(() => subscribers.Take(16).All(subscriber => subscriber.Calls == 1),
            "All sixteen reserved wrappers must be independently executable.");
        Assert.IsTrue(subscribers.Skip(16).All(subscriber => subscriber.Calls == 0));
        Assert.AreEqual(16, scope.Runtime.Inspect().Dispatched);
        Assert.AreEqual(16, scope.Executor.Posts);

        releases[7].TrySetResult(true);
        scope.WaitSettled(observations[7]);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.Posts == 17 && scope.Executor.PendingCount == 1,
            "A single free slot must dispatch exactly the oldest pending registration without waiting for the other fifteen.");
        Assert.AreEqual(new HeartbeatSchedulerCounts(100, 99, 16, 83), scope.Runtime.Inspect());
        Assert.AreEqual(99, scope.Owner.Holds);
        Assert.IsFalse(releases[0].Task.IsCompleted);
        scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => subscribers[16].Calls == 1, "FIFO slot reuse did not select the oldest pending execution.");
        Assert.IsTrue(subscribers.Skip(17).All(subscriber => subscriber.Calls == 0));
        Assert.AreEqual(17, scope.Executor.Posts);
        Assert.AreEqual(16, scope.Runtime.Inspect().Dispatched);
        // The scope releases every held task and drains every posted wrapper on
        // both success and assertion failure; no recurring timer is advanced.
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AdmissionAndGlobalCutoffHaveAProtectedWinnerInBothControlledOrders(bool admissionWins)
    {
        await using var scope = new AdmissionRaceScope();
        var subscriber = new HeartbeatProbe();
        var observer = new HeartbeatRecordingObserver();
        scope.Runtime.Register(scope.Owner, subscriber,
            new() { InitialDelay = TimeSpan.FromMilliseconds(1) }, observerFactory: _ => observer);
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Clock.TimerChanges > 0, "The initial eligibility timer was not armed.");

        if (admissionWins)
        {
            scope.Clock.PauseNextTimestampRead();
            scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await scope.Clock.Entered.Task.WaitAsync(HeartbeatTestScope.Deadline);
            bool unexpectedlyAcquired = Monitor.TryEnter(scope.Runtime.Gate);
            if (unexpectedlyAcquired) { Monitor.Exit(scope.Runtime.Gate); }
            Assert.IsFalse(unexpectedlyAcquired, "Admission timing must be paused while the scheduler owns its coordination gate.");

            var stopAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cutoffEstablished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task stopper = Task.Run(async () =>
            {
                stopAttempted.TrySetResult();
                Task barrier = scope.Runtime.StopAsync();
                cutoffEstablished.TrySetResult();
                await barrier;
            });
            await stopAttempted.Task.WaitAsync(HeartbeatTestScope.Deadline);
            Assert.IsFalse(cutoffEstablished.Task.IsCompleted,
                "Shutdown cannot overtake an admission transition already holding the coordination gate.");
            scope.Clock.Release.TrySetResult();
            await cutoffEstablished.Task.WaitAsync(HeartbeatTestScope.Deadline);
            HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1,
                "The winning admission must leave exactly one tracked posted wrapper.");
            Assert.AreEqual(1, observer.Count(HeartbeatEvent.Admitted));
            Assert.AreEqual(new HeartbeatSchedulerCounts(1, 1, 1, 0), scope.Runtime.Inspect());
            Assert.AreEqual(1, scope.Owner.Holds);
            Assert.IsFalse(stopper.IsCompleted);
            Assert.AreEqual(0, subscriber.Calls);
            scope.Executor.RunNext();
            await stopper.WaitAsync(HeartbeatTestScope.Deadline);
            Assert.AreEqual(1, observer.Count(HeartbeatEvent.Suppressed));
            Assert.AreEqual(1, observer.Count(HeartbeatEvent.Settled));
        }
        else
        {
            Task stop = scope.Runtime.StopAsync();
            scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await stop.WaitAsync(HeartbeatTestScope.Deadline);
            Assert.AreEqual(0, scope.Executor.Posts);
            Assert.AreEqual(0, observer.Count(HeartbeatEvent.Admitted));
            Assert.AreEqual(0, observer.Count(HeartbeatEvent.Suppressed));
            Assert.AreEqual(0, observer.Count(HeartbeatEvent.Settled));
        }

        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(0, observer.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Retired));
        Assert.AreEqual(1, observer.Disposals);
        Assert.AreEqual(0, scope.Owner.Holds);
        Assert.AreEqual(new HeartbeatSchedulerCounts(), scope.Runtime.Inspect());
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
    }

    [TestMethod]
    public async Task ARealExternalMonitorAndRetainedFailedSnapshotCannotRootTheSubscriberOrItsExceptionGraph()
    {
        await using var scope = new HeartbeatTestScope();
        await using var health = new HealthcheckService(new HealthcheckOptions(), timeProvider: scope.Clock);
        await using IConsecutiveFailureMonitor monitor = health.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        FailedGraph graph = RunFailingRegistration(scope, health, monitor);
        for (int attempt = 0; attempt < 12 && (IsAlive(graph.Subscriber) || IsAlive(graph.Error)); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(IsAlive(graph.Subscriber), "Detached monitoring retained the failed subscriber graph.");
        Assert.IsFalse(IsAlive(graph.Error), "Detached monitoring retained an exception carrying the subscriber.");
        Assert.AreEqual(1L, graph.Snapshot.Outcomes!.Failed);
        Assert.AreEqual(HealthExecutionOutcome.Failed, graph.Snapshot.Outcomes.LastOutcome);
        Assert.AreEqual(typeof(TargetCarryingException).FullName,
            graph.Snapshot.Outcomes.Execution!.OriginalError!.ExceptionType);
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active);

        await scope.Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(new HeartbeatSchedulerCounts(), scope.Runtime.Inspect());
        Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.Active,
            "Retiring the collected subscriber's binding must preserve its caller-owned monitor.");
        monitor.ReportSuccess();
        HealthMonitorSnapshot current = health.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
        Assert.AreEqual(1L, current.Outcomes!.Successful);
        Assert.AreEqual(1L, current.Outcomes.Failed);
        Assert.AreEqual(1L, graph.Snapshot.Outcomes.Failed, "The retained detached failure snapshot must remain unchanged.");
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
        GC.KeepAlive(graph.Snapshot);
        GC.KeepAlive(monitor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FailedGraph RunFailingRegistration(HeartbeatTestScope scope, HealthcheckService health,
        IConsecutiveFailureMonitor monitor)
    {
        var subscriber = new HeartbeatProbe();
        var error = new TargetCarryingException(subscriber);
        subscriber.Callback = _ => Task.FromException<bool>(error);
        scope.Owner.Service.Register(subscriber, new(), new HeartbeatHealthcheckOptions { Monitor = monitor });
        scope.Activate();
        scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => scope.Runtime.Inspect().Accepted == 0
            && health.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId)
                .Outcomes?.Execution?.SettledAtUtc is not null, "The failed operation did not actually settle through the real binder.");
        HealthMonitorSnapshot snapshot = health.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
        var graph = new FailedGraph(new WeakReference<HeartbeatProbe>(subscriber),
            new WeakReference<TargetCarryingException>(error), snapshot);
        GC.KeepAlive(subscriber);
        GC.KeepAlive(error);
        return graph;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive<T>(WeakReference<T> reference) where T : class => reference.TryGetTarget(out _);

    private sealed record FailedGraph(WeakReference<HeartbeatProbe> Subscriber,
        WeakReference<TargetCarryingException> Error, HealthMonitorSnapshot Snapshot);

    private sealed class TargetCarryingException(HeartbeatProbe subscriber) : Exception("A detached diagnostic must not retain this target.")
    {
        internal HeartbeatProbe Target { get; } = subscriber;
    }

    private sealed class AdmissionRaceScope : IAsyncDisposable
    {
        internal AdmissionRaceScope()
        {
            Runtime = new HeartbeatRuntime(new HeartbeatSettings(1), Clock, Executor);
            Owner = Runtime.CreateOwner();
        }

        internal PausingClock Clock { get; } = new();
        internal HeartbeatControlledExecutor Executor { get; } = new();
        internal HeartbeatRuntime Runtime { get; }
        internal HeartbeatOwner Owner { get; }

        public async ValueTask DisposeAsync()
        {
            Clock.Release.TrySetResult();
            Task stop = Runtime.StopAsync();
            HeartbeatTestScope.WaitUntil(() =>
            {
                Executor.RunAvailable();
                return stop.IsCompleted;
            }, "The controlled admission-race test did not release all owned work.");
            await stop.WaitAsync(HeartbeatTestScope.Deadline);
            await Runtime.DisposeAsync();
        }
    }

    private sealed class PausingClock : TimeProvider
    {
        private readonly ManualTimeProvider manual = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        private int pauseNextTimestamp, timerChanges;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int TimerChanges => Volatile.Read(ref timerChanges);
        public override long TimestampFrequency => manual.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => manual.GetUtcNow();

        public override long GetTimestamp()
        {
            if (Interlocked.Exchange(ref pauseNextTimestamp, 0) != 0)
            {
                Entered.TrySetResult();
                Release.Task.WaitAsync(HeartbeatTestScope.Deadline).GetAwaiter().GetResult();
            }
            return manual.GetTimestamp();
        }

        internal void PauseNextTimestampRead() => Interlocked.Exchange(ref pauseNextTimestamp, 1);
        internal void Advance(TimeSpan elapsed) => manual.Advance(elapsed);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new TrackingTimer(this, manual.CreateTimer(callback, state, dueTime, period));

        private sealed class TrackingTimer(PausingClock owner, ITimer timer) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = timer.Change(dueTime, period);
                if (changed) { Interlocked.Increment(ref owner.timerChanges); }
                return changed;
            }

            public void Dispose() => timer.Dispose();
            public ValueTask DisposeAsync() => timer.DisposeAsync();
        }
    }
}
