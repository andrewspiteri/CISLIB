using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorResourceBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 19, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PeriodicCompletionAcceptsTheLastRepresentableDueTimeAndRetiresOnOverflow(bool overflow)
    {
        await using var scope = new Scope();
        var owner = new GatedOwner(scope.Clock)
        {
            TimestampAfterCleanup = overflow ? long.MaxValue : long.MaxValue - 1
        };
        IHealthCollectionRegistration registration = scope.Register(owner, TimeSpan.FromTicks(1));
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        registration.ActivatePeriodic();
        await owner.EvaluationEntered.Task.WaitAsync(Deadline);
        Task<HealthCollectionAttempt> joined = registration.CollectAsync();
        owner.ReleaseEvaluation.TrySetResult();
        await owner.CleanupEntered.Task.WaitAsync(Deadline);
        owner.ReleaseCleanup.TrySetResult();

        HealthCollectionAttempt attempt = await joined.WaitAsync(Deadline);
        if (overflow) { await registration.WaitForDisposalAsync().WaitAsync(Deadline); }
        else
        {
            await scope.Clock.Timer.Scheduled.Task.WaitAsync(Deadline);
            Assert.IsTrue(scope.Clock.Timer.ScheduledDue > TimeSpan.Zero);
            Assert.IsFalse(registration.WaitForDisposalAsync().IsCompleted);
            registration.ActivatePeriodic();
        }

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        Assert.AreEqual(1, owner.Acquisitions, "An unrepresentable next due time must not wrap into another owner acquisition.");
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        HealthMonitorSnapshot snapshot = scope.Service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.AreEqual(MonitorState.Healthy, snapshot.State);
        Assert.AreEqual(Epoch, snapshot.Collection!.LastCompletedAtUtc);
        Assert.IsNull(snapshot.Collection.LastError);
        if (overflow)
        {
            Assert.AreEqual(HealthCollectionDisposition.Retired, (await registration.CollectAsync().WaitAsync(Deadline)).Disposition);
            Assert.ThrowsExactly<ObjectDisposedException>(registration.ActivatePeriodic);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnexpectedTimerChangeFailureStopsAdmissionAndReportsActualResourceFailure(bool disposedException)
    {
        await using var scope = new Scope { ExpectedStopFailure = true };
        scope.Clock.Timer.DuringChange = () =>
        {
            if (disposedException) { throw new ObjectDisposedException("owner-timer"); }
            throw new TimerChangeException();
        };
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        // Automatic timer disposal proves the failing pump has already requested
        // stop, avoiding a caller-induced stop racing the ObjectDisposed guard.
        await scope.Clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StopAsync().WaitAsync(Deadline));

        Assert.AreEqual(1, scope.Clock.Timer.Disposals);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync());
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Register(new GatedOwner(scope.Clock)));
        scope.Monitor.ReportSuccess();
        Assert.AreEqual(1L, scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
    }

    [TestMethod]
    public async Task ADisposedTimerExceptionDuringAnActualRequestedStopIsHarmless()
    {
        await using var scope = new Scope();
        Task? stop = null;
        scope.Clock.Timer.DuringChange = () =>
        {
            stop = scope.Runner.StopAsync();
            throw new ObjectDisposedException("owner-timer");
        };
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await scope.Clock.Timer.ChangeFinished.Task.WaitAsync(Deadline);
        Assert.IsNotNull(stop);

        await stop.WaitAsync(Deadline);
        await scope.Runner.DisposeAsync().AsTask().WaitAsync(Deadline);

        Assert.AreEqual(1, scope.Clock.Timer.Disposals);
        scope.Monitor.ReportSuccess();
        Assert.AreEqual(1L, scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ThrowingOwnerCancellationAndAnUnavailableDiagnosticClockCannotStrandLeaseSettlement(bool clockFails)
    {
        await using var scope = new Scope();
        var owner = new GatedOwner(scope.Clock) { ThrowDuringCancellation = true, FailClockDuringCancellation = clockFails };
        IHealthCollectionRegistration registration = scope.Register(owner);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> collection = registration.CollectAsync();
        await owner.EvaluationEntered.Task.WaitAsync(Deadline);

        registration.Withdraw();
        Task drain = registration.WaitForDisposalAsync();

        Assert.IsTrue(owner.LifetimeCancellation.IsCancellationRequested);
        Assert.AreEqual(1, owner.CancellationCalls);
        Assert.AreEqual(clockFails ? 1 : 0, scope.Clock.UtcFaults);
        Assert.IsFalse(drain.IsCompleted);
        Assert.IsFalse(collection.IsCompleted);
        scope.Monitor.ReportSuccess();
        Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection!.LastError);
        owner.ReleaseEvaluation.TrySetResult();
        await owner.CleanupEntered.Task.WaitAsync(Deadline);
        Task stop = scope.Runner.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        Assert.IsFalse(drain.IsCompleted);
        Assert.IsFalse(collection.IsCompleted);
        owner.ReleaseCleanup.TrySetResult();

        HealthCollectionAttempt attempt = await collection.WaitAsync(Deadline);
        await Task.WhenAll(drain, stop).WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        HealthMonitorSnapshot snapshot = scope.Service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(MonitorState.Healthy, snapshot.State);
        Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.IsNull(snapshot.Collection!.LastError, "A closed collection cannot publish a late cancellation diagnostic over the retained monitor.");
        Assert.IsNull(snapshot.Collection.LastCompletedAtUtc);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<GatedOwner> owners = [];
        private readonly List<IHealthCollectionRegistration> registrations = [];
        internal Scope()
        {
            Service = new HealthcheckService(new(), new() { Name = "collector resource boundaries", InstanceId = Guid.NewGuid() }, Clock);
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Runner = new HealthCollectorRunner(Service);
        }
        internal Clock Clock { get; } = new();
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }
        internal bool ExpectedStopFailure { get; init; }
        internal IHealthCollectionRegistration Register(GatedOwner owner, TimeSpan? interval = null)
        {
            owners.Add(owner);
            IHealthCollectionRegistration registration = Runner.Register(Monitor, owner, new() { Interval = interval });
            registrations.Add(registration);
            return registration;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (GatedOwner owner in owners) { owner.ReleaseEvaluation.TrySetResult(); owner.ReleaseCleanup.TrySetResult(); }
            foreach (IHealthCollectionRegistration registration in registrations) { registration.Withdraw(); }
            try { await Runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
            catch (InvalidOperationException) when (ExpectedStopFailure) { }
            await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
            GC.KeepAlive(Monitor);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        private int failUtcThread, utcFaults;
        internal long Timestamp { get => Interlocked.Read(ref timestamp); set => Interlocked.Exchange(ref timestamp, value); }
        internal int UtcFaults => Volatile.Read(ref utcFaults);
        internal TimerResource Timer { get; } = new();
        internal void FailNextUtcOnThisThread() => Volatile.Write(ref failUtcThread, Environment.CurrentManagedThreadId);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Timestamp;
        public override DateTimeOffset GetUtcNow()
        {
            int thread = Environment.CurrentManagedThreadId;
            if (Interlocked.CompareExchange(ref failUtcThread, 0, thread) == thread)
            { Interlocked.Increment(ref utcFaults); throw new DiagnosticClockException(); }
            return Epoch;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Timer;
    }

    private sealed class TimerResource : ITimer
    {
        private int disposals;
        internal Action? DuringChange { get; set; }
        internal int Disposals => Volatile.Read(ref disposals);
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ChangeFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Scheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TimeSpan ScheduledDue { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                DuringChange?.Invoke();
                if (dueTime != Timeout.InfiniteTimeSpan) { ScheduledDue = dueTime; Scheduled.TrySetResult(); }
                return true;
            }
            finally { ChangeFinished.TrySetResult(); }
        }
        public void Dispose() { Interlocked.Increment(ref disposals); DisposalEntered.TrySetResult(); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class GatedOwner(Clock clock) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups, cancellations;
        internal bool ThrowDuringCancellation { get; init; }
        internal bool FailClockDuringCancellation { get; init; }
        internal long? TimestampAfterCleanup { get; init; }
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal int CancellationCalls => Volatile.Read(ref cancellations);
        internal CancellationToken LifetimeCancellation { get; private set; }
        internal TaskCompletionSource EvaluationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseEvaluation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            if (Interlocked.Increment(ref acquisitions) != 1) { lease = null; return false; }
            lease = new Lease(this, clock);
            return true;
        }
        private sealed class Lease(GatedOwner owner, Clock clock) : IHealthCollectionLease
        {
            private CancellationTokenRegistration cancellation;
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.evaluations);
                owner.LifetimeCancellation = lifetimeCancellation;
                if (owner.ThrowDuringCancellation)
                {
                    cancellation = lifetimeCancellation.Register(() =>
                    {
                        Interlocked.Increment(ref owner.cancellations);
                        if (owner.FailClockDuringCancellation) { clock.FailNextUtcOnThisThread(); }
                        throw new OwnerCancellationException();
                    });
                }
                owner.EvaluationEntered.TrySetResult();
                await owner.ReleaseEvaluation.Task;
                lifetimeCancellation.ThrowIfCancellationRequested();
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }
            public async ValueTask DisposeAsync()
            {
                owner.CleanupEntered.TrySetResult();
                await owner.ReleaseCleanup.Task;
                cancellation.Dispose();
                if (owner.TimestampAfterCleanup is long timestamp) { clock.Timestamp = timestamp; }
                Interlocked.Increment(ref owner.cleanups);
            }
        }
    }

    private sealed class TimerChangeException : Exception;
    private sealed class DiagnosticClockException : Exception;
    private sealed class OwnerCancellationException : Exception;
}
