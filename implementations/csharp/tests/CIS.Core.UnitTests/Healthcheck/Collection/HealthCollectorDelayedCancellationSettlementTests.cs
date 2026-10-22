using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorDelayedCancellationSettlementTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinishedLeaseCleanupDoesNotReleaseStateSettlementWhileItsCancellationObserverStillRuns(bool queueAnotherOwner)
    {
        await using var scope = new Scope();
        Owner owner = scope.CreateOwner(blockCancellation: true);
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        IHealthCollectionRegistration registration = scope.Register(monitor, owner);
        Owner nextOwner = scope.CreateOwner(blockCancellation: false);
        IConsecutiveFailureMonitor? nextMonitor = null;
        IHealthCollectionRegistration? nextRegistration = null;
        if (queueAnotherOwner)
        {
            nextMonitor = scope.Monitor();
            nextRegistration = scope.Register(nextMonitor, nextOwner);
        }
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> collection = registration.CollectAsync();
        await owner.EvaluationEntered.Task.WaitAsync(Deadline);
        Task<HealthCollectionAttempt>? queued = nextRegistration?.CollectAsync();
        Task withdrawal = Task.Run(registration.Withdraw);
        Task? stop = null;
        try
        {
            await owner.CancellationEntered.Task.WaitAsync(Deadline);
            Assert.IsTrue(owner.LifetimeCancellation.IsCancellationRequested);
            owner.ReleaseEvaluation.TrySetResult();
            await owner.CleanupFinished.Task.WaitAsync(Deadline);

            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(0, owner.ActiveLeases);
            Assert.IsTrue(owner.ResourceDisposed);
            Assert.IsFalse(owner.CancellationFinished);
            Assert.IsFalse(withdrawal.IsCompleted);
            Assert.IsFalse(collection.IsCompleted, "Manual completion includes actual state cancellation settlement after the owner lease is cleaned up.");
            Task drain = registration.WaitForDisposalAsync();
            Assert.IsFalse(drain.IsCompleted);
            HealthSnapshot beforeRelease = scope.Service.CaptureSnapshot();
            HealthMonitorSnapshot retained = beforeRelease.Monitors.Single(item => item.MonitorId == monitor.MonitorId);
            Assert.IsTrue(retained.Collection!.InProgress);
            Assert.IsNull(retained.Collection.LastCompletedAtUtc);
            Assert.IsNull(retained.Collection.LastError);
            Assert.AreEqual(0L, retained.Outcomes!.Successful);
            Assert.AreEqual(0L, retained.Outcomes.Failed);
            if (queueAnotherOwner)
            {
                Assert.IsNotNull(queued);
                Assert.IsFalse(queued.IsCompleted);
                Assert.AreEqual(0, nextOwner.Acquisitions, "The global slot includes outstanding state settlement, even after lease cleanup.");
                Assert.IsFalse(nextOwner.EvaluationEntered.Task.IsCompleted);
            }
            else
            {
                stop = scope.Runner.StopAsync();
                Assert.IsFalse(stop.IsCompleted, "Stopping must include the still-running cancellation observer.");
            }

            owner.ReleaseCancellation.Set();
            await withdrawal.WaitAsync(Deadline);
            HealthCollectionAttempt attempt = await collection.WaitAsync(Deadline);
            await drain.WaitAsync(Deadline);

            Assert.IsTrue(owner.CancellationFinished);
            Assert.IsTrue(owner.CallbackReleased);
            Assert.AreEqual(1, owner.CancellationCalls);
            Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
            if (queueAnotherOwner)
            {
                await nextOwner.EvaluationEntered.Task.WaitAsync(Deadline);
                Assert.AreEqual(1, nextOwner.Acquisitions);
                Assert.AreEqual(1, nextOwner.ActiveLeases);
                nextOwner.ReleaseEvaluation.TrySetResult();
                HealthCollectionAttempt next = await queued!.WaitAsync(Deadline);
                Assert.AreEqual(HealthCollectionDisposition.Committed, next.Disposition);
                Assert.IsFalse(next.EvaluationFailed);
                Assert.AreEqual(1, nextOwner.Cleanups);
                Assert.AreEqual(0, nextOwner.ActiveLeases);
                Assert.AreEqual(1L, scope.Service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == nextMonitor!.MonitorId).Outcomes!.Successful);
            }
            else
            {
                await stop!.WaitAsync(Deadline);
                Assert.AreEqual(beforeRelease.Revision + 1, scope.Service.CaptureSnapshot().Revision,
                    "Final cancellation settlement changes the still-active monitor's observable collection state once.");
            }
            HealthMonitorSnapshot settled = scope.Service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
            Assert.IsFalse(settled.Collection!.InProgress);
            Assert.IsNull(settled.Collection.LastError);
            Assert.AreEqual(0L, settled.Outcomes!.Successful);
            Assert.AreEqual(0L, settled.Outcomes.Failed);
        }
        finally
        {
            owner.ReleaseCancellation.Set();
            owner.ReleaseEvaluation.TrySetResult();
            nextOwner.ReleaseEvaluation.TrySetResult();
            await withdrawal.WaitAsync(Deadline);
            // This registration is deliberately disposed only after its callback
            // has returned; lease cleanup must not supply the missing state wait.
            owner.DisposeCancellationRegistration();
        }
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IHealthMonitor> monitors = [];
        private readonly List<IHealthCollectionRegistration> registrations = [];
        private readonly List<Owner> owners = [];
        internal Scope()
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero));
            Service = new HealthcheckService(new() { MaxConcurrentCollections = 1 },
                new() { Name = "delayed cancellation settlement", InstanceId = Guid.NewGuid() }, clock);
            Runner = new HealthCollectorRunner(Service);
        }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal Owner CreateOwner(bool blockCancellation)
        {
            var owner = new Owner(blockCancellation);
            owners.Add(owner);
            return owner;
        }
        internal IConsecutiveFailureMonitor Monitor()
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            monitors.Add(monitor);
            return monitor;
        }
        internal IHealthCollectionRegistration Register(IHealthMonitor monitor, Owner owner)
        {
            IHealthCollectionRegistration registration = Runner.Register(monitor, owner, new());
            registrations.Add(registration);
            return registration;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (Owner owner in owners) { owner.ReleaseCancellation.Set(); owner.ReleaseEvaluation.TrySetResult(); }
            foreach (IHealthCollectionRegistration registration in registrations) { registration.Withdraw(); }
            try
            {
                await Runner.DisposeAsync().AsTask().WaitAsync(Deadline);
                await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
            }
            finally
            {
                foreach (Owner owner in owners) { owner.Dispose(); }
                GC.KeepAlive(monitors);
            }
        }
    }

    private sealed class Owner(bool blockCancellation) : IHealthCollector, IDisposable
    {
        private int acquisitions, evaluations, cleanups, activeLeases, cancellationCalls, cancellationFinished, resourceDisposed;
        private CancellationTokenRegistration cancellation;
        private bool BlocksCancellation { get; } = blockCancellation;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal int ActiveLeases => Volatile.Read(ref activeLeases);
        internal int CancellationCalls => Volatile.Read(ref cancellationCalls);
        internal bool CancellationFinished => Volatile.Read(ref cancellationFinished) != 0;
        internal bool ResourceDisposed => Volatile.Read(ref resourceDisposed) != 0;
        internal bool CallbackReleased { get; private set; }
        internal CancellationToken LifetimeCancellation { get; private set; }
        internal TaskCompletionSource EvaluationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseEvaluation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim ReleaseCancellation { get; } = new();
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            Interlocked.Increment(ref activeLeases);
            lease = new Lease(this);
            return true;
        }
        internal void DisposeCancellationRegistration() => cancellation.Dispose();
        public void Dispose() { DisposeCancellationRegistration(); ReleaseCancellation.Dispose(); }
        private sealed class Lease(Owner owner) : IHealthCollectionLease
        {
            private readonly MemoryStream resource = new();
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.evaluations);
                owner.LifetimeCancellation = lifetimeCancellation;
                if (owner.BlocksCancellation)
                {
                    owner.cancellation = lifetimeCancellation.Register(() =>
                    {
                        Interlocked.Increment(ref owner.cancellationCalls);
                        owner.CancellationEntered.TrySetResult();
                        owner.CallbackReleased = owner.ReleaseCancellation.Wait(Deadline);
                        Volatile.Write(ref owner.cancellationFinished, 1);
                    });
                }
                owner.EvaluationEntered.TrySetResult();
                await owner.ReleaseEvaluation.Task;
                // An owner may finish independently of its advisory cancellation.
                resource.WriteByte(1);
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }
            public ValueTask DisposeAsync()
            {
                resource.Dispose();
                Volatile.Write(ref owner.resourceDisposed, 1);
                Interlocked.Increment(ref owner.cleanups);
                Interlocked.Decrement(ref owner.activeLeases);
                owner.CleanupFinished.TrySetResult();
                return ValueTask.CompletedTask;
            }
        }
    }
}
