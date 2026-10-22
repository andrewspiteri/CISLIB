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
        int callbackCalls = 0;
        try
        {
            monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId,
                new Guid("1126125a-b393-4a7f-bfa3-348f573c8de6"), new HealthCollectorOptions());
            HealthCollectionReservationResult reserved = state.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, reserved.Disposition);
            Assert.IsNotNull(reserved.Reservation);
            operation = reserved.Reservation;
            Task settlement = state.WaitForSettlementAsync(operation);
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.IsNull(before.Monitors.Single().Collection!.LastError);
            using CancellationTokenRegistration callback = operation.LifetimeCancellation.Register(() =>
            {
                Interlocked.Increment(ref callbackCalls);
                if (clockFails) { clock.FailNextUtcRead(); }
                throw new CancellationObserverException();
            });

            // No runner or attachment withdrawal precedes this public stop. The
            // actual state-issued attachment is still current when its observer faults.
            Exception? propagated = null;
            try { service.BeginStopping(); }
            catch (Exception exception) { propagated = exception; }

            Assert.IsNull(propagated, "Cancellation observation and diagnostic-clock failures must not escape stopping.");
            Assert.AreEqual(1, callbackCalls);
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
            Assert.AreEqual(HealthCollectionDisposition.Retired, completion.Disposition);
            Assert.IsFalse(completion.EvaluationFailed);
            Assert.IsTrue(settlement.IsCompletedSuccessfully);
            Assert.IsFalse(service.CaptureSnapshot().Monitors.Single().Collection!.InProgress);
        }
        finally
        {
            if (operation is not null)
            {
                state.Complete(operation, new(HealthCollectionCompletionReason.RetiredBeforeInvocation, Epoch));
            }
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
            GC.KeepAlive(monitor);
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
