using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Startup;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Configuration;

[TestClass]
public sealed class ConfigurationHostLifetimeTests
{
    [TestMethod]
    public async Task ActualHeartbeatPollingCancelsItsOwnedProviderWorkBeforeItsDrainAndRetainsDependenciesUntilTheReadEnds()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore { NotificationMode = ConfigurationNotificationMode.None };
        store.Set<ConfigurationHostSettings>("{\"Count\":10}", "c1");
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager,
            ConfigurationHostTestSupport.Options(store, interval: TimeSpan.FromMilliseconds(1)));
        var dependency = new StartupDependency();
        var entered = ConfigurationHostTestSupport.Signal();
        var release = ConfigurationHostTestSupport.Signal();
        var canceled = ConfigurationHostTestSupport.Signal();
        int commonReads = 0;
        bool readHadLiveDependency = false;
        store.Read = async (key, token, cancellation) =>
        {
            if (key.Scope == "CommonConfiguration" && key.Definition == ConfigurationHostTestSupport.Definition<ConfigurationHostSettings>()
                && Interlocked.Increment(ref commonReads) == 2)
            {
                using CancellationTokenRegistration registration = cancellation.Register(() => canceled.TrySetResult());
                entered.TrySetResult();
                await release.Task; // Ignore cancellation until the actual provider operation is released.
                readHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
            }
            return store.Current(key, token);
        };
        var host = new StartupHost(services =>
        { services.AddCisConfiguration(configuration); services.AddSingleton<IHostedService>(_ => dependency); });
        await using (host)
        {
            host.Release.Add(() => release.TrySetResult());
            await host.StartAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            await entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsTrue(configuration.CaptureStatus().ActivationCompleted);
            Assert.IsTrue(host.Health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, store.Watches);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            await canceled.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(host.Runtime.StopAsync().IsCompleted, "The real Heartbeat callback still owns the held provider operation.");
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            Assert.AreEqual(0, store.Disposals);
            release.TrySetResult();
            await ConfigurationHostTestSupport.ObserveStopAsync(stop);
            await configuration.StopAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            await host.Runtime.StopAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            await dependency.Stopping.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsTrue(readHadLiveDependency);
            Assert.AreEqual(10, host.App.Services.GetRequiredService<ConfigurationHostSettings>().Count);
        }
        Assert.AreEqual(1, dependency.Disposals);
        Assert.AreEqual(0, store.Disposals, "Configuration must not dispose the externally owned adapter.");
    }

    [TestMethod]
    public async Task WatchOnlyRefreshDetachesObservationAndDrainsTheIgnoringReadBeforeTheRealRootDisposesItsStore()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        store.Set<ConfigurationHostSettings>("{\"Count\":10}", "c1");
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        var dependency = new StartupDependency();
        var entered = ConfigurationHostTestSupport.Signal();
        var release = ConfigurationHostTestSupport.Signal();
        var canceled = ConfigurationHostTestSupport.Signal();
        bool readHadLiveDependency = false;
        var host = new StartupHost(services =>
        {
            services.AddCisConfiguration(configuration);
            services.AddSingleton(_ => store);
            services.AddSingleton<IHostedService>(_ => dependency);
        });
        await using (host)
        {
            host.Release.Add(() => release.TrySetResult());
            Assert.AreSame(store, host.App.Services.GetRequiredService<ConfigurationHostStore>());
            await host.StartAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            var root = host.App.Services.GetRequiredService<ConfigurationHostSettings>();
            int changes = 0;
            root.ConfigurationChanged += (_, _) => changes++;
            store.Set<ConfigurationHostSettings>("{\"Count\":99}", "c2");
            store.Read = async (key, token, cancellation) =>
            {
                ConfigurationReadResult result = store.Current(key, token);
                if (key.Scope == "CommonConfiguration" && key.Definition == ConfigurationHostTestSupport.Definition<ConfigurationHostSettings>())
                {
                    using CancellationTokenRegistration registration = cancellation.Register(() => canceled.TrySetResult());
                    entered.TrySetResult();
                    await release.Task;
                    readHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0 && store.Disposals == 0;
                }
                return result;
            };
            store.Signal();
            await entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            await canceled.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            await store.WatchDisposalEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            Assert.AreEqual(0, store.Disposals);
            release.TrySetResult();
            await ConfigurationHostTestSupport.ObserveStopAsync(stop);
            await configuration.StopAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsTrue(readHadLiveDependency);
            Assert.AreEqual(10, root.Count);
            Assert.AreEqual(0, changes);
            Assert.AreEqual(0L, ConfigurationHostTestSupport.Entry(configuration).Revision);
            Assert.AreEqual(1, store.WatchDisposals);
            int completedReads = store.Reads;
            store.Signal();
            Assert.AreEqual(completedReads, store.Reads);
        }
        Assert.AreEqual(1, dependency.Disposals);
        Assert.AreEqual(1, store.Disposals, "Only actual asynchronous root disposal owns this resolved store registration.");
    }

    [TestMethod]
    public async Task StopJoinsCommittedConfigurationNotificationAndActualHeartbeatCallbackRecoveryBeforeDependencyTeardown()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        store.Set<ConfigurationHostSettings>("{\"Count\":10}", "c1");
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        var dependency = new StartupDependency();
        var callbackRelease = ConfigurationHostTestSupport.Signal();
        var recoveryEntered = ConfigurationHostTestSupport.Signal();
        var recoveryRelease = ConfigurationHostTestSupport.Signal();
        var notificationEntered = ConfigurationHostTestSupport.Signal();
        var notificationRelease = ConfigurationHostTestSupport.Signal();
        bool callbackHadLiveDependency = false, recoveryHadLiveDependency = false, notificationHadLiveDependency = false;
        long notifiedRevision = 0;
        var subscriber = new StartupSubscriber(async _ =>
        {
            await callbackRelease.Task;
            callbackHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
            throw new InvalidOperationException("Controlled callback failure.");
        }, async _ =>
        {
            recoveryEntered.TrySetResult();
            await recoveryRelease.Task;
            recoveryHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
        });
        var host = new StartupHost(services =>
        { services.AddCisConfiguration(configuration); services.AddSingleton<IHostedService>(_ => dependency); });
        await using (host)
        {
            host.Release.Add(() => callbackRelease.TrySetResult());
            host.Release.Add(() => recoveryRelease.TrySetResult());
            host.Release.Add(() => notificationRelease.TrySetResult());
            host.Owner.Service.Register(subscriber, StartupHost.Schedule);
            await host.StartAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            await subscriber.Entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            var root = host.App.Services.GetRequiredService<ConfigurationHostSettings>();
            root.ConfigurationChanged += (_, change) =>
            {
                notifiedRevision = change.Revision;
                notificationEntered.TrySetResult();
                notificationRelease.Task.GetAwaiter().GetResult();
                notificationHadLiveDependency = !dependency.Stopping.Task.IsCompleted && dependency.Disposals == 0;
            };
            store.Set<ConfigurationHostSettings>("{\"Count\":20}", "c2");
            store.Signal();
            await notificationEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            await store.WatchDisposalEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            callbackRelease.TrySetResult();
            await recoveryEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            notificationRelease.TrySetResult();
            await configuration.StopAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted, "Configuration has drained, but actual Heartbeat recovery remains held.");
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            recoveryRelease.TrySetResult();
            await ConfigurationHostTestSupport.ObserveStopAsync(stop);
            await dependency.Stopping.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsTrue(callbackHadLiveDependency);
            Assert.IsTrue(recoveryHadLiveDependency);
            Assert.IsTrue(notificationHadLiveDependency);
            Assert.AreEqual(1L, notifiedRevision);
            Assert.AreEqual(20, root.Count);
            Assert.AreEqual(1, subscriber.Calls);
            Assert.AreEqual(1, subscriber.Errors);
            Assert.IsFalse(host.Runtime.InfrastructureFailure.IsCompleted);
        }
        Assert.AreEqual(1, dependency.Disposals);
    }

    [TestMethod]
    public async Task StopJoinsLateWatchAcquisitionAndHeldSubscriptionDisposalBeforeDependencyTeardown()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        var acquireRelease = ConfigurationHostTestSupport.Signal();
        var disposalRelease = ConfigurationHostTestSupport.Signal();
        var cancellationObserved = ConfigurationHostTestSupport.Signal();
        var dependency = new StartupDependency();
        store.Watch = async token =>
        {
            using CancellationTokenRegistration registration = token.Register(() => cancellationObserved.TrySetResult());
            await acquireRelease.Task;
            return store.Subscription();
        };
        store.DisposeWatch = async () => await disposalRelease.Task;
        var host = new StartupHost(services =>
        { services.AddCisConfiguration(configuration); services.AddSingleton<IHostedService>(_ => dependency); });
        await using (host)
        {
            host.Release.Add(() => acquireRelease.TrySetResult());
            host.Release.Add(() => disposalRelease.TrySetResult());
            Task start = host.StartAsync();
            await store.WatchEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            await cancellationObserved.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(start.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, store.WatchDisposals);
            acquireRelease.TrySetResult();
            await store.WatchDisposalEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            disposalRelease.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await ConfigurationHostTestSupport.FailureAsync(start));
            await ConfigurationHostTestSupport.ObserveStopAsync(stop);
            Assert.AreEqual(1, store.WatchDisposals);
            Assert.IsFalse(configuration.CaptureStatus().ActivationCompleted);
            Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(0, store.Disposals);
        }
        Assert.AreEqual(1, dependency.Disposals);
    }

    [TestMethod]
    public async Task FirstHostStartupTokenClosesConfigurationAndHeartbeatWhileALaterInitializerIgnoresCancellation()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        using var firstToken = new CancellationTokenSource();
        var entered = ConfigurationHostTestSupport.Signal();
        var release = ConfigurationHostTestSupport.Signal();
        var dependency = new StartupDependency();
        var later = new StartupParticipant(async _ => { entered.TrySetResult(); await release.Task; });
        var host = new StartupHost(services =>
        {
            services.AddCisConfiguration(configuration);
            services.AddSingleton<IStartable>(later);
            services.AddSingleton<IHostedService>(_ => dependency);
        });
        await using (host)
        {
            host.Release.Add(() => release.TrySetResult());
            Task start = host.StartAsync(firstToken.Token);
            await entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsTrue(configuration.CaptureStatus().ActivationCompleted);
            firstToken.Cancel();
            Assert.IsTrue(configuration.CaptureStatus().IsStopping,
                "The first token closes module admission before a held initializer returns or host stop is called.");
            Assert.ThrowsExactly<InvalidOperationException>(() => host.Runtime.CreateOwner());
            Assert.IsFalse(start.IsCompleted);
            Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
            int reads = store.Reads;
            store.Signal();
            Assert.AreEqual(reads, store.Reads);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            await store.WatchDisposalEntered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(dependency.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, dependency.Disposals);
            release.TrySetResult();
            Assert.IsInstanceOfType<OperationCanceledException>(await ConfigurationHostTestSupport.FailureAsync(start));
            await ConfigurationHostTestSupport.ObserveStopAsync(stop);
            Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(1, later.Calls);
        }
        Assert.AreEqual(1, dependency.Disposals);
    }
}
