using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorUncoveredPathTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task SelectionDoesNotRetainAnUnselectedHandleWhileInvokingAnotherOwner()
    {
        var service = new HealthcheckService(new() { MaxConcurrentCollections = 1 });
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor selectedMonitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IConsecutiveFailureMonitor idleMonitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var selectedOwner = new ReleasingOwner();
        var idleOwner = new NextOwner();
        IHealthCollectionRegistration? selected = null;
        Task<HealthCollectionAttempt>? caller = null;
        Exception? primary = null;
        try
        {
            selected = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.Register(selectedMonitor, selectedOwner, new()));
            WeakReference idleHandle = RetainIdleRegistration(runner, idleMonitor, idleOwner, selectedOwner);
            selectedOwner.IdleHandle = idleHandle;
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => selected.CollectAsync());
            HealthCollectionAttempt result = await caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
            Assert.IsFalse(result.EvaluationFailed);
            Assert.IsNull(result.Diagnostic);
            Assert.IsTrue(selectedOwner.UnselectedWasCollected,
                "The selected owner's external acquisition must not inherit strong retention from the runner's inspection of another idle handle.");
            Assert.IsFalse(idleHandle.IsAlive);
            Assert.AreEqual(0, idleOwner.Acquisitions);
            HealthSnapshot snapshot = service.CaptureSnapshot();
            Assert.AreEqual(1L, snapshot.Monitors.Single(item => item.MonitorId == selectedMonitor.MonitorId).Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Monitors.Single(item => item.MonitorId == idleMonitor.MonitorId).Outcomes!.Successful);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, caller is null ? [] : [caller],
                () => selected?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(),
                () => service.DisposeAsync().AsTask());
            GC.KeepAlive(selectedMonitor);
            GC.KeepAlive(idleMonitor);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetainIdleRegistration(HealthCollectorRunner runner, IHealthMonitor monitor,
        IHealthCollector idleOwner, ReleasingOwner selectedOwner)
    {
        IHealthCollectionRegistration handle = runner.Register(monitor, idleOwner, new());
        selectedOwner.Retain(handle);
        return new(handle);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualCancellationSettlementOwnsTheCallerAndCapacityAfterLeaseCleanup(bool stopWhileCallbackRuns)
    {
        using var owner = new CancellationOwner();
        var clock = new SettlementClock(owner.Cleanup);
        var service = new HealthcheckService(new() { MaxConcurrentCollections = 1 }, timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor firstMonitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IConsecutiveFailureMonitor nextMonitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var nextOwner = new NextOwner();
        IHealthCollectionRegistration? first = null, next = null;
        List<Task> callers = [];
        Task? withdrawal = null, completing = null, stop = null;
        Exception? primary = null;
        try
        {
            first = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.Register(firstMonitor, owner, new()));
            next = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.Register(nextMonitor, nextOwner, new()));
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            Task<HealthCollectionAttempt> original = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => first.CollectAsync());
            callers.Add(original);
            await owner.Cleanup.ContinuationRegistered.Task.WaitAsync(Deadline);
            Task<HealthCollectionAttempt> joined = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => first.CollectAsync());
            callers.Add(joined);
            Task<HealthCollectionAttempt> queued = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => next.CollectAsync());
            callers.Add(queued);

            withdrawal = Task.Run(first.Withdraw);
            await owner.CancellationEntered.Task.WaitAsync(Deadline);
            Thread? driverThread = null;
            completing = Task.Run(() =>
            {
                driverThread = Thread.CurrentThread;
                owner.Cleanup.Complete();
            });
            await completing.WaitAsync(Deadline);
            Assert.AreSame(driverThread, await clock.CompletionUtcRead.Task.WaitAsync(Deadline),
                "The actual disposal continuation must have run inline before its completion driver returned.");
            Assert.IsTrue(owner.Cleanup.ResourceDisposed);
            Assert.IsFalse(withdrawal.IsCompleted);
            Assert.IsFalse(original.IsCompleted, "Lease cleanup does not complete the actual cancellation callback.");
            Assert.IsFalse(joined.IsCompleted, "All callers still join the same state-owned settlement.");
            Assert.IsFalse(queued.IsCompleted);
            Assert.AreEqual(0, nextOwner.Acquisitions, "A callback still inside actual cancellation owns the sole capacity slot.");
            Assert.IsTrue(service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == firstMonitor.MonitorId).Collection!.InProgress);

            if (stopWhileCallbackRuns)
            {
                stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
                Assert.IsFalse(stop.IsCompleted, "Terminal stop must await the state-owned cancellation callback too.");
                HealthCollectionAttempt queuedRetirement = await queued.WaitAsync(Deadline);
                Assert.AreEqual(HealthCollectionDisposition.Retired, queuedRetirement.Disposition);
                Assert.IsFalse(queuedRetirement.EvaluationFailed);
                Assert.IsNull(queuedRetirement.Diagnostic);
                Assert.AreEqual(0, nextOwner.Acquisitions);
            }

            owner.ReleaseCancellation();
            await withdrawal.WaitAsync(Deadline);
            HealthCollectionAttempt completed = await original.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, completed.Disposition);
            Assert.IsFalse(completed.EvaluationFailed);
            Assert.IsNull(completed.Diagnostic);
            Assert.AreSame(completed, await joined.WaitAsync(Deadline));
            if (!stopWhileCallbackRuns)
            {
                HealthCollectionAttempt admitted = await queued.WaitAsync(Deadline);
                Assert.AreEqual(HealthCollectionDisposition.Committed, admitted.Disposition);
                Assert.IsFalse(admitted.EvaluationFailed);
                Assert.IsNull(admitted.Diagnostic);
                Assert.AreEqual(1, nextOwner.Acquisitions);
                Assert.AreEqual(1, nextOwner.Cleanups);
            }
            HealthMonitorSnapshot settled = service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == firstMonitor.MonitorId);
            Assert.IsFalse(settled.Collection!.InProgress);
            Assert.AreEqual(1L, settled.Outcomes!.Successful);
            Assert.AreEqual(0L, settled.Outcomes.Failed);
            stop ??= await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
            await stop.WaitAsync(Deadline);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.ReleaseCancellation();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, callers,
                () => Task.Run(owner.Cleanup.Complete),
                () => completing ?? Task.CompletedTask,
                () => withdrawal ?? Task.CompletedTask,
                () => first?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => next?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(),
                () => service.DisposeAsync().AsTask());
            GC.KeepAlive(firstMonitor);
            GC.KeepAlive(nextMonitor);
        }
    }

    private sealed class SettlementClock(HeldCleanup cleanup) : TimeProvider
    {
        internal TaskCompletionSource<Thread> CompletionUtcRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow()
        {
            if (cleanup.ResourceDisposed) { CompletionUtcRead.TrySetResult(Thread.CurrentThread); }
            return TimeProvider.System.GetUtcNow();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class CancellationOwner : IHealthCollector, IDisposable
    {
        private readonly ManualResetEventSlim released = new();
        private CancellationTokenRegistration cancellation;
        internal HeldCleanup Cleanup { get; } = new();
        internal TaskCompletionSource CancellationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ReleaseCancellation() => released.Set();
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(this); return true; }
        public void Dispose()
        {
            released.Set();
            cancellation.Dispose();
            Cleanup.DisposeFixtureResource();
            released.Dispose();
        }
        private sealed class Lease(CancellationOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                owner.cancellation = lifetimeCancellation.Register(() =>
                {
                    owner.CancellationEntered.TrySetResult();
                    if (!owner.released.Wait(Deadline)) { throw new TimeoutException("The actual cancellation callback was not released."); }
                });
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
            // The still-running cancellation callback is independently owned by
            // the real coordinator; lease-resource disposal need not wait for it.
            public ValueTask DisposeAsync() => owner.Cleanup.WaitAsync();
        }
    }

    private sealed class HeldCleanup : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> completion = new() { RunContinuationsAsynchronously = false };
        private readonly MemoryStream resource = new();
        private int completed, resourceDisposed;
        internal TaskCompletionSource ContinuationRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ResourceDisposed => Volatile.Read(ref resourceDisposed) != 0;
        internal ValueTask WaitAsync() => new(this, completion.Version);
        internal void Complete() { if (Interlocked.Exchange(ref completed, 1) == 0) { completion.SetResult(true); } }
        internal void DisposeFixtureResource() => resource.Dispose();
        public void GetResult(short token)
        {
            completion.GetResult(token);
            resource.Dispose();
            Volatile.Write(ref resourceDisposed, 1);
        }
        public ValueTaskSourceStatus GetStatus(short token) => completion.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            completion.OnCompleted(continuation, state, token, flags);
            ContinuationRegistered.TrySetResult();
        }
    }

    private sealed class NextOwner : IHealthCollector
    {
        private int acquisitions, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this);
            return true;
        }
        private sealed class Lease(NextOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
                => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            public ValueTask DisposeAsync() { Interlocked.Increment(ref owner.cleanups); return ValueTask.CompletedTask; }
        }
    }

    private sealed class ReleasingOwner : IHealthCollector
    {
        private IHealthCollectionRegistration? retained;
        internal WeakReference IdleHandle { get; set; } = null!;
        internal bool UnselectedWasCollected { get; private set; }
        internal void Retain(IHealthCollectionRegistration registration) => retained = registration;
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            ReleaseHeldRegistration();
            for (int attempt = 0; attempt < 10 && IdleHandle.IsAlive; attempt++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            UnselectedWasCollected = !IdleHandle.IsAlive;
            lease = new Lease();
            return true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ReleaseHeldRegistration()
        {
            Assert.IsNotNull(retained, "The component owned the idle handle throughout the runner's preceding inspection.");
            retained = null;
        }
        private sealed class Lease : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
                => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
