using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionPurePushReleaseTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task FirstPurePushReleaseReturnsTheActualAlreadySettledStateBarrier(bool owned, bool availability)
    {
        var service = new HealthcheckService(new(),
            new HealthApplicationIdentity { Name = "pure push binding release", InstanceId = Guid.NewGuid() });
        IHealthMonitor? external = owned ? null : availability
            ? service.CreateAvailability(new() { IsCritical = false })
            : service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
        IHealthExecutionState state = ((IHealthStateAccess)service).ExecutionState;
        var options = new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid(), SuccessPublishesUsableData = true };
        BindingRequest request = owned ? availability
            ? BindingRequest.ForAvailability(new() { IsCritical = false }, options)
            : BindingRequest.ForConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 }, options)
            : BindingRequest.ForMonitor(external!.MonitorId, options);
        BindingTarget target = state.Bind(request);
        Task? released = null;
        try
        {
            Assert.IsTrue(state.IsActive(target));
            HealthSnapshot before = service.CaptureSnapshot();

            // No collector, reservation or owner work exists. The lifetime contract
            // requires pure-push settlement immediately (technical-intent.md).
            // Inspect the real state owner's actual barrier; this is not a promise
            // about a public async wrapper's continuation scheduling.
            released = state.ReleaseBinding(target);

            Assert.IsFalse(state.IsActive(target));
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(owned ? 0 : 1, after.MonitorCounts.Active);
            Assert.IsTrue(after.Revision > before.Revision);
            Assert.IsTrue(released.IsCompletedSuccessfully,
                "The state-owned actual barrier has no outstanding work after a pure-push release.");

            Task repeated = state.ReleaseBinding(target);
            Assert.AreSame(released, repeated);
            Assert.AreEqual(after.Revision, service.CaptureSnapshot().Revision);
            if (external is IAvailabilityMonitor usable) { usable.ReportPublished(); }
            if (external is IConsecutiveFailureMonitor failures) { failures.ReportSuccess(); }
            if (!owned) { Assert.AreEqual(1L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful); }
        }
        finally
        {
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            // If a deliberately removed dispatch lost the retired entry's signal,
            // awaiting that incorrect barrier would conceal the direct assertion.
            // All actual work is absent, and the real service has now been disposed.
            if (released is { IsCompleted: true }) { await released; }
            if (external is not null) { await external.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            GC.KeepAlive(external);
            GC.KeepAlive(target);
        }
    }
}
