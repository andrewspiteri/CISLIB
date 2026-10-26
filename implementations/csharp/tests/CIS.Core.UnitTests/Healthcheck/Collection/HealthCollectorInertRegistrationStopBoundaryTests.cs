using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorInertRegistrationStopBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void RetiredInertRegistrationLeavesNoActualWorkForTerminalStop()
    {
        BoundaryObservation observed = RunAndReleaseScenario();
        var ownershipFailures = new List<Exception>();
        if (observed.PendingSignalRunner is { } pending)
        {
            CollectUntilDead(pending);
            if (pending.IsAlive)
            {
                ownershipFailures.Add(new InvalidOperationException("Cleanup could not prove that only an unreachable managed Stop/Dispose signal remained; queued or asynchronous work is not excused."));
            }
        }
        if (observed.RejectedRegistrationGraph is { } graph)
        {
            var observation = Stopwatch.StartNew();
            while (graph.Any(reference => reference.IsAlive) && observation.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (graph.Any(reference => reference.IsAlive)) { Thread.Sleep(1); }
            }
            if (graph.Any(reference => reference.IsAlive))
            { ownershipFailures.Add(new InvalidOperationException("Rejected registration still has a reachable actual service, state or runner owner; cleanup remains a hold.")); }
        }
        // These stores do not reference owner/clock/runner objects. Check again
        // after graph observation so completed unexpected work cannot disappear
        // behind a pre-GC zero-count snapshot or the primary assertion.
        ownershipFailures.AddRange(observed.Facts.Errors);
        if (Volatile.Read(ref observed.Facts.Acquisitions) != 0 || Volatile.Read(ref observed.Facts.TimerCreations) != 0)
        { ownershipFailures.Add(new InvalidOperationException("Unexpected owner acquisition or timer creation remains a runtime hold.")); }
        if (ownershipFailures.Count != 0)
        {
            if (observed.Primary is not null) { ownershipFailures.Insert(0, observed.Primary.SourceException); }
            if (observed.RegistrationRejection is not null) { ownershipFailures.Add(observed.RegistrationRejection); }
            throw new AggregateException("Actual inert-fixture ownership or producer work remains unresolved.", ownershipFailures);
        }
        observed.Primary?.Throw();
    }

    // Return no strong runner/service/registration/Task reference or helper async
    // state machine. A queued Pump or ExecuteAsync owner would still root runner.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BoundaryObservation RunAndReleaseScenario()
    {
        var facts = new NoWorkFacts();
        var clock = new NeverStartedClock(facts);
        var owner = new NeverAcquiredOwner(facts);
        var service = new HealthcheckService(new(), new() { Name = "inert registration stop", InstanceId = Guid.NewGuid() }, clock);
        IConsecutiveFailureMonitor? monitor = null;
        HealthCollectorRunner? runner = null;
        IHealthCollectionRegistration? registration = null;
        Task? stop = null;
        bool actualRegistrationJoined = false, pendingStopAssertionFailed = false;
        Exception? primary = null;
        InvalidOperationException? registrationRejection = null;
        AssertFailedException? registrationAcceptanceAssertion = null;
        Task? rejectedEntryDrain = null;
        WeakReference? pendingSignalRunner = null;
        WeakReference[]? rejectedRegistrationGraph = null;
        try
        {
            monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            runner = new HealthCollectorRunner(service);
            try { registration = runner.Register(monitor, owner, new()); }
            catch (InvalidOperationException error) when (error.GetType() == typeof(InvalidOperationException)
                && error.TargetSite is { } site && site.DeclaringType == typeof(HealthCollectorRunner)
                && site.Name == nameof(HealthCollectorRunner.Register))
            {
                // The pinned Register source has one direct InvalidOperation
                // throw: its stopping rejection. Caller/clock/owner/nullable
                // exception sites do not satisfy this exact method guard.
                registrationRejection = error;
                rejectedEntryDrain = ReadActualRejectedEntryDrain(runner);
            }
            try
            {
                Assert.IsTrue(registrationRejection is null, "Valid inert registration must be accepted before any Start, collection or stop.");
                Assert.IsNotNull(registration);
            }
            catch (AssertFailedException error) when (registrationRejection is not null && registration is null)
            {
                registrationAcceptanceAssertion = error;
                throw;
            }

            // No Start, Collect, timer, owner lease or external retirement observer.
            // Join the real registration retirement before checking terminal Stop.
            Observe(registration.DisposeAsync().AsTask());
            actualRegistrationJoined = true;
            Assert.AreEqual(0, owner.Acquisitions);
            Assert.AreEqual(0, clock.TimerCreations);
            monitor.ReportSuccess();
            HealthSnapshot beforeStop = service.CaptureSnapshot();
            Assert.IsFalse(beforeStop.Monitors.Single().Collection!.InProgress);
            Assert.IsFalse(beforeStop.Application.IsStopping);

            // Nonasync Stop/BeginStop publishes SignalProgress inline after this
            // completed-input path. This does not impose a Start/callback SLA.
            stop = runner.StopAsync();
            if (stop.IsFaulted || stop.IsCanceled) { Observe(stop); }
            try
            {
                Assert.IsTrue(stop.IsCompletedSuccessfully,
                    "After the inert registration's actual retirement has returned, terminal stop must publish its owned no-work completion barrier.");
            }
            catch (AssertFailedException) when (!stop.IsCompleted)
            {
                pendingStopAssertionFailed = true;
                throw;
            }
            Observe(stop);

            monitor.ReportSuccess();
            HealthSnapshot afterStop = service.CaptureSnapshot();
            Assert.IsFalse(afterStop.Application.IsStopping);
            Assert.AreEqual(1, afterStop.MonitorCounts.Active);
            Assert.AreEqual(2L, afterStop.Monitors.Single().Outcomes!.Successful);
            Assert.AreEqual(0, owner.Acquisitions);
            Assert.AreEqual(0, clock.TimerCreations);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // Start every actual drain independently, including after partial setup.
            Task registrationDrain = StartDispose(registration);
            Task runnerDrain = StartDispose(runner);
            Task monitorDrain = StartDispose(monitor);
            Task serviceDrain = StartDispose(service);
            Task stateDrains = Task.WhenAll(registrationDrain, monitorDrain, serviceDrain);
            try
            {
                Observe(stateDrains);
                if (Volatile.Read(ref facts.Acquisitions) != 0 || Volatile.Read(ref facts.TimerCreations) != 0 || !facts.Errors.IsEmpty)
                { throw new InvalidOperationException("Unexpected actual producer work cannot be waived by an inert-fixture assertion."); }
                bool rejectedWithoutHandle = registrationRejection is not null && registration is null
                    && registrationAcceptanceAssertion is not null && !actualRegistrationJoined
                    && ReferenceEquals(primary, registrationAcceptanceAssertion)
                    && monitorDrain.IsCompletedSuccessfully && serviceDrain.IsCompletedSuccessfully;
                if (rejectedWithoutHandle)
                {
                    // This is the created entry's actual Disposal.Task, captured
                    // before BeginStop removes the entry. It is not a fabricated
                    // returned handle or a claim that registrationDrain joined it.
                    if (rejectedEntryDrain is null)
                    { throw new InvalidOperationException("The rejected registration's actual retirement completion was not observed."); }
                    Observe(rejectedEntryDrain);
                    RequireNoActualRetirementError(runner!);
                    // This joins the synchronous real Stop invocation, not its
                    // pending signal. All real independent state drains above
                    // already returned. No absent registration is marked joined.
                    stop = runner!.StopAsync();
                    if (stop.IsFaulted || stop.IsCanceled) { Observe(stop); }
                    if (!stop.IsCompleted && !runnerDrain.IsCompleted)
                    {
                        rejectedRegistrationGraph = [new(runner), new(service), new(((IHealthStateAccess)service).CollectionState),
                            new(monitor!), new(clock), new(owner)];
                    }
                    else { Observe(runnerDrain); }
                }
                bool candidatePendingSignal = pendingStopAssertionFailed && primary is AssertFailedException
                    && actualRegistrationJoined && registrationDrain.IsCompletedSuccessfully
                    && monitorDrain.IsCompletedSuccessfully && serviceDrain.IsCompletedSuccessfully
                    && owner.Acquisitions == 0 && clock.TimerCreations == 0
                    && stop is { IsCompleted: false } && !runnerDrain.IsCompleted;
                if (candidatePendingSignal)
                {
                    // This predicate alone cannot exclude an unrelated queued
                    // Pump mutation. The caller must independently prove that no
                    // strong runner owner remains after this helper has returned.
                    pendingSignalRunner = new WeakReference(runner!);
                }
                else if (rejectedRegistrationGraph is null) { Observe(runnerDrain); }
            }
            catch (Exception error)
            {
                primary = primary is null ? error
                    : new AggregateException("The terminal-stop assertion and actual cleanup both failed.",
                        primary, stateDrains.Exception ?? runnerDrain.Exception ?? error);
            }
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), pendingSignalRunner,
            rejectedRegistrationGraph, registrationRejection, facts);
    }

    // Source-specific, read-only observation of the one entry that the pinned
    // Register source added before rejecting publication. Missing or altered
    // metadata is an ownership hold; no private production field is changed.
    private static Task ReadActualRejectedEntryDrain(HealthCollectorRunner runner)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Type runnerType = typeof(HealthCollectorRunner);
        FieldInfo? gateField = runnerType.GetField("gate", fields);
        FieldInfo? entriesField = runnerType.GetField("entries", fields);
        Type? entryType = runnerType.GetNestedType("RegistrationEntry", BindingFlags.NonPublic);
        if (gateField?.FieldType != typeof(object) || gateField.GetValue(runner) is not { } actualGate
            || entriesField is null || entryType is null
            || entriesField.FieldType != typeof(List<>).MakeGenericType(entryType))
        { throw new InvalidOperationException("The rejected registration's source-owned entry metadata is unavailable."); }
        lock (actualGate)
        {
            if (entriesField.GetValue(runner) is not IList entries || entries.Count != 1
                || entries[0] is not { } entry || entry.GetType() != entryType)
            { throw new InvalidOperationException("The rejected registration does not have the one expected actual entry."); }
            FieldInfo? disposalField = entryType.GetField("Disposal", fields);
            if (disposalField?.FieldType != typeof(TaskCompletionSource)
                || disposalField.GetValue(entry) is not TaskCompletionSource completion)
            { throw new InvalidOperationException("The rejected registration's actual retirement task is unavailable."); }
            return completion.Task;
        }
    }

    private static void RequireNoActualRetirementError(HealthCollectorRunner runner)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Type runnerType = typeof(HealthCollectorRunner);
        FieldInfo? gateField = runnerType.GetField("gate", fields);
        FieldInfo? errorField = runnerType.GetField("resourceError", fields);
        if (gateField?.FieldType != typeof(object) || gateField.GetValue(runner) is not { } actualGate
            || errorField?.FieldType != typeof(string))
        { throw new InvalidOperationException("The actual retirement error record is unavailable; cleanup remains a hold."); }
        lock (actualGate)
        {
            if (errorField.GetValue(runner) is { } recorded)
            {
                var hold = new InvalidOperationException("Actual retirement recorded a resource failure; cleanup remains a hold.");
                hold.Data["RunnerResourceError"] = recorded;
                throw hold;
            }
        }
    }

    private static void Observe(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();

    private static Task StartDispose(IAsyncDisposable? owned)
    {
        try { return owned?.DisposeAsync().AsTask() ?? Task.CompletedTask; }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static void CollectUntilDead(WeakReference reference)
    {
        for (int attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed record BoundaryObservation(ExceptionDispatchInfo? Primary, WeakReference? PendingSignalRunner,
        WeakReference[]? RejectedRegistrationGraph, Exception? RegistrationRejection, NoWorkFacts Facts);

    private sealed class NoWorkFacts
    {
        internal int Acquisitions, TimerCreations;
        internal readonly ConcurrentQueue<Exception> Errors = new();
    }

    private sealed class NeverStartedClock(NoWorkFacts facts) : TimeProvider
    {
        internal int TimerCreations;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref TimerCreations);
            Interlocked.Increment(ref facts.TimerCreations);
            var error = new InvalidOperationException("The inert scenario must not create a timer.");
            facts.Errors.Enqueue(error);
            throw error;
        }
    }

    private sealed class NeverAcquiredOwner(NoWorkFacts facts) : IHealthCollector
    {
        internal int Acquisitions;
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref Acquisitions);
            Interlocked.Increment(ref facts.Acquisitions);
            lease = null;
            var error = new InvalidOperationException("The inert scenario must not acquire an owner.");
            facts.Errors.Enqueue(error);
            throw error;
        }
    }
}
