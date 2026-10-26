using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorIntervalValidationBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(42_949_672_950_000L)]
    [DataRow(long.MaxValue)]
    public void InvalidIntervalsRejectTheOptionsBeforeReadingAnUnavailableClock(long ticks)
    {
        AssertInertTerminalCompletion(RunAndReleaseScenario(Scenario.InvalidInterval, ticks));
    }

    [TestMethod]
    public void AValidIntervalActuallyReachesTheUnavailableClockBeforeAttaching()
    {
        AssertInertTerminalCompletion(RunAndReleaseScenario(Scenario.UnavailableClock, 0));
    }

    [TestMethod]
    [DataRow(1L)]
    [DataRow(42_949_672_940_000L)]
    public void PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner(long ticks)
    {
        AssertInertTerminalCompletion(RunAndReleaseScenario(Scenario.PositiveInterval, ticks));
    }

    [TestMethod]
    public void TimerCreationFailureClosesRegistrationBeforeAnyExplicitStop()
    {
        AssertInertTerminalCompletion(RunAndReleaseScenario(Scenario.FactoryFailure, 0));
    }

    private static void InvalidIntervalsRejectTheOptionsBeforeReadingAnUnavailableClock(Scope scope, long ticks)
    {
        scope.Clock.FailReads = true;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            scope.Runner.Register(scope.Monitor, scope.Owner, new() { Interval = TimeSpan.FromTicks(ticks) }));

        Assert.AreEqual("options", error.ParamName);
        Assert.AreEqual(0, scope.Clock.Reads, "An invalid interval cannot become a clock dependency failure before its argument is rejected.");
        Assert.AreEqual(0, scope.Owner.Acquisitions);
        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.IsNull(after.Monitors.Single().Collection);
    }

    private static void AValidIntervalActuallyReachesTheUnavailableClockBeforeAttaching(Scope scope)
    {
        scope.Clock.FailReads = true;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<ClockUnavailableException>(() =>
            scope.Runner.Register(scope.Monitor, scope.Owner, new() { Interval = TimeSpan.FromTicks(1) }));

        Assert.AreEqual(1, scope.Clock.Reads);
        Assert.AreEqual(0, scope.Owner.Acquisitions);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
    }

    private static void PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner(Scope scope, long ticks)
    {
        var options = new HealthCollectorOptions { Interval = TimeSpan.FromTicks(ticks) };
        IHealthCollectionRegistration? registration = null;
        ArgumentException? rejection = null;
        try { registration = scope.Runner.Register(scope.Monitor, scope.Owner, options); }
        catch (ArgumentException error) when (error.GetType() == typeof(ArgumentException) && error.ParamName == "options") { rejection = error; }
        Assert.IsNull(rejection, "The actual positive representable interval must be admitted by Register.");
        Assert.IsNotNull(registration);
        try
        {
            Assert.AreEqual(1, scope.Clock.Reads);
            Assert.AreEqual(0, scope.Owner.Acquisitions);
            Assert.IsNotNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
        }
        finally { Observe(registration.DisposeAsync().AsTask()); }
    }

    private static void TimerCreationFailureClosesRegistrationBeforeAnyExplicitStop(Scope scope)
    {
        scope.Clock.FailTimerCreation = true;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Observe(Assert.ThrowsExactlyAsync<InvalidOperationException>(() => scope.Runner.StartAsync()));

        // No StopAsync or disposal has run yet. Start's failure must close
        // admission itself, rather than require the caller to repair its state.
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Runner.Register(scope.Monitor, scope.Owner, new()));
        Assert.AreEqual(1, scope.Clock.TimerCreations);
        Assert.AreEqual(0, scope.Owner.Acquisitions);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
    }

    private enum Scenario { InvalidInterval, UnavailableClock, PositiveInterval, FactoryFailure }

    private static void AssertInertTerminalCompletion(TerminalObservation observed)
    {
        if (observed.Graph.Length != 0)
        {
            var observation = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && observation.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && observation.Elapsed < Deadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual inert runner/service ownership remained reachable; queued or asynchronous work is not excused.");
            }
            if (observed.Clock.SuccessfulTimerCreations != 0 || observed.Owner.Acquisitions != 0)
            {
                throw new InvalidOperationException("Actual timer creation or owner acquisition appeared during ownership observation; its cleanup remains a hold.");
            }
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { Observe(actual); }
            }
            Exception[] failures = observed.Completion.Failures();
            if (failures.Any(error => error.GetType() != typeof(InvalidOperationException)))
            {
                throw new AggregateException("Actual inert Stop/Dispose had an unaccepted runtime failure or cancellation.", failures);
            }
            // Preserve118's narrow already-completed InvalidOperation rejection
            // assertion only after actual service disposal and owner proof.
            Assert.AreEqual(0, failures.Length,
                "Valid inert terminal calls must not reject shutdown.");
            Assert.IsTrue(observed.Completion.StopSucceeded,
                "Actual Stop must publish success after the private inert owner graph has been released.");
            Assert.IsTrue(observed.Completion.DisposeSucceeded,
                "Actual Dispose must publish success after the private inert owner graph has been released.");
        }
        observed.Primary?.Throw();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TerminalObservation RunAndReleaseScenario(Scenario scenario, long ticks)
    {
        var scope = new Scope();
        Exception? primary = null;
        try
        {
            switch (scenario)
            {
                case Scenario.InvalidInterval: InvalidIntervalsRejectTheOptionsBeforeReadingAnUnavailableClock(scope, ticks); break;
                case Scenario.UnavailableClock: AValidIntervalActuallyReachesTheUnavailableClockBeforeAttaching(scope); break;
                case Scenario.PositiveInterval: PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner(scope, ticks); break;
                case Scenario.FactoryFailure: TimerCreationFailureClosesRegistrationBeforeAnyExplicitStop(scope); break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }
        catch (Exception error) { primary = error; }

        // Start each actual terminal invocation independently. No terminal task
        // or helper async state machine is strongly returned to the caller.
        Task stopping = Begin(() => scope.Runner.StopAsync());
        Task disposing = Begin(() => scope.Runner.DisposeAsync().AsTask());
        Task service = Begin(() => scope.Service.DisposeAsync().AsTask());
        var completion = new TerminalCompletion();
        WeakReference<Task>[] observers = [];
        WeakReference[] graph = [];
        try
        {
            bool inertCompletedBody = primary is null && scope.Clock.SuccessfulTimerCreations == 0 && scope.Owner.Acquisitions == 0;
            if (inertCompletedBody)
            {
                // A pending/faulted real service state drain is still a runtime
                // failure. Its actual invocation must join before owner release.
                Observe(service);
                observers = [ObserveCompletion(stopping, completion, isDispose: false),
                    ObserveCompletion(disposing, completion, isDispose: true)];
                graph = [new(scope), new(scope.Runner), new(scope.Service), new(scope.Monitor)];
            }
            else { Observe(Task.WhenAll(stopping, disposing, service)); }
        }
        catch (Exception cleanup)
        {
            primary = primary is null ? cleanup : new AggregateException("The original inert scenario and actual cleanup both failed.", primary, cleanup);
        }
        // Clock/Owner contain only scalar fields and no reference to Scope,
        // Runner, service or monitor; zero successful timer creations excludes
        // any callback retained by a real timer. Recheck them after graph death.
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), graph,
            completion, observers, scope.Clock, scope.Owner);
    }

    private static Task Begin(Func<Task> begin)
    {
        try { return begin(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static WeakReference<Task> ObserveCompletion(Task actual, TerminalCompletion completion, bool isDispose)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((TerminalCompletion Completion, bool IsDispose))state!;
            if (completed.Exception is { } failure) { observed.Completion.Add(failure); }
            else if (completed.IsCanceled) { observed.Completion.Add(new TaskCanceledException("Actual inert terminal call was canceled.")); }
            else { observed.Completion.Success(observed.IsDispose); }
        }, (completion, isDispose), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }

    private static void Observe(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T Observe<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();

    private sealed record TerminalObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        TerminalCompletion Completion, WeakReference<Task>[] Observers, FaultingClock Clock, InertOwner Owner);

    private sealed class TerminalCompletion
    {
        private readonly object gate = new();
        private readonly List<Exception> failures = [];
        private bool stopSucceeded, disposeSucceeded;
        internal bool StopSucceeded { get { lock (gate) { return stopSucceeded; } } }
        internal bool DisposeSucceeded { get { lock (gate) { return disposeSucceeded; } } }
        internal void Success(bool isDispose) { lock (gate) { if (isDispose) { disposeSucceeded = true; } else { stopSucceeded = true; } } }
        internal void Add(AggregateException errors) { lock (gate) { failures.AddRange(errors.Flatten().InnerExceptions); } }
        internal void Add(Exception error) { lock (gate) { failures.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. failures]; } }
    }

    private sealed class Scope
    {
        internal Scope()
        {
            Service = new(new(), new HealthApplicationIdentity { Name = "collection interval receiving boundary", InstanceId = Guid.NewGuid() }, Clock);
            Monitor = Service.CreateCondition(new() { IsCritical = false, InitiallyFlagged = false });
            Runner = new(Service);
        }
        internal FaultingClock Clock { get; } = new();
        internal InertOwner Owner { get; } = new();
        internal HealthcheckService Service { get; }
        internal IConditionMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }

    }

    private sealed class FaultingClock : TimeProvider
    {
        private int reads, timerCreations, successfulTimerCreations;
        internal bool FailReads { get; set; }
        internal bool FailTimerCreation { get; set; }
        internal int Reads => Volatile.Read(ref reads);
        internal int TimerCreations => Volatile.Read(ref timerCreations);
        internal int SuccessfulTimerCreations => Volatile.Read(ref successfulTimerCreations);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            Interlocked.Increment(ref reads);
            if (FailReads) { throw new ClockUnavailableException(); }
            return 0;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref timerCreations);
            if (FailTimerCreation) { throw new ClockUnavailableException(); }
            ITimer created = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref successfulTimerCreations);
            return created;
        }
    }

    private sealed class InertOwner : IHealthCollector
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

    private sealed class ClockUnavailableException : Exception;
}
