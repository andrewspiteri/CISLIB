using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Healthcheck;
using CIS.Startup;
using CIS.Startup.Hosting;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Configuration;

[TestClass]
public sealed class ConfigurationHostBootstrapTests
{
    [TestMethod]
    public async Task FullPreparedTypeManagerInitializesBothRetainedRootsBeforeTheOneRealProviderAndConsumerConstruction()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        CollectionAssert.AreEquivalent(new[] { typeof(ConfigurationHostSettings), typeof(ConfigurationHostDirectSettings) },
            manager.GetImplementations<IConfigurationObject>().ToArray());
        var store = new ConfigurationHostStore();
        store.Set<ConfigurationHostSettings>("{\"Count\":40}", "c1");
        store.Set<ConfigurationHostSettings>("{\"Count\":42}", "a1", application: true);
        store.Set<ConfigurationHostDirectSettings>("{\"Enabled\":false}", "d1");
        var entered = ConfigurationHostTestSupport.Signal();
        var release = ConfigurationHostTestSupport.Signal();
        store.Read = async (key, token, _) =>
        {
            ConfigurationReadResult result = store.Current(key, token);
            if (key.Scope == "CommonConfiguration" && key.Definition == ConfigurationHostTestSupport.Definition<ConfigurationHostSettings>())
            { entered.TrySetResult(); await release.Task; }
            return result;
        };
        int constructions = 0, changes = 0;
        var options = new ConfigurationOptions(ConfigurationHostTestSupport.HostName, store)
        {
            Factories = new Dictionary<Type, Func<IConfigurationObject>>
            {
                [typeof(ConfigurationHostSettings)] = () =>
                {
                    var value = new ConfigurationHostSettings();
                    value.ConfigurationChanged += (_, _) => Interlocked.Increment(ref changes);
                    return value;
                },
                [typeof(ConfigurationHostDirectSettings)] = () => new ConfigurationHostDirectSettings()
            }
        };
        Task<ConfigurationRuntime> preparation = ConfigurationRuntime.PrepareAsync(manager, options);
        ConfigurationRuntime? runtime = null;
        IHost? host = null;
        try
        {
            await entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            Assert.IsFalse(preparation.IsCompleted);
            Assert.AreEqual(0, constructions);
            release.TrySetResult();
            runtime = await preparation.WaitAsync(ConfigurationHostTestSupport.Deadline);
            ConfigurationRuntime prepared = runtime;
            store.Read = null;
            Assert.AreEqual(4, store.Reads, "Initial loading reads both scopes for every actual discovered type.");
            Assert.AreEqual(0, store.Watches);
            Assert.AreEqual(0, changes);
            CollectionAssert.AreEquivalent(new[] { typeof(ConfigurationHostSettings), typeof(ConfigurationHostDirectSettings) },
                runtime.CaptureStatus().Entries.Select(entry => entry.ConfigurationType).ToArray());
            string[] definitions = store.Requests.Select(request => request.Key.Definition).Distinct().ToArray();
            CollectionAssert.AreEquivalent(new[] { ConfigurationHostTestSupport.Definition<ConfigurationHostSettings>(),
                ConfigurationHostTestSupport.Definition<ConfigurationHostDirectSettings>() }, definitions);
            Assert.IsTrue(store.Requests.All(request => request.Key.Scope is "CommonConfiguration" or ConfigurationHostTestSupport.HostName));
            host = Host.CreateDefaultBuilder()
                .UseDefaultServiceProvider(provider => { provider.ValidateScopes = true; provider.ValidateOnBuild = true; })
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton(manager);
                    services.AddCisConfiguration(prepared);
                    services.AddSingleton(provider =>
                    {
                        Interlocked.Increment(ref constructions);
                        return new ConfigurationHostConsumer(provider.GetRequiredService<ConfigurationHostSettings>(),
                            provider.GetRequiredService<ConfigurationHostDirectSettings>(), provider.GetRequiredService<IConfigurationReader>());
                    });
                    services.AddSingleton<IStartable>(provider => provider.GetRequiredService<ConfigurationHostConsumer>());
                    services.AddCisStartup();
                }).Build();
            Assert.AreEqual(0, constructions);
            await host.StartAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
            ConfigurationHostConsumer consumer = host.Services.GetRequiredService<ConfigurationHostConsumer>();
            Assert.AreEqual((42, false), consumer.ConstructorValues);
            Assert.AreEqual((42, false), consumer.StartedValues.GetValueOrDefault());
            Assert.AreEqual(1, constructions);
            Assert.AreEqual(1, consumer.Calls);
            using IServiceScope first = host.Services.CreateScope();
            using IServiceScope second = host.Services.CreateScope();
            Assert.AreSame(consumer.Settings, first.ServiceProvider.GetRequiredService<ConfigurationHostSettings>());
            Assert.AreSame(consumer.Settings, second.ServiceProvider.GetRequiredService<ConfigurationHostSettings>());
            Assert.AreSame(consumer.Direct, second.ServiceProvider.GetRequiredService<ConfigurationHostDirectSettings>());
            Assert.AreSame(manager, host.Services.GetRequiredService<ITypeManager>());
            Assert.IsTrue(runtime.CaptureStatus().ActivationCompleted);
            Assert.AreEqual(1, store.Watches, "Reliable observation works without optional Heartbeat or Healthcheck.");
            Assert.AreEqual(4, store.WatchedKeys!.Count);
            Assert.AreEqual(0, changes);
        }
        finally
        {
            release.TrySetResult();
            if (runtime is null)
            {
                try { runtime = await preparation.WaitAsync(ConfigurationHostTestSupport.Deadline); }
                catch (Exception) when (preparation.IsCompleted) { }
            }
            if (host is not null)
            {
                await host.StopAsync(CancellationToken.None).WaitAsync(ConfigurationHostTestSupport.Deadline);
                if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync(); }
                else { host.Dispose(); }
            }
            if (runtime is not null) { await runtime.DisposeAsync(); }
        }
        Assert.AreEqual(1, store.WatchDisposals);
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InitialFailureOrCancellationCannotYieldAPreparedRuntimeBeforeTheActualIgnoringReadSettles(bool cancel)
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        using var cancellation = new CancellationTokenSource();
        var store = new ConfigurationHostStore();
        var entered = ConfigurationHostTestSupport.Signal();
        var release = ConfigurationHostTestSupport.Signal();
        store.Read = async (_, _, _) =>
        { entered.TrySetResult(); await release.Task; return ConfigurationReadResult.Failure(); };
        Task<ConfigurationRuntime> preparation = ConfigurationRuntime.PrepareAsync(manager,
            ConfigurationHostTestSupport.Options(store), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
            if (cancel) { cancellation.Cancel(); }
            Assert.IsFalse(preparation.IsCompleted);
            Assert.AreEqual(0, store.Disposals);
            release.TrySetResult();
            Exception failure = await ConfigurationHostTestSupport.FailureAsync(preparation);
            if (cancel) { Assert.IsInstanceOfType<OperationCanceledException>(failure); }
            else
            {
                Assert.IsInstanceOfType<CoreConfigurationException>(failure);
                Assert.IsTrue(((CoreConfigurationException)failure).Issues.Any(issue => issue.Code == "Configuration.StoreRead"));
            }
            Assert.AreEqual(0, store.Watches);
            Assert.AreEqual(0, store.Disposals);
        }
        finally
        {
            release.TrySetResult();
            try { await preparation.WaitAsync(ConfigurationHostTestSupport.Deadline); }
            catch (Exception) when (preparation.IsCompleted) { }
        }
    }

    [TestMethod]
    [DataRow(false, "Configuration.HealthPolicy")]
    [DataRow(true, "Configuration.HeartbeatRequired")]
    public async Task MissingRequiredHostingInputsFailBeforeWatchAcquisitionOrDependentStartup(bool polling, string expectedCode)
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore
        { NotificationMode = polling ? ConfigurationNotificationMode.None : ConfigurationNotificationMode.Reliable };
        await using ConfigurationRuntime runtime = await ConfigurationRuntime.PrepareAsync(manager,
            ConfigurationHostTestSupport.Options(store, critical: null, interval: polling ? TimeSpan.FromDays(1) : null));
        await using var health = new HealthcheckService(new HealthcheckOptions());
        var later = new CIS.Tests.IntegrationTests.Startup.StartupParticipant();
        IHost host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(provider => { provider.ValidateScopes = true; provider.ValidateOnBuild = true; })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                if (!polling) { services.AddSingleton(health); }
                services.AddCisConfiguration(runtime);
                services.AddSingleton<IStartable>(later);
                services.AddCisStartup();
            }).Build();
        try
        {
            Exception failure = await ConfigurationHostTestSupport.FailureAsync(host.StartAsync());
            Assert.IsInstanceOfType<CoreConfigurationException>(failure);
            Assert.IsTrue(((CoreConfigurationException)failure).Issues.Any(issue => issue.Code == expectedCode));
            Assert.AreEqual(0, later.Calls);
            Assert.AreEqual(0, store.Watches);
            Assert.IsFalse(runtime.CaptureStatus().ActivationCompleted);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).WaitAsync(ConfigurationHostTestSupport.Deadline);
            if (host is IAsyncDisposable asynchronous) { await asynchronous.DisposeAsync(); }
            else { host.Dispose(); }
        }
    }
}
