using CIS.CommonTesting;
using CIS.Examples.CountryData;
using CIS.RestClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CountryData;

[TestClass]
public sealed class CountryCatalogUnitExamples
{
    private const string ReplacementData = """
        {"error":false,"data":[{"name":"Japan","Iso2":"jp","Iso3":"jpn"}]}
        """;

    [TestMethod]
    [DataRow("MT")]
    [DataRow(" mLt ")]
    [DataRow("malta")]
    public async Task InitialLoadPublishesEquivalentAliasesWithoutLookupRequests(string identifier)
    {
        await using var example = new CountryCatalogExample();
        Assert.IsFalse(example.Catalog.TryGet("MT", out _));

        await example.StartAsync();
        // Keep a result available so an accidental logical REST call would be counted.
        example.Rest.EnqueueJsonValue(CountryCatalogExample.InitialData);
        bool found = example.Catalog.TryGet(identifier, out Country? actual);

        Assert.IsTrue(found);
        Assert.IsNotNull(actual);
        Assert.AreEqual(new Country("Malta", "MT", "MLT"), actual);
        Assert.IsTrue(example.Catalog.TryGet("MT", out Country? published));
        Assert.AreSame(published, actual);
        Assert.IsFalse(example.Catalog.TryGet("unknown", out _));
        Assert.AreEqual(1L, example.Rest.TotalCalls, "Lookups and misses read the published snapshot.");
        Assert.AreEqual(1L, example.Monitor.Count(HealthReportKind.Published));
    }

