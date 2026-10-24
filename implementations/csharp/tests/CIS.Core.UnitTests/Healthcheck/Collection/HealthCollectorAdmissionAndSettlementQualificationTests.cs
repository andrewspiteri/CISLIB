using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorAdmissionAndSettlementQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("acquisition", "runner-stop")]
    [DataRow("acquisition", "runner-dispose")]
    [DataRow("acquisition", "registration-dispose")]
    [DataRow("acquisition", "collection")]
    [DataRow("evaluation", "runner-stop")]
    [DataRow("evaluation", "runner-dispose")]
    [DataRow("evaluation", "registration-dispose")]
    [DataRow("evaluation", "collection")]
    [DataRow("cleanup", "runner-stop")]
    [DataRow("cleanup", "runner-dispose")]
    [DataRow("cleanup", "registration-dispose")]
    [DataRow("cleanup", "collection")]
    public async Task OwnerCallbacksRejectTheirOwnPendingBarrierWithoutAwaitingTheReturnedTask(string phase, string barrier)
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        Owner owner = scope.Owner();
        IHealthCollectionRegistration registration = scope.Register(monitor, owner);
        Exception? rejected = null;
        Task? acceptedWait = null;
        int observations = 0;

        void ObserveOwnBarrier()
        {
            Interlocked.Increment(ref observations);
            try
            {
                Task wait = barrier switch
                {
                    "runner-stop" => scope.Runner.StopAsync(),
                    "runner-dispose" => scope.Runner.DisposeAsync().AsTask(),
                    "registration-dispose" => registration.DisposeAsync().AsTask(),
                    _ => registration.CollectAsync()
                };
                // An async ValueTask can carry an immediate rejection as an already-faulted
                // task. Never await an incorrectly returned task containing this actual lease.
                if (wait.IsFaulted) { rejected = wait.Exception!.InnerExceptions.Single(); }
                else { acceptedWait = wait; }
            }
            catch (Exception error) { rejected = error; }
        }

        if (phase == "acquisition") { owner.Acquiring = ObserveOwnBarrier; }
        else if (phase == "evaluation") { owner.Evaluating = ObserveOwnBarrier; }
        else { owner.Cleaning = ObserveOwnBarrier; }
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt result = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, observations);
        Assert.IsNotNull(rejected, "A known executing owner must be rejected before it can wait for its own pending operation.");
        Assert.AreEqual(typeof(InvalidOperationException), rejected.GetType());
        Assert.IsNull(acceptedWait);
        Assert.IsFalse(result.EvaluationFailed);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, owner.ActiveLeases);
        Assert.IsFalse(owner.ResourceDisposed);
        Assert.AreEqual(phase == "acquisition" && barrier != "collection" ? 0 : 1, owner.Evaluations);
        if (barrier == "collection")
        {
            Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
            Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);
        }
        else if (barrier == "registration-dispose")
        {
            await registration.WaitForDisposalAsync().WaitAsync(Deadline);
        }
        else { await scope.Runner.StopAsync().WaitAsync(Deadline); }
        monitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, scope.Snapshot(monitor).State);
    }

    [TestMethod]
    public async Task PreCanceledQueuedRequestCannotCreateWorkBeforeARealFollowingDispatcherWitness()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor blockingMonitor = scope.Monitor();
        Owner blocker = scope.Owner(blockEvaluation: true);
        IHealthCollectionRegistration blocking = scope.Register(blockingMonitor, blocker);
        IConsecutiveFailureMonitor canceledMonitor = scope.Monitor();
        Owner canceledOwner = scope.Owner();
        IHealthCollectionRegistration canceled = scope.Register(canceledMonitor, canceledOwner);
        IConsecutiveFailureMonitor witnessMonitor = scope.Monitor();
        Owner witness = scope.Owner(blockEvaluation: true);
        IHealthCollectionRegistration following = scope.Register(witnessMonitor, witness);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> first = blocking.CollectAsync();
        await WaitForBlockedEvaluationAsync(blocker, first);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(() => canceled.CollectAsync(cancellation.Token)).WaitAsync(Deadline);

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, canceledOwner.Acquisitions);
        Task<HealthCollectionAttempt> progress = following.CollectAsync();
        blocker.Release();
        await WaitForBlockedEvaluationAsync(witness, progress);

        // With one occupied slot, this later registered owner is a real dispatcher
        // progress witness; the canceled request cannot hide behind an immediate race.
        Assert.AreEqual(0, canceledOwner.Acquisitions);
        Assert.AreEqual(0, canceledOwner.Evaluations);
        Assert.AreEqual(0, canceledOwner.Cleanups);
        Assert.IsFalse(scope.Snapshot(canceledMonitor).Collection!.InProgress);
        Assert.AreEqual(0L, scope.Snapshot(canceledMonitor).Outcomes!.Successful);
        witness.Release();
        await Task.WhenAll(first, progress).WaitAsync(Deadline);

        HealthCollectionAttempt actual = await canceled.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, actual.Disposition);
        Assert.IsFalse(actual.EvaluationFailed);
        Assert.AreEqual(1, canceledOwner.Acquisitions);
        Assert.AreEqual(1, canceledOwner.Evaluations);
        Assert.AreEqual(1, canceledOwner.Cleanups);
        Assert.AreEqual(1L, scope.Snapshot(canceledMonitor).Outcomes!.Successful);
    }

    [TestMethod]
    public async Task AFailedCompletionUtcSampleRetainsItsDiagnosticAlongsideTheAlreadyCommittedSuccess()
    {
        var clockFailure = new CompletionClockException();
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(clockFailure, "Healthcheck.CollectionClock"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual completion-clock exception must support its valid diagnostic projection before collection begins.");
        Assert.IsNotNull(projected);
        Assert.AreEqual("Healthcheck.CollectionClock", projected.Code);
        Assert.AreEqual(clockFailure.Message, projected.Message);
        Assert.AreEqual(typeof(CompletionClockException).FullName, projected.ExceptionType);
        Assert.IsFalse(projected.IsTruncated);

        var clock = new CompletionFaultClock { Failure = clockFailure };
        await using var scope = new Scope(clock);
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        Owner owner = scope.Owner();
        owner.Cleaning = clock.FailNextUtcOnThisThread;
        IHealthCollectionRegistration registration = scope.Register(monitor, owner);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt result = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
        Assert.IsFalse(result.EvaluationFailed);
        HealthMonitorSnapshot snapshot = scope.Snapshot(monitor);
        Assert.AreEqual(MonitorState.Healthy, snapshot.State);
        Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.IsFalse(snapshot.Collection!.InProgress);
        Assert.AreEqual("Healthcheck.CollectionClock", snapshot.Collection.LastError!.Code);
        Assert.AreEqual(typeof(CompletionClockException).FullName, snapshot.Collection.LastError.ExceptionType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.Collection.LastError.Message));
        Assert.IsNotNull(snapshot.Collection.LastCompletedAtUtc);
        Assert.AreEqual(snapshot.Collection.LastCompletedAtUtc, snapshot.Collection.LastErrorAtUtc);
        Assert.AreEqual(TimeSpan.Zero, snapshot.Collection.LastCompletedAtUtc.Value.Offset);
        Assert.AreEqual(1, clock.Faults);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, owner.ActiveLeases);

        owner.Cleaning = null;
        HealthCollectionAttempt next = await registration.CollectAsync().WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Committed, next.Disposition);
        Assert.IsFalse(next.EvaluationFailed);
        Assert.AreEqual(2L, scope.Snapshot(monitor).Outcomes!.Successful);
        Assert.AreEqual(1, clock.Faults);
        Assert.AreEqual(2, owner.Cleanups);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IHealthMonitor> monitors = [];
        private readonly List<Owner> owners = [];
        internal Scope(TimeProvider? clock = null)
        {
            Service = new HealthcheckService(new HealthcheckOptions { MaxConcurrentCollections = 1 },
                new HealthApplicationIdentity { Name = "collector admission milestones", InstanceId = Guid.NewGuid() },
                clock ?? new ManualTimeProvider(Epoch));
            Runner = new HealthCollectorRunner(Service);
        }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal IConsecutiveFailureMonitor Monitor()
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            monitors.Add(monitor);
            return monitor;
        }
        internal Owner Owner(bool blockEvaluation = false)
        {
            var owner = new Owner(blockEvaluation);
            owners.Add(owner);
            return owner;
        }
        internal IHealthCollectionRegistration Register(IHealthMonitor monitor, Owner owner) =>
            Runner.Register(monitor, owner, new HealthCollectorOptions());
        internal HealthMonitorSnapshot Snapshot(IHealthMonitor monitor) => Service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
        public async ValueTask DisposeAsync()
        {
            foreach (Owner owner in owners) { owner.Release(); }
            Task runner = DrainRunnerAsync();
            Task service = DrainServiceAsync();
            try { await Task.WhenAll(runner, service).WaitAsync(Deadline); }
            finally
            {
                foreach (Owner owner in owners) { owner.DisposeReleasedResource(); }
                GC.KeepAlive(monitors);
            }

            async Task DrainRunnerAsync() => await Runner.DisposeAsync();
            async Task DrainServiceAsync() => await Service.DisposeAsync();
        }
    }

    private static async Task WaitForBlockedEvaluationAsync(Owner owner, Task<HealthCollectionAttempt> actualCaller)
    {
        await Task.WhenAny(owner.EvaluationEntered.Task, owner.CleanupEntered.Task, actualCaller).WaitAsync(Deadline);
        Assert.IsTrue(owner.EvaluationEntered.Task.IsCompletedSuccessfully, "The actual collection must invoke its callback before lease cleanup or settlement.");
        Assert.IsFalse(actualCaller.IsCompleted, "The actual collection cannot settle while its evaluation gate remains held.");
    }

    private sealed class Owner : IHealthCollector
    {
        private readonly MemoryStream resource = new();
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int acquisitions, evaluations, cleanups, active;
        internal Owner(bool blockEvaluation)
        {
            if (!blockEvaluation) { release.TrySetResult(); }
        }
        internal Action? Acquiring { get; set; }
        internal Action? Evaluating { get; set; }
        internal Action? Cleaning { get; set; }
        internal TaskCompletionSource EvaluationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal int ActiveLeases => Volatile.Read(ref active);
        internal bool ResourceDisposed => !resource.CanWrite;
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            Interlocked.Increment(ref active);
            lease = new Lease(this);
            Acquiring?.Invoke();
            return true;
        }
        internal void Release() => release.TrySetResult();
        internal void DisposeReleasedResource()
        {
            if (ActiveLeases == 0) { resource.Dispose(); }
        }
        private sealed class Lease(Owner owner) : IHealthCollectionLease
        {
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.evaluations);
                owner.resource.WriteByte(42);
                owner.Evaluating?.Invoke();
                owner.EvaluationEntered.TrySetResult();
                await owner.release.Task;
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }
            public ValueTask DisposeAsync()
            {
                owner.CleanupEntered.TrySetResult();
                try { owner.Cleaning?.Invoke(); }
                finally
                {
                    Interlocked.Increment(ref owner.cleanups);
                    Interlocked.Decrement(ref owner.active);
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class CompletionFaultClock : TimeProvider
    {
        private readonly ManualTimeProvider inner = new(Epoch);
        private int armedThread, armed, faults;
        internal CompletionClockException? Failure { get; init; }
        internal int Faults => Volatile.Read(ref faults);
        internal void FailNextUtcOnThisThread()
        {
            Volatile.Write(ref armedThread, Environment.CurrentManagedThreadId);
            Volatile.Write(ref armed, 1);
        }
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override long GetTimestamp() => inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow()
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref armedThread) && Interlocked.Exchange(ref armed, 0) != 0)
            {
                Interlocked.Increment(ref faults);
                throw Failure ?? new CompletionClockException();
            }
            return inner.GetUtcNow();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class CompletionClockException : Exception;
}
