using System.Numerics;
using System.Runtime.CompilerServices;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckServicePolicyTests
{
    private static readonly DateTimeOffset InitialTime = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid InstanceId = Guid.Parse("849b3ad9-2097-4d89-a8cc-1822549e8976");

    [TestMethod]
    public async Task ConstructionPublishesAnEmptyHealthyApplicationBeforeStartup()
    {
        var clock = new ManualTimeProvider(InitialTime);
        await using HealthcheckService service = CreateService(clock);
        HealthSnapshot snapshot = service.CaptureSnapshot();

        Assert.AreEqual(1, snapshot.SchemaVersion);
        Assert.IsTrue(snapshot.Revision >= BigInteger.Zero);
        Assert.AreEqual(InitialTime, snapshot.CapturedAtUtc);
        Assert.AreEqual("healthcheck-tests", snapshot.Application.Name);
        Assert.AreEqual(InstanceId, snapshot.Application.InstanceId);
        Assert.IsFalse(snapshot.Application.StartupCompleted);
        Assert.IsFalse(snapshot.Application.IsStopping);
        Assert.AreEqual(HealthStatus.Healthy, snapshot.HealthStatus);
        Assert.IsFalse(snapshot.Ready);
        Assert.AreEqual(0, snapshot.Monitors.Count);
        AssertCounts(snapshot, active: 0, critical: 0, failing: 0, criticalFailing: 0, pending: 0);
    }

    [TestMethod]
    public async Task OmittedIdentityUsesTheExistingEnvironmentFacts()
    {
        await using var service = new HealthcheckService(new HealthcheckOptions());
        HealthApplicationSnapshot application = service.CaptureSnapshot().Application;

        Assert.AreEqual(EnvironmentOptions.ApplicationName, application.Name);
        Assert.AreEqual(EnvironmentOptions.BuildVersion, application.BuildVersion);
        Assert.AreEqual(EnvironmentOptions.ServiceVersion, application.ServiceVersion);
        Assert.AreEqual(Guid.Parse(EnvironmentOptions.InstanceId), application.InstanceId);
        Assert.AreEqual(EnvironmentOptions.HostName, application.HostName);
        Assert.AreEqual(EnvironmentOptions.OperatingSystemDescription, application.OsDescription);
    }

    [TestMethod]
    public async Task ExplicitIdentityPreservesAllIndependentMetadata()
    {
        var identity = new HealthApplicationIdentity
        {
            Name = "embedded-host",
            InstanceId = InstanceId,
            BuildVersion = "1.2.3+build",
            ServiceVersion = "deployment-blue",
            HostName = "host-1",
            OsDescription = "controlled-runtime"
        };
        await using var service = new HealthcheckService(new HealthcheckOptions(), identity);
        HealthApplicationSnapshot application = service.CaptureSnapshot().Application;

        Assert.AreEqual(identity.Name, application.Name);
        Assert.AreEqual(identity.InstanceId, application.InstanceId);
        Assert.AreEqual(identity.BuildVersion, application.BuildVersion);
        Assert.AreEqual(identity.ServiceVersion, application.ServiceVersion);
        Assert.AreEqual(identity.HostName, application.HostName);
        Assert.AreEqual(identity.OsDescription, application.OsDescription);
        Assert.IsFalse(application.StartupCompleted);
        Assert.IsFalse(application.IsStopping);
    }

    [TestMethod]
    public async Task ServiceCopiesCallerOwnedIdentityAndOptionsWithoutRetainingTheInputs()
    {
        (HealthcheckService service, WeakReference identity, WeakReference options) = CreateServiceWithWeakInputs();
        await using (service)
        {
            CollectUntilDead(identity, options);
            Assert.IsFalse(identity.IsAlive, "Application identity input must be copied into service-owned values.");
            Assert.IsFalse(options.IsAlive, "Configuration input must be copied into service-owned values.");
            Assert.AreEqual("copied-identity", service.CaptureSnapshot().Application.Name);
            IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
            Assert.ThrowsExactly<HealthcheckCapacityException>(() =>
                service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false }));
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public void NullOptionsAndInvalidApplicationIdentityAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new HealthcheckService(null!, Identity()));
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "valid", InstanceId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = " ", InstanceId = InstanceId }));
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = null!, InstanceId = InstanceId }));
        Assert.ThrowsExactly<CoreConfigurationException>(() => new HealthcheckService(
            new HealthcheckOptions { MaxMonitors = 0 }, Identity()));
    }

    [TestMethod]
    [DataRow("Name", 255)]
    [DataRow("BuildVersion", 512)]
    [DataRow("ServiceVersion", 512)]
    [DataRow("HostName", 255)]
    [DataRow("OsDescription", 1024)]
    public async Task ApplicationIdentityBoundsCountUnicodeScalars(string field, int maximum)
    {
        string atLimit = string.Concat(Enumerable.Repeat("\U0001F30D", maximum));
        await using var service = new HealthcheckService(new HealthcheckOptions(), IdentityWith(field, atLimit));
        Assert.AreEqual(atLimit, IdentityValue(service.CaptureSnapshot().Application, field));
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(), IdentityWith(field, atLimit + "x")));
        Assert.Throws<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(), IdentityWith(field, "\ud800")));
    }

    [TestMethod]
    public async Task IdenticalOptionsProduceIndependentMonitorsAndUniqueIdentities()
    {
        await using HealthcheckService service = CreateService();
        var options = new ConditionMonitorOptions { Name = "same-name", IsCritical = true, InitiallyFlagged = false };
        IConditionMonitor first = service.CreateCondition(options);
        IConditionMonitor second = service.CreateCondition(options);

        Assert.AreNotEqual(Guid.Empty, first.MonitorId);
        Assert.AreNotEqual(first.MonitorId, second.MonitorId);
        first.Flag(AcceptedDiagnostic("FirstOnly"));
        HealthSnapshot snapshot = service.CaptureSnapshot();
        Assert.AreEqual(MonitorState.Unhealthy, Find(snapshot, first).State);
        Assert.AreEqual(MonitorState.Healthy, Find(snapshot, second).State);
        Assert.AreEqual("FirstOnly", Find(snapshot, first).Diagnostic!.Code);
        Assert.IsNull(Find(snapshot, second).Diagnostic);
        Assert.IsNull(Find(snapshot, first).Outcomes);
        AssertCounts(snapshot, active: 2, critical: 2, failing: 1, criticalFailing: 1, pending: 0);
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [TestMethod]
    public async Task ExplicitlyClearInitialConditionDoesNotPublishAnInapplicableReason()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor condition = service.CreateCondition(new ConditionMonitorOptions
        {
            IsCritical = true,
            InitiallyFlagged = false,
            InitialDiagnostic = AcceptedDiagnostic("UnusedInitialReason")
        });
        HealthMonitorSnapshot initial = Find(service.CaptureSnapshot(), condition);
        Assert.AreEqual(MonitorState.Healthy, initial.State);
        Assert.IsNull(initial.Diagnostic);
        Assert.IsNull(initial.Outcomes);
    }

    [TestMethod]
    public async Task CriticalFailureAloneControlsTheAggregateAndReadiness()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor critical = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        IConditionMonitor diagnostic = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = true });
        service.MarkStartupCompleted();

        HealthSnapshot noncriticalFailure = service.CaptureSnapshot();
        Assert.AreEqual(HealthStatus.Healthy, noncriticalFailure.HealthStatus);
        Assert.IsTrue(noncriticalFailure.Ready);
        Assert.AreEqual(MonitorState.Unhealthy, Find(noncriticalFailure, diagnostic).State);
        AssertCounts(noncriticalFailure, active: 2, critical: 1, failing: 1, criticalFailing: 0, pending: 0);

        critical.Flag();
        Assert.AreEqual(HealthStatus.Unhealthy, service.CaptureSnapshot().HealthStatus);
        Assert.IsFalse(service.CaptureSnapshot().Ready);
        critical.Clear();
        Assert.IsTrue(service.CaptureSnapshot().Ready);
        Assert.AreEqual(MonitorState.Unhealthy, Find(service.CaptureSnapshot(), diagnostic).State);
        GC.KeepAlive(critical);
        GC.KeepAlive(diagnostic);
    }

    [TestMethod]
    public async Task PendingPoliciesDoNotInventFailuresOrPreventReadinessAfterStartup()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor failures = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 3 });
        INumericThresholdMonitor numeric = service.CreateNumericThreshold(NumericOptions());
        IAvailabilityMonitor available = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });

        HealthSnapshot beforeStartup = service.CaptureSnapshot();
        Assert.IsTrue(beforeStartup.Monitors.All(monitor => monitor.State == MonitorState.Pending));
        Assert.AreEqual(HealthStatus.Healthy, beforeStartup.HealthStatus);
        Assert.IsFalse(beforeStartup.Ready);
        AssertCounts(beforeStartup, active: 3, critical: 3, failing: 0, criticalFailing: 0, pending: 3);
        Assert.IsNull(Find(beforeStartup, numeric).Numeric!.LastValue);
        Assert.IsNull(Find(beforeStartup, numeric).Numeric!.LastValueAtUtc);

        failures.ReportSkipped();
        failures.ReportExpectedCancellation();
        Assert.AreEqual(MonitorState.Pending, Find(service.CaptureSnapshot(), failures).State);
        service.MarkStartupCompleted();
        Assert.IsTrue(service.CaptureSnapshot().Ready);
        GC.KeepAlive(failures);
        GC.KeepAlive(numeric);
        GC.KeepAlive(available);
    }

    [TestMethod]
    public async Task StartupCompletionIsIdempotentAndDoesNotClearAStartupFailure()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor startupFailure = service.CreateCondition(new ConditionMonitorOptions
        {
            IsCritical = true,
            InitiallyFlagged = true,
            InitialDiagnostic = AcceptedDiagnostic("PrimingFailed")
        });
        service.MarkStartupCompleted();
        HealthSnapshot first = service.CaptureSnapshot();
        service.MarkStartupCompleted();
        HealthSnapshot repeated = service.CaptureSnapshot();

        Assert.IsTrue(repeated.Application.StartupCompleted);
        Assert.IsFalse(repeated.Ready);
        Assert.AreEqual(HealthStatus.Unhealthy, repeated.HealthStatus);
        Assert.AreEqual("PrimingFailed", Find(repeated, startupFailure).Diagnostic!.Code);
        Assert.AreEqual(first.Revision, repeated.Revision);
        GC.KeepAlive(startupFailure);
    }

    [TestMethod]
    public async Task ConsecutiveFailuresBreachAtTheThresholdAndSuccessResetsOnlyTheStreak()
    {
        var clock = new ManualTimeProvider(InitialTime);
        await using HealthcheckService service = CreateService(clock);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 3 });
        var failure = AcceptedDiagnostic("RefreshFailed");

        for (int count = 1; count <= 3; count++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            monitor.ReportFailure(failure);
            HealthMonitorSnapshot observed = Find(service.CaptureSnapshot(), monitor);
            Assert.AreEqual((long)count, observed.Outcomes!.Failed);
            Assert.AreEqual((long)count, observed.Outcomes.ConsecutiveFailures);
            Assert.AreEqual(count == 3 ? MonitorState.Unhealthy : MonitorState.Healthy, observed.State);
            Assert.AreEqual(clock.GetUtcNow(), observed.LastObservationAtUtc);
            Assert.AreEqual(clock.GetUtcNow(), observed.Outcomes.LastFailureAtUtc);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        monitor.ReportSuccess();
        HealthMonitorSnapshot recovered = Find(service.CaptureSnapshot(), monitor);
        Assert.AreEqual(MonitorState.Healthy, recovered.State);
        Assert.IsNull(recovered.Diagnostic);
        Assert.AreEqual(3L, recovered.Outcomes!.Failed);
        Assert.AreEqual(1L, recovered.Outcomes.Successful);
        Assert.AreEqual(0L, recovered.Outcomes.ConsecutiveFailures);
        Assert.AreEqual(HealthExecutionOutcome.Succeeded, recovered.Outcomes.LastOutcome);
        Assert.AreEqual(clock.GetUtcNow(), recovered.Outcomes.LastSuccessAtUtc);
        Assert.AreEqual("RefreshFailed", recovered.Outcomes.LastFailureDiagnostic!.Code);
        monitor.ReportFailure();
        Assert.AreEqual(1L, Find(service.CaptureSnapshot(), monitor).Outcomes!.ConsecutiveFailures);
        Assert.AreEqual(4L, Find(service.CaptureSnapshot(), monitor).Outcomes!.Failed);
        Assert.IsFalse(recovered.Outcomes.CountersSaturated);
    }

    [TestMethod]
    public async Task SkipAndExpectedCancellationRemainNeutralToAnExistingFailureStreak()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 2 });
        monitor.ReportFailure(AcceptedDiagnostic("OriginalFailure"));
        monitor.ReportSkipped();
        monitor.ReportExpectedCancellation();
        HealthMonitorSnapshot neutral = Find(service.CaptureSnapshot(), monitor);

        Assert.AreEqual(MonitorState.Healthy, neutral.State);
        Assert.AreEqual(1L, neutral.Outcomes!.Failed);
        Assert.AreEqual(0L, neutral.Outcomes.Successful);
        Assert.AreEqual(1L, neutral.Outcomes.Skipped);
        Assert.AreEqual(1L, neutral.Outcomes.ExpectedCanceled);
        Assert.AreEqual(1L, neutral.Outcomes.ConsecutiveFailures);
        Assert.AreEqual(HealthExecutionOutcome.ExpectedCancellation, neutral.Outcomes.LastOutcome);
        monitor.ReportFailure();
        HealthMonitorSnapshot breached = Find(service.CaptureSnapshot(), monitor);
        Assert.AreEqual(2L, breached.Outcomes!.ConsecutiveFailures);
        Assert.AreEqual(MonitorState.Unhealthy, breached.State);
        monitor.ReportSkipped();
        monitor.ReportExpectedCancellation();
        Assert.AreEqual(MonitorState.Unhealthy, Find(service.CaptureSnapshot(), monitor).State);
    }

    [TestMethod]
    [DataRow(NumericComparison.GreaterThan, 9d, false)]
    [DataRow(NumericComparison.GreaterThan, 10d, false)]
    [DataRow(NumericComparison.GreaterThan, 11d, true)]
    [DataRow(NumericComparison.GreaterThanOrEqual, 9d, false)]
    [DataRow(NumericComparison.GreaterThanOrEqual, 10d, true)]
    [DataRow(NumericComparison.GreaterThanOrEqual, 11d, true)]
    [DataRow(NumericComparison.LessThan, 9d, true)]
    [DataRow(NumericComparison.LessThan, 10d, false)]
    [DataRow(NumericComparison.LessThan, 11d, false)]
    [DataRow(NumericComparison.LessThanOrEqual, 9d, true)]
    [DataRow(NumericComparison.LessThanOrEqual, 10d, true)]
    [DataRow(NumericComparison.LessThanOrEqual, 11d, false)]
    public async Task NumericComparisonsUseTheirExactSelectedBoundary(NumericComparison comparison, double value, bool breached)
    {
        var clock = new ManualTimeProvider(InitialTime);
        await using HealthcheckService service = CreateService(clock);
        INumericThresholdMonitor monitor = service.CreateNumericThreshold(NumericOptions(comparison));
        clock.Advance(TimeSpan.FromSeconds(2));
        monitor.ReportValue(value, AcceptedDiagnostic("Observation"));
        HealthMonitorSnapshot observed = Find(service.CaptureSnapshot(), monitor);

        Assert.AreEqual(breached ? MonitorState.Unhealthy : MonitorState.Healthy, observed.State);
        Assert.AreEqual(breached ? HealthStatus.Unhealthy : HealthStatus.Healthy, service.CaptureSnapshot().HealthStatus);
        Assert.AreEqual(comparison, observed.Numeric!.Comparison);
        Assert.AreEqual(10d, observed.Numeric.Threshold);
        Assert.AreEqual("ms", observed.Numeric.Unit);
        Assert.AreEqual(value, observed.Numeric.LastValue);
        Assert.AreEqual(clock.GetUtcNow(), observed.Numeric.LastValueAtUtc);
        Assert.AreEqual(clock.GetUtcNow(), observed.LastObservationAtUtc);
        Assert.AreEqual(0, observed.Measurements.Count);
        Assert.IsNull(observed.Outcomes);
    }

    [TestMethod]
    public async Task InvalidDirectNumericValuesLeaveTheLastAcceptedVersionUntouched()
    {
        await using HealthcheckService service = CreateService();
        INumericThresholdMonitor monitor = service.CreateNumericThreshold(NumericOptions());
        monitor.ReportValue(11, AcceptedDiagnostic("TooSlow"));
        HealthSnapshot accepted = service.CaptureSnapshot();
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => monitor.ReportValue(invalid));
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(accepted.Revision, after.Revision);
            Assert.AreEqual(MonitorState.Unhealthy, Find(after, monitor).State);
            Assert.AreEqual(11d, Find(after, monitor).Numeric!.LastValue);
            Assert.AreEqual(Find(accepted, monitor).Numeric!.LastValueAtUtc, Find(after, monitor).Numeric!.LastValueAtUtc);
            Assert.AreEqual("TooSlow", Find(after, monitor).Diagnostic!.Code);
        }

        monitor.ReportValue(0);
        HealthMonitorSnapshot recovered = Find(service.CaptureSnapshot(), monitor);
        Assert.AreEqual(MonitorState.Healthy, recovered.State);
        Assert.AreEqual(0d, recovered.Numeric!.LastValue);
        Assert.IsNull(recovered.Diagnostic);
    }

    [TestMethod]
    public async Task AvailabilitySurvivesRefreshFailureUntilExplicitInvalidation()
    {
        await using HealthcheckService service = CreateService();
        IAvailabilityMonitor monitor = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        monitor.ReportRefreshFailure(AcceptedDiagnostic("InitialFailure"));
        Assert.AreEqual(MonitorState.Unhealthy, Find(service.CaptureSnapshot(), monitor).State);
        monitor.ReportPublished();
        Assert.AreEqual(MonitorState.Healthy, Find(service.CaptureSnapshot(), monitor).State);
        monitor.ReportRefreshFailure(AcceptedDiagnostic("RefreshFailure"));
        monitor.ReportRefreshFailure();
        HealthMonitorSnapshot usable = Find(service.CaptureSnapshot(), monitor);
        Assert.AreEqual(MonitorState.Healthy, usable.State);
        Assert.AreEqual(1L, usable.Outcomes!.Successful);
        Assert.AreEqual(3L, usable.Outcomes.Failed);
        Assert.AreEqual(2L, usable.Outcomes.ConsecutiveFailures);
        Assert.AreEqual(HealthStatus.Healthy, service.CaptureSnapshot().HealthStatus);

        monitor.Invalidate(AcceptedDiagnostic("CacheInvalid"));
        HealthMonitorSnapshot invalid = Find(service.CaptureSnapshot(), monitor);
        Assert.AreEqual(MonitorState.Unhealthy, invalid.State);
        Assert.AreEqual("CacheInvalid", invalid.Diagnostic!.Code);
        Assert.AreEqual(3L, invalid.Outcomes!.Failed);
        Assert.AreEqual(1L, invalid.Outcomes.Successful);
        monitor.ReportPublished();
        Assert.AreEqual(MonitorState.Healthy, Find(service.CaptureSnapshot(), monitor).State);
        Assert.IsNull(Find(service.CaptureSnapshot(), monitor).Diagnostic);

        IAvailabilityMonitor replacement = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        Assert.AreEqual(MonitorState.Pending, Find(service.CaptureSnapshot(), replacement).State);
        Assert.AreEqual(0L, Find(service.CaptureSnapshot(), replacement).Outcomes!.Successful);
        GC.KeepAlive(replacement);
    }

    [TestMethod]
    public async Task MeasurementsAreDiagnosticAndUseOrdinalKeyOrderingAndActualTimes()
    {
        var clock = new ManualTimeProvider(InitialTime);
        await using HealthcheckService service = CreateService(clock);
        INumericThresholdMonitor monitor = service.CreateNumericThreshold(NumericOptions());
        DateTimeOffset expectedObservation = InitialTime - TimeSpan.FromHours(1);
        monitor.ReportMeasurement(HealthMeasurement.Number("z", 1000, expectedObservation, "ms"));
        monitor.ReportMeasurement(HealthMeasurement.Unavailable("a", expectedObservation));
        monitor.ReportMeasurement(HealthMeasurement.Integer("A", 0, expectedObservation));
        HealthSnapshot accepted = service.CaptureSnapshot();
        HealthMonitorSnapshot details = Find(accepted, monitor);

        Assert.AreEqual(MonitorState.Pending, details.State);
        Assert.AreEqual(HealthStatus.Healthy, accepted.HealthStatus);
        Assert.IsNull(details.Numeric!.LastValue);
        Assert.IsNull(details.LastObservationAtUtc);
        CollectionAssert.AreEqual(new[] { "A", "a", "z" }, details.Measurements.Select(measurement => measurement.Key).ToArray());
        Assert.AreEqual(0L, details.Measurements[0].IntegerValue);
        Assert.AreEqual(HealthMeasurementValueKind.Unavailable, details.Measurements[1].ValueKind);
        Assert.IsTrue(details.Measurements.All(measurement => measurement.ObservedAtUtc == expectedObservation));

        clock.Advance(TimeSpan.FromHours(2));
        monitor.ReportMeasurement(HealthMeasurement.Number("z", 0, clock.GetUtcNow(), "ms"));
        HealthSnapshot replaced = service.CaptureSnapshot();
        Assert.IsTrue(replaced.Revision > accepted.Revision);
        Assert.AreEqual(3, Find(replaced, monitor).Measurements.Count);
        Assert.AreEqual(0d, Find(replaced, monitor).Measurements[2].NumberValue);
        Assert.AreEqual(1000d, details.Measurements[2].NumberValue);
        Assert.AreEqual(expectedObservation, details.Measurements[2].ObservedAtUtc);
    }

    [TestMethod]
    public async Task InvalidMeasurementReportsLeaveHealthAndRevisionUnchanged()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true });
        HealthSnapshot accepted = service.CaptureSnapshot();
        Assert.ThrowsExactly<ArgumentNullException>(() => monitor.ReportMeasurement(null!));
        Assert.Throws<ArgumentException>(() => monitor.ReportMeasurement(HealthMeasurement.Boolean("required", false, InitialTime, isDiagnostic: false)));

        HealthSnapshot after = service.CaptureSnapshot();
        Assert.AreEqual(accepted.Revision, after.Revision);
        Assert.AreEqual(MonitorState.Unhealthy, Find(after, monitor).State);
        Assert.AreEqual(0, Find(after, monitor).Measurements.Count);
    }

    [TestMethod]
    public async Task MeasurementKeyLimitRejectsOnlyNewKeysAndAllowsExistingKeyReplacement()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        for (int index = 0; index < 32; index++)
        {
            monitor.ReportMeasurement(HealthMeasurement.Integer("key-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), index, InitialTime));
        }

        HealthSnapshot full = service.CaptureSnapshot();
        Assert.AreEqual(32, Find(full, monitor).Measurements.Count);
        Assert.Throws<ArgumentException>(() => monitor.ReportMeasurement(HealthMeasurement.Integer("new-key", 99, InitialTime)));
        HealthSnapshot rejected = service.CaptureSnapshot();
        Assert.AreEqual(full.Revision, rejected.Revision);
        Assert.AreEqual(32, Find(rejected, monitor).Measurements.Count);
        Assert.AreEqual(MonitorState.Healthy, Find(rejected, monitor).State);

        monitor.ReportMeasurement(HealthMeasurement.Integer("key-00", 99, InitialTime + TimeSpan.FromSeconds(1)));
        HealthSnapshot replaced = service.CaptureSnapshot();
        Assert.IsTrue(replaced.Revision > full.Revision);
        Assert.AreEqual(32, Find(replaced, monitor).Measurements.Count);
        Assert.AreEqual(99L, Find(replaced, monitor).Measurements[0].IntegerValue);
        Assert.AreEqual(0L, Find(full, monitor).Measurements[0].IntegerValue);
    }

    [TestMethod]
    public async Task SnapshotReadsHaveFreshCaptureTimesWithoutChangingObservationOrRevision()
    {
        var clock = new ManualTimeProvider(InitialTime);
        await using HealthcheckService service = CreateService(clock);
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        monitor.Flag(AcceptedDiagnostic("Fault"));
        HealthSnapshot before = service.CaptureSnapshot();
        clock.Advance(TimeSpan.FromDays(1));
        HealthSnapshot after = service.CaptureSnapshot();

        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(InitialTime, before.CapturedAtUtc);
        Assert.AreEqual(InitialTime + TimeSpan.FromDays(1), after.CapturedAtUtc);
        Assert.AreEqual(InitialTime, Find(after, monitor).RegisteredAtUtc);
        Assert.AreEqual(InitialTime, Find(after, monitor).LastObservationAtUtc);
        Assert.AreEqual(MonitorState.Unhealthy, Find(before, monitor).State);
        monitor.Clear();
        Assert.AreEqual(MonitorState.Unhealthy, Find(before, monitor).State);
        Assert.AreEqual("Fault", Find(before, monitor).Diagnostic!.Code);
        Assert.AreEqual(MonitorState.Healthy, Find(service.CaptureSnapshot(), monitor).State);
    }

    [TestMethod]
    public async Task MonitorOrderingTracksRegistrationRatherThanDisplayNameOrPolicy()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor first = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false, Name = "z" });
        IAvailabilityMonitor second = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = false, Name = "a" });
        INumericThresholdMonitor third = service.CreateNumericThreshold(NumericOptions());
        CollectionAssert.AreEqual(new[] { first.MonitorId, second.MonitorId, third.MonitorId },
            service.CaptureSnapshot().Monitors.Select(monitor => monitor.MonitorId).ToArray());
        second.Withdraw();
        IConditionMonitor fourth = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false, Name = "a" });
        CollectionAssert.AreEqual(new[] { first.MonitorId, third.MonitorId, fourth.MonitorId },
            service.CaptureSnapshot().Monitors.Select(monitor => monitor.MonitorId).ToArray());
        GC.KeepAlive(first);
        GC.KeepAlive(third);
        GC.KeepAlive(fourth);
    }

    [TestMethod]
    public async Task CapacityFailureDoesNotEvictAnExistingFailureAndWithdrawalFreesAdmission()
    {
        await using HealthcheckService service = CreateService(capacity: 1);
        IConditionMonitor original = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true, Name = "same" });
        HealthSnapshot accepted = service.CaptureSnapshot();
        HealthcheckCapacityException error = Assert.ThrowsExactly<HealthcheckCapacityException>(() =>
            service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = false }));
        Assert.AreEqual(1, error.Limit);
        Assert.AreEqual("Healthcheck.MonitorCapacity", error.Code);
        HealthSnapshot rejected = service.CaptureSnapshot();
        Assert.AreEqual(accepted.Revision, rejected.Revision);
        Assert.AreEqual(original.MonitorId, rejected.Monitors.Single().MonitorId);
        Assert.AreEqual(HealthStatus.Unhealthy, rejected.HealthStatus);

        original.Withdraw();
        IConditionMonitor replacement = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false, Name = "same" });
        Assert.AreNotEqual(original.MonitorId, replacement.MonitorId);
        Assert.ThrowsExactly<ObjectDisposedException>(() => original.Flag());
        Assert.AreEqual(MonitorState.Healthy, service.CaptureSnapshot().Monitors.Single().State);
        GC.KeepAlive(replacement);
    }

    [TestMethod]
    public async Task InvalidPolicyOptionsNeverPublishAPartialRegistration()
    {
        await using HealthcheckService service = CreateService(capacity: 1);
        BigInteger initialRevision = service.CaptureSnapshot().Revision;
        Assert.ThrowsExactly<ArgumentNullException>(() => service.CreateCondition(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => service.CreateConsecutiveFailures(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => service.CreateNumericThreshold(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => service.CreateAvailability(null!));
        Assert.Throws<ArgumentException>(() => service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 0 }));
        Assert.Throws<ArgumentException>(() => service.CreateNumericThreshold(new NumericThresholdMonitorOptions { IsCritical = true, Comparison = (NumericComparison)99, Threshold = 10, Unit = "ms" }));
        Assert.Throws<ArgumentException>(() => service.CreateNumericThreshold(new NumericThresholdMonitorOptions { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = double.NaN, Unit = "ms" }));
        Assert.Throws<ArgumentException>(() => service.CreateNumericThreshold(new NumericThresholdMonitorOptions { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = 10, Unit = " " }));
        Assert.Throws<ArgumentException>(() => service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false, Name = new string('n', 129) }));
        Assert.AreEqual(initialRevision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, service.CaptureSnapshot().Monitors.Count);

        IConditionMonitor accepted = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        Assert.AreEqual(1, service.CaptureSnapshot().Monitors.Count);
        GC.KeepAlive(accepted);
    }

    [TestMethod]
    public async Task WithdrawalRemovesContributionImmediatelyAndKeepsOnlyIdentityReadable()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1, Name = "retired" });
        Guid monitorId = monitor.MonitorId;
        monitor.ReportFailure();
        HealthSnapshot before = service.CaptureSnapshot();
        monitor.Withdraw();
        HealthSnapshot after = service.CaptureSnapshot();

        Assert.AreEqual(0, after.Monitors.Count);
        Assert.AreEqual(HealthStatus.Healthy, after.HealthStatus);
        Assert.IsTrue(after.Revision > before.Revision);
        Assert.AreEqual(1L, before.Monitors.Single().Outcomes!.Failed);
        Assert.AreEqual(monitorId, monitor.MonitorId);
        Assert.AreEqual("retired", monitor.Name);
        Assert.IsTrue(monitor.IsCritical);
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.ReportFailure());
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.ReportSuccess());
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.ReportSkipped());
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.ReportExpectedCancellation());
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.ReportMeasurement(HealthMeasurement.Integer("late", 1, InitialTime)));
        monitor.Withdraw();
        await monitor.DisposeAsync();
        await monitor.WaitForDisposalAsync();
        Assert.AreEqual(after.Revision, service.CaptureSnapshot().Revision);
    }

    [TestMethod]
    public async Task AllPolicyMutationMethodsRejectWithdrawnHandles()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor condition = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false });
        INumericThresholdMonitor numeric = service.CreateNumericThreshold(NumericOptions());
        IAvailabilityMonitor availability = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        condition.Withdraw();
        numeric.Withdraw();
        availability.Withdraw();
        Assert.ThrowsExactly<ObjectDisposedException>(() => condition.Flag());
        Assert.ThrowsExactly<ObjectDisposedException>(() => condition.Clear());
        Assert.ThrowsExactly<ObjectDisposedException>(() => numeric.ReportValue(0));
        Assert.ThrowsExactly<ObjectDisposedException>(() => availability.ReportPublished());
        Assert.ThrowsExactly<ObjectDisposedException>(() => availability.ReportRefreshFailure());
        Assert.ThrowsExactly<ObjectDisposedException>(() => availability.Invalidate());
        Assert.AreEqual(0, service.CaptureSnapshot().Monitors.Count);
    }

    [TestMethod]
    public async Task CancelingAMonitorDisposalWaitDoesNotWithdrawTheMonitor()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true });
        using var canceled = new CancellationTokenSource();
        Task wait = monitor.WaitForDisposalAsync(canceled.Token);
        Assert.IsFalse(wait.IsCompleted);
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
        Assert.AreEqual(1, service.CaptureSnapshot().Monitors.Count);
        Assert.AreEqual(HealthStatus.Unhealthy, service.CaptureSnapshot().HealthStatus);
        monitor.Clear();
        Assert.AreEqual(HealthStatus.Healthy, service.CaptureSnapshot().HealthStatus);
        monitor.Withdraw();
        await monitor.WaitForDisposalAsync();
    }

    [TestMethod]
    public async Task StoppingClosesAdmissionWhileExistingMonitorsAcceptFinalReports()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true });
        service.MarkStartupCompleted();
        service.BeginStopping();
        HealthSnapshot stopping = service.CaptureSnapshot();
        service.BeginStopping();
        Assert.AreEqual(stopping.Revision, service.CaptureSnapshot().Revision);
        Assert.IsTrue(stopping.Application.IsStopping);
        Assert.IsFalse(stopping.Ready);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.MarkStartupCompleted());
        Assert.ThrowsExactly<InvalidOperationException>(() => service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false }));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 }));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.CreateNumericThreshold(NumericOptions()));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true }));

        monitor.Clear();
        HealthSnapshot finalReport = service.CaptureSnapshot();
        Assert.AreEqual(HealthStatus.Healthy, finalReport.HealthStatus);
        Assert.IsFalse(finalReport.Ready);
        Assert.AreEqual(1, finalReport.Monitors.Count);
        monitor.Withdraw();
        Assert.IsFalse(service.CaptureSnapshot().Ready);
    }

    [TestMethod]
    public async Task ServiceDisposalWaitIsSeparateFromInitiatingDisposalAndIsIdempotent()
    {
        var service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        using var canceled = new CancellationTokenSource();
        Task wait = service.WaitForDisposalAsync(canceled.Token);
        Assert.IsFalse(wait.IsCompleted);
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
        Assert.IsFalse(service.CaptureSnapshot().Application.IsStopping);
        Task sharedWait = service.WaitForDisposalAsync();
        Assert.IsFalse(sharedWait.IsCompleted);
        await service.DisposeAsync();
        await sharedWait;
        await service.DisposeAsync();
        service.BeginStopping();
        await service.WaitForDisposalAsync();
        await monitor.WaitForDisposalAsync();
        Assert.ThrowsExactly<ObjectDisposedException>(() => service.CaptureSnapshot());
        Assert.ThrowsExactly<ObjectDisposedException>(() => service.MarkStartupCompleted());
        Assert.ThrowsExactly<ObjectDisposedException>(() => service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true }));
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.Flag());
        Assert.AreNotEqual(Guid.Empty, monitor.MonitorId);
    }

    [TestMethod]
    public async Task LostIdleMonitorIsWeaklyRemovedWithoutInventingAMissingDependency()
    {
        await using HealthcheckService service = CreateService(capacity: 1);
        WeakReference lost = RegisterUnownedFailingMonitor(service);
        CollectUntilDead(lost);
        Assert.IsFalse(lost.IsAlive, "The service registry must not keep an idle monitor alive.");
        HealthSnapshot reconciled = service.CaptureSnapshot();
        Assert.AreEqual(0, reconciled.Monitors.Count);
        Assert.AreEqual(HealthStatus.Healthy, reconciled.HealthStatus);
        Assert.IsFalse(reconciled.Ready);
        AssertCounts(reconciled, active: 0, critical: 0, failing: 0, criticalFailing: 0, pending: 0);
        IAvailabilityMonitor replacement = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        Assert.AreEqual(1, service.CaptureSnapshot().Monitors.Count);
        GC.KeepAlive(replacement);
    }

    [TestMethod]
    public async Task ActiveReportKeepsItsTemporaryMonitorAliveDuringAReentrantClockRead()
    {
        var clock = new ReentrantGcTimeProvider();
        await using var service = new HealthcheckService(new HealthcheckOptions(), Identity(), clock);
        int reportingClockCallbacks = 0;
        clock.OnRead = () =>
        {
            // Read 1 belongs to registration. Read 2 occurs inside Flag, after the
            // public handle wrapper has passed its detached entry to the state owner.
            if (clock.ReadCount != 2) { return; }
            reportingClockCallbacks++;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            HealthSnapshot duringAcceptance = service.CaptureSnapshot();
            Assert.AreEqual(1, duringAcceptance.MonitorCounts.Active,
                "A report's receiver must remain alive while supplied clock code reenters snapshot capture.");
            Assert.AreEqual(MonitorState.Healthy, duringAcceptance.Monitors.Single().State);
        };

        ReportConditionOnTemporaryMonitor(service);

        Assert.AreEqual(1, reportingClockCallbacks);
        // The call must commit without ObjectDisposedException. Its temporary
        // receiver may legitimately become collectible after the report returns.
    }

    [TestMethod]
    public async Task ConcurrentReportsAndWithdrawalPublishOnlyCompleteSnapshotVersions()
    {
        const int rounds = 32;
        TimeSpan deadline = TimeSpan.FromSeconds(30);
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions
        {
            IsCritical = true,
            FailureThreshold = 1
        });
        service.MarkStartupCompleted();
        var starts = Enumerable.Range(0, rounds).Select(_ => new AsyncBarrier(2)).ToArray();
        var finishes = Enumerable.Range(0, rounds).Select(_ => new AsyncBarrier(2)).ToArray();
        var withdrawalStart = new AsyncBarrier(2);
        var withdrawn = new AsyncSignal();
        Guid monitorId = monitor.MonitorId;
        HealthDiagnostic failureReason = AcceptedDiagnostic("ConcurrentFailure");

        Task writer = Task.Run(async () =>
        {
            for (int round = 0; round < rounds; round++)
            {
                monitor.ReportFailure(failureReason);
                await starts[round].SignalAndWaitAsync(deadline);
                monitor.ReportSuccess();
                await finishes[round].SignalAndWaitAsync(deadline);
            }

            await withdrawalStart.SignalAndWaitAsync(deadline);
            monitor.Withdraw();
            withdrawn.Set();
        });
        Task reader = Task.Run(async () =>
        {
            BigInteger lastRevision = BigInteger.Zero;
            for (int round = 0; round < rounds; round++)
            {
                // The failure is accepted before this barrier. Success races the
                // captures after both participants are released together.
                await starts[round].SignalAndWaitAsync(deadline);
                for (int capture = 0; capture < 2; capture++)
                {
                    HealthSnapshot snapshot = service.CaptureSnapshot();
                    Assert.IsTrue(snapshot.Revision >= lastRevision);
                    lastRevision = snapshot.Revision;
                    AssertCompleteConcurrentVersion(snapshot, monitorId);
                }

                await finishes[round].SignalAndWaitAsync(deadline);
            }

            HealthSnapshot beforeWithdrawal = service.CaptureSnapshot();
            AssertCompleteConcurrentVersion(beforeWithdrawal, monitorId);
            Assert.AreEqual((long)rounds, beforeWithdrawal.Monitors.Single().Outcomes!.Successful);
            Assert.AreEqual((long)rounds, beforeWithdrawal.Monitors.Single().Outcomes!.Failed);
            Assert.IsTrue(beforeWithdrawal.Ready);
            await withdrawalStart.SignalAndWaitAsync(deadline);
            HealthSnapshot racingWithdrawal = service.CaptureSnapshot();
            if (racingWithdrawal.Monitors.Count == 0)
            {
                AssertCounts(racingWithdrawal, active: 0, critical: 0, failing: 0, criticalFailing: 0, pending: 0);
                Assert.AreEqual(HealthStatus.Healthy, racingWithdrawal.HealthStatus);
                Assert.IsTrue(racingWithdrawal.Ready);
            }
            else { AssertCompleteConcurrentVersion(racingWithdrawal, monitorId); }

            await withdrawn.WaitAsync(deadline);
            HealthSnapshot afterWithdrawal = service.CaptureSnapshot();
            AssertCounts(afterWithdrawal, active: 0, critical: 0, failing: 0, criticalFailing: 0, pending: 0);
            Assert.AreEqual(0, afterWithdrawal.Monitors.Count);
            Assert.AreEqual(HealthStatus.Healthy, afterWithdrawal.HealthStatus);
            Assert.IsTrue(afterWithdrawal.Ready);
            Assert.IsTrue(afterWithdrawal.Revision > beforeWithdrawal.Revision);
        });

        await Task.WhenAll(writer, reader);
    }

    private static HealthcheckService CreateService(ManualTimeProvider? clock = null, int capacity = 1024) =>
        new(new HealthcheckOptions { MaxMonitors = capacity }, Identity(), clock ?? new ManualTimeProvider(InitialTime));

    private static HealthApplicationIdentity Identity() => new() { Name = "healthcheck-tests", InstanceId = InstanceId };

    private static HealthApplicationIdentity IdentityWith(string field, string value) => new()
    {
        Name = field == "Name" ? value : "healthcheck-tests",
        InstanceId = InstanceId,
        BuildVersion = field == "BuildVersion" ? value : null,
        ServiceVersion = field == "ServiceVersion" ? value : null,
        HostName = field == "HostName" ? value : null,
        OsDescription = field == "OsDescription" ? value : null
    };

    private static string? IdentityValue(HealthApplicationSnapshot application, string field) => field switch
    {
        "Name" => application.Name,
        "BuildVersion" => application.BuildVersion,
        "ServiceVersion" => application.ServiceVersion,
        "HostName" => application.HostName,
        "OsDescription" => application.OsDescription,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static NumericThresholdMonitorOptions NumericOptions(NumericComparison comparison = NumericComparison.GreaterThan) => new()
    {
        IsCritical = true,
        Comparison = comparison,
        Threshold = 10,
        Unit = "ms"
    };

    private static HealthMonitorSnapshot Find(HealthSnapshot snapshot, IHealthMonitor monitor) =>
        snapshot.Monitors.Single(item => item.MonitorId == monitor.MonitorId);

    private static void AssertCounts(HealthSnapshot snapshot, int active, int critical, int failing, int criticalFailing, int pending)
    {
        Assert.AreEqual(active, snapshot.MonitorCounts.Active);
        Assert.AreEqual(critical, snapshot.MonitorCounts.Critical);
        Assert.AreEqual(failing, snapshot.MonitorCounts.Failing);
        Assert.AreEqual(criticalFailing, snapshot.MonitorCounts.CriticalFailing);
        Assert.AreEqual(pending, snapshot.MonitorCounts.Pending);
    }

    private static void AssertCompleteConcurrentVersion(HealthSnapshot snapshot, Guid monitorId)
    {
        HealthMonitorSnapshot monitor = snapshot.Monitors.Single();
        HealthOutcomeSnapshot outcomes = monitor.Outcomes!;
        Assert.AreEqual(monitorId, monitor.MonitorId);
        Assert.IsTrue(monitor.IsCritical);
        Assert.AreEqual(HealthPolicyKind.ConsecutiveFailures, monitor.Policy);
        Assert.IsTrue(snapshot.Application.StartupCompleted);
        Assert.IsFalse(snapshot.Application.IsStopping);
        Assert.IsTrue(outcomes.Failed > 0);
        Assert.AreEqual(0L, outcomes.Skipped);
        Assert.AreEqual(0L, outcomes.ExpectedCanceled);
        Assert.IsFalse(outcomes.CountersSaturated);
        Assert.AreEqual("ConcurrentFailure", outcomes.LastFailureDiagnostic!.Code);
        Assert.IsNotNull(outcomes.LastFailureAtUtc);
        if (monitor.State == MonitorState.Unhealthy)
        {
            Assert.AreEqual(outcomes.Successful + 1, outcomes.Failed);
            Assert.AreEqual(1L, outcomes.ConsecutiveFailures);
            Assert.AreEqual(HealthExecutionOutcome.Failed, outcomes.LastOutcome);
            Assert.AreEqual(outcomes.LastFailureAtUtc, monitor.LastObservationAtUtc);
            Assert.AreEqual("ConcurrentFailure", monitor.Diagnostic!.Code);
            Assert.AreEqual(HealthStatus.Unhealthy, snapshot.HealthStatus);
            Assert.IsFalse(snapshot.Ready);
            AssertCounts(snapshot, active: 1, critical: 1, failing: 1, criticalFailing: 1, pending: 0);
        }
        else
        {
            Assert.AreEqual(MonitorState.Healthy, monitor.State);
            Assert.AreEqual(outcomes.Failed, outcomes.Successful);
            Assert.AreEqual(0L, outcomes.ConsecutiveFailures);
            Assert.AreEqual(HealthExecutionOutcome.Succeeded, outcomes.LastOutcome);
            Assert.IsNotNull(outcomes.LastSuccessAtUtc);
            Assert.AreEqual(outcomes.LastSuccessAtUtc, monitor.LastObservationAtUtc);
            Assert.IsNull(monitor.Diagnostic);
            Assert.AreEqual(HealthStatus.Healthy, snapshot.HealthStatus);
            Assert.IsTrue(snapshot.Ready);
            AssertCounts(snapshot, active: 1, critical: 1, failing: 0, criticalFailing: 0, pending: 0);
        }
    }

    private static void CollectUntilDead(params WeakReference[] references)
    {
        for (int attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (HealthcheckService Service, WeakReference Identity, WeakReference Options) CreateServiceWithWeakInputs()
    {
        var identity = new HealthApplicationIdentity { Name = "copied-identity", InstanceId = InstanceId };
        var options = new HealthcheckOptions { MaxMonitors = 1 };
        var service = new HealthcheckService(options, identity);
        return (service, new WeakReference(identity), new WeakReference(options));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportConditionOnTemporaryMonitor(HealthcheckService service) =>
        service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false })
            .Flag(AcceptedDiagnostic("CommittedAfterClock"));

    private sealed class ReentrantGcTimeProvider : TimeProvider
    {
        internal Action? OnRead { get; set; }
        internal int ReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            ReadCount++;
            OnRead?.Invoke();
            return InitialTime;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterUnownedFailingMonitor(HealthcheckService service)
    {
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true });
        Assert.AreEqual(HealthStatus.Unhealthy, service.CaptureSnapshot().HealthStatus);
        return new WeakReference(monitor);
    }
    private static HealthDiagnostic AcceptedDiagnostic(string code, string? message = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce the actual detached reason.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }
}
