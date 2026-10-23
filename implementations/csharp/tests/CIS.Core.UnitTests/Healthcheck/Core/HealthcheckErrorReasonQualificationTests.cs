using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckErrorReasonQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Observed = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EmptyApplicationIdentityReportsAReasonAtTheServiceConstructor()
    {
        var identity = new HealthApplicationIdentity { Name = "reason-boundary", InstanceId = Guid.Empty };
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(), identity));
        AssertArgumentReason(error, "identity");
    }

    [TestMethod]
    public void InvalidApplicationTextReportsAReasonAndTheReceivingField()
    {
        var identity = new HealthApplicationIdentity { Name = "invalid\u0001name", InstanceId = Guid.NewGuid() };
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => new HealthcheckService(new HealthcheckOptions(), identity));
        AssertArgumentReason(error, "Name");
    }

    [TestMethod]
    public async Task InvalidFailureThresholdReportsARangeReasonWithoutPublishingAMonitor()
    {
        await using var service = Service();
        HealthSnapshot before = service.CaptureSnapshot();
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 0 }));
        AssertArgumentReason(error, "options");
        AssertRejectedRegistration(service, before);
    }

    [TestMethod]
    public async Task InvalidNumericComparisonAndThresholdReportTheReceivingPolicyReason()
    {
        await using var service = Service();
        HealthSnapshot before = service.CaptureSnapshot();
        NumericThresholdMonitorOptions[] invalid =
        [
            new() { IsCritical = true, Comparison = (NumericComparison)99, Threshold = 10, Unit = "ms" },
            new() { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = double.NaN, Unit = "ms" },
            new() { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = double.PositiveInfinity, Unit = "ms" }
        ];
        foreach (NumericThresholdMonitorOptions options in invalid)
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => service.CreateNumericThreshold(options));
            AssertArgumentReason(error, "options");
            AssertRejectedRegistration(service, before);
        }
    }

    [TestMethod]
    public async Task BlankNumericUnitReportsAReasonWithoutPublishingAMonitor()
    {
        await using var service = Service();
        HealthSnapshot before = service.CaptureSnapshot();
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => service.CreateNumericThreshold(new NumericThresholdMonitorOptions
        { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = 10, Unit = " \t" }));
        AssertArgumentReason(error, "options");
        AssertRejectedRegistration(service, before);
    }

    [TestMethod]
    public async Task TerminalServiceAdmissionReportsAReasonToItsActualCaller()
    {
        await using var service = Service();
        service.BeginStopping();
        HealthSnapshot before = service.CaptureSnapshot();
        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => service.CreateCondition(new ConditionMonitorOptions
        { IsCritical = true, InitiallyFlagged = false }));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message), "Terminal admission must explain its rejection to the receiving caller.");
        HealthSnapshot after = service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.IsTrue(after.Application.IsStopping);
        Assert.IsFalse(after.Ready);
        Assert.AreEqual(0, after.MonitorCounts.Active);
    }

    [TestMethod]
    public async Task ASecondRunnerClaimReportsAReasonAndPreservesTheFirstRunner()
    {
        var service = Service();
        HealthCollectorRunner? first = null;
        Exception? primary = null;
        try
        {
            first = new HealthCollectorRunner(service);
            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() => new HealthCollectorRunner(service));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message), "The second claimant must receive an explanation rather than an empty error.");
            await first.StartAsync().WaitAsync(Deadline);
            await first.StopAsync().WaitAsync(Deadline);
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Task runner = DrainRunnerAsync();
            Task state = DrainServiceAsync();
            Task drains = Task.WhenAll(runner, state);
            try
            {
                await drains.WaitAsync(Deadline);
            }
            catch (Exception cleanup) when (primary is not null)
            { throw new AggregateException("The receiving body failed and actual cleanup did not complete successfully.", primary, drains.Exception ?? cleanup); }
            catch (Exception cleanup) when (cleanup is not TimeoutException && drains.Exception is not null)
            { throw drains.Exception; }

            async Task DrainRunnerAsync() { if (first is not null) { await first.DisposeAsync(); } }
            async Task DrainServiceAsync() => await service.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task NondiagnosticCommonMeasurementReportsAReasonWithoutChangingState()
    {
        await using var service = Service();
        await using IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        HealthMeasurement measurement = HealthMeasurement.Integer("business-value", 1, Observed, isDiagnostic: false);
        Assert.IsFalse(measurement.IsDiagnostic, "The rejection must occur at the real receiving monitor seam.");
        HealthSnapshot before = service.CaptureSnapshot();
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => monitor.ReportMeasurement(measurement));
        AssertArgumentReason(error, "measurement");
        HealthSnapshot after = service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(0, after.Monitors.Single().Measurements.Count);
        Assert.AreEqual(HealthStatus.Healthy, after.HealthStatus);
    }

    [TestMethod]
    public async Task TheThirtyThirdMetricReportsACapacityReasonWhileExistingKeysRemainUsable()
    {
        await using var service = Service();
        await using IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        for (int index = 0; index < 32; index++)
        { monitor.ReportMeasurement(HealthMeasurement.Integer("metric-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture), index, Observed)); }
        HealthSnapshot before = service.CaptureSnapshot();
        Assert.AreEqual(32, before.Monitors.Single().Measurements.Count);
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => monitor.ReportMeasurement(HealthMeasurement.Integer("metric-32", 32, Observed)));
        AssertArgumentReason(error, "measurement");
        HealthSnapshot rejected = service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, rejected.Revision);
        Assert.AreEqual(32, rejected.Monitors.Single().Measurements.Count);
        Assert.IsFalse(rejected.Monitors.Single().Measurements.Any(item => item.Key == "metric-32"));
        monitor.ReportMeasurement(HealthMeasurement.Integer("metric-00", 100, Observed));
        HealthMonitorSnapshot replaced = service.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(32, replaced.Measurements.Count);
        Assert.AreEqual(100L, replaced.Measurements.Single(item => item.Key == "metric-00").IntegerValue);
    }

    private static void AssertArgumentReason(ArgumentException error, string parameter)
    {
        Assert.AreEqual(parameter, error.ParamName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message), "The actual receiving seam must provide a reason.");
        // ArgumentException adds localized parameter text even when its reason
        // is empty. Compare with the same runtime/type sentinel, not that text.
        ArgumentException emptyReason = error is ArgumentOutOfRangeException
            ? new ArgumentOutOfRangeException(error.ParamName, "")
            : new ArgumentException("", error.ParamName);
        Assert.AreNotEqual(emptyReason.Message.Trim(), error.Message.Trim(), "A parameter label alone is not a descriptive error reason.");
    }

    private static void AssertRejectedRegistration(HealthcheckService service, HealthSnapshot before)
    {
        HealthSnapshot after = service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(0, after.MonitorCounts.Active);
    }

    private static HealthcheckService Service() => new(new HealthcheckOptions(), new HealthApplicationIdentity
    { Name = "reason-boundary", InstanceId = Guid.NewGuid() });
}
