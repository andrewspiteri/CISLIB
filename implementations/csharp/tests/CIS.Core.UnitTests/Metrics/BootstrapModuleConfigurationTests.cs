using System.Diagnostics.Metrics;
using CIS.Configuration;
using CIS.Metrics;
using CIS.Tests.UnitTests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class BootstrapModuleConfigurationTests
{
    [TestMethod]
    public async Task PreparedSnapshotsExposeKnownFactsWithoutReplayingInitialLoadsAndRealRefreshesKeepTheirValuesAndEvents()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var refresh = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await refresh.LoadAsync(CancellationToken.None);
        refresh.AttachMetrics(metrics);
        refresh.AttachMetrics(metrics);
        listener.Collect();
        Assert.IsEmpty(listener.Points("cis.configuration.operation.duration"));
        Assert.AreEqual(1d, listener.Points("cis.configuration.prepared").Single().Value);
        Assert.IsTrue((double)listener.Points("cis.configuration.initialization.duration").Single().Value >= 0);
        Assert.AreEqual(refresh.CaptureStatus().Entries.Single().LastSuccessfulReconciliationUtc!.Value.ToUnixTimeMilliseconds() / 1_000d,
            listener.Points("cis.configuration.last.success").Single().Value);
        MetricsPoint valid = listener.Points("cis.configuration.entries").Single(point => point.Tags.Single().Value!.Equals("valid"));
        Assert.AreEqual(1d, valid.Value);
        int changes = 0;
        entry.Instance.ConfigurationChanged += (_, _) => changes++;
        store.Set(entry, 8, "c8");
        Assert.IsTrue(await refresh.ReconcileAsync(entry).WaitAsync(MetricsTestSupport.Deadline));
        Assert.AreEqual(8, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(1, changes);
        MetricsTestSupport.AssertTags(listener.Points("cis.configuration.operation.duration").Single(),
            new("operation", "refresh"), new("outcome", "success"));
    }

    [TestMethod]
    public async Task FailedRefreshDoesNotReplaceLiveValuesAndItsLaterRecoveryUsesTheSameBinding()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var refresh = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await refresh.LoadAsync(CancellationToken.None);
        refresh.AttachMetrics(metrics);
        store.Read = (_, _, _) => ValueTask.FromResult(ConfigurationReadResult.Failure());
        Assert.IsFalse(await refresh.ReconcileAsync(entry));
        Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
        listener.Collect();
        Assert.AreEqual(1d, listener.Points("cis.configuration.entries").Single(point => point.Tags.Single().Value!.Equals("invalid")).Value);
        store.Read = null;
        store.Set(entry, 9, "c9");
        Assert.IsTrue(await refresh.ReconcileAsync(entry));
        Assert.AreEqual(9, ((RefreshSettings)entry.Instance).Value);
        CollectionAssert.AreEqual(new[] { "failed", "success" }, listener.Points("cis.configuration.operation.duration")
            .Select(point => (string)point.Tags.Single(tag => tag.Key == "outcome").Value!).ToArray());
    }

    [TestMethod]
    public async Task UnknownInitialDurationIsOmittedAndAttachmentDuringTheInitialReadDoesNotCreateAnEvent()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var entered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        var store = new ConfigurationRefreshStore();
        store.Read = async (_, _, _) => { entered.TrySetResult(); await release.Task; return ConfigurationReadResult.Missing(); };
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        await using var refresh = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        try
        {
            Task loading = refresh.LoadAsync(CancellationToken.None);
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            refresh.AttachMetrics(metrics);
            listener.Collect();
            Assert.IsEmpty(listener.Points("cis.configuration.initialization.duration"));
            Assert.IsEmpty(listener.Points("cis.configuration.last.success"));
            Assert.IsEmpty(listener.Points("cis.configuration.entries"));
            Assert.AreEqual(0d, listener.Points("cis.configuration.prepared").Single().Value);
            release.TrySetResult();
            await loading.WaitAsync(MetricsTestSupport.Deadline);
            Assert.IsEmpty(listener.Points("cis.configuration.operation.duration"));
            listener.Collect();
            Assert.AreEqual(1, listener.Points("cis.configuration.initialization.duration").Length);
        }
        finally { release.TrySetResult(); await refresh.StopAsync().WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    public async Task AnAcceptedRefreshDurationIncludesSynchronousNotificationSettlementAndStopStillJoinsIt()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var refresh = new ConfigurationRefreshCoordinator(catalog, new ConfigurationValuePipeline());
        await refresh.LoadAsync(CancellationToken.None);
        refresh.AttachMetrics(metrics);
        var entered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        entry.Instance.ConfigurationChanged += (_, _) => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); };
        try
        {
            store.Set(entry, 4, "c4");
            Task<bool> changed = Task.Run(() => refresh.ReconcileAsync(entry));
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            Assert.IsEmpty(listener.Points("cis.configuration.operation.duration"));
            Task stop = refresh.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            release.TrySetResult();
            Assert.IsTrue(await changed.WaitAsync(MetricsTestSupport.Deadline));
            await stop.WaitAsync(MetricsTestSupport.Deadline);
            MetricsTestSupport.AssertTags(listener.Points("cis.configuration.operation.duration").Single(),
                new("operation", "refresh"), new("outcome", "success"));
        }
        finally { release.TrySetResult(); await refresh.StopAsync().WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    public async Task ActiveInitialFailureIsObservedWithoutTurningItIntoPreparedDataOrAllowingAnotherHostClaim()
    {
        using var meter = new Meter("CIS.Metrics");
        using var otherMeter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var other = new MetricsRuntime(otherMeter);
        var store = new ConfigurationRefreshStore { Read = (_, _, _) => ValueTask.FromResult(ConfigurationReadResult.Failure()) };
        await using var refresh = new ConfigurationRefreshCoordinator(ConfigurationRefreshTestSupport.Catalog(store), new ConfigurationValuePipeline());
        refresh.AttachMetrics(metrics);
        Assert.ThrowsExactly<InvalidOperationException>(() => refresh.AttachMetrics(other));
        Assert.IsInstanceOfType<CoreConfigurationException>(await ConfigurationRefreshTestSupport.FailureAsync(refresh.LoadAsync(CancellationToken.None)));
        MetricsTestSupport.AssertTags(listener.Points("cis.configuration.operation.duration").Single(),
            new("operation", "initial_load"), new("outcome", "failed"));
        listener.Collect();
        Assert.AreEqual(0d, listener.Points("cis.configuration.prepared").Single().Value);
        Assert.IsEmpty(listener.Points("cis.configuration.entries"));
    }
}
