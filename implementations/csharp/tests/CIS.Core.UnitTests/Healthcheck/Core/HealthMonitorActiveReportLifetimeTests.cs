using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthMonitorActiveReportLifetimeTests
{
    [TestMethod]
    [DataRow("condition-clear")]
    [DataRow("failure-success")]
    [DataRow("failure-failure")]
    [DataRow("failure-skipped")]
    [DataRow("failure-canceled")]
    [DataRow("numeric-value")]
    [DataRow("availability-published")]
    [DataRow("availability-refresh-failure")]
    [DataRow("availability-invalidated")]
    public async Task TemporaryReceiverRemainsActiveDuringItsReportClockCallback(string operation)
    {
        var clock = new ReentrantClock();
        await using var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "active-report-lifetime", InstanceId = Guid.NewGuid() }, clock);
        int callbacks = 0;
        clock.OnRead = () =>
        {
            // Registration reads first; the report reads next, after the public
            // receiver has passed its detached entry into the state owner.
            if (clock.Reads != 2) { return; }
            callbacks++;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            HealthSnapshot duringReport = service.CaptureSnapshot();
            Assert.AreEqual(1, duringReport.MonitorCounts.Active,
                "An active report's receiver must survive a supplied clock reentering snapshot capture.");
            Assert.AreEqual(operation, duringReport.Monitors.Single().Name);
        };

        ReportOnTemporaryReceiver(service, operation);

        Assert.AreEqual(1, callbacks, "The real reporting path must reach the supplied clock callback.");
        // The temporary receiver may become collectible after the report returns.
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportOnTemporaryReceiver(HealthcheckService service, string operation)
    {
        switch (operation)
        {
            case "condition-clear":
                service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true, Name = operation }).Clear();
                break;
            case "failure-success":
                service.CreateConsecutiveFailures(FailureOptions(operation)).ReportSuccess();
                break;
            case "failure-failure":
                service.CreateConsecutiveFailures(FailureOptions(operation)).ReportFailure();
                break;
            case "failure-skipped":
                service.CreateConsecutiveFailures(FailureOptions(operation)).ReportSkipped();
                break;
            case "failure-canceled":
                service.CreateConsecutiveFailures(FailureOptions(operation)).ReportExpectedCancellation();
                break;
            case "numeric-value":
                service.CreateNumericThreshold(new NumericThresholdMonitorOptions
                {
                    IsCritical = true,
                    Name = operation,
                    Threshold = 1,
                    Comparison = NumericComparison.GreaterThan,
                    Unit = "items"
                }).ReportValue(2);
                break;
            case "availability-published":
                service.CreateAvailability(AvailabilityOptions(operation)).ReportPublished();
                break;
            case "availability-refresh-failure":
                service.CreateAvailability(AvailabilityOptions(operation)).ReportRefreshFailure();
                break;
            case "availability-invalidated":
                service.CreateAvailability(AvailabilityOptions(operation)).Invalidate();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static ConsecutiveFailureMonitorOptions FailureOptions(string name) =>
        new() { IsCritical = true, FailureThreshold = 1, Name = name };
    private static AvailabilityMonitorOptions AvailabilityOptions(string name) => new() { IsCritical = true, Name = name };

    private sealed class ReentrantClock : TimeProvider
    {
        internal int Reads { get; private set; }
        internal Action? OnRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            OnRead?.Invoke();
            return new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        }
    }
}
