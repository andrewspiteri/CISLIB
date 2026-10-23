using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRunnerMutationQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RunnerRequiresAService()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new HealthCollectorRunner(null!));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task AClockWithNoPositiveTimestampFrequencyCannotClaimTheRunner(long frequency)
    {
        var clock = new Clock(frequency);
        await using var service = new HealthcheckService(new HealthcheckOptions(), Identity(), clock);
        Assert.ThrowsExactly<ArgumentException>(() => new HealthCollectorRunner(service));
    }

    [TestMethod]
    public async Task RepeatedStartCreatesOnlyOneInitiallyInertTimer()
    {
        await using var scope = new Scope();

        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, scope.Clock.TimerCreations);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, scope.Clock.InitialDue);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, scope.Clock.InitialPeriod);
    }

    [TestMethod]
    public async Task TimerCreationFailureClosesAdmissionAndSettlesStop()
    {
        await using var scope = new Scope();
        scope.Clock.CreationError = new ClockException();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync().WaitAsync(Deadline));
        await scope.Runner.StopAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, scope.Clock.TimerCreations);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync());
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Register(new Collector()));
        Assert.AreEqual(0, scope.Clock.Timer.Disposals);
    }

    [TestMethod]
    public async Task StopDuringTimerCreationWaitsForTheTimerThatHasNotYetBeenPublished()
    {
        await using var scope = new Scope();
        Task? stop = null;
        bool completedInsideCreation = false;
        scope.Clock.Timer.BlockDisposal = true;
        scope.Clock.DuringCreation = () =>
        {
            stop = scope.Runner.StopAsync();
            completedInsideCreation = stop.IsCompleted;
        };
        try
        {
            await scope.Runner.StartAsync().WaitAsync(Deadline);

            Assert.IsFalse(completedInsideCreation, "A timer still being created remains actual unsettled runner work.");
            Assert.IsNotNull(stop);
            Assert.AreEqual(1, scope.Clock.Timer.Disposals);
            Assert.IsFalse(stop.IsCompleted);
        }
        finally { scope.Clock.Timer.Release(); }
        await stop!.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task StopAndDisposeBothWaitForActualAsynchronousTimerDisposal()
    {
        await using var scope = new Scope();
        await scope.Runner.StartAsync();
        scope.Clock.Timer.BlockDisposal = true;
        Task stop = scope.Runner.StopAsync();
        Task disposal = scope.Runner.DisposeAsync().AsTask();
        try
        {
            Assert.AreEqual(1, scope.Clock.Timer.Disposals);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreSame(stop, scope.Runner.StopAsync());
        }
        finally { scope.Clock.Timer.Release(); }
        await Task.WhenAll(stop, disposal).WaitAsync(Deadline);
        Assert.AreEqual(1, scope.Clock.Timer.Disposals);
    }

    [TestMethod]
    public async Task TimerDisposalFailureIsReportedInsteadOfAcknowledgingSuccessfulStop()
    {
        await using var scope = new Scope();
        scope.ExpectedStopFailure = true;
        scope.Clock.Timer.DisposalError = new TimerDisposalException();
        await scope.Runner.StartAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StopAsync().WaitAsync(Deadline));

        Assert.AreEqual(1, scope.Clock.Timer.Disposals);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.DisposeAsync().AsTask().WaitAsync(Deadline));
    }

    [TestMethod]
    public async Task DisposedRunnerRejectsNewWorkWithTheDisposedObjectContract()
    {
        await using var scope = new Scope();
        await scope.Runner.DisposeAsync();

        Assert.ThrowsExactly<ObjectDisposedException>(() => scope.Runner.StartAsync());
        Assert.ThrowsExactly<ObjectDisposedException>(() => scope.Register(new Collector()));

        scope.Monitor.ReportSuccess();
        Assert.AreEqual(1L, scope.Snapshot().Outcomes!.Successful);
    }

    [TestMethod]
    public async Task LargestRepresentableTimerIntervalIsAcceptedAndTheNextMillisecondIsRejected()
    {
        await using var scope = new Scope(new Clock(1));
        Assert.ThrowsExactly<ArgumentException>(() => scope.Register(new Collector(), TimeSpan.FromMilliseconds(uint.MaxValue)));

        IHealthCollectionRegistration registration = scope.Register(new Collector(), TimeSpan.FromMilliseconds(uint.MaxValue - 1d));

        Assert.AreEqual(scope.Monitor.MonitorId, registration.MonitorId);
        Assert.IsFalse(scope.Snapshot().Collection!.InProgress);
    }

    [TestMethod]
    public async Task AnIntervalOfExactlyInt64MaxTimestampUnitsIsRepresentable()
    {
        await using var scope = new Scope(new Clock(long.MaxValue));

        IHealthCollectionRegistration registration = scope.Register(new Collector(), TimeSpan.FromSeconds(1));

        Assert.AreEqual(scope.Monitor.MonitorId, registration.MonitorId);
    }

    [TestMethod]
    public async Task UnrepresentableTimestampIntervalIsRejectedWithoutAttachingTheMonitor()
    {
        await using var scope = new Scope(new Clock(long.MaxValue));

        Assert.ThrowsExactly<ArgumentException>(() => scope.Register(new Collector(), TimeSpan.FromSeconds(2)));

        Assert.IsNull(scope.Snapshot().Collection);
        Assert.AreEqual(scope.Monitor.MonitorId, scope.Register(new Collector()).MonitorId);
    }

    [TestMethod]
    public async Task RegistrationChecksFutureTimestampOverflowBeforeAttachingTheMonitor()
    {
        await using var scope = new Scope(new Clock(TimeSpan.TicksPerSecond) { Timestamp = long.MaxValue });

        Assert.ThrowsExactly<OverflowException>(() => scope.Register(new Collector(), TimeSpan.FromTicks(1)));

        Assert.IsNull(scope.Snapshot().Collection);
    }

    [TestMethod]
    public async Task ARepresentableIntervalCanStartAtANegativeMonotonicTimestamp()
    {
        await using var scope = new Scope(new Clock(TimeSpan.TicksPerSecond) { Timestamp = long.MinValue });

        IHealthCollectionRegistration registration = scope.Register(new Collector(), TimeSpan.FromTicks(1));

        Assert.AreEqual(scope.Monitor.MonitorId, registration.MonitorId);
    }

    [TestMethod]
    public async Task PeriodicActivationAlsoChecksFutureTimestampOverflow()
    {
        await using var scope = new Scope();
        IHealthCollectionRegistration registration = scope.Register(new Collector(), TimeSpan.FromTicks(1));
        await scope.Runner.StartAsync();
        scope.Clock.Timestamp = long.MaxValue;

        Assert.ThrowsExactly<OverflowException>(registration.ActivatePeriodic);

        Assert.IsFalse(scope.Snapshot().Collection!.InProgress);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task EvaluationFaultsHaveStableDiagnosticCategoriesAndExactlyOnePolicyFailure(int fault)
    {
        await using var scope = new Scope();
        var collector = new Collector
        {
            Evaluate = _ => fault switch
            {
                0 => null!,
                1 => Task.FromException<HealthCollectionResult>(new EvaluationException()),
                2 => Task.FromException<HealthCollectionResult>(new OperationCanceledException()),
                _ => Task.FromResult<HealthCollectionResult>(null!)
            }
        };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync();

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsTrue(attempt.EvaluationFailed);
        Assert.AreEqual(fault == 3 ? "Healthcheck.CollectionResult" : "Healthcheck.CollectionEvaluation", attempt.Diagnostic!.Code);
        Assert.AreSame(attempt.Diagnostic, scope.Snapshot().Collection!.LastError);
        Assert.AreEqual(1L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
        Assert.AreEqual(1, collector.Acquisitions);
        Assert.AreEqual(1, collector.Cleanups);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcquisitionProtocolFaultsAreIntegrationDiagnosticsAndStillReleaseEveryReturnedLease(bool returnsLease)
    {
        await using var scope = new Scope();
        var collector = new Collector { Acquired = !returnsLease, ReturnLease = returnsLease };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync();

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual("Healthcheck.LeaseProtocol", scope.Snapshot().Collection!.LastError!.Code);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(0, collector.Evaluations);
        Assert.AreEqual(returnsLease ? 1 : 0, collector.Cleanups);
    }

    [TestMethod]
    public async Task AcquisitionExceptionRetainsItsDiagnosticCategoryAndExceptionTypeWithoutAnOutcome()
    {
        await using var scope = new Scope();
        var collector = new Collector { AcquisitionError = new AcquisitionException() };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync();

        HealthCollectionAttempt result = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(result.EvaluationFailed);
        Assert.AreEqual("Healthcheck.LeaseAcquisition", scope.Snapshot().Collection!.LastError!.Code);
        Assert.AreEqual(typeof(AcquisitionException).FullName, scope.Snapshot().Collection!.LastError!.ExceptionType);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(0, collector.Cleanups);
    }

    [TestMethod]
    public async Task ActualCleanupErrorTakesPrecedenceOverASecondClockFailure()
    {
        await using var scope = new Scope();
        var collector = new Collector
        {
            Cleanup = () =>
            {
                scope.Clock.FailNextUtcOnThisThread();
                throw new LeaseCleanupException();
            }
        };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync();

        HealthCollectionAttempt result = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
        Assert.IsFalse(result.EvaluationFailed);
        HealthDiagnostic diagnostic = scope.Snapshot().Collection!.LastError!;
        Assert.AreEqual("Healthcheck.LeaseCleanup", diagnostic.Code);
        Assert.AreEqual(typeof(LeaseCleanupException).FullName, diagnostic.ExceptionType);
        Assert.AreEqual(1, scope.Clock.UtcFaults);
        Assert.AreEqual(1L, scope.Snapshot().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task APostCleanupTimestampFailurePreservesTheActualEvaluationAndRetiresFurtherCollection(bool evaluationFailed)
    {
        await using var scope = new Scope();
        var collector = new Collector
        {
            Evaluate = _ => evaluationFailed ? Task.FromException<HealthCollectionResult>(new EvaluationException())
                : Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded)),
            Cleanup = scope.Clock.FailNextTimestampOnThisThread
        };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync();

        HealthCollectionAttempt result = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
        Assert.AreEqual(evaluationFailed, result.EvaluationFailed);
        Assert.AreEqual(evaluationFailed ? "Healthcheck.CollectionEvaluation" : "Healthcheck.CollectionClock", result.Diagnostic!.Code);
        Assert.AreEqual(1, scope.Clock.TimestampFaults);
        HealthCollectionAttempt retired = await registration.CollectAsync().WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition);
        Assert.IsFalse(retired.EvaluationFailed);
        Assert.AreEqual(1, collector.Acquisitions);
        Assert.AreEqual(evaluationFailed ? 1L : 0L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(evaluationFailed ? 0L : 1L, scope.Snapshot().Outcomes!.Successful);
    }

    private static HealthApplicationIdentity Identity() => new() { Name = "collector mutation qualification", InstanceId = Guid.NewGuid() };

    private sealed class Scope : IAsyncDisposable
    {
        internal Scope(Clock? clock = null)
        {
            Clock = clock ?? new Clock(TimeSpan.TicksPerSecond);
            Service = new HealthcheckService(new HealthcheckOptions(), Identity(), Clock);
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Runner = new HealthCollectorRunner(Service);
        }
        internal Clock Clock { get; }
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }
        internal bool ExpectedStopFailure { get; set; }
        internal IHealthCollectionRegistration Register(Collector collector, TimeSpan? interval = null) => Runner.Register(Monitor, collector, new() { Interval = interval });
        internal HealthMonitorSnapshot Snapshot() => Service.CaptureSnapshot().Monitors.Single();
        public async ValueTask DisposeAsync()
        {
            Clock.Timer.Release();
            Task runner = DrainRunnerAsync();
            Task service = DrainServiceAsync();
            try { await Task.WhenAll(runner, service).WaitAsync(Deadline); }
            finally { GC.KeepAlive(Monitor); }

            async Task DrainRunnerAsync()
            {
                try { await Runner.DisposeAsync(); }
                catch (InvalidOperationException) when (ExpectedStopFailure) { }
            }
            async Task DrainServiceAsync() => await Service.DisposeAsync();
        }
    }

    private sealed class Clock(long frequency) : TimeProvider
    {
        private long timestamp;
        private int failUtcThread, failTimestampThread, utcFaults, timestampFaults;
        internal long Timestamp { get => Interlocked.Read(ref timestamp); set => Interlocked.Exchange(ref timestamp, value); }
        internal int TimerCreations { get; private set; }
        internal TimeSpan InitialDue { get; private set; }
        internal TimeSpan InitialPeriod { get; private set; }
        internal Exception? CreationError { get; set; }
        internal Action? DuringCreation { get; set; }
        internal ControlledTimer Timer { get; } = new();
        internal int UtcFaults => Volatile.Read(ref utcFaults);
        internal int TimestampFaults => Volatile.Read(ref timestampFaults);
        internal void FailNextUtcOnThisThread() => Volatile.Write(ref failUtcThread, Environment.CurrentManagedThreadId);
        internal void FailNextTimestampOnThisThread() => Volatile.Write(ref failTimestampThread, Environment.CurrentManagedThreadId);
        public override long TimestampFrequency => frequency;
        public override DateTimeOffset GetUtcNow()
        {
            int thread = Environment.CurrentManagedThreadId;
            if (Interlocked.CompareExchange(ref failUtcThread, 0, thread) == thread) { Interlocked.Increment(ref utcFaults); throw new ClockException(); }
            return Epoch;
        }
        public override long GetTimestamp()
        {
            int thread = Environment.CurrentManagedThreadId;
            if (Interlocked.CompareExchange(ref failTimestampThread, 0, thread) == thread) { Interlocked.Increment(ref timestampFaults); throw new ClockException(); }
            return Timestamp;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCreations++;
            InitialDue = dueTime;
            InitialPeriod = period;
            if (CreationError is not null) { throw CreationError; }
            DuringCreation?.Invoke();
            return Timer;
        }
    }

    private sealed class ControlledTimer : ITimer
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposals;
        internal bool BlockDisposal { get; set; }
        internal Exception? DisposalError { get; set; }
        internal int Disposals => Volatile.Read(ref disposals);
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() => Release();
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            if (BlockDisposal) { await release.Task; }
            if (DisposalError is not null) { throw DisposalError; }
        }
        internal void Release() => release.TrySetResult();
    }

    private sealed class Collector : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal bool Acquired { get; set; } = true;
        internal bool ReturnLease { get; set; } = true;
        internal Exception? AcquisitionError { get; set; }
        internal Func<CancellationToken, Task<HealthCollectionResult>> Evaluate { get; set; } = _ => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        internal Action? Cleanup { get; set; }
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            if (AcquisitionError is not null) { throw AcquisitionError; }
            lease = ReturnLease ? new Lease(this) : null;
            return Acquired;
        }
        private sealed class Lease(Collector owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.evaluations);
                return owner.Evaluate(lifetimeCancellation);
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.cleanups);
                owner.Cleanup?.Invoke();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ClockException : Exception;
    private sealed class TimerDisposalException : Exception;
    private sealed class EvaluationException : Exception;
    private sealed class AcquisitionException : Exception;
    private sealed class LeaseCleanupException : Exception;
}
