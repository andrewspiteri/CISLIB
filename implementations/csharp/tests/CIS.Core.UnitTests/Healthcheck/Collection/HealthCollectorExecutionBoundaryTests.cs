using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorExecutionBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow("monitor-dispose")]
    [DataRow("monitor-wait")]
    [DataRow("registration-dispose")]
    [DataRow("registration-wait")]
    [DataRow("foreign-service-dispose")]
    [DataRow("foreign-service-wait")]
    [DataRow("foreign-runner-stop")]
    public void ACollectorCanAwaitBarriersThatDoNotContainItsOwnOperation(string barrier)
    {
        AssertTerminalOwnership(RunAndReleaseScenario(Scenario.OtherBarrier, barrier, string.Empty));
    }

    [TestMethod]
    [DataRow("acquisition", "monitor")]
    [DataRow("acquisition", "registration")]
    [DataRow("acquisition", "service")]
    [DataRow("cleanup", "monitor")]
    [DataRow("cleanup", "registration")]
    [DataRow("cleanup", "service")]
    public void ActualAcquisitionAndLeaseCleanupCannotWaitForTheirOwnLifetime(string phase, string barrier)
    {
        AssertTerminalOwnership(RunAndReleaseScenario(Scenario.OwnBarrier, phase, barrier));
    }

    [TestMethod]
    [DataRow("registration")]
    [DataRow("monitor")]
    [DataRow("runner")]
    [DataRow("service")]
    public void AChildContextCapturedByTheOwnerCanDisposeAfterTheActualOperationSettles(string barrier)
    {
        AssertTerminalOwnership(RunAndReleaseScenario(Scenario.ChildBarrier, barrier, string.Empty));
    }

    private static async Task ACollectorCanAwaitBarriersThatDoNotContainItsOwnOperation(Scope local, Scope? foreign, string barrier, Action? foreignRunnerProof = null)
    {
        IConsecutiveFailureMonitor actor = local.Monitor();
        IConsecutiveFailureMonitor otherMonitor = local.Monitor();
        var idleOwner = new Owner();
        IHealthCollectionRegistration otherRegistration = local.Register(otherMonitor, idleOwner);
        int barrierCompleted = 0;
        ExceptionDispatchInfo? foreignFailure = null;
        var owner = new Owner
        {
            Evaluate = async _ =>
            {
                switch (barrier)
                {
                    case "monitor-dispose": await otherMonitor.DisposeAsync().AsTask().WaitAsync(Deadline); break;
                    case "monitor-wait": otherMonitor.Withdraw(); await otherMonitor.WaitForDisposalAsync().WaitAsync(Deadline); break;
                    case "registration-dispose": await otherRegistration.DisposeAsync().AsTask().WaitAsync(Deadline); break;
                    case "registration-wait": otherRegistration.Withdraw(); await otherRegistration.WaitForDisposalAsync().WaitAsync(Deadline); break;
                    case "foreign-service-dispose": await foreign!.Service.DisposeAsync().AsTask().WaitAsync(Deadline); break;
                    case "foreign-service-wait":
                        await foreign!.Service.DisposeAsync().AsTask().WaitAsync(Deadline);
                        await foreign.Service.WaitForDisposalAsync().WaitAsync(Deadline);
                        break;
                    case "foreign-runner-stop":
                        try
                        {
                            if (foreignRunnerProof is null) { AssertTerminalOwnership(StopAndReleaseForeignRunner(local.ForeignExpectedTimerCountForControl), local.KnownForeignAssertions.Add); }
                            else { foreignRunnerProof(); }
                        }
                        catch (Exception error)
                        {
                            foreignFailure = ExceptionDispatchInfo.Capture(error);
                            if (foreignRunnerProof is not null || !local.KnownForeignAssertions.Any(known => ReferenceEquals(known, error))) { throw; }
                        }
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(barrier));
                }
                Interlocked.Increment(ref barrierCompleted);
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }
        };
        IHealthCollectionRegistration registration = local.Register(actor, owner);
        await local.Runner.StartAsync().WaitAsync(Deadline);

        local.BeforeCollection?.Invoke();
        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);
        // Runner reports evaluator failures as collection diagnostics. Preserve
        // the actual foreign proof failure after the real operation and lease
        // join so a local milestone assertion cannot mask an ownership hold.
        foreignFailure?.Throw();

        Assert.AreEqual(1, barrierCompleted);
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, idleOwner.Acquisitions);
        HealthMonitorSnapshot snapshot = local.Snapshot(actor);
        Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.IsNull(snapshot.Collection!.LastError);
    }

    private static async Task ActualAcquisitionAndLeaseCleanupCannotWaitForTheirOwnLifetime(Scope scope, string phase, string barrier)
    {
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        IHealthCollectionRegistration? registration = null;
        Exception? observed = null;
        Task? incorrectlyAdmittedWait = null;
        int attempts = 0;
        void TryOwnWait()
        {
            Interlocked.Increment(ref attempts);
            try
            {
                incorrectlyAdmittedWait = barrier switch
                {
                    "monitor" => monitor.WaitForDisposalAsync(),
                    "registration" => registration!.WaitForDisposalAsync(),
                    _ => scope.Service.WaitForDisposalAsync()
                };
            }
            catch (Exception error) { observed = error; }
        }
        var owner = new Owner
        {
            DuringAcquisition = phase == "acquisition" ? TryOwnWait : null,
            Cleanup = () =>
            {
                if (phase == "cleanup") { TryOwnWait(); }
                return ValueTask.CompletedTask;
            }
        };
        registration = scope.Register(monitor, owner);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, attempts);
        Assert.IsNotNull(observed);
        Assert.AreEqual(typeof(InvalidOperationException), observed.GetType());
        Assert.IsNull(incorrectlyAdmittedWait, "A self-containing wait must be rejected before an incomplete task is handed to its owner.");
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);
    }

    private static async Task AChildContextCapturedByTheOwnerCanDisposeAfterTheActualOperationSettles(Scope scope, string barrier, Action? beforeChildRelease = null)
    {
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        IHealthCollectionRegistration? registration = null;
        var childEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? child = null;
        int completed = 0;
        var owner = new Owner
        {
            Evaluate = _ =>
            {
                child = Task.Run(async () =>
                {
                    childEntered.TrySetResult();
                    await releaseChild.Task;
                    switch (barrier)
                    {
                        case "registration": await registration!.DisposeAsync().AsTask().WaitAsync(Deadline); break;
                        case "monitor": await monitor.DisposeAsync().AsTask().WaitAsync(Deadline); break;
                        case "runner":
                            // Stop performs its execution-context check before
                            // returning this exact barrier task. The real child
                            // makes that call and joins here; completion of the
                            // same public task is asserted after real cleanup.
                            scope.ChildRunnerStops.Add(scope.Runner.StopAsync());
                            return;
                        default: await scope.Service.DisposeAsync().AsTask().WaitAsync(Deadline); break;
                    }
                    Interlocked.Increment(ref completed);
                });
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
        };
        registration = scope.Register(monitor, owner);
        try
        {
            await scope.Runner.StartAsync().WaitAsync(Deadline);
            HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.AreEqual(1, owner.Evaluations);
            await childEntered.Task.WaitAsync(Deadline);
            Assert.IsNotNull(child);
            Assert.IsFalse(child.IsCompleted);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);

            beforeChildRelease?.Invoke();
            releaseChild.TrySetResult();
            await child.WaitAsync(Deadline);

            if (barrier == "runner") { Assert.AreEqual(1, scope.ChildRunnerStops.Count, "The actual child must make exactly one runner Stop call."); }
            else { Assert.AreEqual(1, completed); }
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
        }
        finally
        {
            releaseChild.TrySetResult();
            if (child is not null) { await child.WaitAsync(Deadline); }
        }
    }

    private enum Scenario { OtherBarrier, OwnBarrier, ChildBarrier }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TerminalObservation StopAndReleaseForeignRunner(int expectedTimerCount = 0)
    {
        // Called synchronously inside the real local evaluation. Only this
        // newly created foreign graph is released; the local operation stays
        // active until this actual foreign barrier has been asserted.
        var foreign = new Scope();
        Task stop = Begin(() => foreign.Runner.StopAsync());
        Task dispose = Begin(() => foreign.Runner.DisposeAsync().AsTask());
        Task service = Begin(() => foreign.Service.DisposeAsync().AsTask());
        Exception? primary = null;
        var completion = new TerminalCompletion();
        WeakReference[] graph = [];
        WeakReference<Task>[] observers = [];
        try
        {
            Observe(service);
            if (foreign.Registrations.Count != 0 || foreign.Owners.Count != 0 || foreign.Clock.Timers.Count != 0)
            {
                throw new InvalidOperationException("The foreign runner must remain unstarted with no registrations, owners or timer callbacks.");
            }
            observers = [ObserveCompletion(stop, completion, "foreign.Stop"), ObserveCompletion(dispose, completion, "foreign.RunnerDispose")];
            graph = foreign.WeakGraph();
        }
        catch (Exception error)
        {
            primary = error;
            try { Observe(Task.WhenAll(stop, dispose, service)); }
            catch (Exception cleanup) { primary = new AggregateException("Foreign barrier phase and actual cleanup both failed.", error, cleanup); }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), graph, completion,
            observers, [], [], ["foreign.Stop", "foreign.RunnerDispose"], ExpectedTimerCount: expectedTimerCount);
    }

    private static void AssertTerminalOwnership(TerminalObservation observed, Action<Exception>? recordProvenForeignAssertion = null)
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
                throw new InvalidOperationException("Actual execution-boundary owners remain reachable; pending callbacks, children, leases or queued work remain a hold.");
            }
            foreach (OwnerCounters owner in observed.Owners)
            {
                if (!owner.ActualWorkReturned) { throw new InvalidOperationException("Owner or lease work appeared during ownership observation."); }
            }
            foreach (TimerCounters timer in observed.Timers)
            {
                if (Volatile.Read(ref timer.CallbackEntries) != 0 || Volatile.Read(ref timer.CallbackReturns) != 0)
                { throw new InvalidOperationException("The private manual timer unexpectedly invoked a callback; this phase proof does not excuse it."); }
            }
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { Observe(actual); }
            }
            TerminalFailure[] failures = observed.Completion.Failures();
            if (failures.Any(failure => failure.Error.GetType() != typeof(InvalidOperationException)))
            {
                throw new AggregateException("An actual terminal call had an unaccepted runtime failure or cancellation.", failures.Select(failure => failure.Error));
            }
            if (observed.ProvenForeignAssertion is { } proven)
            {
                if (!ReferenceEquals(observed.Primary?.SourceException, proven))
                { throw new InvalidOperationException("The exact proved foreign assertion provenance changed."); }
                observed.Primary.Throw();
            }
            // These actual private manual timers have no native scheduler, and
            // their entire callback/state graph is already unreachable. A missed
            // disposal is still a failed API contract, never successful cleanup.
            try
            {
                Assert.AreEqual(observed.ExpectedTimerCount, observed.Timers.Length, "Successful Start must create the expected actual runner timer.");
                foreach (TimerCounters timer in observed.Timers)
                {
                    Assert.IsTrue(Volatile.Read(ref timer.DisposalRequests) > 0, "The runner must actually invoke disposal for each created manual timer.");
                    Assert.IsTrue(Volatile.Read(ref timer.DisposalCompletions) > 0, "Each actual timer disposal must return successfully.");
                }
                Assert.AreEqual(0, failures.Length, "Valid terminal calls must not reject shutdown.");
                foreach (string name in observed.RequiredCompletions)
                {
                    Assert.IsTrue(observed.Completion.Succeeded(name), "Actual terminal call must publish successful completion after real ownership is released: " + name);
                }
            }
            catch (AssertFailedException error)
            {
                // Only the default foreign path supplies this recorder, after
                // all actual foreign ownership/runtime checks above passed.
                recordProvenForeignAssertion?.Invoke(error);
                throw;
            }
        }
        observed.Primary?.Throw();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TerminalObservation RunAndReleaseScenario(Scenario scenario, string first, string second, Action<Scope>? initialize = null)
    {
        var local = new Scope();
        Scope? foreign = scenario == Scenario.OtherBarrier && first != "foreign-runner-stop" ? new Scope() : null;
        Scope[] scopes = foreign is null ? [local] : [local, foreign];
        Exception? primary = null;
        try
        {
            initialize?.Invoke(local);
            Task body = scenario switch
            {
                Scenario.OtherBarrier => ACollectorCanAwaitBarriersThatDoNotContainItsOwnOperation(local, foreign, first, local.ForeignRunnerProof),
                Scenario.OwnBarrier => ActualAcquisitionAndLeaseCleanupCannotWaitForTheirOwnLifetime(local, first, second),
                Scenario.ChildBarrier => AChildContextCapturedByTheOwnerCanDisposeAfterTheActualOperationSettles(local, first),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            // Every scenario body, callback and actual Task.Run child joins.
            // The runner child's exact public Stop task remains separately
            // owned until its real terminal completion assertion below.
            Observe(body);
        }
        catch (Exception error) { primary = error; }

        var actual = new List<(string Name, Task Task)>();
        for (int index = 0; index < scopes.Length; index++)
        {
            Scope scope = scopes[index];
            // Stop must run before registration disposal: an independent
            // Withdraw must not repair a missing BeginStop retirement call.
            actual.Add(($"scope{index}.Stop", Begin(() => scope.Runner.StopAsync())));
        }
        for (int index = 0; index < scopes.Length; index++)
        {
            Scope scope = scopes[index];
            for (int child = 0; child < scope.ChildRunnerStops.Count; child++)
            {
                actual.Add(($"scope{index}.ChildRunnerStop{child}", scope.ChildRunnerStops[child]));
            }
            actual.Add(($"scope{index}.RunnerDispose", Begin(() => scope.Runner.DisposeAsync().AsTask())));
            for (int registration = 0; registration < scope.Registrations.Count; registration++)
            {
                IHealthCollectionRegistration handle = scope.Registrations[registration];
                actual.Add(($"scope{index}.Registration{registration}", Begin(() => handle.DisposeAsync().AsTask())));
            }
        }
        Task[] services = scopes.Select(scope => Begin(() => scope.Service.DisposeAsync().AsTask())).ToArray();
        var completion = new TerminalCompletion();
        WeakReference[] graph = [];
        WeakReference<Task>[] observers = [];
        OwnerCounters[] owners = scopes.SelectMany(scope => scope.Owners).Select(owner => owner.Counters).ToArray();
        TimerCounters[] timers = scopes.SelectMany(scope => scope.Clock.Timers).Select(timer => timer.Counters).ToArray();
        try
        {
            // Actual state disposal is an independent mandatory join. Ghost
            // State settlement cannot become a graph-completion assertion.
            Observe(Task.WhenAll(services));
            bool provenForeignPrimary = primary is not null && scopes.Any(scope => scope.KnownForeignAssertions.Any(known => ReferenceEquals(known, primary)));
            bool candidate = (primary is null || provenForeignPrimary) && owners.All(owner => owner.ActualWorkReturned)
                && timers.All(timer => Volatile.Read(ref timer.CallbackEntries) == 0 && Volatile.Read(ref timer.CallbackReturns) == 0);
            if (candidate)
            {
                observers = actual.Select(item => ObserveCompletion(item.Task, completion, item.Name)).ToArray();
                graph = scopes.SelectMany(scope => scope.WeakGraph()).ToArray();
            }
            else { Observe(Task.WhenAll(actual.Select(item => item.Task).Concat(services))); }
        }
        catch (Exception cleanup)
        {
            primary = primary is null ? cleanup : new AggregateException("The original execution-boundary body and actual cleanup both failed.", primary, cleanup);
        }
        primary = PreserveOwnerFailures(primary, owners, timers, scopes.SelectMany(scope => scope.KnownForeignAssertions).ToArray());
        Exception? provenAssertion = graph.Length != 0 && primary is not null && scopes.Any(scope => scope.KnownForeignAssertions.Any(known => ReferenceEquals(known, primary))) ? primary : null;
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), graph, completion,
            observers, owners, timers, actual.Select(item => item.Name).ToArray(), ExpectedTimerCount: 1, ProvenForeignAssertion: provenAssertion);
    }

    private static Exception? PreserveOwnerFailures(Exception? primary, OwnerCounters[] owners, TimerCounters[] timers, Exception[] provenForeignAssertions)
    {
        // Production reports evaluator failures as diagnostics. Original body
        // assertions must never hide a recorded unexpected owner exception,
        // even when the body failed and the strict cleanup path was selected.
        Exception[] unknownOwnerErrors = owners.SelectMany(owner => owner.Errors()).Select(error => error.Error.SourceException)
            .Where(error => !ReferenceEquals(error, primary) || !provenForeignAssertions.Any(known => ReferenceEquals(known, error)))
            .Distinct(ReferenceEqualityComparer.Instance).Cast<Exception>().ToArray();
        bool forbiddenCallback = timers.Any(timer => Volatile.Read(ref timer.CallbackEntries) != 0 || Volatile.Read(ref timer.CallbackReturns) != 0);
        if (unknownOwnerErrors.Length != 0 || forbiddenCallback)
        {
            var failures = new List<Exception>();
            if (primary is not null) { failures.Add(primary); }
            failures.AddRange(unknownOwnerErrors.Where(error => !failures.Any(existing => ReferenceEquals(existing, error))));
            if (forbiddenCallback) { failures.Add(new InvalidOperationException("A private execution-boundary manual timer unexpectedly invoked a callback.")); }
            primary = new AggregateException("Actual unexpected owner errors or forbidden callbacks remain runtime holds after real cleanup.", failures);
        }
        return primary;
    }

    private static Task Begin(Func<Task> begin)
    {
        try { return begin(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveCompletion(Task actual, TerminalCompletion completion, string name)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((TerminalCompletion Completion, string Name))state!;
            if (completed.Exception is { } failure)
            {
                foreach (Exception error in failure.Flatten().InnerExceptions) { observed.Completion.Add(observed.Name, error); }
            }
            else if (completed.IsCanceled) { observed.Completion.Add(observed.Name, new TaskCanceledException("Actual terminal call was canceled.")); }
            else { observed.Completion.Success(observed.Name); }
        }, (completion, name), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private static void Observe(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private sealed record TerminalObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, TerminalCompletion Completion,
        WeakReference<Task>[] Observers, OwnerCounters[] Owners, TimerCounters[] Timers, string[] RequiredCompletions, int ExpectedTimerCount, Exception? ProvenForeignAssertion = null);
    private sealed record TerminalFailure(string Name, Exception Error);
    private sealed class TerminalCompletion
    {
        private readonly object gate = new();
        private readonly List<TerminalFailure> failures = [];
        private readonly HashSet<string> successes = new(StringComparer.Ordinal);
        internal void Success(string name) { lock (gate) { successes.Add(name); } }
        internal bool Succeeded(string name) { lock (gate) { return successes.Contains(name); } }
        internal void Add(string name, Exception error) { lock (gate) { failures.Add(new(name, error)); } }
        internal TerminalFailure[] Failures() { lock (gate) { return [.. failures]; } }
    }

    private sealed class Scope
    {
        internal Scope()
        {
            Service = new HealthcheckService(new(), new() { Name = "collector execution boundaries", InstanceId = Guid.NewGuid() }, Clock);
            Runner = new HealthCollectorRunner(Service);
        }
        private List<IHealthMonitor> Monitors { get; } = [];
        internal List<IHealthCollectionRegistration> Registrations { get; } = [];
        internal List<Owner> Owners { get; } = [];
        internal List<Task> ChildRunnerStops { get; } = [];
        internal List<Exception> KnownForeignAssertions { get; } = [];
        internal Action? BeforeCollection { get; set; }
        internal Action? ForeignRunnerProof { get; set; }
        internal int ForeignExpectedTimerCountForControl { get; set; }
        internal TrackingClock Clock { get; } = new();
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal IConsecutiveFailureMonitor Monitor()
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Monitors.Add(monitor);
            return monitor;
        }
        internal IHealthCollectionRegistration Register(IHealthMonitor monitor, Owner owner)
        {
            IHealthCollectionRegistration registration = Runner.Register(monitor, owner, new());
            Registrations.Add(registration);
            Owners.Add(owner);
            return registration;
        }
        internal HealthMonitorSnapshot Snapshot(IHealthMonitor monitor) => Service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
        internal WeakReference[] WeakGraph() => [new(this), new(Service), new(Runner), .. Monitors.Select(monitor => new WeakReference(monitor)),
            .. Registrations.Select(registration => new WeakReference(registration)), .. Owners.Select(owner => new WeakReference(owner)), .. Clock.WeakGraph()];
    }

    private sealed class TimerCounters
    {
        internal int CallbackEntries, CallbackReturns, DisposalRequests, DisposalCompletions;
    }
    private sealed class TrackingClock : TimeProvider
    {
        private readonly ManualTimeProvider inner = new(new DateTimeOffset(2026, 10, 22, 18, 0, 0, TimeSpan.Zero));
        internal List<TrackedTimer> Timers { get; } = [];
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TrackedTimer(callback, state);
            timer.Initialize(inner.CreateTimer(static item => ((TrackedTimer)item!).Invoke(), timer, dueTime, period));
            Timers.Add(timer);
            return timer;
        }
        // No Advance is exposed or called. Every actual manual timer and its
        // readonly callback/state remain within this private ownership graph.
        internal WeakReference[] WeakGraph() => [new(this), new(inner), .. Timers.SelectMany(timer => timer.WeakGraph())];
    }
    private sealed class TrackedTimer(TimerCallback callback, object? state) : ITimer
    {
        private ITimer? actual;
        internal TimerCounters Counters { get; } = new();
        internal void Initialize(ITimer created) => actual = created;
        internal void Invoke()
        {
            Interlocked.Increment(ref Counters.CallbackEntries);
            try { callback(state); }
            finally { Interlocked.Increment(ref Counters.CallbackReturns); }
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) => actual!.Change(dueTime, period);
        public void Dispose()
        {
            Interlocked.Increment(ref Counters.DisposalRequests);
            actual!.Dispose();
            Interlocked.Increment(ref Counters.DisposalCompletions);
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Counters.DisposalRequests);
            await actual!.DisposeAsync().ConfigureAwait(false);
            Interlocked.Increment(ref Counters.DisposalCompletions);
        }
        internal WeakReference[] WeakGraph() => [new(this), new(actual!)];
    }

    private sealed class OwnerCounters
    {
        private readonly object errorGate = new();
        private readonly List<OwnerError> errors = [];
        internal void Record(string phase, Exception error) { lock (errorGate) { errors.Add(new(phase, ExceptionDispatchInfo.Capture(error))); } }
        internal OwnerError[] Errors() { lock (errorGate) { return [.. errors]; } }
        internal int Acquisitions, AcquisitionReturns, AcquisitionFaults, Leases;
        internal int Evaluations, EvaluationReturns, EvaluationCompletions, EvaluationFaults;
        internal int Cleanups, CleanupReturns, CleanupCompletions, CleanupFaults;
        internal bool ActualWorkReturned => Volatile.Read(ref Acquisitions) == Volatile.Read(ref AcquisitionReturns)
            && Volatile.Read(ref AcquisitionFaults) == 0 && Volatile.Read(ref Leases) == Volatile.Read(ref CleanupCompletions)
            && Volatile.Read(ref Evaluations) == Volatile.Read(ref EvaluationReturns) && Volatile.Read(ref Evaluations) == Volatile.Read(ref EvaluationCompletions)
            && Volatile.Read(ref EvaluationFaults) == 0 && Volatile.Read(ref Cleanups) == Volatile.Read(ref CleanupReturns)
            && Volatile.Read(ref Cleanups) == Volatile.Read(ref CleanupCompletions) && Volatile.Read(ref CleanupFaults) == 0;
    }
    private sealed record OwnerError(string Phase, ExceptionDispatchInfo Error);
    private sealed class Owner : IHealthCollector
    {
        internal OwnerCounters Counters { get; } = new();
        internal Action? DuringAcquisition { get; init; }
        internal Func<CancellationToken, Task<HealthCollectionResult>>? Evaluate { get; init; }
        internal Func<ValueTask>? Cleanup { get; init; }
        internal int Acquisitions => Volatile.Read(ref Counters.Acquisitions);
        internal int Evaluations => Volatile.Read(ref Counters.Evaluations);
        internal int Cleanups => Volatile.Read(ref Counters.Cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref Counters.Acquisitions);
            try
            {
                DuringAcquisition?.Invoke();
                lease = new Lease(this);
                Interlocked.Increment(ref Counters.Leases);
                return true;
            }
            catch (Exception error) { Counters.Record("acquisition", error); Interlocked.Increment(ref Counters.AcquisitionFaults); throw; }
            finally { Interlocked.Increment(ref Counters.AcquisitionReturns); }
        }
        private sealed class Lease(Owner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.Counters.Evaluations);
                Task<HealthCollectionResult> actual;
                try
                {
                    actual = owner.Evaluate?.Invoke(lifetimeCancellation)
                        ?? Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
                    Interlocked.Increment(ref owner.Counters.EvaluationReturns);
                }
                catch (Exception error) { owner.Counters.Record("evaluation-invocation", error); Interlocked.Increment(ref owner.Counters.EvaluationFaults); throw; }
                return ObserveEvaluationAsync(actual);
            }
            private async Task<HealthCollectionResult> ObserveEvaluationAsync(Task<HealthCollectionResult> actual)
            {
                try
                {
                    HealthCollectionResult result = await actual.ConfigureAwait(false);
                    Interlocked.Increment(ref owner.Counters.EvaluationCompletions);
                    return result;
                }
                catch (Exception error) { owner.Counters.Record("evaluation-task", error); Interlocked.Increment(ref owner.Counters.EvaluationFaults); throw; }
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.Counters.Cleanups);
                ValueTask actual;
                try
                {
                    actual = owner.Cleanup?.Invoke() ?? ValueTask.CompletedTask;
                    Interlocked.Increment(ref owner.Counters.CleanupReturns);
                }
                catch (Exception error) { owner.Counters.Record("cleanup-invocation", error); Interlocked.Increment(ref owner.Counters.CleanupFaults); throw; }
                return ObserveCleanupAsync(actual);
            }
            private async ValueTask ObserveCleanupAsync(ValueTask actual)
            {
                try
                {
                    await actual.ConfigureAwait(false);
                    Interlocked.Increment(ref owner.Counters.CleanupCompletions);
                }
                catch (Exception error) { owner.Counters.Record("cleanup-task", error); Interlocked.Increment(ref owner.Counters.CleanupFaults); throw; }
            }
        }
    }
}
