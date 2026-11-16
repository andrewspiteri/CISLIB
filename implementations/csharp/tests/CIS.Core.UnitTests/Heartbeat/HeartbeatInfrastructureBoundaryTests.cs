using CIS.Heartbeat;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatInfrastructureBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AClockFailureDuringRunningRegistrationClosesAdmissionAndReleasesItsProvisionalBinding(bool utc)
    {
        await using var scope = new BoundaryScope();
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Clock.TimerChanges > 0, "The empty runtime did not finish activation.");
        var subscriber = new HeartbeatProbe();
        var observer = new HeartbeatRecordingObserver();
        scope.Clock.FailNext(utc);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            scope.Runtime.Register(scope.Owner, subscriber, new(), observerFactory: _ => observer));
        await scope.AssertClockFaultAndDrainAsync();
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(1, observer.Disposals);
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Retired));
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Owner.Service.Register(subscriber, new()));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AClockFailureAtAdmissionCannotLeakAPartialOperationOrOwnerHold(bool utc)
    {
        await using var scope = new BoundaryScope();
        var subscriber = new HeartbeatProbe();
        var observer = new HeartbeatRecordingObserver();
        scope.Runtime.Register(scope.Owner, subscriber,
            new() { InitialDelay = TimeSpan.FromMilliseconds(5) }, observerFactory: _ => observer);
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Clock.TimerChanges > 0, "The eligibility timer was not armed.");
        Assert.AreEqual(0, scope.Owner.Holds);
        scope.Clock.FailNext(utc);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(5));
        await scope.AssertClockFaultAndDrainAsync();
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(0, observer.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(1, observer.Disposals);
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Retired));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AClockFailureAtSettlementStillReleasesTheCompletedExecutionAndReportsOneConsistentEventTime(bool utc)
    {
        await using var scope = new BoundaryScope();
        TaskCompletionSource<bool> work = scope.HoldResult();
        var subscriber = new HeartbeatProbe { Callback = _ => work.Task };
        var observer = new HeartbeatRecordingObserver
        {
            OnReport = notification =>
            {
                // Outcome handoff has ended all subscriber/cleanup work; the next
                // clock access belongs to actual ownership settlement.
                if (notification.Kind == HeartbeatEvent.Outcome) { scope.Clock.FailNext(utc); }
            }
        };
        scope.Runtime.Register(scope.Owner, subscriber, new(), observerFactory: _ => observer);
        scope.Activate();
        scope.Executor.RunNext();
        Assert.AreEqual(1, scope.Owner.Holds);
        work.TrySetResult(true);
        await scope.AssertClockFaultAndDrainAsync();
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(HeartbeatResult.Succeeded, observer.Last(HeartbeatEvent.Outcome).Notification.Outcome);
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Settled));
        HeartbeatNotification settled = observer.Last(HeartbeatEvent.Settled).Notification;
        Assert.AreEqual(settled.OccurredAtUtc, settled.SettledAtUtc,
            "A one-shot clock fault cannot create contradictory timestamps within the same settlement event.");
        Assert.AreEqual(1, observer.Disposals);
    }

    [TestMethod]
    public async Task AUtcClockFailureWhileRetiringAnIdleRegistrationCannotSkipBindingDisposal()
    {
        await using var scope = new BoundaryScope();
        var subscriber = new HeartbeatProbe();
        var observer = new HeartbeatRecordingObserver();
        scope.Runtime.Register(scope.Owner, subscriber, new(), observerFactory: _ => observer);
        scope.Clock.FailNext(utc: true);
        Task unregister = scope.Owner.Service.UnregisterAsync(subscriber);
        await unregister.WaitAsync(HeartbeatTestScope.Deadline);
        await scope.AssertClockFaultAndDrainAsync();
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Retired));
        Assert.AreEqual(1, observer.Disposals);
        await scope.Owner.CloseAsync().WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, observer.Disposals, "Owner/global closure cannot release the retired binding again.");
    }

    [TestMethod]
    public async Task AValidMonotonicDelayRemainsUsableWhenItsProjectedUtcEligibilityWouldOverflow()
    {
        await using var scope = new BoundaryScope();
        scope.Clock.FixedUtc = DateTimeOffset.MaxValue - TimeSpan.FromTicks(1);
        var subscriber = new HeartbeatProbe();
        var observer = new HeartbeatRecordingObserver();
        TimeSpan delay = TimeSpan.FromDays(1);
        scope.Runtime.Register(scope.Owner, subscriber, new() { InitialDelay = delay }, observerFactory: _ => observer);
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Clock.TimerChanges > 0, "The valid monotonic delay was not armed.");
        scope.Clock.Advance(delay - TimeSpan.FromTicks(1));
        Assert.AreEqual(0, scope.Executor.Posts);
        scope.Clock.Advance(TimeSpan.FromTicks(1));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1,
            "UTC correlation overflow invalidated an otherwise accepted monotonic schedule.");
        scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => observer.Count(HeartbeatEvent.Settled) == 1, "The eligible execution did not settle.");
        HeartbeatInfrastructureFault fault = await scope.Runtime.InfrastructureFailure.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual("Heartbeat.CorrelationTime", fault.Code);
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(1, observer.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(HeartbeatResult.Succeeded, observer.Last(HeartbeatEvent.Outcome).Notification.Outcome);
        Assert.AreEqual(0, scope.Owner.Holds);
        await scope.Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, observer.Disposals);
        Assert.AreEqual(new HeartbeatSchedulerCounts(), scope.Runtime.Inspect());
    }

    [TestMethod]
    public async Task ABlockedWithdrawnCleanupPrologueCannotBlockAnotherOwnersBindingRetirement()
    {
        await using var scope = new BoundaryScope();
        TaskCompletionSource<bool> runningWork = scope.HoldResult();
        TaskCompletionSource cleanupEntered = scope.Hold();
        TaskCompletionSource releaseCleanup = scope.Hold();
        TaskCompletionSource bindingEntered = scope.Hold();
        TaskCompletionSource releaseBinding = scope.Hold();
        var running = new HeartbeatProbe { Callback = _ => runningWork.Task };
        var withdrawn = new HeartbeatProbe();
        var runningObserver = new HeartbeatRecordingObserver();
        var withdrawnObserver = new HeartbeatRecordingObserver();
        scope.Runtime.Register(scope.Owner, running, new(), observerFactory: _ => runningObserver);
        scope.Runtime.Register(scope.Owner, withdrawn, new(), observerFactory: _ => withdrawnObserver, cleanup: () =>
        {
            cleanupEntered.TrySetResult();
            releaseCleanup.Task.GetAwaiter().GetResult();
            return ValueTask.CompletedTask;
        });

        HeartbeatOwner idleOwner = scope.Runtime.CreateOwner();
        var idleSubscriber = new HeartbeatProbe();
        var idleObserver = new HeartbeatRecordingObserver();
        Task<Exception?> initializing = Task.Run(() =>
        {
            try
            {
                scope.Runtime.Register(idleOwner, idleSubscriber,
                    new() { InitialDelay = TimeSpan.FromHours(1) }, observerFactory: _ =>
                    {
                        bindingEntered.TrySetResult();
                        releaseBinding.Task.GetAwaiter().GetResult();
                        return idleObserver;
                    });
                return null;
            }
            catch (Exception error) { return error; }
        });
        await bindingEntered.Task.WaitAsync(HeartbeatTestScope.Deadline);
        scope.Activate();
        scope.Executor.RunNext();
        Assert.AreEqual(1, running.Calls);
        Assert.AreEqual(1, scope.Runtime.Inspect().Pending);
        Task stop = scope.Runtime.StopAsync();
        await cleanupEntered.Task.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.IsFalse(stop.IsCompleted);

        // Publish this otherwise idle registration only after the withdrawn
        // cleanup has synchronously blocked its own infrastructure continuation.
        releaseBinding.TrySetResult();
        Exception? rejection = await initializing.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.IsInstanceOfType<InvalidOperationException>(rejection);
        Task idleClose = idleOwner.CloseAsync();
        await idleClose.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.IsFalse(releaseCleanup.Task.IsCompleted);
        Assert.AreEqual(1, idleObserver.Disposals);
        Assert.AreEqual(1, idleObserver.Count(HeartbeatEvent.Retired));
        Assert.AreEqual(0, idleSubscriber.Calls);
        Assert.AreEqual(0, withdrawn.Calls);
        Assert.IsFalse(stop.IsCompleted);

        releaseCleanup.TrySetResult();
        runningWork.TrySetResult(true);
        await stop.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, withdrawnObserver.Count(HeartbeatEvent.Suppressed));
        Assert.AreEqual(1, withdrawnObserver.Count(HeartbeatEvent.Settled));
        Assert.AreEqual(1, withdrawnObserver.Disposals);
        Assert.AreEqual(1, runningObserver.Disposals);
        Assert.AreEqual(new HeartbeatSchedulerCounts(), scope.Runtime.Inspect());
    }

    private sealed class BoundaryScope : IAsyncDisposable
    {
        private readonly List<Action> releases = [];
        internal BoundaryScope()
        {
            Runtime = new HeartbeatRuntime(new HeartbeatSettings(1), Clock, Executor);
            Owner = Runtime.CreateOwner();
        }

        internal FaultOnceClock Clock { get; } = new();
        internal HeartbeatControlledExecutor Executor { get; } = new();
        internal HeartbeatRuntime Runtime { get; }
        internal HeartbeatOwner Owner { get; }

        internal TaskCompletionSource Hold()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            releases.Add(() => completion.TrySetResult());
            return completion;
        }

        internal TaskCompletionSource<bool> HoldResult()
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            releases.Add(() => completion.TrySetResult(true));
            return completion;
        }

        internal void Activate()
        {
            Runtime.Activate();
            HeartbeatTestScope.WaitUntil(() => Executor.PendingCount == 1, "The first wrapper was not posted.");
        }

        internal async Task AssertClockFaultAndDrainAsync()
        {
            HeartbeatInfrastructureFault fault = await Runtime.InfrastructureFailure.WaitAsync(HeartbeatTestScope.Deadline);
            Assert.AreEqual("Heartbeat.Clock", fault.Code);
            Assert.AreEqual(1, Clock.Failures);
            await Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
            Assert.AreEqual(0, Owner.Holds);
            Assert.AreEqual(0, Owner.Registrations);
            Assert.AreEqual(new HeartbeatSchedulerCounts(), Runtime.Inspect());
        }

        public async ValueTask DisposeAsync()
        {
            Clock.Disarm();
            foreach (Action release in releases) { release(); }
            Task stop = Runtime.StopAsync();
            HeartbeatTestScope.WaitUntil(() =>
            {
                Executor.RunAvailable();
                return stop.IsCompleted;
            }, "Infrastructure-boundary test work did not settle during cleanup.");
            await stop.WaitAsync(HeartbeatTestScope.Deadline);
            await Runtime.DisposeAsync();
        }
    }

    private sealed class FaultOnceClock : TimeProvider
    {
        private readonly ManualTimeProvider manual = new(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        private int failureKind, failures, timerChanges;
        internal DateTimeOffset? FixedUtc { get; set; }
        internal int Failures => Volatile.Read(ref failures);
        internal int TimerChanges => Volatile.Read(ref timerChanges);
        public override long TimestampFrequency => manual.TimestampFrequency;

        public override long GetTimestamp()
        {
            FailIfArmed(1);
            return manual.GetTimestamp();
        }

        public override DateTimeOffset GetUtcNow()
        {
            FailIfArmed(2);
            return FixedUtc ?? manual.GetUtcNow();
        }

        internal void FailNext(bool utc)
        {
            Assert.AreEqual(0, Interlocked.Exchange(ref failureKind, utc ? 2 : 1), "A previous one-shot clock fault was not consumed.");
        }

        internal void Disarm() => Interlocked.Exchange(ref failureKind, 0);
        internal void Advance(TimeSpan elapsed) => manual.Advance(elapsed);

        private void FailIfArmed(int kind)
        {
            if (Interlocked.CompareExchange(ref failureKind, 0, kind) != kind) { return; }
            Interlocked.Increment(ref failures);
            throw new InvalidOperationException("Injected one-shot clock failure.");
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new TrackingTimer(this, manual.CreateTimer(callback, state, dueTime, period));

        private sealed class TrackingTimer(FaultOnceClock owner, ITimer timer) : ITimer
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
