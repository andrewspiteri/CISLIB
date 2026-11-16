using System.Runtime.CompilerServices;
using CIS.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatWeakOwnershipTests
{
    [TestMethod]
    [DataRow("bound")]
    [DataRow("capturing")]
    public async Task AnIdleConditionAndSurvivingObserverCannotRootTheSubscriberOrItsOwnerGraph(string conditionKind)
    {
        await using var scope = new HeartbeatTestScope();
        CollectedGraph graph = CreateIdleGraph(scope.Runtime, conditionKind);
        Assert.AreEqual(1, scope.Runtime.Inspect().Registrations);
        CollectUntilUnreachable(graph);
        Assert.IsFalse(IsAlive(graph.Subscriber), "The idle subscriber is rooted through its condition/observer/owner graph.");
        Assert.IsFalse(IsAlive(graph.Owner), "The idle owner is rooted through the shared scheduler.");

        scope.Runtime.Activate();
        HeartbeatTestScope.WaitUntil(() => scope.Runtime.Inspect().Registrations == 0,
            "A collected subscriber's independent reporting association was not retired.");
        Assert.AreEqual(0, scope.Executor.Posts);
        Assert.AreEqual(0, graph.Observer.Count(HeartbeatEvent.Outcome));
        Assert.AreEqual(1, graph.Observer.Count(HeartbeatEvent.Retired));
        Assert.AreEqual(1, graph.Observer.Disposals);
        await scope.Runtime.StopAsync().WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, graph.Observer.Disposals, "Collection and shutdown must not release a reporting association twice.");
        GC.KeepAlive(graph.Observer);
    }

    [TestMethod]
    public async Task AConditionWithNoIndependentOwnerRemainsAvailableWhileItsSubscriberIsExternallyAlive()
    {
        await using var scope = new HeartbeatTestScope();
        var subscriber = new HeartbeatProbe();
        (WeakReference<ConditionBinding> condition, HeartbeatRecordingObserver facts) = RegisterCondition(scope, subscriber);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsTrue(IsAlive(condition), "Weak idle ownership must not independently discard a live subscriber's condition.");
        scope.Activate();
        scope.Executor.RunNext();
        scope.WaitSettled(facts);
        Assert.AreEqual(1, subscriber.Calls);
        Assert.IsTrue(condition.TryGetTarget(out ConditionBinding? retained));
        Assert.AreEqual(1, retained!.Calls);
        GC.KeepAlive(subscriber);
    }

    [TestMethod]
    public async Task AcceptedPostedWorkStronglyProtectsTheTargetAndOwnerUntilActualWrapperSettlement()
    {
        await using var scope = new HeartbeatTestScope();
        CollectedGraph graph = CreateIdleGraph(scope.Runtime, "capturing", () => scope.Activate());
        Assert.AreEqual(new HeartbeatSchedulerCounts(1, 1, 1, 0), scope.Runtime.Inspect());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.IsTrue(IsAlive(graph.Subscriber), "Acceptance must retain the target before executor invocation.");
        Assert.IsTrue(IsAlive(graph.Owner), "Acceptance must retain the actual owner hold before queueing.");
        Task stop = scope.Runtime.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        scope.Executor.RunNext();
        await stop.WaitAsync(HeartbeatTestScope.Deadline);
        Assert.AreEqual(1, graph.Observer.Count(HeartbeatEvent.Suppressed));
        Assert.AreEqual(1, graph.Observer.Count(HeartbeatEvent.Settled));
        Assert.AreEqual(1, graph.Observer.Disposals);
        CollectUntilUnreachable(graph);
        Assert.IsFalse(IsAlive(graph.Subscriber), "Settled suppression retained a temporary strong target.");
        Assert.IsFalse(IsAlive(graph.Owner), "Settled suppression retained a temporary owner hold.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CollectedGraph CreateIdleGraph(HeartbeatRuntime runtime, string conditionKind, Action? beforeRelease = null)
    {
        HeartbeatOwner owner = runtime.CreateOwner();
        var subscriber = new OwnedProbe(owner);
        var observer = new HeartbeatRecordingObserver();
        Func<CancellationToken, Task<bool>> condition = conditionKind == "bound"
            ? subscriber.ConditionAsync
            : _ => Task.FromResult(subscriber.Owner.Service is not null);
        runtime.Register(owner, subscriber, new(), condition, _ => observer);
        beforeRelease?.Invoke();
        var graph = new CollectedGraph(new WeakReference<HeartbeatProbe>(subscriber), new WeakReference<HeartbeatOwner>(owner), observer);
        GC.KeepAlive(subscriber);
        GC.KeepAlive(owner);
        return graph;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<ConditionBinding>, HeartbeatRecordingObserver) RegisterCondition(HeartbeatTestScope scope,
        HeartbeatProbe subscriber)
    {
        var binding = new ConditionBinding(subscriber);
        HeartbeatRecordingObserver facts = scope.Register(subscriber, condition: binding.ConditionAsync);
        return (new WeakReference<ConditionBinding>(binding), facts);
    }

    private static void CollectUntilUnreachable(CollectedGraph graph)
    {
        for (int attempt = 0; attempt < 12 && (IsAlive(graph.Subscriber) || IsAlive(graph.Owner)); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive<T>(WeakReference<T> reference) where T : class => reference.TryGetTarget(out _);

    private sealed class OwnedProbe(HeartbeatOwner owner) : HeartbeatProbe
    {
        internal HeartbeatOwner Owner { get; } = owner;
        internal Task<bool> ConditionAsync(CancellationToken _) => Task.FromResult(Owner.Service is not null);
    }

    private sealed class ConditionBinding(HeartbeatProbe subscriber)
    {
        internal int Calls;
        internal Task<bool> ConditionAsync(CancellationToken _)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(subscriber.Calls == 0);
        }
    }

    private sealed record CollectedGraph(WeakReference<HeartbeatProbe> Subscriber,
        WeakReference<HeartbeatOwner> Owner, HeartbeatRecordingObserver Observer);
}
