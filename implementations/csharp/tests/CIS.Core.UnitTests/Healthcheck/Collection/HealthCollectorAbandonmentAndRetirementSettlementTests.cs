using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorAbandonmentAndRetirementSettlementTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task SweptQueuedRegistrationReleasesItsAttachmentForAReplacementOwner()
    {
        var clock = new InertTimerClock();
        var service = new HealthcheckService(new() { MaxConcurrentCollections = 1 },
            new HealthApplicationIdentity { Name = "abandoned attachment replacement", InstanceId = Guid.NewGuid() }, clock);
        var runner = new HealthCollectorRunner(service);
        var occupiedOwner = new GatedOwner();
        var sentinelOwner = new GatedOwner();
        var replacementOwner = new GatedOwner();
        var unusedOwner = new RefusingOwner();
        var monitors = new List<IConsecutiveFailureMonitor>();
        var callers = new List<Task<HealthCollectionAttempt>>();
        var operations = new List<Task<HealthCollectionAttempt>>();
        bool assertionsCompleted = false;
        IHealthCollectionRegistration? occupied = null, sentinel = null, replacement = null;
        try
        {
            IConsecutiveFailureMonitor NewMonitor(string name)
            {
                IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { Name = name, IsCritical = true, FailureThreshold = 1 });
                monitors.Add(monitor);
                return monitor;
            }
            IConsecutiveFailureMonitor occupiedMonitor = NewMonitor("occupied");
            IConsecutiveFailureMonitor retainedMonitor = NewMonitor("retained after lost registration");
            IConsecutiveFailureMonitor sentinelMonitor = NewMonitor("following actual owner");
            occupied = runner.Register(occupiedMonitor, occupiedOwner, new());
            sentinel = runner.Register(sentinelMonitor, sentinelOwner, new());
            await runner.StartAsync().WaitAsync(Deadline);
            Task<HealthCollectionAttempt> occupiedCaller = occupied.CollectAsync();
            callers.Add(occupiedCaller);
            Invocation occupiedInvocation = await occupiedOwner.NextForPendingCallerAsync(occupiedCaller);
            operations.Add(occupied.CollectAsync());

            AbandonedCaller abandoned = QueueAndForgetRegistration(runner, retainedMonitor, unusedOwner);
            for (int attempt = 0; attempt < 10 && abandoned.Handle.IsAlive; attempt++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            Assert.IsFalse(abandoned.Handle.IsAlive, "The queued registration, rather than its retained monitor or owner, must actually be collected.");
            Task<HealthCollectionAttempt> sentinelCaller = sentinel.CollectAsync();
            callers.Add(sentinelCaller);
            occupiedInvocation.Release();
            Invocation sentinelInvocation = await sentinelOwner.NextForPendingCallerAsync(sentinelCaller);
            operations.Add(sentinel.CollectAsync());
            Assert.IsTrue(abandoned.Caller.IsCompletedSuccessfully, "The next real owner enters only after the dispatcher sweeps the lost queued registration.");
            Assert.AreEqual(HealthCollectionDisposition.Retired, abandoned.Caller.Result.Disposition);
            Assert.AreEqual(0, unusedOwner.Acquisitions);

            InvalidOperationException? rejected = null;
            try { replacement = runner.Register(retainedMonitor, replacementOwner, new()); }
            catch (InvalidOperationException error) { rejected = error; }
            Assert.IsNull(rejected, "Sweeping a lost registration must release its actual attachment so its still-owned monitor can accept a replacement collector.");
            Assert.IsNotNull(replacement);
            Task<HealthCollectionAttempt> replacementCaller = replacement.CollectAsync();
            callers.Add(replacementCaller);
            sentinelInvocation.Release();
            Invocation replacementInvocation = await replacementOwner.NextForPendingCallerAsync(replacementCaller);
            operations.Add(replacement.CollectAsync());
            replacementInvocation.Release();
            await Task.WhenAll(operations).WaitAsync(Deadline);
            Assert.AreEqual(1, replacementOwner.Acquisitions);
            Assert.AreEqual(1, replacementOwner.Cleanups);
            HealthMonitorSnapshot observed = service.CaptureSnapshot().Monitors.Single(monitor => monitor.MonitorId == retainedMonitor.MonitorId);
            Assert.AreEqual(1L, observed.Outcomes!.Successful);
            Assert.AreEqual(0L, observed.Outcomes.Failed);
            Assert.AreEqual(MonitorState.Healthy, observed.State);
            assertionsCompleted = true;
        }
        finally
        {
            // The test clock owns no worker. Independently join every actual
            // lease/caller, dispose the timer and drain the real state even when
            // the tested omission strands the runner's managed closing counter.
            Task stop = runner.StopAsync();
            foreach (GatedOwner owner in new[] { occupiedOwner, sentinelOwner, replacementOwner }) { owner.ReleaseAll(); }
            await Task.WhenAll(operations).WaitAsync(Deadline);
            await clock.TimerDisposed.Task.WaitAsync(Deadline);
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
            if (assertionsCompleted || stop.IsCompleted)
            {
                await stop.WaitAsync(Deadline);
                await runner.DisposeAsync().AsTask().WaitAsync(Deadline);
            }
            await ObserveCompletedCallersAsync(callers);
            foreach (GatedOwner owner in new[] { occupiedOwner, sentinelOwner, replacementOwner })
            { Assert.AreEqual(owner.Acquisitions, owner.Cleanups, "Every actually acquired owner lease must be cleaned up."); }
            GC.KeepAlive(occupied);
            GC.KeepAlive(sentinel);
            GC.KeepAlive(replacement);
            GC.KeepAlive(monitors);
        }
        HealthCollectionAttempt[] results = await AssertOriginalCallersCompleteAsync(callers);
        Assert.IsTrue(results.All(result => result.Disposition == HealthCollectionDisposition.Committed));
    }

    [TestMethod]
    public async Task RetirementReleasesCompletedOperationAfterItsEarlierCancellationHasSettled()
    {
        var service = new HealthcheckService(new(),
            new HealthApplicationIdentity { Name = "retirement after cancellation settlement", InstanceId = Guid.NewGuid() });
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor? monitor = null;
        HealthCollectionReservation? operation = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        CancellationTokenRegistration callback = default;
        Task? withdrawal = null;
        try
        {
            monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
            HealthCollectionReservationResult reserved = state.TryReserve(target);
            operation = reserved.Reservation;
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, reserved.Disposition);
            Assert.IsNotNull(operation);
            Task settlement = state.WaitForSettlementAsync(operation);
            Assert.IsTrue(state.TryEnter(operation));
            service.BeginStopping();
            Assert.IsTrue(operation.LifetimeCancellation.IsCancellationRequested);
            callback = target.Retirement.Register(() =>
            {
                entered.TrySetResult();
                release.Wait();
            });
            withdrawal = Task.Run(monitor.Withdraw);
            await entered.Task.WaitAsync(Deadline);
            HealthCollectionAttempt result = state.Complete(operation,
                new(HealthCollectionCompletionReason.Settled, Epoch));
            Assert.AreEqual(HealthCollectionDisposition.Retired, result.Disposition);
            Assert.IsFalse(settlement.IsCompleted, "The genuine retirement observer still owns unsettled lifetime work.");
            release.Set();
            await withdrawal.WaitAsync(Deadline);

            Assert.IsTrue(release.IsSet);
            Assert.IsTrue(settlement.IsCompletedSuccessfully,
                "Returning from retirement after earlier cancellation and wrapper completion must release the previously captured settlement task.");
            Task disposal = service.DisposeAsync().AsTask();
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
        }
        finally
        {
            release.Set();
            try
            {
                if (withdrawal is not null) { await withdrawal.WaitAsync(Deadline); }
            }
            finally
            {
                // Disposal cannot wait on this callback's gate: it was released
                // before the bounded join, including assertion failures in setup.
                callback.Dispose();
                try
                {
                    if (operation is not null)
                    { state.Complete(operation, new(HealthCollectionCompletionReason.Settled, Epoch)); }
                }
                finally
                {
                    Task disposal = service.DisposeAsync().AsTask();
                    // No runner, timer, lease or external resource belongs to
                    // this seam. Never await the exact managed signal whose
                    // omission failed the immediate assertion. The callback is
                    // released and any actual withdrawal was boundedly joined.
                    if (disposal.IsCompleted) { disposal.GetAwaiter().GetResult(); }
                    GC.KeepAlive(monitor);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AbandonedCaller QueueAndForgetRegistration(HealthCollectorRunner runner, IHealthMonitor monitor, IHealthCollector owner)
    {
        IHealthCollectionRegistration registration = runner.Register(monitor, owner, new());
        Task<HealthCollectionAttempt> caller = registration.CollectAsync();
        Assert.IsFalse(caller.IsCompleted);
        var result = new AbandonedCaller(new WeakReference(registration), caller);
        GC.KeepAlive(registration);
        return result;
    }
    private sealed record AbandonedCaller(WeakReference Handle, Task<HealthCollectionAttempt> Caller);
    private static async Task<HealthCollectionAttempt[]> AssertOriginalCallersCompleteAsync(IEnumerable<Task<HealthCollectionAttempt>> callers)
    {
        Task<HealthCollectionAttempt>[] original = callers.ToArray();
        await ObserveCompletedCallersAsync(original);
        Assert.IsTrue(original.All(caller => caller.IsCompleted),
            "The original queued callers must complete after all actual operations and resources have settled.");
        return await Task.WhenAll(original);
    }
    private static async Task ObserveCompletedCallersAsync(IEnumerable<Task<HealthCollectionAttempt>> original)
    {
        var errors = new List<Exception>();
        foreach (Task<HealthCollectionAttempt> caller in original)
        {
            if (!caller.IsCompleted) { continue; }
            try { await caller; }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Count > 1) { throw new AggregateException("The original queued callers failed after actual settlement.", errors); }
    }
    private sealed class Invocation
    {
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => Released.TrySetResult();
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
        internal async Task<Invocation> NextForPendingCallerAsync(Task<HealthCollectionAttempt> actualCaller)
        {
            using var entryRead = new CancellationTokenSource();
            Task<Invocation> entry = entered.Reader.ReadAsync(entryRead.Token).AsTask().WaitAsync(Deadline);
            try
            {
                await Task.WhenAny(entry, actualCaller).WaitAsync(Deadline);
                Assert.IsFalse(actualCaller.IsCompleted,
                    "The actual collection settled before the receiving test released its owner callback.");
                return await entry;
            }
            finally { entryRead.Cancel(); }
        }
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this);
            return true;
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
            await invocation.Released.Task;
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
        public bool TryAcquire(out IHealthCollectionLease? lease) { Interlocked.Increment(ref acquisitions); lease = null; return false; }
    }
    private sealed class InertTimerClock : TimeProvider
    {
        internal TaskCompletionSource TimerDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer(TimerDisposed);
        private sealed class Timer(TaskCompletionSource disposed) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => disposed.TrySetResult();
            public ValueTask DisposeAsync() { disposed.TrySetResult(); return ValueTask.CompletedTask; }
        }
    }
}
