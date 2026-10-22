using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionIdleWithdrawalReceivingTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task WithdrawingAnIdleCollectionRejectsNewReservationsAndPreservesItsExternalMonitor()
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "idle collection withdrawal", InstanceId = Guid.NewGuid() },
            new ManualTimeProvider(Epoch));
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionReservation? unexpectedReservation = null;
        try
        {
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
            Assert.IsFalse(target.Retirement.IsCancellationRequested);
            Task withdrawal = state.WithdrawCollection(target);

            // Observe admission before awaiting the returned barrier. A forgotten
            // close must fail this assertion rather than strand an awaited drain.
            HealthCollectionReservationResult denied = state.TryReserve(target);
            unexpectedReservation = denied.Reservation;
            Assert.AreEqual(HealthCollectionReservationDisposition.Retired, denied.Disposition,
                "Withdrawal must close admission even when there was no active operation to cancel.");
            Assert.IsNull(denied.Reservation);
            Assert.IsTrue(target.Retirement.IsCancellationRequested);
            await withdrawal.WaitAsync(Deadline);

            monitor.ReportSuccess();
            HealthMonitorSnapshot retained = service.CaptureSnapshot().Monitors.Single();
            Assert.AreEqual(monitor.MonitorId, retained.MonitorId);
            Assert.AreEqual(MonitorState.Healthy, retained.State);
            Assert.AreEqual(1L, retained.Outcomes!.Successful);
        }
        finally
        {
            // Monitor withdrawal closes through the separate monitor-lifetime
            // receiving path. It also recovers a reservation admitted by a broken
            // collection-only close, so the real service can still be drained.
            monitor.Withdraw();
            if (unexpectedReservation is not null)
            {
                state.Complete(unexpectedReservation,
                    new(HealthCollectionCompletionReason.RetiredBeforeInvocation, Epoch));
                await state.WaitForSettlementAsync(unexpectedReservation).WaitAsync(Deadline);
            }
            await monitor.DisposeAsync().AsTask().WaitAsync(Deadline);
            await service.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }
}
