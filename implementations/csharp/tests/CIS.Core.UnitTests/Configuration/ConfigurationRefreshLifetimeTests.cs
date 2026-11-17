using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationRefreshLifetimeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopDetachesWatchBeforeDrainingAnIgnoringReadAndNeverCommitsItsLateResult(bool duringActivation)
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        CancellationToken providerToken = default;
        Task? active = null;
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            if (!duringActivation) { await coordinator.ActivateAsync(CancellationToken.None); }
            int changes = 0;
            entry.Instance.ConfigurationChanged += (_, _) => changes++;
            store.Set(entry, 2, "c2");
            store.Read = async (key, token, cancellation) =>
            {
                if (key.Scope == "CommonConfiguration")
                { providerToken = cancellation; entered.TrySetResult(); await release.Task; }
                return store.Current(key, token);
            };
            active = duringActivation ? coordinator.ActivateAsync(CancellationToken.None) : coordinator.ReconcileAsync(entry);
            await entered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Task stop = coordinator.StopAsync();
            await store.SubscriptionDisposalEntered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.IsTrue(providerToken.IsCancellationRequested);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(active.IsCompleted);
            Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
            release.TrySetResult();
            if (duringActivation) { Assert.IsInstanceOfType<OperationCanceledException>(await ConfigurationRefreshTestSupport.FailureAsync(active)); }
            else { Assert.IsFalse(await (Task<bool>)active); }
            await stop.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.AreEqual(0, changes);
            Assert.AreEqual(1, ((RefreshSettings)entry.Instance).Value);
            Assert.AreEqual(1, store.SubscriptionDisposals);
            Assert.AreEqual(0, store.Disposals);
        }
        finally
        {
            release.TrySetResult();
            await coordinator.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task StopJoinsLateWatchAcquisitionAndDisposesItsHandleWithoutStartingReconciliation()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var release = ConfigurationRefreshTestSupport.Signal();
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            int before = store.Reads;
            store.Watch = async _ => { await release.Task; return store.Subscription(); };
            Task activation = coordinator.ActivateAsync(CancellationToken.None);
            await store.WatchEntered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Task stop = coordinator.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            Assert.AreEqual(0, store.SubscriptionDisposals);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await ConfigurationRefreshTestSupport.FailureAsync(activation));
            await stop.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.AreEqual(1, store.SubscriptionDisposals);
            Assert.AreEqual(before, store.Reads);
        }
        finally { release.TrySetResult(); await coordinator.DisposeAsync(); }
    }

    [TestMethod]
    public async Task WatchDisposalFailureIsSafeAndCannotSkipTheRemainingActualReadDrain()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            await coordinator.ActivateAsync(CancellationToken.None);
            store.DisposeSubscription = () => ValueTask.FromException(new InvalidOperationException("PRIVATE subscription failure"));
            store.Read = async (key, token, _) =>
            {
                if (key.Scope == "CommonConfiguration") { entered.TrySetResult(); await release.Task; }
                return store.Current(key, token);
            };
            Task<bool> active = coordinator.ReconcileAsync(entry);
            await entered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Task stop = coordinator.StopAsync();
            await store.SubscriptionDisposalEntered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            release.TrySetResult();
            Assert.IsFalse(await active.WaitAsync(ConfigurationRefreshTestSupport.Deadline));
            Exception failure = await ConfigurationRefreshTestSupport.FailureAsync(stop);
            Assert.IsInstanceOfType<CoreConfigurationException>(failure);
            Assert.IsFalse(failure.ToString().Contains("PRIVATE", StringComparison.Ordinal));
            Assert.AreEqual("Configuration.WatchDisposal", coordinator.CaptureStatus().WatchFailureCode);
            Assert.AreSame(stop, coordinator.StopAsync());
        }
        finally
        {
            release.TrySetResult();
            try { await coordinator.DisposeAsync(); }
            catch (CoreConfigurationException) { /* Cleanup failure is asserted after real work settles. */ }
        }
    }

    [TestMethod]
    public async Task BeginStoppingDoesNotBlockOnAProviderCancellationCallbackButStopJoinsThatCallback()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var callbackEntered = ConfigurationRefreshTestSupport.Signal();
        var callbackRelease = ConfigurationRefreshTestSupport.Signal();
        CancellationTokenRegistration registration = default;
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            store.Read = (key, token, cancellation) =>
            {
                if (key.Scope == "CommonConfiguration")
                {
                    registration = cancellation.Register(() =>
                    { callbackEntered.TrySetResult(); callbackRelease.Task.GetAwaiter().GetResult(); });
                }
                return ValueTask.FromResult(store.Current(key, token));
            };
            Assert.IsTrue(await coordinator.ReconcileAsync(entry));
            coordinator.BeginStopping();
            await callbackEntered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Task stop = coordinator.StopAsync();
            Assert.IsFalse(stop.IsCompleted, "The provider read has ended; its owned cancellation callback has not.");
            callbackRelease.TrySetResult();
            await stop.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
        }
        finally { callbackRelease.TrySetResult(); await coordinator.DisposeAsync(); registration.Dispose(); }
    }

    [TestMethod]
    public async Task StopIncludesAnAlreadyCommittedSynchronousHandlerAndPreservesItsAcceptedValues()
    {
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        ConfigurationEntry entry = catalog.Entries.Single();
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        try
        {
            await coordinator.LoadAsync(CancellationToken.None);
            await coordinator.ActivateAsync(CancellationToken.None);
            entry.Instance.ConfigurationChanged += (_, _) => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); };
            store.Set(entry, 2, "c2");
            Task<bool> refresh = Task.Run(() => coordinator.ReconcileAsync(entry));
            await entered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Task stop = coordinator.StopAsync();
            await store.SubscriptionDisposalEntered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(refresh.IsCompleted);
            Assert.AreEqual(2, catalog.Reader.Read((RefreshSettings)entry.Instance, current => current.Value));
            release.TrySetResult();
            Assert.IsTrue(await refresh.WaitAsync(ConfigurationRefreshTestSupport.Deadline));
            await stop.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.AreEqual(1L, coordinator.CaptureStatus().Entries.Single().Revision);
        }
        finally { release.TrySetResult(); await coordinator.DisposeAsync(); }
    }

    [TestMethod]
    public async Task FirstLoadTokenOwnsCancellationAndAnIgnoringReadMustActuallyEndBeforeDisposal()
    {
        using var firstToken = new CancellationTokenSource();
        using var laterToken = new CancellationTokenSource();
        var store = new ConfigurationRefreshStore();
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        var entered = ConfigurationRefreshTestSupport.Signal();
        var release = ConfigurationRefreshTestSupport.Signal();
        CancellationToken providerToken = default;
        try
        {
            store.Read = async (_, _, cancellation) =>
            { providerToken = cancellation; entered.TrySetResult(); await release.Task; return ConfigurationReadResult.Missing(); };
            Task load = coordinator.LoadAsync(firstToken.Token);
            await entered.Task.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.AreSame(load, coordinator.LoadAsync(laterToken.Token));
            laterToken.Cancel();
            Assert.IsFalse(providerToken.IsCancellationRequested);
            firstToken.Cancel();
            Assert.IsTrue(providerToken.IsCancellationRequested);
            Task stop = coordinator.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(load.IsCompleted);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await ConfigurationRefreshTestSupport.FailureAsync(load));
            await stop.WaitAsync(ConfigurationRefreshTestSupport.Deadline);
            Assert.AreEqual(0, store.Disposals);
        }
        finally { release.TrySetResult(); await coordinator.DisposeAsync(); }
    }

    [TestMethod]
    public async Task WatchAcquisitionFailureIsSanitizedAndRetainedWithoutAnotherSubscriptionAttempt()
    {
        var store = new ConfigurationRefreshStore
        { Watch = _ => ValueTask.FromException<IAsyncDisposable>(new InvalidOperationException("PRIVATE backend details")) };
        ConfigurationCatalog catalog = ConfigurationRefreshTestSupport.Catalog(store);
        await using var coordinator = new ConfigurationRefreshCoordinator(catalog, new ConfigurationRefreshPipeline());
        await coordinator.LoadAsync(CancellationToken.None);
        Task activation = coordinator.ActivateAsync(CancellationToken.None);
        Exception failure = await ConfigurationRefreshTestSupport.FailureAsync(activation);
        Assert.IsInstanceOfType<CoreConfigurationException>(failure);
        Assert.IsFalse(failure.ToString().Contains("PRIVATE", StringComparison.Ordinal));
        Assert.AreSame(activation, coordinator.ActivateAsync(CancellationToken.None));
        Assert.AreEqual(1, store.Watches);
        Assert.AreEqual(ConfigurationWatchState.Failed, coordinator.CaptureStatus().WatchState);
        Assert.AreEqual("Configuration.WatchActivation", coordinator.CaptureStatus().WatchFailureCode);
    }
}
