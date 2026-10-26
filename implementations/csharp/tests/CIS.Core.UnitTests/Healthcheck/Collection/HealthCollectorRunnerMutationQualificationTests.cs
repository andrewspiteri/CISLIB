using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    [DataRow(1L)]
    [DataRow(TimeSpan.TicksPerSecond)]
    [DataRow(long.MaxValue)]
    public async Task PositiveTimestampFrequencyAllowsAnInertRunnerToDisposeWithoutCreatingATimer(long frequency)
    {
        var clock = new Clock(frequency);
        var service = new HealthcheckService(new HealthcheckOptions(), Identity(), clock);
        HealthCollectorRunner? runner = null;
        ArgumentException? rejection = null;
        try
        {
            try { runner = new HealthCollectorRunner(service); }
            catch (ArgumentException error) when (error.GetType() == typeof(ArgumentException) && error.ParamName == "service") { rejection = error; }
        }
        finally
        {
            Task? runnerDisposal = runner?.DisposeAsync().AsTask();
            Task serviceDisposal = service.DisposeAsync().AsTask();
            if (runnerDisposal is null) { await serviceDisposal.WaitAsync(Deadline); }
            else { await Task.WhenAll(runnerDisposal, serviceDisposal).WaitAsync(Deadline); }
        }

        Assert.IsNull(rejection, "An actual positive-frequency clock must admit an inert runner before genuine terminal disposal.");
        Assert.IsNotNull(runner);
        Assert.AreEqual(0, clock.TimerCreations, "Construction and unstarted disposal must not create a timer.");
    }

    [TestMethod]
    public void RepeatedStartCreatesOnlyOneInitiallyInertTimer()
    {
        AssertRepeatedStart(RunAndReleaseRepeatedStart());
    }

    [TestMethod]
    public async Task TimerCreationFailureClosesAdmissionAndSettlesStop()
    {
        await using var scope = new Scope();
        scope.Clock.CreationError = new ClockException();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync()).WaitAsync(Deadline);
        await scope.Runner.StopAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, scope.Clock.TimerCreations);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync()).WaitAsync(Deadline);
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
        await scope.Runner.StartAsync().WaitAsync(Deadline);
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
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StopAsync()).WaitAsync(Deadline);

        Assert.AreEqual(1, scope.Clock.Timer.Disposals);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.DisposeAsync().AsTask()).WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task DisposedRunnerRejectsNewWorkWithTheDisposedObjectContract()
    {
        await using var scope = new Scope();
        await scope.Runner.DisposeAsync().AsTask().WaitAsync(Deadline);

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
        await scope.Runner.StartAsync().WaitAsync(Deadline);
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
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await CollectWithOwnedAcquisitionAsync(registration, collector);

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
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = new HealthDiagnostic("Healthcheck.LeaseProtocol", "The owner returned an inconsistent acquisition result and lease."); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid inconsistent-acquisition diagnostic inputs must be accepted before dispatch.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope();
        var collector = new Collector { Acquired = !returnsLease, ReturnLease = returnsLease };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await CollectWithOwnedAcquisitionAsync(registration, collector);

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
        var acquisitionFailure = new AcquisitionException();
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(acquisitionFailure, "Healthcheck.LeaseAcquisition"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid acquisition exception must produce its detached diagnostic before dispatch.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope();
        var collector = new Collector { AcquisitionError = acquisitionFailure };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt result = await CollectWithOwnedAcquisitionAsync(registration, collector);

        Assert.IsFalse(result.EvaluationFailed);
        Assert.AreEqual("Healthcheck.LeaseAcquisition", scope.Snapshot().Collection!.LastError!.Code);
        Assert.AreEqual(typeof(AcquisitionException).FullName, scope.Snapshot().Collection!.LastError!.ExceptionType);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(0, collector.Cleanups);
    }

    [TestMethod]
    public async Task ActualCleanupErrorTakesPrecedenceOverASecondClockFailure()
    {
        var cleanupFailure = new LeaseCleanupException();
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(cleanupFailure, "Healthcheck.LeaseCleanup"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid cleanup exception must produce its precedence diagnostic before dispatch.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope();
        var collector = new Collector
        {
            Cleanup = () =>
            {
                scope.Clock.FailNextUtcOnThisThread();
                throw cleanupFailure;
            }
        };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt result = await CollectWithOwnedAcquisitionAsync(registration, collector);

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
        Exception actualFailure = evaluationFailed ? new EvaluationException() : new ClockException();
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try
        {
            projected = HealthDiagnostic.FromException(actualFailure,
                evaluationFailed ? "Healthcheck.CollectionEvaluation" : "Healthcheck.CollectionClock");
        }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual selected evaluation or timestamp exception must produce its diagnostic before dispatch.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope(new Clock(TimeSpan.TicksPerSecond)
        { TimestampFailure = evaluationFailed ? null : actualFailure });
        var collector = new Collector
        {
            Evaluate = _ => evaluationFailed ? Task.FromException<HealthCollectionResult>(actualFailure)
                : Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded)),
            Cleanup = scope.Clock.FailNextTimestampOnThisThread
        };
        IHealthCollectionRegistration registration = scope.Register(collector);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt result = await CollectWithOwnedAcquisitionAsync(registration, collector);

        Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
        Assert.AreEqual(evaluationFailed, result.EvaluationFailed);
        Assert.IsNotNull(result.Diagnostic);
        Assert.AreEqual(evaluationFailed ? "Healthcheck.CollectionEvaluation" : "Healthcheck.CollectionClock", result.Diagnostic.Code);
        Assert.AreEqual(1, scope.Clock.TimestampFaults);
        HealthCollectionAttempt retired = await registration.CollectAsync().WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition);
        Assert.IsFalse(retired.EvaluationFailed);
        Assert.AreEqual(1, collector.Acquisitions);
        Assert.AreEqual(evaluationFailed ? 1L : 0L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(evaluationFailed ? 0L : 1L, scope.Snapshot().Outcomes!.Successful);
    }

    // This capsule belongs only to the resource-free repeated-Start scenario.
    // The original fake clock has no scheduler and retains no callback/state.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartObservation RunAndReleaseRepeatedStart(Action<StartLifetime>? configure = null)
    {
        var facts = new StartFacts();
        var scope = new StartLifetime(facts);
        Task? first = null, second = null;
        try
        {
            configure?.Invoke(scope);
            Interlocked.Increment(ref facts.StartCalls);
            first = scope.Runner.StartAsync();
            Interlocked.Increment(ref facts.StartReturns);
            Interlocked.Increment(ref facts.StartCalls);
            second = scope.Runner.StartAsync();
            Interlocked.Increment(ref facts.StartReturns);
        }
        catch (Exception error) { facts.Record(error); }
        finally
        {
            // Initiate every actual terminal operation before waiting for any.
            // No startup Task is replaced, canceled or completed by this fixture.
            scope.Clock.Timer.Release();
            Task? stop = Begin(() => scope.Runner.StopAsync(), facts, ref facts.StopCalls, ref facts.StopReturns);
            Task? runner = Begin(() => scope.Runner.DisposeAsync().AsTask(), facts, ref facts.RunnerDisposeCalls, ref facts.RunnerDisposeReturns);
            Task? service = Begin(() => scope.Service.DisposeAsync().AsTask(), facts, ref facts.ServiceDisposeCalls, ref facts.ServiceDisposeReturns);
            Join(stop, facts, ref facts.StopCompletions, ref facts.StopSuccesses);
            Join(runner, facts, ref facts.RunnerDisposeCompletions, ref facts.RunnerDisposeSuccesses);
            Join(service, facts, ref facts.ServiceDisposeCompletions, ref facts.ServiceDisposeSuccesses);
            ObserveTerminalFailure(first, facts);
            ObserveTerminalFailure(second, facts);
            GC.KeepAlive(scope.Monitor);
        }
        return new(first, second, facts,
            new(scope.Clock.TimerCreations, scope.Clock.InitialDue, scope.Clock.InitialPeriod));

        static Task? Begin(Func<Task> call, StartFacts facts, ref int calls, ref int returns)
        {
            Interlocked.Increment(ref calls);
            try
            {
                Task task = call();
                Interlocked.Increment(ref returns);
                return task;
            }
            catch (Exception error) { facts.Record(error); return null; }
        }
        static void Join(Task? task, StartFacts facts, ref int completions, ref int successes)
        {
            if (task is null) { return; }
            try { task.WaitAsync(Deadline).GetAwaiter().GetResult(); Interlocked.Increment(ref successes); }
            catch (Exception error) { facts.Record(error); }
            finally { if (task.IsCompleted) { Interlocked.Increment(ref completions); } }
        }
        static void ObserveTerminalFailure(Task? task, StartFacts facts)
        {
            if (task is null || !task.IsCompleted || task.IsCompletedSuccessfully) { return; }
            try { task.GetAwaiter().GetResult(); }
            catch (Exception error) { facts.Record(error); }
        }
    }

    private static void AssertRepeatedStart(StartObservation observation)
    {
        StartFacts facts = observation.Facts;
        // Unknown producer/resource failures remain runtime holds even if an
        // original value assertion would also fail. Counts are never cleared.
        ThrowRecordedErrors(facts);
        RequireTerminalReturns(facts);
        var elapsed = Stopwatch.StartNew();
        while (facts.Owners.Any(static owner => owner.IsAlive) && elapsed.Elapsed < Deadline)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(1);
        }
        if (facts.Owners.Any(static owner => owner.IsAlive))
        {
            throw new InvalidOperationException("The actual repeated-Start owner graph remains live after terminal cleanup.");
        }
        ThrowRecordedErrors(facts);
        RequireTerminalReturns(facts);
        if (facts.StartCalls != 2 || facts.StartReturns != 2 || observation.First is null || observation.Second is null
            || facts.CreationEntries != facts.CreationReturns || facts.TimerDisposeEntries != facts.TimerDisposeReturns
            || facts.TimerDisposeReturns != facts.CreationReturns || facts.TimerDisposeSuccesses != facts.CreationReturns)
        {
            throw new InvalidOperationException("Actual startup/timer calls and resource returns are not fully accounted for.");
        }

        AssertActualStart(observation.First);
        AssertActualStart(observation.Second);
        Assert.AreEqual(1, observation.Clock.TimerCreations);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, observation.Clock.InitialDue);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, observation.Clock.InitialPeriod);

        static void AssertActualStart(Task task)
        {
            if (task.IsFaulted || task.IsCanceled) { task.GetAwaiter().GetResult(); }
            Assert.IsTrue(task.IsCompleted, "An accepted startup promise must settle after all actual runner resources and startup owners have returned and died.");
            task.GetAwaiter().GetResult();
        }
    }

    private static void RequireTerminalReturns(StartFacts facts)
    {
        if (facts.StopCalls != 1 || facts.StopReturns != 1 || facts.StopCompletions != 1 || facts.StopSuccesses != 1
            || facts.RunnerDisposeCalls != 1 || facts.RunnerDisposeReturns != 1 || facts.RunnerDisposeCompletions != 1 || facts.RunnerDisposeSuccesses != 1
            || facts.ServiceDisposeCalls != 1 || facts.ServiceDisposeReturns != 1 || facts.ServiceDisposeCompletions != 1 || facts.ServiceDisposeSuccesses != 1)
        {
            throw new InvalidOperationException("Every actual Stop, runner disposal and service disposal must return successfully.");
        }
    }

    private static void ThrowRecordedErrors(StartFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length != 0) { throw new AggregateException("Actual repeated-Start producer or resource failures remain unresolved.", errors); }
    }

    private sealed record StartObservation(Task? First, Task? Second, StartFacts Facts, StartClockValues Clock);
    private sealed record StartClockValues(int TimerCreations, TimeSpan InitialDue, TimeSpan InitialPeriod);

    private sealed class StartFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        internal readonly List<WeakReference> Owners = [];
        internal int StartCalls, StartReturns, CreationEntries, CreationReturns;
        internal int TimerDisposeEntries, TimerDisposeReturns, TimerDisposeSuccesses;
        internal int StopCalls, StopReturns, StopCompletions, StopSuccesses;
        internal int RunnerDisposeCalls, RunnerDisposeReturns, RunnerDisposeCompletions, RunnerDisposeSuccesses;
        internal int ServiceDisposeCalls, ServiceDisposeReturns, ServiceDisposeCompletions, ServiceDisposeSuccesses;
        internal void Record(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return errors.ToArray(); } }
        internal void Own(object value) { lock (gate) { Owners.Add(new(value)); } }
    }

    private sealed class StartLifetime
    {
        internal StartLifetime(StartFacts facts)
        {
            Clock = new Clock(TimeSpan.TicksPerSecond);
            var observer = new StartClock(Clock, facts);
            Service = new HealthcheckService(new HealthcheckOptions(), Identity(), observer);
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Runner = new HealthCollectorRunner(Service);
            facts.Own(this);
            facts.Own(Clock);
            facts.Own(Clock.Timer);
            facts.Own(observer);
            facts.Own(Service);
            facts.Own(Monitor);
            facts.Own(Runner);
        }
        internal Clock Clock { get; }
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }
    }

    private sealed class StartClock(Clock inner, StartFacts facts) : TimeProvider
    {
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override DateTimeOffset GetUtcNow()
        {
            try { return inner.GetUtcNow(); }
            catch (Exception error) { facts.Record(error); throw; }
        }
        public override long GetTimestamp()
        {
            try { return inner.GetTimestamp(); }
            catch (Exception error) { facts.Record(error); throw; }
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref facts.CreationEntries);
            try
            {
                ITimer timer = inner.CreateTimer(callback, state, dueTime, period);
                var observer = new StartTimer(timer, facts);
                facts.Own(observer);
                Interlocked.Increment(ref facts.CreationReturns);
                return observer;
            }
            catch (Exception error) { facts.Record(error); throw; }
        }
    }

    private sealed class StartTimer(ITimer inner, StartFacts facts) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            try { return inner.Change(dueTime, period); }
            catch (Exception error) { facts.Record(error); throw; }
        }
        public void Dispose()
        {
            // The runner contract uses async disposal. A different path cannot
            // satisfy the required actual async entry/return inventory.
            try { inner.Dispose(); }
            catch (Exception error) { facts.Record(error); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref facts.TimerDisposeEntries);
            try
            {
                await inner.DisposeAsync();
                Interlocked.Increment(ref facts.TimerDisposeSuccesses);
            }
            catch (Exception error) { facts.Record(error); throw; }
            finally { Interlocked.Increment(ref facts.TimerDisposeReturns); }
        }
    }


    private static HealthApplicationIdentity Identity() => new() { Name = "collector mutation qualification", InstanceId = Guid.NewGuid() };

    private static async Task<HealthCollectionAttempt> CollectWithOwnedAcquisitionAsync(
        IHealthCollectionRegistration registration, Collector collector)
    {
        AcquisitionHold hold = collector.HoldNextAcquisition();
        try
        {
            Task<HealthCollectionAttempt> caller = registration.CollectAsync();
            await Task.WhenAny(hold.Entered.Task, caller).WaitAsync(Deadline);
            if (caller.IsCompleted)
            {
                await caller;
                Assert.Fail("The queued caller cannot settle before its actual acquisition is released.");
            }
            await hold.Entered.Task.WaitAsync(Deadline);
            Task<HealthCollectionAttempt> operation = registration.CollectAsync();
            hold.Released.TrySetResult();
            await operation.WaitAsync(Deadline);
            if (caller.IsFaulted || caller.IsCanceled) { await caller; }
            Assert.IsTrue(caller.IsCompleted, "The original queued caller must complete after its actual operation has settled.");
            return await caller;
        }
        finally { hold.Released.TrySetResult(); }
    }

    private sealed class AcquisitionHold
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<Collector> collectors = [];
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
        internal IHealthCollectionRegistration Register(Collector collector, TimeSpan? interval = null)
        {
            IHealthCollectionRegistration registration = Runner.Register(Monitor, collector, new() { Interval = interval });
            collectors.Add(collector);
            return registration;
        }
        internal HealthMonitorSnapshot Snapshot() => Service.CaptureSnapshot().Monitors.Single();
        public async ValueTask DisposeAsync()
        {
            foreach (Collector collector in collectors) { collector.ReleaseAcquisitions(); }
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
        internal Exception? TimestampFailure { get; init; }
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
            if (Interlocked.CompareExchange(ref failTimestampThread, 0, thread) == thread) { Interlocked.Increment(ref timestampFaults); throw TimestampFailure ?? new ClockException(); }
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
        private readonly object acquisitionGate = new();
        private readonly List<AcquisitionHold> acquisitionHolds = [];
        private AcquisitionHold? nextAcquisition;
        private bool releasingAcquisitions;
        private int acquisitions, evaluations, cleanups;
        internal bool Acquired { get; set; } = true;
        internal bool ReturnLease { get; set; } = true;
        internal Exception? AcquisitionError { get; set; }
        internal Func<CancellationToken, Task<HealthCollectionResult>> Evaluate { get; set; } = _ => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        internal Action? Cleanup { get; set; }
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal AcquisitionHold HoldNextAcquisition()
        {
            lock (acquisitionGate)
            {
                if (nextAcquisition is not null) { throw new InvalidOperationException("The fixture already holds an acquisition."); }
                var hold = new AcquisitionHold();
                acquisitionHolds.Add(hold);
                nextAcquisition = hold;
                if (releasingAcquisitions) { hold.Released.TrySetResult(); }
                return hold;
            }
        }
        internal void ReleaseAcquisitions()
        {
            lock (acquisitionGate)
            {
                releasingAcquisitions = true;
                foreach (AcquisitionHold hold in acquisitionHolds) { hold.Released.TrySetResult(); }
            }
        }
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            AcquisitionHold? hold;
            lock (acquisitionGate) { hold = nextAcquisition; nextAcquisition = null; }
            if (hold is not null) { hold.Entered.TrySetResult(); hold.Released.Task.GetAwaiter().GetResult(); }
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
