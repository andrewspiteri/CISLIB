using System.Text.Json;
using CIS.Configuration;
using CIS.Configuration.Hosting;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationHostingContractTests
{
    [TestMethod]
    [DataRow("startable")]
    [DataRow("runtime")]
    [DataRow("keyed-runtime")]
    [DataRow("retained")]
    [DataRow("keyed-retained")]
    [DataRow("reader")]
    [DataRow("keyed-reader")]
    public async Task CompositionConflictsRejectBeforePublishingAnyConfigurationDescriptors(string conflict)
    {
        var store = new MissingStore();
        await using ConfigurationRuntime runtime = await PrepareAsync(store);
        var services = new ServiceCollection();
        switch (conflict)
        {
            case "startable": services.AddSingleton<IStartable>(new Startable()); break;
            case "runtime": services.AddSingleton(runtime); break;
            case "keyed-runtime": services.AddKeyedSingleton("existing", runtime); break;
            case "retained": services.AddSingleton(new DisposableSettings()); break;
            case "keyed-retained": services.AddKeyedSingleton("existing", new DisposableSettings()); break;
            case "reader": services.AddSingleton<IConfigurationReader>(new UnusedReader()); break;
            case "keyed-reader": services.AddKeyedSingleton<IConfigurationReader>("existing", new UnusedReader()); break;
        }
        ServiceDescriptor[] before = services.ToArray();
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Registration", () => services.AddCisConfiguration(runtime));
        CollectionAssert.AreEqual(before, services.ToArray());
        var clean = new ServiceCollection();
        clean.AddCisConfiguration(runtime);
        Assert.AreEqual(1, clean.Count(item => item.ServiceType == typeof(ConfigurationRuntime)));
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task APreparedRuntimeCannotRegisterAgainInItsOriginalOrASecondCollection(bool sameCollection)
    {
        await using ConfigurationRuntime runtime = await PrepareAsync(new MissingStore());
        var first = new ServiceCollection();
        first.AddCisConfiguration(runtime);
        var second = new ServiceCollection();
        ServiceDescriptor[] firstBefore = first.ToArray(), secondBefore = second.ToArray();
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Registration", () =>
            (sameCollection ? first : second).AddCisConfiguration(runtime));
        CollectionAssert.AreEqual(firstBefore, first.ToArray());
        CollectionAssert.AreEqual(secondBefore, second.ToArray());
    }

    [TestMethod]
    public async Task TwoActualRootProvidersCannotClaimOnePreparedRuntime()
    {
        await using ConfigurationRuntime runtime = await PrepareAsync(new MissingStore());
        var services = new ServiceCollection();
        services.AddCisConfiguration(runtime);
        await using ServiceProvider first = services.BuildServiceProvider(Validation());
        await using ServiceProvider second = services.BuildServiceProvider(Validation());
        Assert.AreSame(runtime, first.GetRequiredService<ConfigurationRuntime>());
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Registration", () => second.GetRequiredService<ConfigurationRuntime>());
        Assert.AreSame(runtime, first.GetRequiredService<ConfigurationRuntime>());
        Assert.IsFalse(runtime.CaptureStatus().IsStopping);
    }

    [TestMethod]
    public async Task FactoryOwnedRuntimeAsyncDiDisposalJoinsTheWatchButNeverDisposesRetainedObjectsOrSuppliedStoreInstances()
    {
        var store = new MissingStore();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.ReleaseDisposal = release.Task;
        await using ConfigurationRuntime runtime = await PrepareAsync(store);
        var services = new ServiceCollection();
        services.AddCisConfiguration(runtime);
        services.AddSingleton<IConfigurationStore>(store);
        ServiceProvider provider = services.BuildServiceProvider(Validation());
        Task? disposal = null;
        DisposableSettings? retained = null;
        try
        {
            Assert.AreSame(runtime, provider.GetRequiredService<ConfigurationRuntime>());
            Assert.AreSame(store, provider.GetRequiredService<IConfigurationStore>());
            retained = provider.GetRequiredService<DisposableSettings>();
            await runtime.ActivateAsync(null, null, null, CancellationToken.None).WaitAsync(ConfigurationObjectTestSupport.Deadline);
            disposal = provider.DisposeAsync().AsTask();
            await store.DisposalEntered.Task.WaitAsync(ConfigurationObjectTestSupport.Deadline);
            Assert.IsFalse(disposal.IsCompleted, "Actual DI async disposal must own the incomplete subscription cleanup.");
            Assert.AreEqual(0, retained.Disposals);
            Assert.AreEqual(0, store.Disposals);
            release.TrySetResult();
            await disposal.WaitAsync(ConfigurationObjectTestSupport.Deadline);
            Assert.AreEqual(ConfigurationWatchState.Stopped, runtime.CaptureStatus().WatchState);
            Assert.AreEqual(1, store.WatchDisposals);
            Assert.AreEqual(0, retained.Disposals);
            Assert.AreEqual(0, store.Disposals);
            await runtime.DisposeAsync();
            Assert.AreEqual(1, store.WatchDisposals, "Caller and DI disposal must join one owned cleanup.");
        }
        finally
        {
            release.TrySetResult();
            await (disposal ?? provider.DisposeAsync().AsTask()).WaitAsync(ConfigurationObjectTestSupport.Deadline);
        }
        Assert.IsNotNull(retained);
        Assert.AreEqual(0, retained.Disposals);
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    public async Task ExplicitCallerAsyncOwnershipCleansUpAfterActualProviderValidationFailsBeforeBuildReturns()
    {
        var store = new MissingStore();
        ConfigurationRuntime runtime = await PrepareAsync(store);
        await using (runtime)
        {
            var services = new ServiceCollection();
            services.AddCisConfiguration(runtime);
            services.AddSingleton<UnresolvableService>();
            Assert.ThrowsExactly<AggregateException>(() => services.BuildServiceProvider(Validation()));
            Assert.IsFalse(runtime.CaptureStatus().IsStopping);
            Assert.AreEqual(0, store.Disposals);
        }
        Assert.IsTrue(runtime.CaptureStatus().IsStopping);
        Assert.AreEqual(ConfigurationWatchState.Stopped, runtime.CaptureStatus().WatchState);
        Assert.AreEqual(0, store.Disposals);
    }

    [TestMethod]
    public async Task BootstrapWarningsRemainAvailableWithoutDiLoggingAndTheirCopiedHistoryIsBoundedAndValueFree()
    {
        string record = "{" + string.Join(",", Enumerable.Range(0, 12).Select(index =>
            "\"unknown" + index + "\":\"PRIVATE payload credential\"")) + "}";
        using JsonDocument document = JsonDocument.Parse(record);
        var store = new MissingStore { Result = ConfigurationReadResult.Found(document.RootElement, "source-token-secret") };
        await using ConfigurationRuntime runtime = await PrepareAsync(store);
        IReadOnlyList<ConfigurationIssue> bootstrap = runtime.CaptureDiagnostics();
        Assert.AreEqual(8, bootstrap.Count);
        for (int index = 0; index < 4; index++)
        { Assert.IsTrue(await runtime.ReconcileAsync().WaitAsync(ConfigurationObjectTestSupport.Deadline)); }
        IReadOnlyList<ConfigurationIssue> current = runtime.CaptureDiagnostics();
        Assert.IsTrue(current.Count <= 32 && current.Count >= bootstrap.Count);
        Assert.AreEqual(8, bootstrap.Count, "A copied bootstrap observation must not become the mutable warning queue.");
        Assert.IsTrue(current.All(issue => issue.Code == "Configuration.UnknownProperty"));
        string diagnostic = string.Concat(current.Select(issue => issue.Code + issue.Key + issue.Explanation));
        Assert.IsTrue(diagnostic.Contains("unknown", StringComparison.Ordinal));
        Assert.IsFalse(diagnostic.Contains("PRIVATE", StringComparison.Ordinal));
        Assert.IsFalse(diagnostic.Contains("source-token-secret", StringComparison.Ordinal));
        Assert.AreEqual(0L, runtime.CaptureStatus().Entries.Single().Revision);
    }

    private static Task<ConfigurationRuntime> PrepareAsync(MissingStore store)
        => ConfigurationRuntime.PrepareAsync(new ConfigurationObjectTestSupport.SelectedTypes(typeof(DisposableSettings)),
            new ConfigurationOptions("HostingContracts", store));

    private static ServiceProviderOptions Validation() => new() { ValidateScopes = true, ValidateOnBuild = true };

    private sealed class Startable : IStartable
    { public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask; }

    private sealed class UnusedReader : IConfigurationReader
    {
        public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> reader)
            where TConfiguration : class, IConfigurationObject => throw new AssertFailedException("A conflicting reader must never be resolved or invoked.");
    }

    private sealed class DisposableSettings : ConfigurationObject, IDisposable
    {
        private int disposals;
        public DisposableSettings() { }
        public int Value { get; set; } = 9;
        internal int Disposals => Volatile.Read(ref disposals);
        public void Dispose() => Interlocked.Increment(ref disposals);
    }

    private interface IMissingDependency { }
    private sealed class UnresolvableService
    { public UnresolvableService(IMissingDependency dependency) { _ = dependency; } }

    private sealed class MissingStore : IConfigurationStore, IDisposable
    {
        private int disposals, watchDisposals;
        internal int Disposals => Volatile.Read(ref disposals);
        internal int WatchDisposals => Volatile.Read(ref watchDisposals);
        internal ConfigurationReadResult Result { get; init; } = ConfigurationReadResult.Missing();
        internal Task ReleaseDisposal { get; set; } = Task.CompletedTask;
        internal TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.Reliable;
        public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result);
        public ValueTask<IAsyncDisposable> WatchAsync(IReadOnlyList<ConfigurationRecordKey> keys,
            Action<ConfigurationInvalidation> invalidated, CancellationToken cancellationToken)
            => ValueTask.FromResult<IAsyncDisposable>(new Subscription(this));
        public void Dispose() => Interlocked.Increment(ref disposals);
        private sealed class Subscription(MissingStore store) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref store.watchDisposals);
                store.DisposalEntered.TrySetResult();
                await store.ReleaseDisposal;
            }
        }
    }
}
