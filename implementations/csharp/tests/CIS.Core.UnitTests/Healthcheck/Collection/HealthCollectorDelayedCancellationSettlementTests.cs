using System.Runtime.ExceptionServices;
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
        Task<HealthCollectionAttempt>? collection = null, queued = null;
        var scope = new Scope();
        var actualTasks = new List<Task>();
        var errors = new List<Exception>();
        Exception? primary = null;
        try
        {
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
            Task started = scope.Runner.StartAsync();
            actualTasks.Add(started);
            await started.WaitAsync(Deadline);
            collection = registration.CollectAsync();
            await Task.WhenAny(owner.EvaluationEntered.Task, collection).WaitAsync(Deadline);
            Assert.IsFalse(collection.IsCompleted, "The actual uncanceled caller cannot settle before its gated owner evaluation is released.");
            await owner.EvaluationEntered.Task.WaitAsync(Deadline);
            Task<HealthCollectionAttempt> operation = registration.CollectAsync();
            actualTasks.Add(operation);
            queued = nextRegistration?.CollectAsync();
            Task withdrawal = Task.Run(registration.Withdraw);
            actualTasks.Add(withdrawal);
            Task? stop = null;
            await Task.WhenAny(owner.CancellationEntered.Task, collection, withdrawal).WaitAsync(Deadline);
            Assert.IsFalse(collection.IsCompleted, "The actual caller must retain the evaluation and cancellation observer until both are released.");
            Assert.IsFalse(withdrawal.IsCompleted, "The actual withdrawal must retain its still-running cancellation observer.");
            await owner.CancellationEntered.Task.WaitAsync(Deadline);
            Assert.IsTrue(owner.LifetimeCancellation.IsCancellationRequested);
            owner.ReleaseEvaluation.TrySetResult();
            await Task.WhenAny(owner.CleanupFinished.Task, collection).WaitAsync(Deadline);
            Assert.IsFalse(collection.IsCompleted, "Lease cleanup cannot release the caller while its cancellation observer is still gated.");
            await owner.CleanupFinished.Task.WaitAsync(Deadline);

            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(0, owner.ActiveLeases);
            Assert.IsTrue(owner.ResourceDisposed);
            Assert.IsFalse(owner.CancellationFinished);
            Assert.IsFalse(withdrawal.IsCompleted);
            Assert.IsFalse(collection.IsCompleted, "Manual completion includes actual state cancellation settlement after the owner lease is cleaned up.");
            Task drain = registration.WaitForDisposalAsync();
            actualTasks.Add(drain);
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
                actualTasks.Add(stop);
                Assert.IsFalse(stop.IsCompleted, "Stopping must include the still-running cancellation observer.");
            }

            owner.ReleaseCancellation.Set();
            await withdrawal.WaitAsync(Deadline);
            Assert.IsTrue(owner.CancellationFinished);
            Assert.IsTrue(owner.CallbackReleased);
            Assert.AreEqual(1, owner.CancellationCalls);

            await operation.WaitAsync(Deadline);
            await drain.WaitAsync(Deadline);
            if (queueAnotherOwner)
            {
                await Task.WhenAny(nextOwner.EvaluationEntered.Task, queued!).WaitAsync(Deadline);
                Assert.IsFalse(queued!.IsCompleted, "The queued caller cannot settle before its own gated evaluation is released.");
                await nextOwner.EvaluationEntered.Task.WaitAsync(Deadline);
                Task<HealthCollectionAttempt> nextOperation = nextRegistration!.CollectAsync();
                actualTasks.Add(nextOperation);
                Assert.AreEqual(1, nextOwner.Acquisitions);
                Assert.AreEqual(1, nextOwner.ActiveLeases);
                nextOwner.ReleaseEvaluation.TrySetResult();
                await nextOperation.WaitAsync(Deadline);
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
        catch (Exception error) { primary = error; }
        finally
        {
            // Release the actual evaluation and cancellation gates before any
            // resource join, including when an early pending assertion failed.
            scope.ReleaseOwners();
            Task disposal = DisposeScopeAsync();
            actualTasks.Add(disposal);
            Task drains = Task.WhenAll(actualTasks);
            if (primary is not null) { errors.Add(primary); }
            try { await drains.WaitAsync(Deadline); }
            catch (Exception error) { RecordTaskError(drains, error); }
            // A completed sibling's real fault must survive even when another
            // actual drain remains pending and WhenAll.Exception is still null.
            foreach (Task actual in actualTasks)
            {
                if (!actual.IsCompleted) { continue; }
                try { await actual; }
                catch (Exception error) { RecordTaskError(actual, error); }
            }
            // These are caller notifications, not operation/resource barriers.
            // Observe only completed errors here; never wait for an orphan.
            foreach (Task original in new[] { collection, queued }.OfType<Task>())
            {
                if (!original.IsCompleted) { continue; }
                try { await original; }
                catch (Exception error) { RecordTaskError(original, error); }
            }
        }
        if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Count > 1) { throw new AggregateException("The delayed-cancellation body, original callers or actual resource drains failed.", errors); }
        Task<HealthCollectionAttempt>[] originals = queued is null ? [collection!] : [collection!, queued];
        await AssertOriginalCallersCompleteAsync(originals);
        HealthCollectionAttempt attempt = await collection!;
        Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        if (queued is not null)
        {
            HealthCollectionAttempt next = await queued;
            Assert.AreEqual(HealthCollectionDisposition.Committed, next.Disposition);
            Assert.IsFalse(next.EvaluationFailed);
        }

        async Task DisposeScopeAsync() => await scope.DisposeAsync();
        void RecordTaskError(Task actual, Exception error)
        {
            if (error is TimeoutException) { Add(error); }
            if (actual.Exception is { } aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions) { Add(inner); }
            }
            else { Add(error); }
        }
        void Add(Exception error) { if (!errors.Any(known => ReferenceEquals(known, error))) { errors.Add(error); } }
    }

    private static async Task AssertOriginalCallersCompleteAsync(Task<HealthCollectionAttempt>[] callers)
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
        Assert.IsTrue(callers.All(caller => caller.IsCompleted),
            "The original queued callers must complete after all actual operations and resources have settled.");
        await Task.WhenAll(callers);
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
        internal void ReleaseOwners()
        {
            foreach (Owner owner in owners) { owner.ReleaseCancellation.Set(); owner.ReleaseEvaluation.TrySetResult(); }
        }
        public async ValueTask DisposeAsync()
        {
            ReleaseOwners();
            Task runner = DrainRunnerAsync();
            Task service = DrainServiceAsync();
            Task[] actual = [runner, service, .. registrations.Select(RequestWithdrawal)];
            Task drains = Task.WhenAll(actual);
            var errors = new List<Exception>();
            try
            {
                try { await drains.WaitAsync(Deadline); }
                catch (Exception error) { RecordTaskError(drains, error); }
                foreach (Task task in actual)
                {
                    if (!task.IsCompleted) { continue; }
                    try { await task; }
                    catch (Exception error) { RecordTaskError(task, error); }
                }
            }
            finally
            {
                // These real owner registrations/handles are released after
                // the resource-drain attempt; no pending caller is substituted.
                foreach (Owner owner in owners)
                {
                    try { owner.Dispose(); }
                    catch (Exception error) { Add(error); }
                }
                GC.KeepAlive(monitors);
            }
            if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
            if (errors.Count > 1) { throw new AggregateException("The actual delayed-cancellation resources did not drain successfully.", errors); }

            void RecordTaskError(Task task, Exception error)
            {
                if (error is TimeoutException) { Add(error); }
                if (task.Exception is { } aggregate)
                {
                    foreach (Exception inner in aggregate.InnerExceptions) { Add(inner); }
                }
                else { Add(error); }
            }
            void Add(Exception error) { if (!errors.Any(known => ReferenceEquals(known, error))) { errors.Add(error); } }
            async Task DrainRunnerAsync() => await Runner.DisposeAsync();
            async Task DrainServiceAsync() => await Service.DisposeAsync();
            static Task RequestWithdrawal(IHealthCollectionRegistration registration)
            {
                try { registration.Withdraw(); return Task.CompletedTask; }
                catch (Exception error) { return Task.FromException(error); }
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
                        owner.ReleaseCancellation.Wait();
                        owner.CallbackReleased = true;
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
