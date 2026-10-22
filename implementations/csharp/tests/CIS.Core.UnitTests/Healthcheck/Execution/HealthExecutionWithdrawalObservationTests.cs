using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionWithdrawalObservationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false, "null")]
    [DataRow(false, "foreign-registration")]
    [DataRow(false, "invalid-execution")]
    [DataRow(true, "null")]
    [DataRow(true, "foreign-registration")]
    [DataRow(true, "invalid-execution")]
    public async Task MonitorWithdrawalIsObservedBeforeAnyLateFactValidation(bool availability, string late)
    {
        await using var scope = new ServiceScope();
        HealthcheckService service = scope.Service;
        IHealthMonitor monitor = Monitor(service, availability);
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, Options());
        try
        {
            monitor.Withdraw();
            HealthSnapshot withdrawn = service.CaptureSnapshot();

            HealthFactDisposition disposition = binding.Report(LateFact(binding.RegistrationId, late));

            Assert.AreEqual(HealthFactDisposition.Retired, disposition, "Withdrawal is terminal before an irrelevant late notification is validated.");
            Assert.AreEqual(withdrawn.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.IsTrue(binding.DisposeAsync().IsCompletedSuccessfully);
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
        }
        finally
        {
            await binding.DisposeAsync().AsTask().WaitAsync(Deadline);
            await monitor.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ServiceWithdrawalRetainsOnlyTheOwnedBindingActualSettlementBarrier(bool owned, bool availability)
    {
        await using var scope = new ServiceScope();
        HealthcheckService service = scope.Service;
        IHealthMonitor? external = owned ? null : Monitor(service, availability);
        IHealthExecutionBinding binding = owned ? OwnedBinding(service, availability) : HealthExecutionBinder.Bind(external!, Options());
        IHealthCollectionState collections = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionTarget collection = collections.RegisterCollection(binding.MonitorId, Guid.NewGuid(), new());
        HealthCollectionReservation reservation = collections.TryReserve(collection).Reservation!;
        Assert.IsTrue(collections.TryEnter(reservation));
        Task? serviceDisposal = null;
        try
        {
            serviceDisposal = service.DisposeAsync().AsTask();
            Assert.IsFalse(serviceDisposal.IsCompleted, "The real admitted operation still belongs to service shutdown.");
            HealthSnapshot withdrawn = service.CaptureSnapshot();
            Assert.AreEqual(0, withdrawn.MonitorCounts.Active);
            Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);

            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
            Task bindingDisposal = binding.DisposeAsync().AsTask();

            Assert.AreEqual(!owned, bindingDisposal.IsCompletedSuccessfully,
                "An owned binding shares monitor settlement; an external binding releases only its association.");
            Assert.AreEqual(withdrawn.Revision, service.CaptureSnapshot().Revision,
                "Observing an association already retired by service withdrawal must not commit another state transition.");
            Assert.IsFalse(serviceDisposal.IsCompleted);
        }
        finally
        {
            collections.Complete(reservation, new(HealthCollectionCompletionReason.Settled, DateTimeOffset.UtcNow));
            await collections.WaitForSettlementAsync(reservation).WaitAsync(Deadline);
            if (serviceDisposal is not null) { await serviceDisposal.WaitAsync(Deadline); }
            await binding.DisposeAsync().AsTask().WaitAsync(Deadline);
            if (external is not null) { await external.DisposeAsync().AsTask().WaitAsync(Deadline); }
            GC.KeepAlive(external);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task AtomicRetirementHandoffIsObservableBeforeAnyLaterBindingRelease(bool owned, bool availability)
    {
        await using var scope = new ServiceScope();
        HealthcheckService service = scope.Service;
        IHealthMonitor? external = owned ? null : Monitor(service, availability);
        IHealthExecutionState state = ((IHealthStateAccess)service).ExecutionState;
        HealthExecutionBindingOptions options = Options();
        BindingRequest request = owned ? availability
            ? BindingRequest.ForAvailability(new() { IsCritical = false }, options)
            : BindingRequest.ForConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 }, options)
            : BindingRequest.ForMonitor(external!.MonitorId, options);
        BindingTarget target = state.Bind(request);
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.IsTrue(state.IsActive(target));

            HealthFactDisposition disposition = state.UpdateLifecycle(target, new HealthExecutionFact
            {
                RegistrationId = target.RegistrationId,
                Kind = HealthExecutionFactKind.Retired,
                OccurredAtUtc = DateTimeOffset.UtcNow
            });

            Assert.AreEqual(HealthFactDisposition.Recorded, disposition);
            Assert.IsFalse(state.IsActive(target), "The atomic retirement handoff itself closes the association.");
            HealthSnapshot retired = service.CaptureSnapshot();
            Assert.IsTrue(retired.Revision > before.Revision);
            Assert.AreEqual(owned ? 0 : 1, retired.MonitorCounts.Active);
            Assert.AreEqual(HealthFactDisposition.Retired, state.UpdateLifecycle(target, new HealthExecutionFact
            {
                RegistrationId = target.RegistrationId,
                Kind = HealthExecutionFactKind.Registered,
                OccurredAtUtc = DateTimeOffset.UtcNow
            }));
            Assert.AreEqual(retired.Revision, service.CaptureSnapshot().Revision);
            if (external is IAvailabilityMonitor usable) { usable.ReportPublished(); }
            if (external is IConsecutiveFailureMonitor failures) { failures.ReportSuccess(); }
            if (!owned) { Assert.AreEqual(1L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful); }
        }
        finally
        {
            await state.ReleaseBinding(target).WaitAsync(Deadline);
            if (external is not null) { await external.DisposeAsync().AsTask().WaitAsync(Deadline); }
            GC.KeepAlive(external);
        }
    }

    private static HealthcheckService Service() => new(new(),
        new HealthApplicationIdentity { Name = "execution withdrawal observations", InstanceId = Guid.NewGuid() });

    private sealed class ServiceScope : IAsyncDisposable
    {
        internal HealthcheckService Service { get; } = HealthExecutionWithdrawalObservationTests.Service();

        public async ValueTask DisposeAsync()
        {
            // Do not add an unbounded compiler-generated service await after an
            // explicit cleanup guard fails. A lost terminal signal must remain
            // a bounded test failure; normal cleanup failures still propagate.
            await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    private static IHealthMonitor Monitor(HealthcheckService service, bool availability) => availability
        ? service.CreateAvailability(new() { IsCritical = false })
        : service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });

    private static HealthExecutionBindingOptions Options() => new() { RegistrationId = Guid.NewGuid(), SuccessPublishesUsableData = true };

    private static IHealthExecutionBinding OwnedBinding(HealthcheckService service, bool availability) => availability
        ? HealthExecutionBinder.Bind(service, new AvailabilityMonitorOptions { IsCritical = false }, Options())
        : HealthExecutionBinder.Bind(service, new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 1 }, Options());

    private static HealthExecutionFact LateFact(Guid registration, string late) => late == "null" ? null! : new()
    {
        RegistrationId = late == "foreign-registration" ? Guid.NewGuid() : registration,
        Kind = late == "foreign-registration" ? HealthExecutionFactKind.Registered : HealthExecutionFactKind.CallbackStarted,
        ExecutionId = late == "invalid-execution" ? -1 : null,
        OccurredAtUtc = DateTimeOffset.UtcNow
    };
}
