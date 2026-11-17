using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationRefreshCoordinationTests
{
    [TestMethod]
    [DataRow(ConfigurationNotificationMode.None)]
    [DataRow(ConfigurationNotificationMode.Reliable)]
    [DataRow(ConfigurationNotificationMode.RequiresReconciliation)]
    public async Task ActivationDirectlyReconcilesTheBootstrapGapInEveryModeWithoutWaitingForAPeriodicCallback(ConfigurationNotificationMode mode)
    {
        var store = new ConfigurationRefreshStore { NotificationMode = mode };
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        store.Set(entry, 6, "c6");
        int changes = 0;
        entry.Instance.ConfigurationChanged += (_, _) => changes++;
        await coordinator.ActivateAsync(CancellationToken.None).WaitAsync(ConfigurationRefreshTestSupport.Deadline);
        Assert.AreEqual(6, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(mode == ConfigurationNotificationMode.None ? 0 : 1, store.Watches);
        Assert.IsTrue(coordinator.CaptureStatus().ActivationCompleted);
    }

    [TestMethod]
    public async Task AnIdleWatchInvalidationStartsTrackedReconciliationWithoutAPollingCall()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        await coordinator.ActivateAsync(CancellationToken.None);
        var observed = ConfigurationRefreshTestSupport.Signal();
        coordinator.Observer = status =>
        {
            if (status.Entries.Single().Revision == 1) { observed.TrySetResult(); }
        };
        store.Set(entry, 8, "c8");
        store.Signal(); // A reconnect/scope invalidation requests a reread of the owned keys.
        await observed.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
        Assert.AreEqual(8, catalog.Reader.Read((RefreshSettings)entry.Instance, current => current.Value));
        await coordinator.StopAsync().WaitAsync(ConfigurationRefreshTestSupport.Deadline);
        Assert.AreEqual(1, store.SubscriptionDisposals);
    }

    [TestMethod]
    public async Task ConcurrentRequestsShareActualWorkAndCoalesceSignalsThroughOrderedPublication()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 1, "c1");
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            await coordinator.ActivateAsync(CancellationToken.None);
            int before = store.Reads;
            var changes = new List<(long Revision, int Value)>();
            entry.Instance.ConfigurationChanged += (_, change) => changes.Add((change.Revision,
                catalog.Reader.Read((RefreshSettings)entry.Instance, value => value.Value)));
            store.Set(entry, 2, "c2");
            bool hold = true;
            store.Read = async (key, token, _) =>
            {
                ConfigurationReadResult result = store.Current(key, token);
                if (key.Scope == "CommonConfiguration" && hold)
                {
                    hold = false;
                    entered.TrySetResult();
                    await release.Task;
                }
                return result;
            };
            Task<bool> first = coordinator.ReconcileAsync(entry);
            await entered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            store.Set(entry, 3, "c3");
            Task<bool> joined = coordinator.ReconcileAsync(entry);
            for (int index = 0; index < 8; index++) { store.Signal(entry); }
            Assert.AreSame(first, joined);
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
            release.TrySetResult();
            Assert.IsTrue(await first.WaitAsync(ConfigurationRefreshTestSupport.Deadline));
            CollectionAssert.AreEqual(new[] { (1L, 2), (2L, 3) }, changes);
            Assert.AreEqual(4, store.Reads - before, "Signals coalesce into one further read of each scope.");
            Assert.AreEqual(1, store.MaximumConcurrentReads, "A later local read cannot race and overwrite the first attempt.");
            Assert.AreEqual(2L, coordinator.CaptureStatus().Entries.Single().Revision);
        }
        finally { release.TrySetResult(); await coordinator.DisposeAsync(); }
    }

    [TestMethod]
    public async Task WatchActivationReconcilesInvalidationsDuringSubscriptionAndDuringItsRead()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 1, "c1");
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            store.Watch = _ =>
            {
                store.Set(entry, 2, "c2");
                store.Signal(entry);
                return ValueTask.FromResult(store.Subscription());
            };
            bool hold = true;
            store.Read = async (key, token, _) =>
            {
                ConfigurationReadResult current = store.Current(key, token);
                if (key.Scope == "CommonConfiguration" && hold)
                { hold = false; entered.TrySetResult(); await release.Task; }
                return current;
            };
            var changes = new List<int>();
            entry.Instance.ConfigurationChanged += (_, _) => changes.Add(((RefreshSettings)entry.Instance).Value);
            Task activation = coordinator.ActivateAsync(CancellationToken.None);
            await entered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.IsFalse(activation.IsCompleted);
            store.Set(entry, 3, "c3");
            store.Signal(entry, "CommonConfiguration");
            release.TrySetResult();
            await activation.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { 2, 3 }, changes);
            Assert.AreSame(activation, coordinator.ActivateAsync(new CancellationToken(canceled: true)));
            Assert.AreEqual(1, store.Watches);
            Assert.AreEqual(3, ((RefreshSettings)entry.Instance).Value);
            Assert.IsTrue(coordinator.CaptureStatus().ActivationCompleted);
            Assert.AreEqual(ConfigurationWatchState.Active, coordinator.CaptureStatus().WatchState);
        }
        finally { release.TrySetResult(); await coordinator.DisposeAsync(); }
        Assert.AreEqual(1, store.SubscriptionDisposals);
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    public async Task AHandlerCanReadAndSignalAnotherReconciliationWithoutRecursivePublication()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        store.Set(entry, 1, "c1");
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        await coordinator.ActivateAsync(CancellationToken.None);
        int depth = 0, maximumDepth = 0;
        var values = new List<int>();
        entry.Instance.ConfigurationChanged += (_, _) =>
        {
            depth++;
            maximumDepth = Math.Max(maximumDepth, depth);
            int value = catalog.Reader.Read((RefreshSettings)entry.Instance, current => current.Value);
            values.Add(value);
            if (value == 2) { store.Set(entry, 3, "c3"); store.Signal(entry); }
            depth--;
        };
        store.Set(entry, 2, "c2");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry).WaitAsync(ConfigurationRefreshTestSupport.Deadline));
        CollectionAssert.AreEqual(new[] { 2, 3 }, values);
        Assert.AreEqual(1, maximumDepth);
    }

    [TestMethod]
    public async Task NotificationFailureIsSafeSeparateFromValidityAndResetsOnlyOnAnotherAcceptedPublication()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        EventHandler<ConfigurationChangedEventArgs> broken = (_, _) => throw new InvalidOperationException("PRIVATE subscriber graph");
        entry.Instance.ConfigurationChanged += broken;
        int later = 0;
        entry.Instance.ConfigurationChanged += (_, _) => later++;
        store.Set(entry, 2, "c2");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        ConfigurationEntryRefreshStatus status = coordinator.CaptureStatus().Entries.Single();
        Assert.IsTrue(status.IsValid);
        Assert.IsNull(status.FailureCode);
        Assert.AreEqual("Configuration.Notification", status.NotificationFailureCode);
        Assert.AreEqual(1, later);
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.AreEqual("Configuration.Notification", coordinator.CaptureStatus().Entries.Single().NotificationFailureCode);
        entry.Instance.ConfigurationChanged -= broken;
        store.Set(entry, 3, "c3");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.IsNull(coordinator.CaptureStatus().Entries.Single().NotificationFailureCode);
        Assert.AreEqual(2, later);
    }

    [TestMethod]
    public async Task CancellationAfterCommitCannotSuppressItsAcceptedNotification()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        var pipeline = new ConfigurationRefreshPipeline();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, pipeline);
        await coordinator.LoadAsync(CancellationToken.None);
        var changes = new List<(long Revision, int Value)>();
        entry.Instance.ConfigurationChanged += (_, change) => changes.Add((change.Revision, ((RefreshSettings)entry.Instance).Value));
        pipeline.AfterAcceptedCommit = coordinator.BeginStopping;
        store.Set(entry, 2, "c2");
        Assert.IsTrue(await coordinator.ReconcileAsync(entry));
        Assert.IsTrue(coordinator.IsStopping);
        CollectionAssert.AreEqual(new[] { (1L, 2) }, changes);
        Assert.IsNull(coordinator.CaptureStatus().Entries.Single().NotificationFailureCode);
        await coordinator.StopAsync().WaitAsync(ConfigurationRefreshTestSupport.Deadline);
        Assert.AreEqual(2, ((RefreshSettings)entry.Instance).Value);
        Assert.AreEqual(1L, coordinator.CaptureStatus().Entries.Single().Revision);
    }

    [TestMethod]
    public async Task TerminalCommitPoisonStopsOnlyThatEntryAndDoesNotTurnIntoARetryLoop()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store, typeof(RefreshSettings), typeof(OtherRefreshSettings));
        var pipeline = new ConfigurationRefreshPipeline();
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, pipeline);
        await coordinator.LoadAsync(CancellationToken.None);
        ConfigurationEntry broken = catalog.Entries.Single(entry => entry.Type == typeof(RefreshSettings));
        ConfigurationEntry healthy = catalog.Entries.Single(entry => entry.Type == typeof(OtherRefreshSettings));
        pipeline.BeforeApply = entry =>
        {
            if (entry == broken) { entry.Disabled = true; throw new InvalidOperationException("PRIVATE broken setter"); }
        };
        store.Set(broken, 2, "b2");
        store.Set(healthy, 4, "h4");
        Assert.IsFalse(await coordinator.ReconcileAsync(broken));
        int before = store.Reads;
        Assert.IsFalse(await coordinator.ReconcileAsync(broken));
        Assert.AreEqual(before, store.Reads);
        Assert.IsTrue(await coordinator.ReconcileAsync(healthy));
        Assert.AreEqual(4, ((OtherRefreshSettings)healthy.Instance).Value);
        ConfigurationEntryRefreshStatus status = coordinator.CaptureStatus().Entries.Single(entry => entry.ConfigurationType == broken.Type);
        Assert.IsTrue(status.Disabled);
        Assert.AreEqual("Configuration.CommitDisabled", status.FailureCode);
    }

    [TestMethod]
    public async Task EmptyCatalogDoesNotTouchTheProviderAndNeedsNoWatch()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(Array.Empty<Type>(), new ConfigurationOptions("EmptyHost", store));
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        await coordinator.ActivateAsync(CancellationToken.None);
        Assert.IsTrue(await coordinator.ReconcileAsync());
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Watches);
        Assert.AreEqual(ConfigurationWatchState.NotRequired, coordinator.CaptureStatus().WatchState);
    }
}
