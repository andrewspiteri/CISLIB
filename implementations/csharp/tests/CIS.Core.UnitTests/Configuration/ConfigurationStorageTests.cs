using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationStorageTests
{
    [TestMethod]
    public async Task MaskedCommonChangeIsRetainedWithoutAnEventAndAppearsWhenTheOverrideIsRemoved()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 1, "c1");
        store.Set(entry, 10, "a1", application: true);
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        int changes = 0;
        entry.Instance.ConfigurationChanged += (_, _) => changes++;
        store.Set(entry, 2, "c2");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(10, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0, changes);
        Assert.AreEqual(0L, coordinator.CaptureStatus().Entries.Single().Revision);
        store.Records[new("RefreshHost", entry.Identity)] = ConfigurationReadResult.Missing("a2");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual("c2", store.Requests.Last(request => request.Key.Scope == "CommonConfiguration").Token);
        Assert.AreEqual(2, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public async Task InitialLoadIsRetainedSequentialAndDoesNotStartObservationOrPublishAnEvent()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 4, "c1");
        store.Set(entry, 9, "a1", application: true);
        int changes = 0;
        entry.Instance.ConfigurationChanged += (_, _) => changes++;
        var pipeline = new ConfigurationRefreshPipeline();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, pipeline);
        Task first = coordinator.LoadAsync(CancellationToken.None);
        await first.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
        Assert.AreSame(first, coordinator.LoadAsync(new CancellationToken(canceled: true)));
        Assert.AreEqual(9, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0, changes);
        Assert.AreEqual(2, store.Reads);
        Assert.AreEqual(0, store.Watches);
        Assert.AreEqual(1, pipeline.Prepares);
        ConfigurationEntryRefreshStatus status = coordinator.CaptureStatus().Entries.Single();
        Assert.IsTrue(status.IsValid);
        Assert.AreEqual(0L, status.Revision);
        Assert.IsNotNull(status.LastSuccessfulReconciliationUtc);
        Assert.IsNull(status.FailureCode);
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    public async Task FailedReadDoesNotDeleteAnOverrideAndTheOtherSuccessfulSourceTokenIsRetained()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 1, "c1");
        store.Set(entry, 10, "a1", application: true);
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        int changes = 0;
        entry.Instance.ConfigurationChanged += (_, _) => changes++;
        store.Set(entry, 2, "c2");
        bool failApplication = true;
        store.Read = (key, token, _) => ValueTask.FromResult(key.Scope == "RefreshHost" && failApplication
            ? ConfigurationReadResult.Failure() : store.Current(key, token));
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(10, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0, changes);
        Assert.AreEqual("Configuration.StoreRead", coordinator.CaptureStatus().Entries.Single().FailureCode);

        failApplication = false;
        store.Records[new("RefreshHost", entry.Identity)] = ConfigurationReadResult.Missing("a2");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(2, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual("c2", store.Requests.Last(request => request.Key.Scope == "CommonConfiguration").Token);
        Assert.AreEqual(1, changes);
        Assert.IsNull(coordinator.CaptureStatus().Entries.Single().FailureCode);
    }

    [TestMethod]
    public async Task InvalidCachedPayloadRemainsInvalidWhenConditionalReadsReturnUnchanged()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 1, "c1");
        var pipeline = new ConfigurationRefreshPipeline();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, pipeline);
        await coordinator.LoadAsync(CancellationToken.None);
        int changes = 0;
        entry.Instance.ConfigurationChanged += (_, _) => changes++;
        store.Records[new("CommonConfiguration", entry.Identity)] = ConfigurationRefreshTestSupport.Json("{\"invalid\":true}", "c2");
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        int prepared = pipeline.Prepares;
        Assert.IsFalse(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(prepared + 1, pipeline.Prepares, "An Unchanged invalid source must be validated again, not silently accepted.");
        Assert.AreEqual("c2", store.Requests.Last(request => request.Key.Scope == "CommonConfiguration").Token);
        Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(0, changes);
        Assert.IsFalse(coordinator.CaptureStatus().Entries.Single().IsValid);
        store.Set(entry, 3, "c3");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual(3, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(1, changes);
        Assert.IsTrue(coordinator.CaptureStatus().Entries.Single().IsValid);
    }

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("failure")]
    [DataRow("throw")]
    [DataRow("unexpected-cancellation")]
    public async Task InitialReadFailureIsSafeRetainedAndNeverEquivalentToAbsence(string kind)
    {
        var store = new ConfigurationRefreshStore();
        store.Read = (key, _, _) => key.Scope != "CommonConfiguration" ? ValueTask.FromResult(ConfigurationReadResult.Missing())
            : kind switch
            {
                "unchanged" => ValueTask.FromResult(ConfigurationReadResult.Unchanged()),
                "failure" => ValueTask.FromResult(ConfigurationReadResult.Failure()),
                "unexpected-cancellation" => ValueTask.FromException<ConfigurationReadResult>(new OperationCanceledException("PRIVATE token")),
                _ => ValueTask.FromException<ConfigurationReadResult>(new InvalidOperationException("PRIVATE credentials"))
            };
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        var pipeline = new ConfigurationRefreshPipeline();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, pipeline);
        Task load = coordinator.LoadAsync(CancellationToken.None);
        Exception failure = await ConfigurationRefreshTestSupport.FailureAsync(load);
        Assert.IsInstanceOfType<CoreConfigurationException>(failure);
        var error = (CoreConfigurationException)failure;
        Assert.AreEqual(kind == "unchanged" ? "Configuration.UnchangedWithoutCache" : "Configuration.StoreRead", error.Issues.Single().Code);
        Assert.IsFalse(error.ToString().Contains("PRIVATE", StringComparison.Ordinal));
        Assert.AreSame(load, coordinator.LoadAsync(CancellationToken.None));
        Assert.AreEqual(2, store.Reads, "The other source is read and retained even when one read fails.");
        Assert.AreEqual(0, pipeline.Prepares);
        Assert.AreEqual(0, store.Watches);
    }
}
