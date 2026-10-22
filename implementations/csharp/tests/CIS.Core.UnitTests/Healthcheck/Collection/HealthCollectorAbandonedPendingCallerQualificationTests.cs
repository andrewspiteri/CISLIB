using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorAbandonedPendingCallerQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task ACollectedQueuedRegistrationReleasesItsCallerBeforeTheNextRealOwnerIsAdmitted()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor occupiedMonitor = scope.CreateMonitor("occupied");
        IConsecutiveFailureMonitor abandonedMonitor = scope.CreateMonitor("abandoned registration only");
        IConsecutiveFailureMonitor sentinelMonitor = scope.CreateMonitor("real following owner");
        IHealthCollectionRegistration occupied = scope.Runner.Register(occupiedMonitor, scope.OccupiedOwner, new());
        IHealthCollectionRegistration sentinel = scope.Runner.Register(sentinelMonitor, scope.SentinelOwner, new());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> occupiedCaller = occupied.CollectAsync();
        scope.TrackActualCaller(occupiedCaller);
        Invocation occupiedInvocation = await scope.OccupiedOwner.NextAsync();

        AbandonedCaller abandoned = CreateAbandonedPendingCaller(scope.Runner, abandonedMonitor, scope.UninvokedOwner);
        CollectRegistrationHandle(abandoned.Handle);
        Assert.IsFalse(abandoned.Handle.IsAlive,
            "Only the real queued registration handle must be lost; the test still owns its monitor, collector and returned caller Task.");

        Task<HealthCollectionAttempt> sentinelCaller = sentinel.CollectAsync();
        scope.TrackActualCaller(sentinelCaller);
        occupiedInvocation.Release();
        Invocation sentinelInvocation = await scope.SentinelOwner.NextAsync();

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
        await Task.WhenAll(occupiedCaller, sentinelCaller).WaitAsync(Deadline);
        Assert.AreEqual(1, scope.OccupiedOwner.Acquisitions);
        Assert.AreEqual(1, scope.OccupiedOwner.Cleanups);
        Assert.AreEqual(1, scope.SentinelOwner.Acquisitions);
        Assert.AreEqual(1, scope.SentinelOwner.Cleanups);
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

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IConsecutiveFailureMonitor> monitors = [];
        private readonly List<Task<HealthCollectionAttempt>> actualCallers = [];
        internal Scope()
        {
            Service = new(new() { MaxConcurrentCollections = 1 },
                new HealthApplicationIdentity { Name = "abandoned queued caller", InstanceId = Guid.NewGuid() });
            Runner = new(Service);
        }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
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
        internal void TrackActualCaller(Task<HealthCollectionAttempt> caller) => actualCallers.Add(caller);
        public async ValueTask DisposeAsync()
        {
            // These owners represent actual invoked work. Mark/release every
            // existing or concurrently acquired lease even when the direct
            // abandoned-notification assertion has failed.
            OccupiedOwner.ReleaseAll();
            SentinelOwner.ReleaseAll();
            try
            {
                await Runner.StopAsync().WaitAsync(Deadline);
                await Task.WhenAll(actualCallers).WaitAsync(Deadline);
            }
            finally
            {
                try { await Runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally
                {
                    try { await Service.DisposeAsync().AsTask().WaitAsync(Deadline); }
                    finally { GC.KeepAlive(monitors); }
                }
            }
            // Do not await AbandonedCaller here after an assertion failure. Its
            // missing notification is exactly the tested omission, not an active
            // lease. Omitted actual retirement/closing settlement instead fails
            // bounded cleanup and cannot qualify as an assertion-backed kill.
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
        internal Task<Invocation> NextAsync() => entered.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
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
