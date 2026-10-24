using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckRevisionQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConditionRegistrationRecordsItsActualInitialObservation(bool flagged)
    {
        var clock = new ManualTimeProvider(Epoch);
        await using HealthcheckService service = CreateService(clock);
        clock.Advance(TimeSpan.FromSeconds(7));
        DateTimeOffset registered = clock.GetUtcNow();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions
        {
            IsCritical = true,
            InitiallyFlagged = flagged,
            InitialDiagnostic = flagged ? AcceptedDiagnostic("InitialFlag") : null
        });
        clock.Advance(TimeSpan.FromSeconds(11));

        HealthSnapshot snapshot = service.CaptureSnapshot();
        HealthMonitorSnapshot observed = snapshot.Monitors.Single();
        Assert.AreEqual(registered, observed.RegisteredAtUtc);
        Assert.AreEqual(registered, observed.LastObservationAtUtc);
        Assert.AreEqual(clock.GetUtcNow(), snapshot.CapturedAtUtc);
        Assert.AreNotEqual(snapshot.CapturedAtUtc, observed.LastObservationAtUtc);
        Assert.AreEqual(flagged ? MonitorState.Unhealthy : MonitorState.Healthy, observed.State);
        Assert.AreEqual(flagged ? "InitialFlag" : null, observed.Diagnostic?.Code);
        Assert.IsNull(observed.Outcomes);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(HealthPolicyKind.ConsecutiveFailures)]
    [DataRow(HealthPolicyKind.NumericThreshold)]
    [DataRow(HealthPolicyKind.FirstSuccessAvailability)]
    public async Task PendingPoliciesHaveNoObservationUntilTheOwnerProvidesOne(HealthPolicyKind policy)
    {
        var clock = new ManualTimeProvider(Epoch);
        await using HealthcheckService service = CreateService(clock);
        IHealthMonitor monitor = Register(service, policy);
        clock.Advance(TimeSpan.FromSeconds(5));
        HealthSnapshot pending = service.CaptureSnapshot();
        HealthMonitorSnapshot unevaluated = pending.Monitors.Single();
        Assert.AreEqual(Epoch, unevaluated.RegisteredAtUtc);
        Assert.AreEqual(MonitorState.Pending, unevaluated.State);
        Assert.IsNull(unevaluated.LastObservationAtUtc);
        Assert.IsNull(unevaluated.Outcomes?.LastOutcome);
        Assert.IsNull(unevaluated.Numeric?.LastValue);

        DateTimeOffset accepted = clock.GetUtcNow();
        switch (monitor)
        {
            case IConsecutiveFailureMonitor failures: failures.ReportSuccess(); break;
            case INumericThresholdMonitor numeric: numeric.ReportValue(4); break;
            case IAvailabilityMonitor availability: availability.ReportPublished(); break;
            default: Assert.Fail("The selected public policy requires its own observation."); break;
        }
        HealthSnapshot evaluated = service.CaptureSnapshot();
        Assert.AreEqual(MonitorState.Healthy, evaluated.Monitors.Single().State);
        Assert.AreEqual(accepted, evaluated.Monitors.Single().LastObservationAtUtc);
        Assert.IsTrue(evaluated.Revision > pending.Revision);
        Assert.AreEqual(MonitorState.Pending, unevaluated.State);
        Assert.IsNull(unevaluated.LastObservationAtUtc);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(HealthPolicyKind.Condition)]
    [DataRow(HealthPolicyKind.ConsecutiveFailures)]
    [DataRow(HealthPolicyKind.NumericThreshold)]
    [DataRow(HealthPolicyKind.FirstSuccessAvailability)]
    public async Task EachRegistrationPublishesANewRevisionAndPreservesEarlierSnapshots(HealthPolicyKind policy)
    {
        var clock = new ManualTimeProvider(Epoch);
        await using HealthcheckService service = CreateService(clock);
        HealthSnapshot empty = service.CaptureSnapshot();
        IHealthMonitor first = Register(service, policy);
        HealthSnapshot one = service.CaptureSnapshot();
        IHealthMonitor second = Register(service, policy);
        HealthSnapshot two = service.CaptureSnapshot();

        Assert.IsTrue(one.Revision > empty.Revision);
        Assert.IsTrue(two.Revision > one.Revision);
        Assert.AreEqual(0, empty.Monitors.Count);
        Assert.AreEqual(1, one.Monitors.Count);
        Assert.AreEqual(2, two.Monitors.Count);
        Assert.AreEqual(first.MonitorId, one.Monitors.Single().MonitorId);
        Assert.AreNotEqual(first.MonitorId, second.MonitorId);
        Assert.AreEqual(two.Revision, service.CaptureSnapshot().Revision);
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [TestMethod]
    public async Task StartupAndStoppingAdvanceRevisionOnceWhileRepeatedTransitionsAreIdempotent()
    {
        var clock = new ManualTimeProvider(Epoch);
        await using HealthcheckService service = CreateService(clock);
        HealthSnapshot initial = service.CaptureSnapshot();
        service.MarkStartupCompleted();
        HealthSnapshot started = service.CaptureSnapshot();
        Assert.IsTrue(started.Revision > initial.Revision);
        Assert.IsTrue(started.Application.StartupCompleted);
        Assert.IsTrue(started.Ready);
        service.MarkStartupCompleted();
        Assert.AreEqual(started.Revision, service.CaptureSnapshot().Revision);

        service.BeginStopping();
        HealthSnapshot stopping = service.CaptureSnapshot();
        Assert.IsTrue(stopping.Revision > started.Revision);
        Assert.IsTrue(stopping.Application.IsStopping);
        Assert.IsFalse(stopping.Ready);
        Assert.AreEqual(HealthStatus.Healthy, stopping.HealthStatus);
        service.BeginStopping();
        Assert.AreEqual(stopping.Revision, service.CaptureSnapshot().Revision);
        Assert.IsFalse(initial.Application.StartupCompleted);
        Assert.IsFalse(initial.Application.IsStopping);
        Assert.IsFalse(started.Application.IsStopping);
        Assert.IsTrue(started.Ready);
    }

    [TestMethod]
    public async Task DistinctConditionObservationsPublishNewVersionsAndDetachEarlierReasonsAndTimes()
    {
        var clock = new ManualTimeProvider(Epoch);
        await using HealthcheckService service = CreateService(clock);
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        HealthSnapshot initial = service.CaptureSnapshot();
        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.Flag(AcceptedDiagnostic("First"));
        HealthSnapshot first = service.CaptureSnapshot();
        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.Flag(AcceptedDiagnostic("Second"));
        HealthSnapshot second = service.CaptureSnapshot();
        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.Clear();
        HealthSnapshot cleared = service.CaptureSnapshot();

        Assert.IsTrue(first.Revision > initial.Revision);
        Assert.IsTrue(second.Revision > first.Revision);
        Assert.IsTrue(cleared.Revision > second.Revision);
        Assert.AreEqual("First", first.Monitors.Single().Diagnostic!.Code);
        Assert.AreEqual(Epoch.AddSeconds(1), first.Monitors.Single().LastObservationAtUtc);
        Assert.AreEqual("Second", second.Monitors.Single().Diagnostic!.Code);
        Assert.AreEqual(Epoch.AddSeconds(2), second.Monitors.Single().LastObservationAtUtc);
        Assert.AreEqual(MonitorState.Unhealthy, first.Monitors.Single().State);
        Assert.AreEqual(MonitorState.Unhealthy, second.Monitors.Single().State);
        Assert.AreEqual(MonitorState.Healthy, cleared.Monitors.Single().State);
        Assert.IsNull(cleared.Monitors.Single().Diagnostic);
        Assert.AreEqual(Epoch.AddSeconds(3), cleared.Monitors.Single().LastObservationAtUtc);
        clock.Advance(TimeSpan.FromDays(1));
        HealthSnapshot readAgain = service.CaptureSnapshot();
        Assert.AreEqual(cleared.Revision, readAgain.Revision);
        Assert.AreEqual(cleared.Monitors.Single().LastObservationAtUtc, readAgain.Monitors.Single().LastObservationAtUtc);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task EveryAcceptedOutcomePublishesANewVersionEvenWhenItsPolicyStateIsUnchanged()
    {
        var clock = new ManualTimeProvider(Epoch);
        await using HealthcheckService service = CreateService(clock);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 });
        HealthSnapshot pending = service.CaptureSnapshot();
        monitor.ReportFailure();
        HealthSnapshot first = service.CaptureSnapshot();
        monitor.ReportFailure();
        HealthSnapshot second = service.CaptureSnapshot();
        monitor.ReportSuccess();
        HealthSnapshot recovered = service.CaptureSnapshot();

        Assert.IsTrue(first.Revision > pending.Revision);
        Assert.IsTrue(second.Revision > first.Revision);
        Assert.IsTrue(recovered.Revision > second.Revision);
        Assert.AreEqual(1L, first.Monitors.Single().Outcomes!.Failed);
        Assert.AreEqual(2L, second.Monitors.Single().Outcomes!.Failed);
        Assert.AreEqual(HealthStatus.Unhealthy, first.HealthStatus);
        Assert.AreEqual(HealthStatus.Unhealthy, second.HealthStatus);
        Assert.AreEqual(HealthStatus.Healthy, recovered.HealthStatus);
        Assert.AreEqual(2L, recovered.Monitors.Single().Outcomes!.Failed);
        Assert.AreEqual(1L, recovered.Monitors.Single().Outcomes!.Successful);
        GC.KeepAlive(monitor);
    }

    private static HealthcheckService CreateService(TimeProvider clock) => new(new HealthcheckOptions(),
        new HealthApplicationIdentity { Name = "healthcheck-revision-tests", InstanceId = new Guid("60000000-0000-0000-0000-000000000001") }, clock);

    private static IHealthMonitor Register(HealthcheckService service, HealthPolicyKind policy) => policy switch
    {
        HealthPolicyKind.Condition => service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false }),
        HealthPolicyKind.ConsecutiveFailures => service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 2 }),
        HealthPolicyKind.NumericThreshold => service.CreateNumericThreshold(new NumericThresholdMonitorOptions { IsCritical = false, Comparison = NumericComparison.GreaterThan, Threshold = 10, Unit = "items" }),
        HealthPolicyKind.FirstSuccessAvailability => service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = false }),
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
    private static HealthDiagnostic AcceptedDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

}
