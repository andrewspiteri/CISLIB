using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckCancellationResourceQualificationTests
{
    [TestMethod]
    public async Task CompletingAnEnteredCollectionDisposesItsOwnedCancellationWaitHandle()
    {
        await using var scope = new StateScope();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach());
        using WaitHandle wait = reservation.LifetimeCancellation.WaitHandle;
        SafeWaitHandle resource = wait.SafeWaitHandle;
        Assert.IsFalse(resource.IsClosed);
        Assert.IsFalse(reservation.LifetimeCancellation.IsCancellationRequested);
        Assert.IsTrue(scope.State.TryEnter(reservation));
        Assert.AreEqual(HealthCollectionDisposition.Committed,
            scope.State.CommitResult(reservation, HealthCollectionResult.Condition(true)).Disposition);

        HealthCollectionAttempt completion = scope.Complete(reservation);

        Assert.AreEqual(HealthCollectionDisposition.Committed, completion.Disposition);
        Assert.IsTrue(scope.State.WaitForSettlementAsync(reservation).IsCompletedSuccessfully);
        // Complete returns after state-owned cleanup; cancellation alone would leave this native resource open.
        Assert.IsTrue(resource.IsClosed);
        Assert.IsFalse(reservation.LifetimeCancellation.IsCancellationRequested);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Service.CaptureSnapshot().Monitors.Single().State);
    }

    [TestMethod]
    public async Task RetiringAnIdleAttachmentDisposesItsOwnedCancellationWaitHandle()
    {
        await using var scope = new StateScope();
        HealthCollectionTarget target = scope.Attach();
        using WaitHandle wait = target.Retirement.WaitHandle;
        SafeWaitHandle resource = wait.SafeWaitHandle;
        Assert.IsFalse(resource.IsClosed);
        Assert.IsFalse(target.Retirement.IsCancellationRequested);

        Task retirement = scope.State.WithdrawCollection(target);

        Assert.IsTrue(retirement.IsCompletedSuccessfully);
        Assert.IsTrue(target.Retirement.IsCancellationRequested);
        // No wrapper exists here: the attachment owns this distinct resource and must release it itself.
        Assert.IsTrue(resource.IsClosed);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(target).Disposition);
        Assert.AreEqual(1, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        scope.Monitor.Flag();
        Assert.AreEqual(MonitorState.Unhealthy, scope.Service.CaptureSnapshot().Monitors.Single().State);
    }

    private sealed class StateScope : IAsyncDisposable
    {
        private readonly List<HealthCollectionReservation> reservations = [];
        private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 21, 16, 0, 0, TimeSpan.Zero));

        internal StateScope()
        {
            Service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity
            {
                Name = "Cancellation resource qualification",
                InstanceId = Guid.Parse("3500f9f8-9064-42cc-bcb4-57b8e53cf162")
            }, clock);
            State = ((IHealthStateAccess)Service).CollectionState;
            Monitor = Service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        }

        internal HealthcheckService Service { get; }
        internal IHealthCollectionState State { get; }
        internal IConditionMonitor Monitor { get; }

        internal HealthCollectionTarget Attach() => State.RegisterCollection(Monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());

        internal HealthCollectionReservation Reserve(HealthCollectionTarget target)
        {
            HealthCollectionReservationResult result = State.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, result.Disposition);
            Assert.IsNotNull(result.Reservation);
            reservations.Add(result.Reservation);
            return result.Reservation;
        }

        internal HealthCollectionAttempt Complete(HealthCollectionReservation reservation) => State.Complete(reservation,
            new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, clock.GetUtcNow()));

        public async ValueTask DisposeAsync()
        {
            // Even a failed assertion must release its real state reservation before service disposal.
            foreach (HealthCollectionReservation reservation in reservations) { Complete(reservation); }
            await Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
