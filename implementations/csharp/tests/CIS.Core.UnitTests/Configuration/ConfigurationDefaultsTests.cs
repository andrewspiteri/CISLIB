using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationDefaultsTests
{
    [TestMethod]
    public async Task DefaultsOnlyPreparationAndActivationRetainValidatedRootsWithoutHeartbeatOrObservation()
    {
        await using ConfigurationRuntime runtime = await PrepareDefaultsAsync();
        var services = new ServiceCollection();
        services.AddCisConfiguration(runtime);
        await using ServiceProvider provider = services.BuildServiceProvider(Validation());
        DefaultsSettings retained = provider.GetRequiredService<DefaultsSettings>();
        IConfigurationReader reader = provider.GetRequiredService<IConfigurationReader>();
        int changes = 0;
        retained.ConfigurationChanged += (_, _) => changes++;
        using (IServiceScope scope = provider.CreateScope())
        {
            Assert.AreSame(retained, scope.ServiceProvider.GetRequiredService<DefaultsSettings>());
            Assert.AreSame(reader, scope.ServiceProvider.GetRequiredService<IConfigurationReader>());
        }
        Assert.AreEqual((12, "default"), reader.Read(retained, value => (value.Value, value.Child.Label)));
        Assert.IsFalse(runtime.CaptureStatus().ActivationCompleted);
        await provider.GetRequiredService<IStartable>().StartAsync(CancellationToken.None)
            .WaitAsync(ConfigurationObjectTestSupport.Deadline);
        ConfigurationRefreshStatus status = runtime.CaptureStatus();
        Assert.IsTrue(status.ActivationCompleted);
        Assert.AreEqual(ConfigurationWatchState.NotRequired, status.WatchState);
        Assert.IsTrue(status.Entries.Single().IsValid);
        Assert.AreEqual(0L, status.Entries.Single().Revision);
        Assert.AreEqual(0, changes, "The accepted defaults and unchanged activation are not update notifications.");
        await runtime.StopAsync().WaitAsync(ConfigurationObjectTestSupport.Deadline);
        Assert.AreEqual(0, retained.Disposals, "Retained POCO instance ownership is unchanged in defaults-only mode.");
    }

    [TestMethod]
    public async Task DefaultsOnlyStillUsesExactFactoriesAndCrossPropertyValidation()
    {
        var validated = new List<int>();
        var options = new ConfigurationOptions("DefaultsHost")
        {
            Factories = new Dictionary<Type, Func<IConfigurationObject>>
            { [typeof(DefaultsSettings)] = () => new DefaultsSettings { Value = 23 } },
            Validators = new Dictionary<Type, Action<IConfigurationObject>>
            { [typeof(DefaultsSettings)] = value => validated.Add(((DefaultsSettings)value).Value) }
        };
        await using ConfigurationRuntime runtime = await ConfigurationRuntime.PrepareAsync(Types(), options);
        var services = new ServiceCollection();
        services.AddCisConfiguration(runtime);
        await using ServiceProvider provider = services.BuildServiceProvider(Validation());
        Assert.AreEqual(23, provider.GetRequiredService<DefaultsSettings>().Value);
        Assert.IsTrue(validated.Count > 0);
        Assert.IsTrue(validated.All(value => value == 23));
    }

    [TestMethod]
    public async Task DefaultsOnlyCannotBypassARejectingValidator()
    {
        int validations = 0;
        var options = new ConfigurationOptions("DefaultsHost")
        {
            Validators = new Dictionary<Type, Action<IConfigurationObject>>
            { [typeof(DefaultsSettings)] = _ => { validations++; throw new InvalidOperationException("private validator detail"); } }
        };
        CoreConfigurationException error = await Assert.ThrowsExactlyAsync<CoreConfigurationException>(
            () => ConfigurationRuntime.PrepareAsync(Types(), options));
        Assert.IsTrue(validations > 0);
        Assert.IsTrue(error.Issues.Any(issue => issue.Code == "Configuration.Refresh"));
    }

    [TestMethod]
    public void DefaultsOnlyRejectsPollingRatherThanInventingPeriodicUpdates()
        => ConfigurationObjectTestSupport.AssertIssue("Configuration.Options", () =>
            ConfigurationCatalog.Create([typeof(DefaultsSettings)],
                new ConfigurationOptions("DefaultsHost") { PollingInterval = TimeSpan.FromMinutes(1) }));

    [TestMethod]
    [DataRow(ConfigurationNotificationMode.None)]
    [DataRow(ConfigurationNotificationMode.RequiresReconciliation)]
    public void ExternalNonReliableStoresStillRequireTheirExplicitInterval(ConfigurationNotificationMode mode)
    {
        var store = new TestStore(mode);
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Options", () =>
            ConfigurationCatalog.Create([typeof(DefaultsSettings)], new ConfigurationOptions("ExternalHost", store)));
        Assert.AreEqual(0, store.Reads);
    }

    [TestMethod]
    public void PassingTheInternalAdapterToTheExternalConstructorDoesNotSelectDefaultsOnly()
    {
        var defaults = new ConfigurationOptions("DefaultsHost");
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Options", () =>
            ConfigurationCatalog.Create([typeof(DefaultsSettings)], new ConfigurationOptions("ExternalHost", defaults.Store)));
    }

    [TestMethod]
    public async Task AnExternalReadFailureDoesNotFallBackToTheAvailableObjectDefaults()
    {
        var store = new TestStore(ConfigurationNotificationMode.Reliable);
        await Assert.ThrowsExactlyAsync<CoreConfigurationException>(() => ConfigurationRuntime.PrepareAsync(
            Types(), new ConfigurationOptions("ExternalHost", store)));
        Assert.IsTrue(store.Reads > 0);
        Assert.AreEqual(0, store.Watches);
    }

    [TestMethod]
    public async Task InternalPrependActivatesConfigurationBeforeUsersWhilePreservingTheirExactDescriptorsAndOrder()
    {
        await using ConfigurationRuntime runtime = await PrepareDefaultsAsync();
        var services = new ServiceCollection();
        var order = new List<int>();
        services.AddSingleton<object>(new object());
        services.AddSingleton<IStartable>(new Participant(1, runtime, order));
        services.AddKeyedSingleton<IStartable>("keyed", new Participant(99, runtime, order));
        services.AddSingleton<IStartable>(new Participant(2, runtime, order));
        ServiceDescriptor[] original = services.ToArray();
        runtime.Register(services, prependStartup: true);
        CollectionAssert.AreEqual(original, services.Where(original.Contains).ToArray());
        await using ServiceProvider provider = services.BuildServiceProvider(Validation());
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(ConfigurationObjectTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { 1, 2 }, order);
            Assert.AreEqual(3, provider.GetServices<IStartable>().Count());
        }
        finally
        {
            await coordinator.StopAsync().WaitAsync(ConfigurationObjectTestSupport.Deadline);
            await coordinator.DisposeAsync();
            await runtime.StopAsync().WaitAsync(ConfigurationObjectTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InternalPrependStillRejectsRetainedOrRuntimeConflictsBeforeAnyDescriptorChanges(bool runtimeConflict)
    {
        await using ConfigurationRuntime runtime = await PrepareDefaultsAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(new Participant(0, runtime, []));
        if (runtimeConflict) { services.AddKeyedSingleton("conflict", runtime); }
        else { services.AddSingleton(new DefaultsSettings()); }
        ServiceDescriptor[] before = services.ToArray();
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Registration", () => runtime.Register(services, prependStartup: true));
        CollectionAssert.AreEqual(before, services.ToArray());
        var valid = new ServiceCollection();
        runtime.Register(valid, prependStartup: true);
    }

    [TestMethod]
    public async Task PublicRegistrationStillRejectsLateEnrollmentWithoutChangingUserDescriptors()
    {
        await using ConfigurationRuntime runtime = await PrepareDefaultsAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(new Participant(0, runtime, []));
        ServiceDescriptor[] before = services.ToArray();
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Registration", () => services.AddCisConfiguration(runtime));
        CollectionAssert.AreEqual(before, services.ToArray());
    }

    private static Task<ConfigurationRuntime> PrepareDefaultsAsync()
        => ConfigurationRuntime.PrepareAsync(Types(), new ConfigurationOptions("DefaultsHost"));

    private static ConfigurationObjectTestSupport.SelectedTypes Types() => new(typeof(DefaultsSettings));
    private static ServiceProviderOptions Validation() => new() { ValidateScopes = true, ValidateOnBuild = true };

    private sealed class DefaultsSettings : ConfigurationObject, IDisposable
    {
        public DefaultsSettings() { }
        public int Value { get; set; } = 12;
        public W1Child Child { get; set; } = new() { Label = "default" };
        internal int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private sealed class Participant(int value, ConfigurationRuntime runtime, List<int> order) : IStartable
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Assert.IsTrue(runtime.CaptureStatus().ActivationCompleted);
            order.Add(value);
            return Task.CompletedTask;
        }
    }

    private sealed class TestStore(ConfigurationNotificationMode mode) : IConfigurationStore
    {
        internal int Reads { get; private set; }
        internal int Watches { get; private set; }
        public ConfigurationNotificationMode NotificationMode => mode;
        public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken,
            CancellationToken cancellationToken)
        { Reads++; return ValueTask.FromResult(ConfigurationReadResult.Failure()); }
        public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
            Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
        { Watches++; throw new AssertFailedException("Preparation failure must not acquire observation."); }
    }
}
