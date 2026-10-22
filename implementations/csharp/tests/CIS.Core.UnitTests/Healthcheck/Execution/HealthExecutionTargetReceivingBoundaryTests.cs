using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionTargetReceivingBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Observed = new(2026, 10, 22, 7, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("registration")]
    [DataRow("monitor")]
    [DataRow("ownership")]
    [DataRow("reporting-handle")]
    public async Task AlteredReportingTargetsCannotObserveCommitOrReleaseTheIssuedAssociation(string changed)
    {
        await using var scope = new Scope();
        BindingTarget issued = scope.Target;
        var altered = new BindingTarget(
            changed == "registration" ? Guid.Empty : issued.RegistrationId,
            changed == "monitor" ? Guid.Empty : issued.MonitorId,
            changed == "ownership" ? HealthBindingOwnership.CreatedMonitor : issued.Ownership,
            changed == "reporting-handle" ? new UnissuedReportingHandle() : issued.ReportingHandle);
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        HealthExecutionFact outcome = Outcome(issued.RegistrationId);
        var registered = new HealthExecutionFact
        {
            RegistrationId = issued.RegistrationId,
            Kind = HealthExecutionFactKind.Registered,
            OccurredAtUtc = Observed
        };

        Assert.AreEqual("target", Assert.ThrowsExactly<ArgumentException>(() => scope.State.IsActive(altered)).ParamName);
        Assert.AreEqual("target", Assert.ThrowsExactly<ArgumentException>(() => scope.State.ApplyOutcome(altered, outcome)).ParamName);
        Assert.AreEqual("target", Assert.ThrowsExactly<ArgumentException>(() => scope.State.UpdateLifecycle(altered, registered)).ParamName);
        Assert.AreEqual("target", Assert.ThrowsExactly<ArgumentException>(() => scope.State.ReleaseBinding(altered)).ParamName);

        HealthSnapshot rejected = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, rejected.Revision);
        Assert.AreEqual(1, rejected.MonitorCounts.Active);
        HealthMonitorSnapshot monitor = rejected.Monitors.Single();
        Assert.AreEqual(issued.MonitorId, monitor.MonitorId);
        Assert.AreEqual(MonitorState.Pending, monitor.State);
        Assert.AreEqual(0L, monitor.Outcomes!.Successful);
        Assert.AreEqual(0L, monitor.Outcomes.Failed);
        Assert.IsNull(monitor.Outcomes.Execution);
        Assert.IsTrue(scope.State.IsActive(issued));

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.State.UpdateLifecycle(issued, registered));
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.State.ApplyOutcome(issued, outcome));
        HealthMonitorSnapshot accepted = scope.Service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(MonitorState.Healthy, accepted.State);
        Assert.AreEqual(1L, accepted.Outcomes!.Successful);
        Assert.AreEqual(issued.RegistrationId, accepted.Outcomes.Execution!.RegistrationId);
        Assert.AreEqual(1L, accepted.Outcomes.Execution.ExecutionId);
    }

    private static HealthExecutionFact Outcome(Guid registration) => new()
    {
        RegistrationId = registration,
        Kind = HealthExecutionFactKind.Outcome,
        ExecutionId = 1,
        SourcePhase = HealthExecutionPhase.Callback,
        Outcome = HealthExecutionOutcome.Succeeded,
        OccurredAtUtc = Observed,
        WorkCompletedAtUtc = Observed
    };

    private sealed class UnissuedReportingHandle : IHealthExecutionReportingHandle;

    private sealed class Scope : IAsyncDisposable
    {
        internal Scope()
        {
            Service = new HealthcheckService(new(),
                new HealthApplicationIdentity { Name = "execution target receiving", InstanceId = Guid.NewGuid() });
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            State = ((IHealthStateAccess)Service).ExecutionState;
            Target = State.Bind(BindingRequest.ForMonitor(Monitor.MonitorId,
                new() { RegistrationId = Guid.NewGuid() }));
        }

        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal IHealthExecutionState State { get; }
        internal BindingTarget Target { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Monitor.Withdraw();
                await State.ReleaseBinding(Target).WaitAsync(Deadline);
            }
            finally
            {
                await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
                GC.KeepAlive(Monitor);
            }
        }
    }
}
