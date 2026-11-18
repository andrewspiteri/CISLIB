using CIS.CommonTesting;
using CIS.Healthcheck;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class RecordingSupportTests
{
    [TestMethod]
    public async Task HealthRecordEvictionDoesNotLoseTotalsOrInventPolicyEvaluation()
    {
        var factory = new RecordingHealthMonitorFactory(capacity: 1, reportCapacity: 2);
        IAvailabilityMonitor monitor = factory.CreateAvailability(new()
        {
            Name = "cache",
            IsCritical = true
        });
        for (int index = 0; index < 20; index++)
        {
            monitor.ReportPublished();
            monitor.ReportRefreshFailure();
        }
        RecordingHealthMonitor recorded = factory.Monitors.Single();
        Assert.AreEqual(20L, recorded.Count(HealthReportKind.Published));
        Assert.AreEqual(20L, recorded.Count(HealthReportKind.RefreshFailure));
        Assert.HasCount(2, recorded.Reports);
        Assert.AreEqual(39L, recorded.DroppedReports);
        using var waitCancellation = new CancellationTokenSource();
        Task wait = monitor.WaitForDisposalAsync(waitCancellation.Token);
        waitCancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => wait);
        Assert.IsFalse(recorded.IsWithdrawn);
        await monitor.DisposeAsync();
        await monitor.DisposeAsync();
        Assert.AreEqual(1L, recorded.Count(HealthReportKind.Withdrawn));
        Assert.Throws<ObjectDisposedException>(() => monitor.ReportPublished());
        IConditionMonitor other = factory.CreateCondition(new()
        {
            IsCritical = false,
            InitiallyFlagged = true
        });
        Assert.AreEqual(1L, factory.DroppedMonitors);
        Assert.AreSame<IHealthMonitor>(factory.Monitors[0], other);
        await other.DisposeAsync();
    }

    [TestMethod]
    public async Task SnapshotStubReturnsOnlyTheExplicitlyConfiguredGenuineSnapshot()
    {
        await using var health = new HealthcheckService(new HealthcheckOptions());
        HealthSnapshot initial = health.CaptureSnapshot();
        var stub = new HealthSnapshotStub(initial);
        Assert.AreSame(initial, stub.CaptureSnapshot());
        health.BeginStopping();
        HealthSnapshot stopped = health.CaptureSnapshot();
        stub.Snapshot = stopped;
        Assert.AreSame(stopped, stub.CaptureSnapshot());
    }

    [TestMethod]
    public void LoggerCopiesStructuredFieldsKeepsExceptionIdentityAndBoundsEnabledEvents()
    {
        var logger = new RecordingLogger<RecordingSupportTests>(capacity: 2);
        var error = new InvalidOperationException("owned test fault");
        logger.LogWarning(new EventId(7, "country.failed"), error, "Failure {Code} ({Suppressed})", "provider.busy", 3);
        RecordedLog first = logger.Entries.Single();
        Assert.AreSame(error, first.Exception);
        Assert.AreEqual("country.failed", first.EventId.Name);
        Assert.AreEqual("provider.busy", first.Fields["Code"]);
        Assert.AreEqual(3, first.Fields["Suppressed"]);
        logger.LogInformation("second");
        logger.LogInformation("third");
        Assert.AreEqual(3L, logger.TotalCount);
        Assert.AreEqual(1L, logger.DroppedCount);
        Assert.HasCount(2, logger.Entries);
        logger.MinimumLevel = LogLevel.None;
        int formatted = 0;
        logger.Log(LogLevel.Error, new EventId(8), "unused", null, (state, _) =>
        {
            formatted++;
            return state;
        });
        Assert.AreEqual(0, formatted);
        logger.Clear();
        Assert.AreEqual(0L, logger.TotalCount);
    }

    [TestMethod]
    public async Task PromotedClockSeparatesUtcFromMonotonicTimeAndDisposesActualTimers()
    {
        var clock = new CIS.CommonTesting.ManualTimeProvider(DateTimeOffset.UnixEpoch);
        int ticks = 0;
        await using System.Threading.ITimer timer = clock.CreateTimer(_ => ticks++, null,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        long entry = clock.GetTimestamp();
        clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.AreEqual(entry, clock.GetTimestamp());
        clock.Advance(TimeSpan.FromMilliseconds(30));
        Assert.AreEqual(3, ticks);
        Assert.AreEqual(TimeSpan.FromMilliseconds(30), clock.GetElapsedTime(entry));
        await timer.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(3, ticks);
    }
}
