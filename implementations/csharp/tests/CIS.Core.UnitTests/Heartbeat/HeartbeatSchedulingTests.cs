using CIS.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatSchedulingTests
{
    [TestMethod]
    public async Task ConstructionAndPreReadinessRegistrationAreInertAndActivationIsIdempotent()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        int conditions = 0;
        HeartbeatRecordingObserver facts = scope.Register(subscriber, condition: _ =>
        {
            Interlocked.Increment(ref conditions);
            return Task.FromResult(true);
        });
        scope.Clock.Advance(TimeSpan.FromHours(1));
        Assert.AreEqual(new HeartbeatSchedulerCounts(1, 0, 0, 0), scope.Runtime.Inspect());
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(0, conditions);
        Assert.AreEqual(0, subscriber.Calls);

        scope.Activate();
        scope.Runtime.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        Assert.AreEqual(1, conditions);
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(1, scope.Executor.Posts);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FirstDelayUsesReadinessOrRunningRegistrationWithoutStartingEarly(bool registerWhileRunning)
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        var options = new HeartbeatRegistrationOptions { InitialDelay = TimeSpan.FromMilliseconds(5) };
        if (registerWhileRunning)
        {
            scope.Runtime.Activate();
            HeartbeatTestScope.WaitUntil(() => scope.Clock.SchedulingChanges > 0, "The empty runtime did not arm its scheduler.");
            scope.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        int changes = scope.Clock.SchedulingChanges;
        HeartbeatRecordingObserver facts = scope.Register(subscriber, options);
        if (!registerWhileRunning)
        {
            scope.Clock.Advance(TimeSpan.FromSeconds(1));
            scope.Runtime.Activate();
        }
        HeartbeatTestScope.WaitUntil(() => scope.Clock.SchedulingChanges > changes, "The initial-delay timer was not armed.");
        scope.Clock.Advance(TimeSpan.FromMilliseconds(5) - TimeSpan.FromTicks(1));
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(0, subscriber.Calls);
        scope.Clock.Advance(TimeSpan.FromTicks(1));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "The eligible first execution was not dispatched.");
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        Assert.AreEqual(1, subscriber.Calls);
    }

    [TestMethod]
    public async Task FixedDelayBeginsAtFullSettlementAndMissedIntervalsDoNotAccumulate()
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource<bool> callback = scope.HoldResult();
        var subscriber = new HeartbeatProbe { Callback = _ => callback.Task };
        HeartbeatRecordingObserver facts = scope.Register(subscriber);
        scope.Activate();
        scope.Executor.RunNext();
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(1, scope.Executor.Posts);
        Assert.AreEqual(new HeartbeatSchedulerCounts(1, 1, 1, 0), scope.Runtime.Inspect());

        callback.TrySetResult(true);
        scope.WaitSettled(facts);
        long nextEligibility = scope.Clock.GetTimestamp() + TimeSpan.FromMilliseconds(10).Ticks;
        HeartbeatTestScope.WaitUntil(() => scope.Clock.NextWakeTimestamp == nextEligibility, "The settlement-relative timer was not armed.");
        scope.Clock.Advance(TimeSpan.FromMilliseconds(10) - TimeSpan.FromTicks(1));
        Assert.AreEqual(1, scope.Executor.Posts);
        scope.Clock.Advance(TimeSpan.FromTicks(1));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "The next fixed-delay execution was not dispatched.");
        scope.Executor.RunNext();
        scope.WaitSettled(facts, 2);
        Assert.AreEqual(2, subscriber.Calls);
    }

    [TestMethod]
    public async Task DistinctEqualObjectsAndNonuniqueNamesNeverUseSubscriberEqualityOrHashCodes()
    {
        await using var scope = new HeartbeatTestScope(capacity: 2);
        var first = new HeartbeatEqualProbe();
        var second = new HeartbeatEqualProbe();
        var options = new HeartbeatRegistrationOptions { Name = "same name" };
        HeartbeatRecordingObserver firstFacts = scope.Register(first, options);
        HeartbeatRecordingObserver secondFacts = scope.Register(second, options);
        scope.Activate(2);
        scope.Executor.RunAvailable();
        scope.WaitSettled(firstFacts);
        scope.WaitSettled(secondFacts);
        Assert.AreEqual(1, first.Calls);
        Assert.AreEqual(1, second.Calls);
        Assert.AreNotEqual(firstFacts.Last(HeartbeatEvent.Admitted).Notification.RegistrationId,
            secondFacts.Last(HeartbeatEvent.Admitted).Notification.RegistrationId);
    }

    [TestMethod]
    public async Task DuplicateRegistrationValidatesButPreservesOriginalDelayConditionOwnerAndObserver()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        int originalConditions = 0, replacementConditions = 0, replacementBindings = 0;
        HeartbeatRecordingObserver facts = scope.Register(subscriber,
            new() { InitialDelay = TimeSpan.FromMilliseconds(5), Name = "original" }, _ =>
            {
                Interlocked.Increment(ref originalConditions);
                return Task.FromResult(true);
            });
        await using HeartbeatOwner otherOwner = scope.Runtime.CreateOwner();
        scope.Runtime.Register(otherOwner, subscriber, new() { Interval = TimeSpan.FromHours(1), Name = "replacement" }, _ =>
        {
            Interlocked.Increment(ref replacementConditions);
            return Task.FromResult(false);
        }, _ => { Interlocked.Increment(ref replacementBindings); return new HeartbeatRecordingObserver(); });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => otherOwner.Service.Register(subscriber, new() { Interval = TimeSpan.Zero }));
        Assert.AreEqual(1, scope.Runtime.Inspect().Registrations);
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Clock.SchedulingChanges > 0, "The original delay was not armed.");
        Assert.AreEqual(0, scope.Executor.Posts);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(5));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "The original delay did not remain active.");
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        Assert.AreEqual(1, originalConditions);
        Assert.AreEqual(0, replacementConditions);
        Assert.AreEqual(0, replacementBindings);
        Assert.AreEqual(0, otherOwner.Registrations);
        Assert.AreEqual(1, scope.Owner.Registrations);
    }

    [TestMethod]
    public async Task ConcurrentFirstRegistrationsHaveOneBindingAndOneInstanceReservation()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        var facts = new HeartbeatRecordingObserver();
        TaskCompletionSource entered = scope.Hold();
        TaskCompletionSource release = scope.Hold();
        int bindings = 0;
        Task first = Task.Run(() => scope.Runtime.Register(scope.Owner, subscriber, new(), observerFactory: _ =>
        {
            Interlocked.Increment(ref bindings);
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return facts;
        }));
        try
        {
            await entered.Task.WaitAsync(HeartbeatTestScope.Deadline);
            Task duplicate = Task.Run(() => scope.Runtime.Register(scope.Owner, subscriber, new(), observerFactory: _ =>
            {
                Interlocked.Increment(ref bindings);
                return new HeartbeatRecordingObserver();
            }));
            Assert.AreEqual(1, scope.Runtime.Inspect().Registrations);
            release.TrySetResult();
            await Task.WhenAll(first, duplicate).WaitAsync(HeartbeatTestScope.Deadline);
            scope.Activate();
            scope.Executor.RunNext();
            scope.WaitSettled(facts);
            Assert.AreEqual(1, bindings);
            Assert.AreEqual(1, subscriber.Calls);
        }
        finally { release.TrySetResult(); await first.WaitAsync(HeartbeatTestScope.Deadline); }
    }

    [TestMethod]
    public async Task EachReleasedSlotDispatchesTheOldestPendingRegistrationWithoutWaitingForItsPeers()
    {
        await using var scope = new HeartbeatTestScope(capacity: 2);
        TaskCompletionSource<bool>[] releases = Enumerable.Range(0, 4).Select(_ => scope.HoldResult()).ToArray();
        HeartbeatProbe[] subscribers = releases.Select(release => new HeartbeatProbe { Callback = _ => release.Task }).ToArray();
        HeartbeatRecordingObserver[] observations = subscribers.Select(subscriber => scope.Register(subscriber)).ToArray();
        scope.Activate(2);
        Assert.AreEqual(new HeartbeatSchedulerCounts(4, 4, 2, 2), scope.Runtime.Inspect());
        scope.Executor.RunAvailable();
        CollectionAssert.AreEqual(new[] { 1, 1, 0, 0 }, subscribers.Select(subscriber => subscriber.Calls).ToArray());
        releases[1].TrySetResult(true);
        scope.WaitSettled(observations[1]);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "A free slot did not dispatch the oldest pending registration.");
        scope.Executor.RunNext();
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 0 }, subscribers.Select(subscriber => subscriber.Calls).ToArray());
        Assert.IsFalse(releases[0].Task.IsCompleted);
        releases[2].TrySetResult(true);
        scope.WaitSettled(observations[2]);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "The next slot was held behind an unfinished peer.");
        scope.Executor.RunNext();
        Assert.AreEqual(1, subscribers[3].Calls);
        Assert.IsFalse(releases[0].Task.IsCompleted);
    }

    [TestMethod]
    public async Task UtcClockChangesCannotMakeAnExecutionEligibleBeforeItsMonotonicDelay()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        HeartbeatRecordingObserver facts = scope.Register(subscriber, new() { InitialDelay = TimeSpan.FromMilliseconds(50) });
        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Clock.SchedulingChanges > 0, "The initial timer was not armed.");
        scope.Clock.SetUtcNow(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        scope.Clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.AreEqual(0, scope.Executor.Posts);
        scope.Clock.SetUtcNow(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "Monotonic eligibility was lost after a UTC adjustment.");
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        Assert.AreEqual(1, subscriber.Calls);
    }

    [TestMethod]
    public async Task ABlockingSynchronousSubscriberPrologueDoesNotBlockTheSchedulingControlPath()
    {
        await using var scope = new HeartbeatTestScope(capacity: 2);
        TaskCompletionSource entered = scope.Hold();
        TaskCompletionSource release = scope.Hold();
        var blocked = new HeartbeatProbe
        {
            Callback = _ =>
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return Task.FromResult(true);
            }
        };
        HeartbeatRecordingObserver firstFacts = scope.Register(blocked);
        scope.Activate();
        Task firstWrapper = Task.Run(scope.Executor.RunNext);
        try
        {
            await entered.Task.WaitAsync(HeartbeatTestScope.Deadline);
            var independent = new HeartbeatProbe();
            HeartbeatRecordingObserver independentFacts = scope.Register(independent);
            HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "Blocking user code prevented independent dispatch consideration.");
            scope.Executor.RunNext();
            scope.WaitSettled(independentFacts);
            Assert.AreEqual(1, independent.Calls);
            Assert.IsFalse(firstWrapper.IsCompleted);
        }
        finally { release.TrySetResult(); await firstWrapper.WaitAsync(HeartbeatTestScope.Deadline); }
        scope.WaitSettled(firstFacts);
    }
}
