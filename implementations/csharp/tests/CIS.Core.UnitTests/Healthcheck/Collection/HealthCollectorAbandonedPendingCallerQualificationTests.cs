using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorAbandonedPendingCallerQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task ACollectedQueuedRegistrationReleasesItsCallerBeforeTheNextRealOwnerIsAdmitted()
    {
        await using var scope = new Scope();
        try { await RunScenarioAsync(scope); }
        catch (Exception error) { scope.Primary = error; throw; }
    }

    private static async Task RunScenarioAsync(Scope scope)
    {
        IConsecutiveFailureMonitor occupiedMonitor = scope.CreateMonitor("occupied");
        IConsecutiveFailureMonitor abandonedMonitor = scope.CreateMonitor("abandoned registration only");
        IConsecutiveFailureMonitor sentinelMonitor = scope.CreateMonitor("real following owner");
        IHealthCollectionRegistration occupied = scope.Runner.Register(occupiedMonitor, scope.OccupiedOwner, new());
        IHealthCollectionRegistration sentinel = scope.Runner.Register(sentinelMonitor, scope.SentinelOwner, new());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> occupiedCaller = occupied.CollectAsync();
        scope.TrackOriginalCaller(occupiedCaller);
        Invocation occupiedInvocation = await scope.OccupiedOwner.NextAsync(occupiedCaller);
        Task<HealthCollectionAttempt> occupiedOperation = occupied.CollectAsync();
        scope.TrackActualOperation(occupiedOperation);

        AbandonedCaller abandoned = CreateAbandonedPendingCaller(scope.Runner, abandonedMonitor, scope.UninvokedOwner);
        CollectRegistrationHandle(abandoned.Handle);
        Assert.IsFalse(abandoned.Handle.IsAlive,
            "Only the real queued registration handle must be lost; the test still owns its monitor, collector and returned caller Task.");

        Task<HealthCollectionAttempt> sentinelCaller = sentinel.CollectAsync();
        scope.TrackOriginalCaller(sentinelCaller);
        occupiedInvocation.Release();
        Invocation sentinelInvocation = await scope.SentinelOwner.NextAsync(sentinelCaller);
        Task<HealthCollectionAttempt> sentinelOperation = sentinel.CollectAsync();
        scope.TrackActualOperation(sentinelOperation);

        // The actual next lease cannot enter until the sole occupied slot is
        // released. The dispatcher scans its dead registrations before selecting
        // that next operation, so this is a causal post-sweep observation, not a
        // race against an arbitrary delay or an immediate post-GC assertion.
        Assert.IsTrue(abandoned.Caller.IsCompletedSuccessfully,
            "A caller queued behind real occupied capacity must receive retirement when its collected registration is swept, before the following owner enters.");
        HealthCollectionAttempt retired = abandoned.Caller.Result;
        Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition);
        Assert.IsFalse(retired.EvaluationFailed);
        Assert.IsNull(retired.Diagnostic);
        Assert.AreEqual(0, scope.UninvokedOwner.Acquisitions);

        HealthSnapshot snapshot = scope.Service.CaptureSnapshot();
        Assert.AreEqual(3, snapshot.MonitorCounts.Active);
        HealthMonitorSnapshot retained = snapshot.Monitors.Single(item => item.MonitorId == abandonedMonitor.MonitorId);
        Assert.AreEqual(MonitorState.Pending, retained.State);
        Assert.IsNull(retained.LastObservationAtUtc);
        Assert.AreEqual(0L, retained.Outcomes!.Successful);
        Assert.AreEqual(0L, retained.Outcomes.Failed);
        Assert.IsFalse(retained.Collection!.InProgress);
        Assert.IsNull(retained.Collection.LastCompletedAtUtc);
        Assert.IsNull(retained.Collection.LastError);
        abandonedMonitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, scope.Service.CaptureSnapshot().Monitors
            .Single(item => item.MonitorId == abandonedMonitor.MonitorId).State);

        sentinelInvocation.Release();
        await Task.WhenAll(occupiedOperation, sentinelOperation).WaitAsync(Deadline);
        Assert.AreEqual(1, scope.OccupiedOwner.Acquisitions);
        Assert.AreEqual(1, scope.OccupiedOwner.Cleanups);
        Assert.AreEqual(1, scope.SentinelOwner.Acquisitions);
        Assert.AreEqual(1, scope.SentinelOwner.Cleanups);
        await scope.DisposeAsync();
        await AssertOriginalCallersCompleteAsync(occupiedCaller, sentinelCaller);
        GC.KeepAlive(occupied);
        GC.KeepAlive(sentinel);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AbandonedCaller CreateAbandonedPendingCaller(HealthCollectorRunner runner,
        IHealthMonitor retainedMonitor, IHealthCollector retainedCollector)
    {
        IHealthCollectionRegistration registration = runner.Register(retainedMonitor, retainedCollector, new());
        Task<HealthCollectionAttempt> caller = registration.CollectAsync();
        Assert.IsFalse(caller.IsCompleted, "An actual occupied slot queues this still-retained manual registration.");
        var abandoned = new AbandonedCaller(new WeakReference(registration), caller);
        GC.KeepAlive(registration);
        return abandoned;
    }

    private static void CollectRegistrationHandle(WeakReference handle)
    {
        for (int attempt = 0; attempt < 10 && handle.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed record AbandonedCaller(WeakReference Handle, Task<HealthCollectionAttempt> Caller);

    private static async Task AssertOriginalCallersCompleteAsync(params Task<HealthCollectionAttempt>[] callers)
    {
        await ObserveCompletedCallersAsync(callers);
        Assert.IsTrue(callers.All(caller => caller.IsCompleted),
            "The original queued callers must complete after all actual operations and resources have settled.");
        await Task.WhenAll(callers);
    }

    private static async Task ObserveCompletedCallersAsync(IEnumerable<Task<HealthCollectionAttempt>> callers)
    {
        var errors = new List<Exception>();
        foreach (Task<HealthCollectionAttempt> caller in callers)
        {
            if (!caller.IsCompleted) { continue; }
            try { await caller; }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Count > 1) { throw new AggregateException("The original queued callers failed after actual settlement.", errors); }
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IConsecutiveFailureMonitor> monitors = [];
        private readonly List<Task<HealthCollectionAttempt>> actualOperations = [];
        private readonly List<Task<HealthCollectionAttempt>> originalCallers = [];
        private Task? cleanup;
        internal Scope()
        {
            Service = new(new() { MaxConcurrentCollections = 1 },
                new HealthApplicationIdentity { Name = "abandoned queued caller", InstanceId = Guid.NewGuid() });
            Runner = new(Service);
        }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal Exception? Primary { get; set; }
        internal GatedOwner OccupiedOwner { get; } = new();
        internal GatedOwner SentinelOwner { get; } = new();
        internal RefusingOwner UninvokedOwner { get; } = new();
        internal IConsecutiveFailureMonitor CreateMonitor(string name)
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new()
            {
                Name = name,
                IsCritical = true,
                FailureThreshold = 1
            });
            monitors.Add(monitor);
            return monitor;
        }
        internal void TrackActualOperation(Task<HealthCollectionAttempt> operation) => actualOperations.Add(operation);
        internal void TrackOriginalCaller(Task<HealthCollectionAttempt> caller) => originalCallers.Add(caller);
        public ValueTask DisposeAsync() => new(cleanup ??= DrainOnceAsync());

        private async Task DrainOnceAsync()
        {
            // These owners represent actual invoked work. Mark/release every
            // existing or concurrently acquired lease even when the direct
            // abandoned-notification assertion has failed.
            OccupiedOwner.ReleaseAll();
            SentinelOwner.ReleaseAll();
            try
            {
                await HealthCollectorTestCleanup.DrainAsync(Deadline, Primary, originalCallers,
                    () => Runner.DisposeAsync().AsTask(),
                    () => Service.DisposeAsync().AsTask(),
                    () => Task.WhenAll(actualOperations));
            }
            finally { GC.KeepAlive(monitors); }
            await ObserveCompletedCallersAsync(originalCallers);
            // The orphaned notification is asserted separately. Actual owner,
            // state and runner drains above still have to finish successfully.
        }
    }

    private sealed class Invocation
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Released => released.Task;
        internal void Release() => released.TrySetResult();
    }

    private sealed class GatedOwner : IHealthCollector
    {
        private readonly object gate = new();
        private readonly List<Invocation> invocations = [];
        private readonly Channel<Invocation> entered = Channel.CreateUnbounded<Invocation>();
        private int acquisitions, cleanups;
        private bool releasing;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this);
            return true;
        }
        internal async Task<Invocation> NextAsync(Task<HealthCollectionAttempt> actualCaller)
        {
            using var entryWait = new CancellationTokenSource();
            Task<Invocation> entry = entered.Reader.ReadAsync(entryWait.Token).AsTask();
            try
            {
                await Task.WhenAny(entry, actualCaller).WaitAsync(Deadline);
                Assert.IsFalse(actualCaller.IsCompleted, "The actual collection cannot settle before its expected blocked callback enters.");
                return await entry.WaitAsync(Deadline);
            }
            finally { entryWait.Cancel(); }
        }
        internal void ReleaseAll()
        {
            lock (gate)
            {
                releasing = true;
                foreach (Invocation invocation in invocations) { invocation.Release(); }
            }
        }
        private async Task<HealthCollectionResult> EvaluateAsync(CancellationToken cancellation)
        {
            var invocation = new Invocation();
            lock (gate)
            {
                invocations.Add(invocation);
                if (releasing) { invocation.Release(); }
            }
            entered.Writer.TryWrite(invocation);
            await invocation.Released;
            cancellation.ThrowIfCancellationRequested();
            return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
        }
        private sealed class Lease(GatedOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) => owner.EvaluateAsync(lifetimeCancellation);
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.cleanups);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RefusingOwner : IHealthCollector
    {
        private int acquisitions;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = null;
            return false;
        }
    }
}
