using System.Net;
using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Healthcheck;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Startup;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Configuration;

[TestClass]
public sealed class ConfigurationHostReadinessTests
{
    [TestMethod]
    public async Task BoundHealthRoutesStayNotReadyWhileStageTwoReconcilesTheBootstrapGapBeforeLaterParticipants()
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        store.Set<ConfigurationHostSettings>("{\"Count\":10}", "c1");
        await using ConfigurationRuntime runtime = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        var entered = ConfigurationHostTestSupport.Signal();
        var release = ConfigurationHostTestSupport.Signal();
        store.Set<ConfigurationHostSettings>("{\"Count\":20}", "c2");
        store.Read = async (key, token, _) =>
        {
            ConfigurationReadResult result = store.Current(key, token);
            if (key.Scope == "CommonConfiguration" && key.Definition == ConfigurationHostTestSupport.Definition<ConfigurationHostSettings>())
            { entered.TrySetResult(); await release.Task; }
            return result;
        };
        await using var host = new StartupHost(services =>
        {
            services.AddCisConfiguration(runtime);
            services.AddSingleton<ConfigurationHostConsumer>();
            services.AddSingleton<IStartable>(provider => provider.GetRequiredService<ConfigurationHostConsumer>());
        });
        host.Release.Add(() => release.TrySetResult());
        var consumer = host.App.Services.GetRequiredService<ConfigurationHostConsumer>();
        Assert.AreEqual((10, true), consumer.ConstructorValues);
        var changes = new List<(long Revision, int Count)>();
        consumer.Settings.ConfigurationChanged += (_, change) => changes.Add((change.Revision, consumer.Settings.Count));
        Task start = host.StartAsync();
        await entered.Task.WaitAsync(ConfigurationHostTestSupport.Deadline);
        Assert.IsFalse(start.IsCompleted);
        Assert.AreEqual(0, consumer.Calls);
        Assert.IsFalse(runtime.CaptureStatus().ActivationCompleted);
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
        await host.AssertStatusAsync("/_health", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/business", HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, host.BusinessCalls);
        release.TrySetResult();
        await start.WaitAsync(ConfigurationHostTestSupport.Deadline);
        Assert.AreEqual((20, true), consumer.StartedValues.GetValueOrDefault());
        Assert.AreEqual(1, consumer.Calls);
        CollectionAssert.AreEqual(new[] { (1L, 20) }, changes);
        Assert.IsTrue(runtime.CaptureStatus().ActivationCompleted);
        Assert.IsTrue(host.Health.CaptureSnapshot().Application.StartupCompleted);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
        await host.AssertStatusAsync("/business", HttpStatusCode.OK);
        Assert.AreEqual(1, host.BusinessCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActivationReadOrWatchFailureCannotPublishReadinessOrRunLaterParticipants(bool watchFailure)
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        await using ConfigurationRuntime runtime = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        if (watchFailure)
        { store.Watch = _ => ValueTask.FromException<IAsyncDisposable>(new InvalidOperationException("PRIVATE provider details")); }
        else { store.Set<ConfigurationHostSettings>("{\"Count\":\"PRIVATE invalid data\"}", "invalid"); }
        var later = new StartupParticipant();
        await using var host = new StartupHost(services =>
        { services.AddCisConfiguration(runtime); services.AddSingleton<IStartable>(later); });
        Exception failure = await ConfigurationHostTestSupport.FailureAsync(host.StartAsync());
        Assert.IsInstanceOfType<CoreConfigurationException>(failure);
        Assert.IsFalse(failure.ToString().Contains("PRIVATE", StringComparison.Ordinal));
        Assert.AreEqual(0, later.Calls);
        Assert.IsFalse(runtime.CaptureStatus().ActivationCompleted);
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
        Assert.IsFalse(host.Health.CaptureSnapshot().Ready);
        if (watchFailure)
        { Assert.AreEqual("Configuration.WatchActivation", runtime.CaptureStatus().WatchFailureCode); }
        else { Assert.IsFalse(ConfigurationHostTestSupport.Entry(runtime).IsValid); }
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/business", HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, host.BusinessCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualMappedRefreshFailureAndRecoveryUseExplicitHealthCriticalityWithoutReplayingStartup(bool critical)
    {
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        var store = new ConfigurationHostStore();
        store.Set<ConfigurationHostSettings>("{\"Count\":10}", "c1");
        await using ConfigurationRuntime runtime = await ConfigurationRuntime.PrepareAsync(manager,
            ConfigurationHostTestSupport.Options(store, critical));
        var later = new StartupParticipant();
        await using var host = new StartupHost(services =>
        { services.AddCisConfiguration(runtime); services.AddSingleton<IStartable>(later); });
        await host.StartAsync().WaitAsync(ConfigurationHostTestSupport.Deadline);
        var root = host.App.Services.GetRequiredService<ConfigurationHostSettings>();
        int changes = 0;
        root.ConfigurationChanged += (_, _) => Interlocked.Increment(ref changes);
        store.Set<ConfigurationHostSettings>("{\"Count\":\"PRIVATE invalid data\"}", "bad");
        store.Signal();
        await ConfigurationHostTestSupport.ObserveAsync(() =>
        {
            ConfigurationEntryRefreshStatus current = ConfigurationHostTestSupport.Entry(runtime);
            HealthSnapshot observed = host.Health.CaptureSnapshot();
            return !current.IsValid && current.Revision == 0 && observed.Application.StartupCompleted
                && observed.Ready == !critical && observed.Monitors.Any(monitor =>
                    monitor.Diagnostic?.Code is string code && code.StartsWith("Configuration.", StringComparison.Ordinal));
        }, "Observe both the real watch-driven invalid refresh and its published health outcome.");
        Assert.AreEqual(10, root.Count);
        Assert.AreEqual(0, Volatile.Read(ref changes));
        ConfigurationEntryRefreshStatus failed = ConfigurationHostTestSupport.Entry(runtime);
        Assert.IsFalse(failed.IsValid);
        Assert.AreEqual(0L, failed.Revision);
        HealthSnapshot health = host.Health.CaptureSnapshot();
        Assert.IsTrue(health.Application.StartupCompleted);
        Assert.AreEqual(!critical, health.Ready);
        Assert.IsTrue(health.Monitors.Any(monitor => monitor.Diagnostic?.Code is string code
            && code.StartsWith("Configuration.", StringComparison.Ordinal)));
        await host.AssertStatusAsync("/_health/ready", critical ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        store.Set<ConfigurationHostSettings>("{\"Count\":12}", "recovered");
        store.Signal();
        await ConfigurationHostTestSupport.ObserveAsync(() => ConfigurationHostTestSupport.Entry(runtime) is { IsValid: true, Revision: 1 }
            && Volatile.Read(ref changes) == 1 && host.Health.CaptureSnapshot().Ready,
            "Observe the accepted watch-driven revision, its event and recovered health.");
        Assert.AreEqual(12, root.Count);
        Assert.AreEqual(1, Volatile.Read(ref changes));
        Assert.AreEqual(1L, ConfigurationHostTestSupport.Entry(runtime).Revision);
        Assert.IsTrue(ConfigurationHostTestSupport.Entry(runtime).IsValid);
        Assert.AreEqual(1, later.Calls);
        Assert.IsTrue(host.Health.CaptureSnapshot().Application.StartupCompleted);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
    }
}
