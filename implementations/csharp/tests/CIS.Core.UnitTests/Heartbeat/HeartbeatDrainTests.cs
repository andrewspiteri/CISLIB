using CIS.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatDrainTests
{
    [TestMethod]
    public async Task ShutdownBeforeReadinessRetiresRegistrationsWithoutInvokingAnyUserPhase()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        int conditions = 0;
        HeartbeatRecordingObserver facts = scope.Register(subscriber, condition: _ =>
        {
            Interlocked.Increment(ref conditions);
            return Task.FromResult(true);
        });
        await scope.Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
        scope.Runtime.Activate();
        scope.Clock.Advance(TimeSpan.FromHours(1));
        await scope.Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(0, conditions);
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Retired));
        Assert.AreEqual(1, facts.Disposals);
        Assert.AreEqual(new HeartbeatSchedulerCounts(), scope.Runtime.Inspect());
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Owner.Service.Register(subscriber, new()));
        Assert.IsTrue(scope.Owner.Service.UnregisterAsync(subscriber, new CancellationToken(canceled: true)).IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task OrdinaryUnregisterExecutesAcceptedQueuedWorkAndReuseBeginsOnlyAfterItsDrain()
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource<bool> firstWork = scope.HoldResult();
        var first = new HeartbeatProbe { Callback = _ => firstWork.Task };
        var queued = new HeartbeatProbe();
        HeartbeatRecordingObserver firstFacts = scope.Register(first);
        HeartbeatRecordingObserver oldFacts = scope.Register(queued);
        scope.Activate();
        scope.Executor.RunNext();
        Assert.AreEqual(new HeartbeatSchedulerCounts(2, 2, 1, 1), scope.Runtime.Inspect());
        Task unregister = scope.Owner.Service.UnregisterAsync(queued);
        Task joined = scope.Owner.Service.UnregisterAsync(queued);
        int replacementBindings = 0;
        scope.Runtime.Register(scope.Owner, queued, new() { InitialDelay = TimeSpan.FromHours(1) },
            observerFactory: _ => { replacementBindings++; return new HeartbeatRecordingObserver(); });
        Assert.IsFalse(unregister.IsCompleted);
        Assert.IsFalse(joined.IsCompleted);
        Assert.AreEqual(0, replacementBindings);
        Assert.AreEqual(2, scope.Owner.Holds, "Queued acceptance must already protect both owners' usages.");

        firstWork.TrySetResult(true);
        scope.WaitSettled(firstFacts);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "Ordinary unregister suppressed its already accepted queue entry.");
        scope.Executor.RunNext();
        await Task.WhenAll(unregister, joined).WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, queued.Calls);
        Assert.AreEqual(0, oldFacts.Count(HeartbeatEvent.Suppressed));
        Assert.AreEqual(1, oldFacts.Disposals);

        HeartbeatRecordingObserver newFacts = scope.Register(queued, new() { InitialDelay = TimeSpan.FromMilliseconds(1) });
        long expected = scope.Clock.GetTimestamp() + TimeSpan.FromMilliseconds(1).Ticks;
        HeartbeatTestScope.WaitUntil(() => scope.Clock.NextWakeTimestamp == expected, "The new registration's own initial delay was not armed.");
        scope.Clock.Advance(TimeSpan.FromMilliseconds(1));
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "Completed removal did not permit sequential reuse.");
        scope.Executor.RunNext();
        scope.WaitSettled(newFacts);
        Assert.AreEqual(2, queued.Calls);
        Assert.AreNotEqual(oldFacts.Last(HeartbeatEvent.Admitted).Notification.RegistrationId,
            newFacts.Last(HeartbeatEvent.Admitted).Notification.RegistrationId);
    }

    [TestMethod]
    public async Task GlobalShutdownSuppressesAcceptedQueueEntriesButWaitsForTheRunningCallback()
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource<bool> runningWork = scope.HoldResult();
        var running = new HeartbeatProbe { Callback = _ => runningWork.Task };
        var queued = new HeartbeatProbe();
        HeartbeatRecordingObserver runningFacts = scope.Register(running);
        HeartbeatRecordingObserver queuedFacts = scope.Register(queued);
        scope.Activate();
        scope.Executor.RunNext();
        Task ordinary = scope.Owner.Service.UnregisterAsync(queued);
        Task stop = scope.Runtime.StopAsync();
        await ordinary.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(0, queued.Calls);
        Assert.AreEqual(1, queuedFacts.Count(HeartbeatEvent.Suppressed));
        Assert.AreEqual(0, queuedFacts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(1, queuedFacts.Count(HeartbeatEvent.Settled));
        Assert.IsFalse(stop.IsCompleted);
        Assert.AreEqual(1, scope.Owner.Holds);
        Assert.IsFalse(running.Tokens.Single().IsCancellationRequested);
        runningWork.TrySetResult(true);
        await stop.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, runningFacts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(new HeartbeatSchedulerCounts(), scope.Runtime.Inspect());
    }

    [TestMethod]
    public async Task APostedButUnstartedWrapperKeepsShutdownPendingAndLosesItsUserStartGate()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        int conditions = 0;
        HeartbeatRecordingObserver facts = scope.Register(subscriber, condition: _ =>
        {
            conditions++;
            return Task.FromResult(true);
        });
        scope.Activate();
        Task stop = scope.Runtime.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        Assert.AreEqual(1, scope.Runtime.Inspect().Dispatched);
        Assert.AreEqual(1, scope.Owner.Holds);
        scope.Executor.RunNext();
        await stop.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(0, conditions);
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(HeartbeatPhase.Condition, facts.Last(HeartbeatEvent.Suppressed).Notification.SuppressedPhase);
        Assert.AreEqual(0, facts.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Settled));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ARunningConditionFinishesButCannotStartANewCallbackAfterTheGlobalCutoff(bool conditionResult)
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource<bool> condition = scope.HoldResult();
        var subscriber = new HeartbeatProbe();
        HeartbeatRecordingObserver facts = scope.Register(subscriber, condition: _ => condition.Task);
        scope.Activate();
        scope.Executor.RunNext();
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.ConditionStarted));
        Task stop = scope.Runtime.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        condition.TrySetResult(conditionResult);
        await stop.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(0, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Settled));
        if (conditionResult)
        {
            Assert.AreEqual(1, facts.Count(HeartbeatEvent.Suppressed));
            Assert.AreEqual(HeartbeatPhase.Callback, facts.Last(HeartbeatEvent.Suppressed).Notification.SuppressedPhase);
            Assert.AreEqual(true, facts.Last(HeartbeatEvent.Suppressed).Notification.ConditionResult);
            Assert.AreEqual(0, facts.Count(HeartbeatEvent.Outcome));
        }
        else
        {
            Assert.AreEqual(HeartbeatResult.Skipped, facts.Last(HeartbeatEvent.Outcome).Notification.Outcome);
            Assert.AreEqual(0, facts.Count(HeartbeatEvent.Suppressed));
        }
    }

    [TestMethod]
    [DataRow("unregister", false)]
    [DataRow("unregister", true)]
    [DataRow("owner", false)]
    [DataRow("owner", true)]
    [DataRow("stop", false)]
    [DataRow("stop", true)]
    public async Task CancelingOneWaiterEstablishesItsCutoffButDoesNotCancelTheSharedDrain(string barrier, bool alreadyCanceled)
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource<bool> work = scope.HoldResult();
        var subscriber = new HeartbeatProbe { Callback = _ => work.Task };
        HeartbeatRecordingObserver facts = scope.Register(subscriber);
        scope.Activate();
        scope.Executor.RunNext();
        using var cancellation = new CancellationTokenSource();
        if (alreadyCanceled) { cancellation.Cancel(); }
        Task Wait(CancellationToken token) => barrier switch
        {
            "unregister" => scope.Owner.Service.UnregisterAsync(subscriber, token),
            "owner" => scope.Owner.CloseAsync(token),
            "stop" => scope.Runtime.StopAsync(token),
            _ => throw new ArgumentOutOfRangeException(nameof(barrier))
        };
        Task canceledWait = Wait(cancellation.Token);
        Task joined = Wait(CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => canceledWait);
        Assert.IsFalse(joined.IsCompleted);
        Assert.AreEqual(1, scope.Owner.Holds);
        Assert.AreEqual(1, scope.Runtime.Inspect().Dispatched);
        Assert.IsFalse(subscriber.Tokens.Single().IsCancellationRequested);
        if (barrier == "stop")
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => scope.Owner.Service.Register(subscriber, new()));
        }
        else { scope.Owner.Service.Register(subscriber, new()); }
        work.TrySetResult(true);
        await joined.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Settled));
        Assert.AreEqual(0, scope.Owner.Holds);
        Assert.IsTrue(scope.Owner.Service.UnregisterAsync(subscriber, cancellation.Token).IsCompletedSuccessfully);
    }

    [TestMethod]
    [DataRow("unregister")]
    [DataRow("owner")]
    [DataRow("stop")]
    public async Task UserWorkCannotWaitForADrainContainingItsOwnExecution(string barrier)
    {
        await using var scope = new HeartbeatTestScope();
        Exception? rejected = null;
        Task? incorrectWait = null;
        var subscriber = new HeartbeatProbe();
        subscriber.Callback = _ =>
        {
            try
            {
                incorrectWait = barrier switch
                {
                    "unregister" => scope.Owner.Service.UnregisterAsync(subscriber),
                    "owner" => scope.Owner.CloseAsync(),
                    "stop" => scope.Runtime.StopAsync(),
                    _ => throw new ArgumentOutOfRangeException(nameof(barrier))
                };
            }
            catch (Exception error) { rejected = error; }
            return Task.FromResult(true);
        };
        HeartbeatRecordingObserver facts = scope.Register(subscriber);
        scope.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        Assert.IsInstanceOfType<InvalidOperationException>(rejected);
        Assert.IsNull(incorrectWait);
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
    }

    [TestMethod]
    public async Task ClosingOneOwnerDrainsOnlyItsRegistrationsWhileAnotherOwnerContinues()
    {
        await using var scope = new HeartbeatTestScope(capacity: 2);
        HeartbeatOwner secondOwner = scope.Runtime.CreateOwner();
        TaskCompletionSource<bool> firstWork = scope.HoldResult();
        TaskCompletionSource<bool> secondWork = scope.HoldResult();
        var first = new HeartbeatProbe { Callback = _ => firstWork.Task };
        var second = new HeartbeatProbe { Callback = _ => secondWork.Task };
        HeartbeatRecordingObserver firstFacts = scope.Register(first);
        HeartbeatRecordingObserver secondFacts = scope.Register(second, owner: secondOwner);
        scope.Activate(2);
        scope.Executor.RunAvailable();
        Task closeFirst = scope.Owner.CloseAsync();
        Assert.IsFalse(closeFirst.IsCompleted);
        Assert.AreEqual(1, scope.Owner.Holds);
        Assert.AreEqual(1, secondOwner.Holds);
        firstWork.TrySetResult(true);
        await closeFirst.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, firstFacts.Disposals);
        Assert.AreEqual(0, secondFacts.Count(HeartbeatEvent.Settled));
        Assert.AreEqual(1, secondOwner.Holds);
        var additional = new HeartbeatProbe();
        HeartbeatRecordingObserver additionalFacts = scope.Register(additional, owner: secondOwner);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "An unrelated open owner stopped making progress.");
        scope.Executor.RunNext();
        scope.WaitSettled(additionalFacts);
        Assert.AreEqual(1, additional.Calls);
        secondWork.TrySetResult(true);
        scope.WaitSettled(secondFacts);
    }

    [TestMethod]
    public async Task AnObserverTimeoutCannotReleaseAnExecutionOrItsOwnerHold()
    {
        await using var scope = new HeartbeatTestScope();
        TaskCompletionSource<bool> work = scope.HoldResult();
        var subscriber = new HeartbeatProbe { Callback = _ => work.Task };
        HeartbeatRecordingObserver facts = scope.Register(subscriber);
        scope.Activate();
        scope.Executor.RunNext();
        Task unregister = scope.Owner.Service.UnregisterAsync(subscriber);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => unregister.WaitAsync(TimeSpan.Zero));
        Assert.IsFalse(unregister.IsCompleted);
        Assert.AreEqual(1, scope.Owner.Holds);
        Assert.AreEqual(new HeartbeatSchedulerCounts(1, 1, 1, 0), scope.Runtime.Inspect());
        work.TrySetResult(true);
        await unregister.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, facts.Count(HeartbeatEvent.Settled));
    }
}
