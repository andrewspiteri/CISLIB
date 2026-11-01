using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorReservationJoinCoverageTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    [TestMethod]
    public async Task AnExistingStateReservationIsJoinedWithoutAcquiringOrCompletingItsOwnerWork()
    {
        var service = new HealthcheckService(new(), new() { Name = "existing collection reservation", InstanceId = Guid.NewGuid() });
        var runner = new HealthCollectorRunner(service);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new UnavailableOwner();
        IHealthCollectionRegistration? registration = null;
        HealthCollectionReservation? reserved = null;
        Task<HealthCollectionAttempt>? originalCaller = null;
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => runner.Register(monitor, owner, new()));
            registration = actualRegistration;
            var selection = new SelectionBoundary(runner, state);
            Task start = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StartAsync());
            await start.WaitAsync(Deadline);
            selection.InStateGate(() =>
            {
                originalCaller = HealthCollectorTestCleanup.EnterAsync(Deadline,
                    () => actualRegistration.CollectAsync()).GetAwaiter().GetResult();
                selection.WaitForSelection();
                HealthCollectionReservationResult external = state.TryReserve(selection.Target);
                Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, external.Disposition);
                reserved = external.Reservation;
                Assert.IsNotNull(reserved);
                Assert.IsTrue(state.TryEnter(reserved));
            });

            Assert.IsNotNull(reserved);
            Assert.IsNotNull(originalCaller);
            selection.WaitForReservation(reserved);
            Task actualSettlement = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => state.WaitForSettlementAsync(reserved));
            Assert.IsFalse(actualSettlement.IsCompleted);

            // Cancel a real caller wait while the independently owned state work
            // remains held. Early completion cannot masquerade as cancellation.
            using var cancellation = new CancellationTokenSource();
            Task<HealthCollectionAttempt> canceledCaller = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => actualRegistration.CollectAsync(cancellation.Token));
            // Both real waits are issued before cancellation; arranging this
            // boundary does not require a thread-pool timer to run first.
            cancellation.Cancel();
            OperationCanceledException canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => canceledCaller).WaitAsync(Deadline);
            Assert.AreEqual(cancellation.Token, canceled.CancellationToken);
            Assert.IsFalse(actualSettlement.IsCompleted, "Canceling one joined caller must preserve the actual state reservation.");
            Assert.IsFalse(originalCaller.IsCompleted, "The original caller still owns the existing reservation's settlement wait.");
            Assert.AreEqual(0, owner.Acquisitions);

            HealthCollectionAttempt externalCompletion = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => state.Complete(reserved, new(HealthCollectionCompletionReason.Settled, DateTimeOffset.UtcNow)));
            Assert.AreEqual(HealthCollectionDisposition.Committed, externalCompletion.Disposition,
                "The joining runner must not prematurely complete another state owner's reservation.");
            await actualSettlement.WaitAsync(Deadline);
            HealthCollectionAttempt joined = await originalCaller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, joined.Disposition);
            Assert.IsFalse(joined.EvaluationFailed);
            Assert.IsNull(joined.Diagnostic);
            HealthMonitorSnapshot snapshot = (await Task.Run(service.CaptureSnapshot).WaitAsync(Deadline)).Monitors.Single();
            Assert.IsFalse(snapshot.Collection!.InProgress);
            Assert.AreEqual(0L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            // Release the exact state operation even when the runner assertion
            // fails, then request all real drains before awaiting their union.
            await DrainAsync(runner, service, registration, originalCaller, primary, state, reserved);
            GC.KeepAlive(monitor);
        }
        Assert.AreEqual(0, owner.Acquisitions, "Joining must not invoke the owner, including dispatcher work completing during shutdown.");
    }

    [TestMethod]
    public async Task RetirementBetweenRunnerSelectionAndStateReservationCompletesWithoutAcquiringTheOwner()
    {
        var service = new HealthcheckService(new(), new() { Name = "retired collection reservation", InstanceId = Guid.NewGuid() });
        var runner = new HealthCollectorRunner(service);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new UnavailableOwner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Task? stateRetirement = null;
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => runner.Register(monitor, owner, new()));
            registration = actualRegistration;
            var selection = new SelectionBoundary(runner, state);
            Task start = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StartAsync());
            await start.WaitAsync(Deadline);
            selection.InStateGate(() =>
            {
                caller = HealthCollectorTestCleanup.EnterAsync(Deadline,
                    () => actualRegistration.CollectAsync()).GetAwaiter().GetResult();
                selection.WaitForSelection();
                stateRetirement = selection.WithRunnerGate(() => state.WithdrawCollection(selection.Target));
            });

            Assert.IsNotNull(caller);
            Assert.IsNotNull(stateRetirement);
            HealthCollectionAttempt retired = await caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition);
            Assert.IsFalse(retired.EvaluationFailed);
            Assert.IsNull(retired.Diagnostic);
            await stateRetirement.WaitAsync(Deadline);
            Task registrationDrain = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => actualRegistration.WaitForDisposalAsync());
            await registrationDrain.WaitAsync(Deadline);
            await Task.Run(monitor.ReportSuccess).WaitAsync(Deadline);
            HealthMonitorSnapshot snapshot = (await Task.Run(service.CaptureSnapshot).WaitAsync(Deadline)).Monitors.Single();
            Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Outcomes.Failed);
            Assert.IsFalse(snapshot.Collection!.InProgress);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await DrainAsync(runner, service, registration, caller, primary, state, stateRetirement: stateRetirement);
            GC.KeepAlive(monitor);
        }
        Assert.AreEqual(0, owner.Acquisitions, "A retired state reservation cannot admit owner work before the dispatcher actually stops.");
    }

    [TestMethod]
    public async Task RetirementDuringStateAdmissionSettlesTheReturnedRegistrationWithoutClosingRunnerAdmission()
    {
        var service = new HealthcheckService(new(), new() { Name = "inline registration retirement", InstanceId = Guid.NewGuid() });
        var runner = new HealthCollectorRunner(service);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor destination = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new UnavailableOwner();
        AbandonedAttachment abandoned = CreateAbandonedAttachment(service, state);
        int observations = 0;
        CancellationTokenRegistration observer = abandoned.Target.Retirement.Register(() =>
        {
            Interlocked.Increment(ref observations);
            // The state has attached the destination before dispatching this
            // real old attachment's retirement, but Register has not returned.
            destination.Withdraw();
        });
        IHealthCollectionRegistration? registration = null;
        Exception? primary = null;
        try
        {
            for (int attempt = 0; attempt < 10 && abandoned.Handle.IsAlive; attempt++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            Assert.IsFalse(abandoned.Handle.IsAlive, "The old monitor must actually be abandoned before its retirement can arrange admission.");
            Assert.IsFalse(abandoned.Target.Retirement.IsCancellationRequested);

            IHealthCollectionRegistration actualRegistration = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => runner.Register(destination, owner, new()));
            registration = actualRegistration;

            Assert.AreEqual(1, observations);
            await abandoned.Disposal.WaitAsync(Deadline);
            Task destinationDrain = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => destination.WaitForDisposalAsync());
            Task registrationDrain = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => actualRegistration.WaitForDisposalAsync());
            await Task.WhenAll(destinationDrain, registrationDrain).WaitAsync(Deadline);
            Task<HealthCollectionAttempt> caller = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => actualRegistration.CollectAsync());
            HealthCollectionAttempt retired = await caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition);
            Assert.IsFalse(retired.EvaluationFailed);
            Assert.IsNull(retired.Diagnostic);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => Task.Run(actualRegistration.ActivatePeriodic)).WaitAsync(Deadline);
            Assert.AreEqual(0, owner.Acquisitions);
            Assert.AreEqual(0, (await Task.Run(service.CaptureSnapshot).WaitAsync(Deadline)).MonitorCounts.Active);

            // Local attachment retirement did not stop the independent runner.
            Task start = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StartAsync());
            await start.WaitAsync(Deadline);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await DrainAsync(runner, service, registration, null, primary, state, stateRetirement: abandoned.Disposal,
                additionalCleanup: () => { observer.Dispose(); return Task.CompletedTask; });
            GC.KeepAlive(destination);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AbandonedAttachment CreateAbandonedAttachment(HealthcheckService service, IHealthCollectionState state)
    {
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
        HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
        return new(new WeakReference(monitor), target, monitor.WaitForDisposalAsync());
    }

    private sealed record AbandonedAttachment(WeakReference Handle, HealthCollectionTarget Target, Task Disposal);

    private static Task DrainAsync(HealthCollectorRunner runner, HealthcheckService service,
        IHealthCollectionRegistration? registration, Task? caller, Exception? primary, IHealthCollectionState state,
        HealthCollectionReservation? reservation = null, Task? stateRetirement = null, Func<Task>? additionalCleanup = null)
    {
        return HealthCollectorTestCleanup.DrainAsync(Deadline, primary, caller is null ? [] : [caller],
            () =>
            {
                if (reservation is null) { return Task.CompletedTask; }
                state.Complete(reservation, new(HealthCollectionCompletionReason.Settled, DateTimeOffset.UtcNow));
                return state.WaitForSettlementAsync(reservation);
            },
            () => caller ?? Task.CompletedTask,
            () => stateRetirement ?? Task.CompletedTask,
            () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
            () => runner.DisposeAsync().AsTask(),
            () => service.DisposeAsync().AsTask(),
            () => additionalCleanup?.Invoke() ?? Task.CompletedTask);
    }

    // Read gates and the issued target only to arrange the real concurrent
    // boundary. The fixture never changes fields or creates a forged reservation.
    private sealed class SelectionBoundary
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        private readonly object runnerGate;
        private readonly object entry;
        private object? operation;

        internal SelectionBoundary(HealthCollectorRunner runner, IHealthCollectionState state)
        {
            StateGate = ReadField(state, "gate");
            runnerGate = ReadField(runner, "gate");
            bool entered = Monitor.TryEnter(runnerGate, Deadline);
            Assert.IsTrue(entered, "The registration fixture must enter the runner gate before arranging collection.");
            try
            {
                var entries = (IList)ReadField(runner, "entries");
                Assert.AreEqual(1, entries.Count);
                entry = entries[0]!;
                Target = (HealthCollectionTarget)ReadField(entry, "Target");
            }
            finally { Monitor.Exit(runnerGate); }
        }

        internal object StateGate { get; }
        internal HealthCollectionTarget Target { get; }

        internal void InStateGate(Action action)
        {
            bool entered = Monitor.TryEnter(StateGate, Deadline);
            Assert.IsTrue(entered, "The collection fixture must enter the real state gate before arranging reservation.");
            try { action(); }
            finally { Monitor.Exit(StateGate); }
        }

        internal T WithRunnerGate<T>(Func<T> action)
        {
            bool entered = Monitor.TryEnter(runnerGate, Deadline);
            Assert.IsTrue(entered, "The selected runner must release its gate before state retirement callbacks re-enter it.");
            try { return action(); }
            finally { Monitor.Exit(runnerGate); }
        }

        internal void WaitForSelection()
        {
            bool selected = SpinWait.SpinUntil(() =>
            {
                if (!Monitor.TryEnter(runnerGate)) { return false; }
                try { return (operation = ReadNullableField(entry, "Current")) is not null; }
                finally { Monitor.Exit(runnerGate); }
            }, Deadline);
            Assert.IsTrue(selected, "The actual dispatcher must select the requested entry before the state-reservation interleaving.");
        }

        internal void WaitForReservation(HealthCollectionReservation expected)
        {
            Assert.IsNotNull(operation);
            bool received = SpinWait.SpinUntil(() =>
                ReferenceEquals(ReadNullableField(operation, "Reservation"), expected), Deadline);
            Assert.IsTrue(received, "The selected dispatcher must receive the actual existing state-issued reservation.");
        }

        private static object ReadField(object owner, string name)
        {
            object? value = ReadNullableField(owner, name);
            Assert.IsNotNull(value, "The collection interleaving fixture requires the existing field " + name + ".");
            return value;
        }

        private static object? ReadNullableField(object owner, string name)
        {
            FieldInfo? field = owner.GetType().GetField(name, Fields);
            Assert.IsNotNull(field, "The collection interleaving fixture requires the existing field " + name + ".");
            return field.GetValue(owner);
        }
    }

    private sealed class UnavailableOwner : IHealthCollector
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
}