    [TestMethod]
    [DataRow(60)]
    [DataRow(5)]
    public async Task RefreshUsesTheConfiguredRegistrationAndExplicitManualTrigger(int intervalMinutes)
    {
        CountrySettings? settings = intervalMinutes == 60 ? null : new CountrySettings
        {
            SourceUrl = "https://provider.invalid/countries",
            RefreshInterval = TimeSpan.FromMinutes(intervalMinutes),
            RetryPolicyName = null
        };
        await using var example = new CountryCatalogExample(settings);
        await example.StartAsync();

        ManualHeartbeatRegistration registration = example.Heartbeat.Registrations.Single();
        TimeSpan expectedInterval = TimeSpan.FromMinutes(intervalMinutes);
        Assert.AreSame(example.Catalog, registration.Subscriber);
        Assert.AreEqual(expectedInterval, registration.Options.InitialDelay);
        Assert.AreEqual(expectedInterval, registration.Options.Interval);
        Assert.AreSame(example.Monitor, registration.Monitor);
        Assert.IsTrue(registration.Monitoring!.SuccessPublishesUsableData);

        // The clock does not dispatch ManualHeartbeat; tests invoke and await it explicitly.
        example.Clock.Advance(expectedInterval);
        Assert.AreEqual(1L, example.Rest.TotalCalls);
        example.Rest.EnqueueJsonValue(CountryCatalogExample.InitialData);
        ManualHeartbeatResult refreshed = await example.Heartbeat.TriggerAsync(example.Catalog);

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, refreshed.Outcome);
        Assert.AreEqual(true, refreshed.CallbackResult);
        Assert.IsNull(refreshed.Error);
        Assert.AreEqual(2L, example.Rest.TotalCalls);
        Assert.AreEqual(2L, example.Monitor.Count(HealthReportKind.Published));
        Assert.AreEqual(2L, example.Logger.TotalCount, "A routine refresh adds no normal startup milestone.");
    }

    [TestMethod]
    public async Task SuccessfulRefreshReplacesTheWholeDatasetIncludingRemovedAliases()
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        example.Rest.EnqueueJsonValue(ReplacementData);

        ManualHeartbeatResult refreshed = await example.Heartbeat.TriggerAsync(example.Catalog);

        Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, refreshed.Outcome);
        Assert.IsTrue(example.Catalog.TryGet(" JPN ", out Country? actual));
        Assert.AreEqual(new Country("Japan", "JP", "JPN"), actual);
        Assert.IsFalse(example.Catalog.TryGet("MT", out _));
        Assert.IsFalse(example.Catalog.TryGet("MLT", out _));
        Assert.IsFalse(example.Catalog.TryGet("Malta", out _));
        Assert.AreEqual(2L, example.Rest.TotalCalls);
        Assert.AreEqual(2L, example.Monitor.Count(HealthReportKind.Published));
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("business")]
    [DataRow("invalid-code")]
    [DataRow("duplicate")]
    [DataRow("empty")]
    public async Task FailedRefreshKeepsTheSameLastGoodRecord(string failure)
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        Assert.IsTrue(example.Catalog.TryGet("MT", out Country? published));
        QueueFailure(example.Rest, failure);

        ManualHeartbeatResult refreshed = await example.Heartbeat.TriggerAsync(example.Catalog);

        Assert.AreEqual(ManualHeartbeatOutcome.Failed, refreshed.Outcome);
        Assert.AreEqual(false, refreshed.CallbackResult);
        Assert.IsNull(refreshed.Error, "A logical or invalid-dataset failure is not an unexpected exception.");
        Assert.IsTrue(example.Catalog.TryGet("MLT", out Country? actual));
        Assert.AreSame(published, actual);
        Assert.IsFalse(example.Catalog.TryGet("JPN", out _), "No partial replacement may be published.");
        Assert.AreEqual(1L, example.Monitor.Count(HealthReportKind.Published));
        Assert.AreEqual(1L, example.Monitor.Count(HealthReportKind.RefreshFailure));
        Assert.AreEqual(2L, example.Rest.TotalCalls);
        Assert.AreEqual(LogLevel.Warning, example.Logger.Entries.Single(entry => entry.EventId.Id == 2101).Level);
    }

    [TestMethod]
    public async Task InitialLogicalFailureDoesNotCreateARefreshRegistration()
    {
        await using var example = new CountryCatalogExample();
        example.Rest.EnqueueFailure(RestFailureKind.Http, "provider.unavailable", "Temporarily unavailable.", 503);

        await Assert.ThrowsAsync<InvalidOperationException>(() => example.Catalog.StartAsync(CancellationToken.None));

        Assert.IsFalse(example.Catalog.TryGet("MT", out _));
        Assert.IsEmpty(example.Heartbeat.Registrations);
        Assert.AreEqual(0L, example.Monitor.Count(HealthReportKind.Published));
        Assert.AreEqual(1L, example.Rest.TotalCalls);
    }

    [TestMethod]
    public async Task UnexpectedRefreshFaultRetainsItsIdentityAndLastGoodData()
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        Assert.IsTrue(example.Catalog.TryGet("MT", out Country? published));
        example.Logger.Clear();
        var original = new InvalidOperationException("The configured test dependency failed.");
        example.Rest.EnqueueException(original);

        ManualHeartbeatResult refreshed = await example.Heartbeat.TriggerAsync(example.Catalog);

        Assert.AreEqual(ManualHeartbeatOutcome.Failed, refreshed.Outcome);
        Assert.AreSame(original, refreshed.Error);
        Assert.IsNull(refreshed.ErrorHandlingError);
        Assert.IsNull(refreshed.ReportingError);
        RecordedLog log = example.Logger.Entries.Single();
        Assert.AreEqual(LogLevel.Error, log.Level);
        Assert.AreSame(original, log.Exception);
        Assert.IsTrue(example.Catalog.TryGet("Malta", out Country? actual));
        Assert.AreSame(published, actual);
        Assert.AreEqual(1L, example.Monitor.Count(HealthReportKind.RefreshFailure));
    }

    [TestMethod]
    public async Task DisposalJoinsAHeldRefreshBeforeWithdrawingItsMonitor()
    {
        await using var example = new CountryCatalogExample();
        await example.StartAsync();
        Assert.IsTrue(example.Catalog.TryGet("MT", out Country? published));
        PendingRestCall pending = example.Rest.EnqueuePendingJsonValue(ReplacementData);
        Task<ManualHeartbeatResult> refreshing = example.Heartbeat.TriggerAsync(example.Catalog);
        Task? closing = null;

        try
        {
            await pending.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(refreshing.IsCompleted);
            Assert.IsTrue(example.Catalog.TryGet("MT", out Country? actual));
            Assert.AreSame(published, actual);
            Assert.IsFalse(example.Catalog.TryGet("JPN", out _));

            closing = example.Catalog.DisposeAsync().AsTask();
            Assert.IsFalse(closing.IsCompleted, "Disposal must await the refresh that already entered.");
            Assert.IsFalse(example.Monitor.IsWithdrawn);

            pending.Release();
            ManualHeartbeatResult refreshed = await refreshing.WaitAsync(TimeSpan.FromSeconds(10));
            await closing.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, refreshed.Outcome);
            Assert.IsTrue(example.Catalog.TryGet("JPN", out _));
            Assert.IsTrue(example.Monitor.IsWithdrawn);
            Assert.IsEmpty(example.Heartbeat.Registrations);
        }
        finally
        {
            // A failed assertion still releases and joins actual work before the helper can close.
            pending.Release();
            await refreshing;
            if (closing is not null)
            {
                await closing;
            }
        }
    }

    private static void QueueFailure(ScriptedRestClient rest, string failure)
    {
        switch (failure)
        {
            case "http":
                rest.EnqueueFailure(RestFailureKind.Http, "provider.unavailable", "Temporarily unavailable.", 503);
                break;
            case "business":
                rest.EnqueueFailure(RestFailureKind.Business, "country.provider_rejected", "The provider rejected this read.", 200);
                break;
            case "invalid-code":
                rest.EnqueueJsonValue("""{"error":false,"data":[{"name":"Japan","Iso2":"J","Iso3":"JPN"}]}""");
                break;
            case "duplicate":
                rest.EnqueueJsonValue("""
                    {"error":false,"data":[{"name":"Japan","Iso2":"JP","Iso3":"JPN"},{"name":"Japan","Iso2":"JP","Iso3":"JPN"}]}
                    """);
                break;
            case "empty":
                rest.EnqueueJsonValue("""{"error":false,"data":[]}""");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }
    }
}
