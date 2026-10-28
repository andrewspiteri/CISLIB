using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorResourceBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);
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
        Task<TimeSpan> startupChange = scope.Clock.Timer.NextChangeAsync();
        await AwaitAsync(() => scope.Runner.StartAsync());
        await startupChange;
        Assert.AreEqual(0, owner.Acquisitions,
            "An inert periodic registration must not acquire its owner before explicit activation.");
        Task<TimeSpan> activatedChange = scope.Clock.Timer.NextChangeAsync();
        registration.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, owner.Acquisitions,
            "The actual activation dispatcher must acquire its due owner before installing its next idle timer request.");
        await WaitForOwnedGateAsync(owner.EvaluationEntered.Task, null, owner.CleanupEntered.Task);
        Task<HealthCollectionAttempt> joined = registration.CollectAsync();
        owner.ReleaseEvaluation.TrySetResult();
        await WaitForOwnedGateAsync(owner.CleanupEntered.Task, joined);
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
        Exception timerFailure = disposedException ? new ObjectDisposedException("owner-timer") : new TimerChangeException();
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(timerFailure); }
        catch (ArgumentException error) { rejection = error; }
        // The background pump must detach this same actual resource fault. Its
        // omitted caller code/message and these sealed exception types are valid
        // public inputs. Prove acceptance before creating any resource or worker.
        Assert.IsNull(rejection, "A valid timer resource exception with omitted caller code and message must produce a detached diagnostic.");
        Assert.IsNotNull(projected);
        Assert.IsNull(projected.Code);
        Assert.AreEqual(timerFailure.Message, projected.Message);
        Assert.AreEqual(timerFailure.GetType().FullName, projected.ExceptionType);
        Assert.IsFalse(projected.IsTruncated);

        await using var scope = new Scope();
        scope.Clock.Timer.DuringChange = () => throw timerFailure;
        try
        {
            await AwaitAsync(() => scope.Runner.StartAsync());
            // Automatic timer disposal proves the failing pump requested stop;
            // the caller does not race the ObjectDisposed guard by stopping it.
            await scope.Clock.Timer.DisposalEntered.Task.WaitAsync(Deadline);
            scope.ExpectedStopFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AwaitAsync(() => scope.Runner.StopAsync()));
            Assert.AreEqual(1, scope.Clock.Timer.Disposals);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AwaitAsync(() => scope.Runner.StartAsync()));
            Assert.ThrowsExactly<InvalidOperationException>(() => scope.Register(new GatedOwner(scope.Clock)));
            scope.Monitor.ReportSuccess();
            Assert.AreEqual(1L, scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        }
        catch (Exception error) { scope.Primary = error; throw; }
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
        await AwaitAsync(() => scope.Runner.StartAsync());
        await scope.Clock.Timer.ChangeFinished.Task.WaitAsync(Deadline);
        Assert.IsNotNull(stop);

        await stop.WaitAsync(Deadline);
        await AwaitAsync(() => scope.Runner.DisposeAsync().AsTask());

        Assert.AreEqual(1, scope.Clock.Timer.Disposals);
        scope.Monitor.ReportSuccess();
        Assert.AreEqual(1L, scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ThrowingOwnerCancellationAndAnUnavailableDiagnosticClockCannotStrandLeaseSettlement(bool clockFails)
    {
        StopObservation observed = RunOwnerCancellationScenario(clockFails);
        if (observed.Graph.Length != 0)
        {
            // This is an ownership witness, not a Stop scheduling deadline.
            // Any queued/running Pump or external async owner that still roots
            // this private graph rejects the branch; no pending work is waived.
            CollectReleasedGraph(observed.Graph);
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual runner/resource ownership remained reachable; the pending Stop signal is not qualified as an isolated managed signal.");
            }
        }
        if (observed.Stop.IsFaulted || observed.Stop.IsCanceled) { Observe(observed.Stop); }
        Assert.IsTrue(observed.Stop.IsCompletedSuccessfully,
            "After actual collection, registration, lease/cancellation, timer and state owners settle and the entire private runner graph is unreachable, terminal Stop must have published its actual completion signal.");
        Observe(observed.Stop);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StopObservation RunOwnerCancellationScenario(bool clockFails)
    {
        var scope = new Scope();
        try
        {
            var owner = new GatedOwner(scope.Clock) { ThrowDuringCancellation = true, FailClockDuringCancellation = clockFails };
            IHealthCollectionRegistration registration = scope.Register(owner);
            ObserveCall(() => scope.Runner.StartAsync());
            Task<HealthCollectionAttempt> collection = HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync()).GetAwaiter().GetResult();
            Observe(WaitForOwnedGateAsync(owner.EvaluationEntered.Task, collection, owner.CleanupEntered.Task));
            Task<HealthCollectionAttempt> operation = HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync()).GetAwaiter().GetResult();

            Task.Run(registration.Withdraw).WaitAsync(Deadline).GetAwaiter().GetResult();
            Task drain = registration.WaitForDisposalAsync();

            Assert.IsTrue(owner.LifetimeCancellation.IsCancellationRequested);
            Assert.AreEqual(1, owner.CancellationCalls);
            Assert.AreEqual(clockFails ? 1 : 0, scope.Clock.UtcFaults);
            Assert.IsFalse(drain.IsCompleted);
            Assert.IsFalse(collection.IsCompleted);
            scope.Monitor.ReportSuccess();
            Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection!.LastError);
            owner.ReleaseEvaluation.TrySetResult();
            Observe(WaitForOwnedGateAsync(owner.CleanupEntered.Task, collection));
            Task stop = HealthCollectorTestCleanup.EnterAsync(Deadline, () => scope.Runner.StopAsync()).GetAwaiter().GetResult();
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(drain.IsCompleted);
            Assert.IsFalse(collection.IsCompleted);
            owner.ReleaseCleanup.TrySetResult();

            Observe(operation);
            Observe(drain);
            if (collection.IsFaulted || collection.IsCanceled) { Observe(collection); }
            Assert.IsTrue(collection.IsCompleted,
                "The original queued caller must complete after its actual operation and registration have settled.");
            HealthCollectionAttempt attempt = Observe(collection);

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
            Assert.AreEqual(1, owner.AcquisitionReturns);
            Assert.AreEqual(1, owner.EvaluationReturns);
            Assert.AreEqual(1, owner.CleanupReturns);
            Assert.AreEqual(1, owner.CancellationReturns);
            Assert.AreEqual(1, owner.CancellationRegistrationsDisposed);
            Assert.AreEqual(0, owner.ActiveLeases);
            Assert.AreEqual(1, scope.Clock.Timer.Disposals);
            Assert.AreEqual(1, scope.Clock.Timer.DisposalReturns);

            // Join every actual resource/state owner before considering a managed
            // terminal signal. A pending actual collection/State settlement never
            // reaches this branch, and every earlier failure uses strict cleanup.
            ObserveCall(() => registration.DisposeAsync().AsTask());
            ObserveCall(() => scope.Monitor.DisposeAsync().AsTask());
            ObserveCall(() => scope.Service.DisposeAsync().AsTask());
            if (stop.IsFaulted || stop.IsCanceled) { Observe(stop); }
            if (stop.IsCompletedSuccessfully)
            {
                ObserveCall(() => scope.Runner.DisposeAsync().AsTask());
                return new(stop, []);
            }

            // Do not attach a pending Runner.DisposeAsync state machine to the
            // retained Stop Task: that would itself root Runner. Its remaining
            // post-Stop subscription detach/disposed flag are NOT claimed invoked.
            // The service-owned token and every owner/handle/clock are private to
            // this graph; no timer callback or external cancellation token exists.
            return new(stop, [new(scope.Runner), new(scope.Service), new(scope.Monitor),
                new(registration), new(owner), new(scope.Clock), new(scope.Clock.Timer)]);
        }
        catch (Exception primary)
        {
            // Partial setup, actual work, faults and deadlines retain the
            // original strict cleanup, and any cleanup failure stays visible.
            try { Observe(scope.DisposeAsync().AsTask()); }
            catch (Exception cleanup)
            {
                throw new AggregateException("The actual resource scenario and cleanup both failed.", primary, cleanup);
            }
            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    private static void Observe(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T Observe<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static void ObserveCall(Func<Task> begin) => Observe(HealthCollectorTestCleanup.EnterAsync(Deadline, begin).GetAwaiter().GetResult());
    private static async Task AwaitAsync(Func<Task> begin)
    {
        Task actual = await HealthCollectorTestCleanup.EnterAsync(Deadline, begin);
        await actual.WaitAsync(Deadline);
    }

    private static void CollectReleasedGraph(WeakReference[] graph)
    {
        var observation = Stopwatch.StartNew();
        while (graph.Any(reference => reference.IsAlive) && observation.Elapsed < Deadline)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            // Allow an actual queued tail to leave. Elapsed time supplies no
            // completion/kill credit; a live graph at expiry remains a hold.
            if (graph.Any(reference => reference.IsAlive) && observation.Elapsed < Deadline) { Thread.Sleep(1); }
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed record StopObservation(Task Stop, WeakReference[] Graph);

    private static async Task WaitForOwnedGateAsync(Task entered, Task<HealthCollectionAttempt>? actualCaller,
        Task? cleanupEntered = null)
    {
        using var entryWait = new CancellationTokenSource();
        Task entry = entered.WaitAsync(Deadline, entryWait.Token);
        try
        {
            Task cleanup = cleanupEntered?.WaitAsync(Deadline, entryWait.Token) ?? entry;
            await Task.WhenAny(entry, actualCaller ?? entry, cleanup).WaitAsync(Deadline);
            if (cleanupEntered?.IsCompletedSuccessfully == true)
            {
                Assert.IsTrue(entered.IsCompletedSuccessfully,
                    "The owner cannot begin lease cleanup before its expected callback was invoked.");
            }
            if (actualCaller is not null)
            {
                Assert.IsFalse(actualCaller.IsCompleted,
                    "The actual collection settled before its owned evaluation or cleanup gate was released.");
            }
            await entry;
        }
        finally { entryWait.Cancel(); }
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
        internal InvalidOperationException? ExpectedStopFailure { get; set; }
        internal Exception? Primary { get; set; }
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
            Func<Task>[] drains = [DrainRunnerAsync, () => Service.DisposeAsync().AsTask(),
                .. registrations.Select(registration => (Func<Task>)(() => registration.DisposeAsync().AsTask()))];
            try { await HealthCollectorTestCleanup.DrainAsync(Deadline, Primary, [], drains); }
            finally { GC.KeepAlive(Monitor); }

            async Task DrainRunnerAsync()
            {
                try { await Runner.DisposeAsync(); }
                catch (InvalidOperationException error) when (ReferenceEquals(error, ExpectedStopFailure)) { }
            }
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
        private int disposals, disposalReturns;
        private TaskCompletionSource<TimeSpan>? nextChange;
        internal Action? DuringChange { get; set; }
        internal int Disposals => Volatile.Read(ref disposals);
        internal int DisposalReturns => Volatile.Read(ref disposalReturns);
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ChangeFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Scheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TimeSpan ScheduledDue { get; private set; }
        internal Task<TimeSpan> NextChangeAsync()
        {
            var next = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref nextChange, next, null) is not null)
            { throw new InvalidOperationException("The previous actual timer Change observation is still armed."); }
            return next.Task.WaitAsync(Deadline);
        }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                DuringChange?.Invoke();
                if (dueTime != Timeout.InfiniteTimeSpan) { ScheduledDue = dueTime; Scheduled.TrySetResult(); }
                Interlocked.Exchange(ref nextChange, null)?.TrySetResult(dueTime);
                return true;
            }
            finally { ChangeFinished.TrySetResult(); }
        }
        public void Dispose()
        {
            try { Interlocked.Increment(ref disposals); DisposalEntered.TrySetResult(); }
            finally { Interlocked.Increment(ref disposalReturns); }
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class GatedOwner(Clock clock) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups, cancellations;
        private int acquisitionReturns, evaluationReturns, cleanupReturns, cancellationReturns, cancellationRegistrationsDisposed, activeLeases;
        internal bool ThrowDuringCancellation { get; init; }
        internal bool FailClockDuringCancellation { get; init; }
        internal long? TimestampAfterCleanup { get; init; }
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal int CancellationCalls => Volatile.Read(ref cancellations);
        internal int AcquisitionReturns => Volatile.Read(ref acquisitionReturns);
        internal int EvaluationReturns => Volatile.Read(ref evaluationReturns);
        internal int CleanupReturns => Volatile.Read(ref cleanupReturns);
        internal int CancellationReturns => Volatile.Read(ref cancellationReturns);
        internal int CancellationRegistrationsDisposed => Volatile.Read(ref cancellationRegistrationsDisposed);
        internal int ActiveLeases => Volatile.Read(ref activeLeases);
        internal CancellationToken LifetimeCancellation { get; private set; }
        internal TaskCompletionSource EvaluationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseEvaluation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            try
            {
                if (Interlocked.Increment(ref acquisitions) != 1) { lease = null; return false; }
                lease = new Lease(this, clock);
                Interlocked.Increment(ref activeLeases);
                return true;
            }
            finally { Interlocked.Increment(ref acquisitionReturns); }
        }
        private sealed class Lease(GatedOwner owner, Clock clock) : IHealthCollectionLease
        {
            private CancellationTokenRegistration cancellation;
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                try
                {
                    Interlocked.Increment(ref owner.evaluations);
                    owner.LifetimeCancellation = lifetimeCancellation;
                    if (owner.ThrowDuringCancellation)
                    {
                        cancellation = lifetimeCancellation.Register(() =>
                        {
                            try
                            {
                                Interlocked.Increment(ref owner.cancellations);
                                if (owner.FailClockDuringCancellation) { clock.FailNextUtcOnThisThread(); }
                                throw new OwnerCancellationException();
                            }
                            finally { Interlocked.Increment(ref owner.cancellationReturns); }
                        });
                    }
                    owner.EvaluationEntered.TrySetResult();
                    await owner.ReleaseEvaluation.Task;
                    lifetimeCancellation.ThrowIfCancellationRequested();
                    return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
                }
                finally { Interlocked.Increment(ref owner.evaluationReturns); }
            }
            public async ValueTask DisposeAsync()
            {
                try
                {
                    owner.CleanupEntered.TrySetResult();
                    await owner.ReleaseCleanup.Task;
                    cancellation.Dispose();
                    Interlocked.Increment(ref owner.cancellationRegistrationsDisposed);
                    if (owner.TimestampAfterCleanup is long timestamp) { clock.Timestamp = timestamp; }
                    Interlocked.Increment(ref owner.cleanups);
                    Interlocked.Decrement(ref owner.activeLeases);
                }
                finally { Interlocked.Increment(ref owner.cleanupReturns); }
            }
        }
    }

    private sealed class TimerChangeException : Exception;
    private sealed class DiagnosticClockException : Exception;
    private sealed class OwnerCancellationException : Exception;
}
