using System.Globalization;
using CIS.CommonTesting;
using CIS.Examples.CountryData;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CountryData;

[TestClass]
public sealed class CountryRefreshDiagnosticTests
{
    [TestMethod]
    public async Task SixThousandSuccessfulManualRefreshesLeaveOnlyStartupMilestones()
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        CollectionAssert.AreEqual(new[] { 2100, 2103 },
            fixture.Logger.Recording.Entries.Select(entry => entry.EventId.Id).ToArray());
        ManualHeartbeatRegistration registration = fixture.Heartbeat.Registrations.Single();
        Assert.AreEqual(TimeSpan.FromHours(1), registration.Options.InitialDelay);
        Assert.AreEqual(TimeSpan.FromHours(1), registration.Options.Interval);
        fixture.ClearLogs();

        for (int index = 0; index < 6000; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
            AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);
        }

        Assert.AreEqual(6001L, fixture.Rest.TotalCalls);
        Assert.AreEqual(6001L, fixture.Monitor.Count(HealthReportKind.Published));
        Assert.AreEqual(0L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(0, fixture.Logger.WriteAttempts);
        Assert.AreEqual(0L, fixture.Logger.Recording.TotalCount);
        AssertAliases(fixture.Catalog);
    }

    [TestMethod]
    public async Task RepeatedFailuresReportImmediatelyThenAtExactlyOneMinuteWithSuppressedCount()
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        fixture.ClearLogs();

        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        for (int index = 1; index < 6000; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
            AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        }

        RecordedLog first = fixture.Logger.Recording.Entries.Single();
        AssertFailure(first, 0);
        Assert.AreEqual(6000L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        IReadOnlyList<RecordedLog> logs = fixture.Logger.Recording.Entries;
        Assert.AreEqual(2, logs.Count);
        AssertFailure(logs[1], 5999);
        Assert.AreEqual(6001L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(1L, fixture.Monitor.Count(HealthReportKind.Published));
        AssertAliases(fixture.Catalog);
    }

    [TestMethod]
    public async Task FlappingDoesNotReplenishIndependentBudgetsAndUtcChangesDoNotOpenThem()
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        fixture.ClearLogs();

        for (int index = 0; index < 3000; index++)
        {
            if (index != 0)
            {
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
            }
            AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
            AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);
        }

        Assert.AreEqual(2L, fixture.Logger.Recording.TotalCount);
        fixture.Clock.SetUtcNow(new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);
        fixture.Clock.SetUtcNow(new DateTimeOffset(2006, 1, 1, 0, 0, 0, TimeSpan.Zero));
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);
        Assert.AreEqual(2L, fixture.Logger.Recording.TotalCount);

        // Failure's first report was at 0ms; recovery's was at 10ms.
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);
        Assert.AreEqual(3L, fixture.Logger.Recording.TotalCount);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);

        IReadOnlyList<RecordedLog> logs = fixture.Logger.Recording.Entries;
        CollectionAssert.AreEqual(new[] { 2101, 2102, 2101, 2102 },
            logs.Select(entry => entry.EventId.Id).ToArray());
        AssertFailure(logs[0], 0);
        AssertRecovery(logs[1], 0);
        AssertFailure(logs[2], 3001);
        AssertRecovery(logs[3], 3002);
        Assert.AreEqual(3004L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(3005L, fixture.Monitor.Count(HealthReportKind.Published));
        AssertAliases(fixture.Catalog);
    }

    [TestMethod]
    public async Task DisabledLoggingSkipsReportingClockAndRetainsFirstEligibleVisibility()
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        fixture.ClearLogs();
        fixture.Logger.Recording.MinimumLevel = LogLevel.None;
        fixture.Clock.ThrowTimestamp = true;
        int timestampReads = fixture.Clock.TimestampReads;

        for (int index = 0; index < 100; index++)
        {
            AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        }
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);

        Assert.AreEqual(timestampReads, fixture.Clock.TimestampReads);
        Assert.AreEqual(0, fixture.Logger.WriteAttempts);
        Assert.AreEqual(0L, fixture.Logger.Recording.TotalCount);
        fixture.Clock.ThrowTimestamp = false;
        fixture.Logger.Recording.MinimumLevel = LogLevel.Trace;
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);

        IReadOnlyList<RecordedLog> logs = fixture.Logger.Recording.Entries;
        Assert.AreEqual(2, logs.Count);
        AssertFailure(logs[0], 100);
        AssertRecovery(logs[1], 1);
        Assert.AreEqual(101L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(3L, fixture.Monitor.Count(HealthReportKind.Published));
        AssertAliases(fixture.Catalog);
    }

    [TestMethod]
    [DataRow("timestamp")]
    [DataRow("frequency")]
    [DataRow("enabled")]
    [DataRow("write")]
    public async Task ReportingFailuresCannotChangeActualRefreshResultsOrPublicMonitorHandoff(string failure)
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);
        fixture.ClearLogs();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Clock.ThrowTimestamp = failure == "timestamp";
        fixture.Clock.ThrowFrequency = failure == "frequency";
        fixture.Logger.ThrowEnabled = failure == "enabled";
        fixture.Logger.ThrowWrite = failure == "write";

        AssertCallback(await fixture.FailAsync().WaitAsync(CountryDiagnosticFixture.Deadline), false);
        AssertCallback(await fixture.SucceedAsync().WaitAsync(CountryDiagnosticFixture.Deadline), true);

        Assert.AreEqual(0L, fixture.Logger.Recording.TotalCount);
        Assert.AreEqual(2L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(3L, fixture.Monitor.Count(HealthReportKind.Published));
        AssertAliases(fixture.Catalog);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnexpectedErrorsKeepOriginalIdentityAndUseTheSamePersistentFailureBudget(bool foreignCancellation)
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        fixture.ClearLogs();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        Exception original = foreignCancellation
            ? new OperationCanceledException("An unrelated operation ended.", unrelated.Token)
            : new InvalidOperationException("The provider extension failed.");

        for (int index = 0; index < 100; index++)
        {
            fixture.Rest.EnqueueException(original);
            ManualHeartbeatResult result = await fixture.Heartbeat.TriggerAsync(fixture.Catalog)
                .WaitAsync(CountryDiagnosticFixture.Deadline);
            Assert.AreEqual(ManualHeartbeatOutcome.Failed, result.Outcome);
            Assert.IsNull(result.CallbackResult);
            Assert.AreSame(original, result.Error);
            Assert.IsNull(result.ErrorHandlingError);
            Assert.IsNull(result.ReportingError);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        RecordedLog log = fixture.Logger.Recording.Entries.Single();
        Assert.AreEqual(2101, log.EventId.Id);
        Assert.AreEqual(LogLevel.Error, log.Level);
        Assert.AreSame(original, log.Exception);
        Assert.AreEqual("country.unexpected_error", log.Fields["Code"]);
        Assert.AreEqual(100L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(1L, fixture.Monitor.Count(HealthReportKind.Published));
        AssertAliases(fixture.Catalog);
    }

    [TestMethod]
    public async Task ApplicationStoppingReturnsFalseWithoutFailureOrRecoveryDiagnostics()
    {
        await using var fixture = new CountryDiagnosticFixture();
        await fixture.StartAsync();
        fixture.ClearLogs();
        PendingRestCall pending = fixture.Rest.EnqueuePendingJsonValue(CountryDiagnosticFixture.Malta);
        Task<bool> refresh = fixture.Catalog.OnElapsedAsync(CancellationToken.None);
        try
        {
            await pending.Entered.WaitAsync(CountryDiagnosticFixture.Deadline);
            fixture.Lifetime.StopApplication();
            Assert.IsFalse(await refresh.WaitAsync(CountryDiagnosticFixture.Deadline));
            Assert.AreEqual(0, fixture.Logger.WriteAttempts);
            Assert.AreEqual(0L, fixture.Logger.Recording.TotalCount);
            AssertAliases(fixture.Catalog);
        }
        finally
        {
            fixture.Lifetime.StopApplication();
            pending.Release();
            await refresh.WaitAsync(CountryDiagnosticFixture.Deadline);
        }
        // Invoke directly: the manual helper deliberately does not reproduce
        // Core's global-stopping health-neutrality policy.
        Assert.AreEqual(1L, fixture.Monitor.Count(HealthReportKind.Published));
        Assert.AreEqual(0L, fixture.Monitor.Count(HealthReportKind.RefreshFailure));
    }

    private static void AssertCallback(ManualHeartbeatResult result, bool expected)
    {
        Assert.AreEqual(expected ? ManualHeartbeatOutcome.Succeeded : ManualHeartbeatOutcome.Failed, result.Outcome);
        Assert.AreEqual((bool?)expected, result.CallbackResult);
        Assert.IsNull(result.Error);
        Assert.IsNull(result.ErrorHandlingError);
        Assert.IsNull(result.ReportingError);
    }

    private static void AssertFailure(RecordedLog log, long suppressed)
    {
        Assert.AreEqual(2101, log.EventId.Id);
        Assert.AreEqual("country.refresh.failed", log.EventId.Name);
        Assert.AreEqual(LogLevel.Warning, log.Level);
        Assert.IsNull(log.Exception);
        Assert.AreEqual("country.provider_rejected", log.Fields["Code"]);
        Assert.AreEqual(suppressed, Convert.ToInt64(log.Fields["SuppressedFailures"], CultureInfo.InvariantCulture));
    }

    private static void AssertRecovery(RecordedLog log, long suppressed)
    {
        Assert.AreEqual(2102, log.EventId.Id);
        Assert.AreEqual("country.refresh.recovered", log.EventId.Name);
        Assert.AreEqual(LogLevel.Information, log.Level);
        Assert.IsNull(log.Exception);
        Assert.AreEqual(suppressed, Convert.ToInt64(log.Fields["SuppressedRecoveries"], CultureInfo.InvariantCulture));
    }

    private static void AssertAliases(CountryCatalog catalog)
    {
        Assert.IsTrue(catalog.TryGet("Malta", out Country? name));
        Assert.IsTrue(catalog.TryGet("MT", out Country? iso2));
        Assert.IsTrue(catalog.TryGet("MLT", out Country? iso3));
        Assert.AreEqual(new Country("Malta", "MT", "MLT"), name);
        Assert.AreSame(name, iso2);
        Assert.AreSame(name, iso3);
    }
}
