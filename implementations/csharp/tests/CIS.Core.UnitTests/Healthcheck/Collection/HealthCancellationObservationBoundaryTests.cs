using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCancellationObservationBoundaryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ACurrentCancellationObserverFaultCannotPublishAPartialDiagnosticOrEscapeStopping(bool clockFails)
    {
        var clock = new ProjectionClock();
        var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity
        {
            Name = "current cancellation observation",
            InstanceId = new Guid("6f7fc4f0-b67a-4974-9666-2402ac613191")
        }, clock);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor? monitor = null;
        HealthCollectionReservation? operation = null;
        int callbackCalls = 0, callbackReturns = 0;
        CancellationTokenRegistration callback = default;
        Task? settlement = null, monitorDrain = null, serviceDrain = null;
        bool stoppingCallReturned = false, completionCallReturned = false;
        Exception? primaryFailure = null;
        try
        {
            monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId,
                new Guid("1126125a-b393-4a7f-bfa3-348f573c8de6"), new HealthCollectorOptions());
            HealthCollectionReservationResult reserved = state.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, reserved.Disposition);
            Assert.IsNotNull(reserved.Reservation);
            operation = reserved.Reservation;
            settlement = state.WaitForSettlementAsync(operation);
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.IsNull(before.Monitors.Single().Collection!.LastError);
            callback = operation.LifetimeCancellation.Register(() =>
            {
                try
                {
                    Interlocked.Increment(ref callbackCalls);
                    if (clockFails) { clock.FailNextUtcRead(); }
                    throw new CancellationObserverException();
                }
                finally { Interlocked.Increment(ref callbackReturns); }
            });

            // No runner or attachment withdrawal precedes this public stop. The
            // actual state-issued attachment is still current when its observer faults.
            Exception? propagated = null;
            try { service.BeginStopping(); stoppingCallReturned = true; }
            catch (Exception exception) { propagated = exception; }

            Assert.IsNull(propagated, "Cancellation observation and diagnostic-clock failures must not escape stopping.");
            Assert.AreEqual(1, callbackCalls);
            Assert.AreEqual(1, callbackReturns);
            Assert.AreEqual(clockFails ? 1 : 0, clock.UtcFailures);
            Assert.IsTrue(operation.LifetimeCancellation.IsCancellationRequested);
            Assert.IsFalse(settlement.IsCompleted);
            HealthSnapshot stopped = service.CaptureSnapshot();
            HealthMonitorSnapshot observed = stopped.Monitors.Single();
            Assert.IsTrue(stopped.Application.IsStopping);
            Assert.IsFalse(stopped.Ready);
            Assert.AreEqual(before.Revision + (clockFails ? 1 : 2), stopped.Revision);
            Assert.AreEqual(MonitorState.Pending, observed.State);
            Assert.AreEqual(0L, observed.Outcomes!.Successful);
            Assert.AreEqual(0L, observed.Outcomes.Failed);
            Assert.AreEqual(before.Monitors.Single().LastObservationAtUtc, observed.LastObservationAtUtc);
            Assert.IsTrue(observed.Collection!.InProgress);
            if (clockFails)
            {
                Assert.IsNull(observed.Collection.LastError);
                Assert.IsNull(observed.Collection.LastErrorAtUtc);
            }
            else
            {
                Assert.IsNotNull(observed.Collection.LastError);
                Assert.AreEqual("Healthcheck.Cancellation", observed.Collection.LastError.Code);
                Assert.AreEqual(typeof(AggregateException).FullName, observed.Collection.LastError.ExceptionType);
                Assert.AreEqual(Epoch, observed.Collection.LastErrorAtUtc);
            }
            Assert.IsNull(before.Monitors.Single().Collection!.LastError);
            Assert.IsNull(before.Monitors.Single().Collection!.LastErrorAtUtc);

            HealthCollectionAttempt completion = state.Complete(operation,
                new(HealthCollectionCompletionReason.RetiredBeforeInvocation, Epoch));
            completionCallReturned = true;
            Assert.AreEqual(HealthCollectionDisposition.Retired, completion.Disposition);
            Assert.IsFalse(completion.EvaluationFailed);
            Assert.IsTrue(settlement.IsCompletedSuccessfully);
            Assert.IsFalse(service.CaptureSnapshot().Monitors.Single().Collection!.InProgress);

            // These non-async public methods return the actual state-owned
            // drain tasks after synchronous Withdraw/Dispatch has returned.
            // This fixture has no runner, timer, lease, I/O or async observer.
            monitorDrain = monitor.DisposeAsync().AsTask();
            serviceDrain = service.DisposeAsync().AsTask();
            Assert.IsTrue(monitorDrain.IsCompletedSuccessfully,
                "The monitor has no actual reservation or cancellation callback remaining after synchronous state finalization returns.");
            Assert.IsTrue(serviceDrain.IsCompletedSuccessfully,
                "The service has no actual collection operation or retirement callback remaining after synchronous disposal dispatch returns.");
            await Task.WhenAll(monitorDrain, serviceDrain).WaitAsync(Deadline);
        }
        catch (Exception error) { primaryFailure = error; }
        await FinishScenarioAsync(state, service, monitor, operation, callback,
            stoppingCallReturned, completionCallReturned, callbackCalls, callbackReturns,
            primaryFailure, settlement, monitorDrain, serviceDrain);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AWithdrawnAttachmentFinalizesAfterItsActualCancellationAndCompletionCallsReturn(bool clockFails)
    {
        var clock = new ProjectionClock();
        var service = new HealthcheckService(new(), new() { Name = "closed cancellation settlement", InstanceId = Guid.NewGuid() }, clock);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor? monitor = null;
        HealthCollectionReservation? operation = null;
        CancellationTokenRegistration callback = default;
        Task? settlement = null, attachmentDrain = null, monitorDrain = null, serviceDrain = null;
        int callbackCalls = 0, callbackReturns = 0;
        bool withdrawalCallReturned = false, completionCallReturned = false;
        Exception? primaryFailure = null;
        try
        {
            monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
            HealthCollectionReservationResult reserved = state.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, reserved.Disposition);
            Assert.IsNotNull(reserved.Reservation);
            operation = reserved.Reservation;
            settlement = state.WaitForSettlementAsync(operation);
            Assert.IsTrue(state.TryEnter(operation));
            callback = operation.LifetimeCancellation.Register(() =>
            {
                try
                {
                    Interlocked.Increment(ref callbackCalls);
                    if (clockFails) { clock.FailNextUtcRead(); }
                    throw new CancellationObserverException();
                }
                finally { Interlocked.Increment(ref callbackReturns); }
            });

            // Match the resource fixture's actual withdrawal-before-completion
            // order, at the synchronous state boundary without a dispatcher.
            ArgumentException? diagnosticRejection = null;
            try
            {
                attachmentDrain = state.WithdrawCollection(target);
                withdrawalCallReturned = true;
            }
            catch (ArgumentException error) { diagnosticRejection = error; }
            // This actual state-owned target and its sole real observer are
            // valid controlled inputs. Diagnostic projection must not reject
            // this withdrawal. A thrown call did not return: strict actual
            // completion/callback/drain cleanup still runs with its flag false.
            Assert.IsNull(diagnosticRejection, "The actual valid withdrawal must contain its cancellation observer's diagnostic projection failure.");
            Assert.IsNotNull(attachmentDrain, "A returned actual withdrawal must supply its real state-owned disposal task.");
            Assert.AreEqual(1, callbackCalls);
            Assert.AreEqual(1, callbackReturns);
            Assert.AreEqual(clockFails ? 1 : 0, clock.UtcFailures);
            Assert.IsTrue(operation.LifetimeCancellation.IsCancellationRequested);
            Assert.IsFalse(settlement.IsCompleted);
            Assert.IsFalse(attachmentDrain.IsCompleted);
            monitor.ReportSuccess();
            HealthMonitorSnapshot beforeCompletion = service.CaptureSnapshot().Monitors.Single();
            Assert.IsTrue(beforeCompletion.Collection!.InProgress);
            Assert.IsNull(beforeCompletion.Collection.LastError);
            Assert.IsNull(beforeCompletion.Collection.LastCompletedAtUtc);
            Assert.AreEqual(MonitorState.Healthy, beforeCompletion.State);
            Assert.AreEqual(1L, beforeCompletion.Outcomes!.Successful);
            Assert.AreEqual(0L, beforeCompletion.Outcomes.Failed);

            // Both actual public disposal calls return after synchronous state
            // dispatch; the test still owns the uncompleted real reservation.
            monitorDrain = monitor.DisposeAsync().AsTask();
            serviceDrain = service.DisposeAsync().AsTask();
            Assert.IsFalse(monitorDrain.IsCompleted);
            Assert.IsFalse(serviceDrain.IsCompleted);
            Assert.IsFalse(settlement.IsCompleted);
            Assert.IsFalse(attachmentDrain.IsCompleted);

            // The test owns this real state-issued reservation. Its sole actual
            // cancellation callback returned; this is its single actual Complete
            // call, not replacement completion of a pending runner operation.
            HealthCollectionAttempt completion = state.Complete(operation, new(HealthCollectionCompletionReason.Settled, Epoch));
            completionCallReturned = true;
            Assert.AreEqual(HealthCollectionDisposition.Retired, completion.Disposition);
            Assert.IsFalse(completion.EvaluationFailed);
            Assert.IsNull(completion.Diagnostic);
            Assert.IsTrue(settlement.IsCompletedSuccessfully,
                "Actual operation settlement must be published after the sole cancellation callback and synchronous Complete dispatch return.");
            Assert.IsTrue(attachmentDrain.IsCompletedSuccessfully,
                "A closed attachment has no remaining actual operation or retirement observer after synchronous finalization returns.");
            Assert.IsTrue(monitorDrain.IsCompletedSuccessfully,
                "The withdrawn monitor has no remaining operation or retirement observer after its sole actual completion dispatch returns.");
            Assert.IsTrue(serviceDrain.IsCompletedSuccessfully,
                "The disposing service has no remaining operation or retirement observer after its sole actual completion dispatch returns.");
            Assert.ThrowsExactly<ObjectDisposedException>(() => service.CaptureSnapshot());
            Assert.IsTrue(beforeCompletion.Collection.InProgress, "Earlier detached projection retains the real pending reservation it observed.");
            Assert.IsNull(beforeCompletion.Collection.LastError);
            await Task.WhenAll(settlement, attachmentDrain, monitorDrain, serviceDrain).WaitAsync(Deadline);
        }
        catch (Exception error) { primaryFailure = error; }
        await FinishScenarioAsync(state, service, monitor, operation, callback,
            withdrawalCallReturned, completionCallReturned, callbackCalls, callbackReturns,
            primaryFailure, settlement, attachmentDrain, monitorDrain, serviceDrain);
    }

    private static async Task FinishScenarioAsync(IHealthCollectionState state, HealthcheckService service,
        IConsecutiveFailureMonitor? monitor, HealthCollectionReservation? operation, CancellationTokenRegistration callback,
        bool actualControlCallReturned, bool completionCallReturned, int callbackCalls, int callbackReturns,
        Exception? primaryFailure, params Task?[] observedDrains)
    {
        var failures = new List<Exception>(); bool callbackRegistrationDisposed = false;
        try
        {
            try
            {
                if (operation is not null && !completionCallReturned)
                {
                    state.Complete(operation, new(HealthCollectionCompletionReason.RetiredBeforeInvocation, Epoch));
                    completionCallReturned = true;
                }
            }
            finally { callback.Dispose(); callbackRegistrationDisposed = true; }
        }
        catch (Exception error) { failures.Add(error); }
        // Initiate each actual owner drain independently even after partial setup
        // or an earlier synchronous disposal failure. Faults are retained.
        Task monitorDrain = BeginDispose(monitor), serviceDrain = BeginDispose(service);
        bool actualWorkJoined = actualControlCallReturned && completionCallReturned
            && callbackCalls == 1 && callbackReturns == 1 && callbackRegistrationDisposed;
        foreach (Task drain in observedDrains.Append(monitorDrain).Append(serviceDrain).OfType<Task>().Distinct())
        {
            // A completed assertion may expose a missing managed completion
            // signal. Only this direct no-runner/no-timer state fixture can
            // establish every actual invocation and its sole observer returned.
            // Real faults, deadlines and unfinished observers remain failures.
            if (primaryFailure is AssertFailedException && actualWorkJoined && !drain.IsCompleted) { continue; }
            try { await drain.WaitAsync(Deadline); }
            catch (Exception error) { failures.Add(drain.Exception ?? error); }
        }
        GC.KeepAlive(monitor);
        if (primaryFailure is not null && failures.Count > 0)
        {
            throw new AggregateException("The state assertion and actual owned-work cleanup both failed.", new[] { primaryFailure }.Concat(failures));
        }
        if (failures.Count > 0) { throw new AggregateException("Actual owned-work cleanup failed.", failures); }
        if (primaryFailure is not null) { ExceptionDispatchInfo.Capture(primaryFailure).Throw(); }

        static Task BeginDispose(IAsyncDisposable? owned)
        {
            try { return owned?.DisposeAsync().AsTask() ?? Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }
    }

    private sealed class ProjectionClock : TimeProvider
    {
        private int failNextUtc;
        private int utcFailures;

        internal int UtcFailures => Volatile.Read(ref utcFailures);
        internal void FailNextUtcRead() => Interlocked.Exchange(ref failNextUtc, 1);

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref failNextUtc, 0) != 0)
            {
                Interlocked.Increment(ref utcFailures);
                throw new DiagnosticClockException();
            }
            return Epoch;
        }
    }

    private sealed class CancellationObserverException : Exception;
    private sealed class DiagnosticClockException : Exception;
}
