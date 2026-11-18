using System.Reflection;
using System.Text.Json;
using CIS.Hosting;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Metrics;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using global::OpenTelemetry.Metrics;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
public sealed class MetricsApplicationCompositionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task BuilderCapturesOptionsOnceWithoutResolvingServicesOrStartingCollection()
    {
        int callbacks = 0, resolutions = 0;
        await using var application = new CISApplication(builder =>
        {
            builder.Services.AddSingleton(_ => { resolutions++; return new object(); });
            MetricsOptions? retained = null;
            Assert.AreSame(builder, builder.ConfigureCisMetrics(options =>
            {
                callbacks++;
                retained = options;
                options.MaxDefinitions = 80;
                options.ResourceAttributes.Add("region", "initial");
                options.Streams.Add("app.fixture.size", new() { HistogramBounds = [1d, 4d] });
            }));
            Assert.IsFalse(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(MeterProvider)));
            retained!.MaxDefinitions = 1;
            retained.ResourceAttributes["region"] = "changed";
            retained.Streams["app.fixture.size"].HistogramBounds = [2d];
            new MetricsApplicationIntegration().ConfigureServices(builder.Services);
            new MetricsApplicationIntegration().ConfigureServices(builder.Services);
            MetricsSettings captured = (MetricsSettings)builder.Services
                .Single(descriptor => descriptor.ServiceType == typeof(MetricsSettings)).ImplementationInstance!;
            Assert.AreEqual(80, captured.MaxDefinitions);
            Assert.AreEqual("initial", captured.ResourceAttributes["region"]);
            CollectionAssert.AreEqual(new[] { 1d, 4d }, captured.Streams["app.fixture.size"].HistogramBounds!.ToArray());
            Assert.AreEqual(1, builder.Services.Count(descriptor => descriptor.ServiceType == typeof(IMetrics)));
        });
        Assert.AreEqual(1, callbacks);
        Assert.AreEqual(0, resolutions);
    }

    [TestMethod]
    public async Task EquivalentBuilderProfilesAreIdempotentAndConflictingProfilesFailDuringSetup()
    {
        await using var application = new CISApplication(builder =>
        {
            builder.ConfigureCisMetrics(options => options.Enabled = false);
            builder.ConfigureCisMetrics(options => options.Enabled = false);
            Assert.AreEqual(1, builder.Services.Count(descriptor => descriptor.ServiceType == typeof(CISApplicationMetricsProfile)));
            Assert.Throws<InvalidOperationException>(() => builder.ConfigureCisMetrics(options => options.Enabled = true));
        });
    }

    [TestMethod]
    public async Task ExplicitProfileConflictsWithManualInstallationBeforeAnyApplicationFactoryRuns()
    {
        using var directory = new TemporaryDirectory();
        int resolutions = 0;
        await using CISApplication application = Create(directory, builder =>
        {
            builder.Services.AddCisMetrics(options => options.CollectCoreModules = false);
            builder.ConfigureCisMetrics(options => options.CollectCoreModules = true);
            builder.Services.AddSingleton(_ => { resolutions++; return new object(); });
        });
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => application.StartAsync().WaitAsync(Deadline));
            Assert.AreEqual(0, resolutions);
            Assert.Throws<InvalidOperationException>(() => _ = application.Services);
        }
        finally { await application.StopAsync().WaitAsync(Deadline); }
    }

    [TestMethod]
    public async Task AutomaticRegistrarExposesMetricsWhileSequentialStartupStillBlocksBusiness()
    {
        using var directory = new TemporaryDirectory();
        var initializer = new HeldInitializer();
        await using CISApplication application = Create(directory, builder =>
        {
            builder.ConfigureCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
            builder.Services.AddSingleton<IStartable>(_ => initializer);
        });
        Task startup = application.StartAsync();
        try
        {
            await initializer.Entered.Task.WaitAsync(Deadline);
            Assert.IsFalse(startup.IsCompleted);
            using HttpClient client = Client(application);
            Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/business")).StatusCode);
            string held = await HostingTestSupport.ScrapeAsync(client);
            Assert.IsTrue(held.Contains("cis_startup_completed", StringComparison.Ordinal), held);
            MetricsCollectionContext context = application.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.IsTrue(context.IsActive);
            Assert.AreEqual(1, application.Services.GetServices<MeterProvider>().Count());
            Assert.AreEqual(1, application.Services.GetRequiredService<ITypeManager>()
                .GetImplementations<ICISApplicationIntegration>().Count);
            initializer.Release.TrySetResult();
            await startup.WaitAsync(Deadline);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, (await client.GetAsync("/business")).StatusCode);
            Assert.AreEqual(1, initializer.Calls);
        }
        finally
        {
            initializer.Release.TrySetResult();
            await application.StopAsync().WaitAsync(Deadline);
            await ObserveStartupAsync(startup);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingManualProfileIsPreservedInsteadOfReplacedByDefaults(bool disabled)
    {
        using var directory = new TemporaryDirectory();
        await using CISApplication application = Create(directory, builder => builder.Services.AddCisMetrics(options =>
        {
            options.Enabled = !disabled;
            options.CollectCoreModules = false;
            options.MaxDefinitions = 2;
            options.ScrapeCacheDuration = TimeSpan.Zero;
        }));
        try
        {
            await application.StartAsync().WaitAsync(Deadline);
            MetricsSettings settings = application.Services.GetRequiredService<MetricsSettings>();
            Assert.AreEqual(2, settings.MaxDefinitions);
            Assert.IsFalse(settings.CollectCoreModules);
            using HttpClient client = Client(application);
            if (disabled)
            {
                Assert.AreSame(DisabledMetrics.Instance, application.Services.GetRequiredService<IMetrics>());
                Assert.IsNull(application.Services.GetService<MeterProvider>());
                Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/_metrics")).StatusCode);
            }
            else
            {
                application.Services.GetRequiredService<IMetrics>().CreateCounter(new("app.manual.events")).Add(3);
                string text = await HostingTestSupport.ScrapeAsync(client);
                Assert.IsTrue(text.Contains("app_manual_events_total", StringComparison.Ordinal), text);
                Assert.AreEqual(1, application.Services.GetServices<MeterProvider>().Count());
            }
        }
        finally { await application.StopAsync().WaitAsync(Deadline); }
    }

    [TestMethod]
    public async Task ExplicitDisabledProfileUsesTheCoreSingletonWithoutACollectorOrReservedBranch()
    {
        using var directory = new TemporaryDirectory();
        await using CISApplication application = Create(directory,
            builder => builder.ConfigureCisMetrics(options => options.Enabled = false));
        try
        {
            await application.StartAsync().WaitAsync(Deadline);
            Assert.AreSame(DisabledMetrics.Instance, application.Services.GetRequiredService<IMetrics>());
            Assert.IsNull(application.Services.GetService<MetricsCollectionContext>());
            Assert.IsNull(application.Services.GetService<MeterProvider>());
            using HttpClient client = Client(application);
            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/_metrics")).StatusCode);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, (await client.GetAsync("/business")).StatusCode);
        }
        finally { await application.StopAsync().WaitAsync(Deadline); }
    }

    [TestMethod]
    public async Task ExplicitFacadeReplacementIsPreservedWithoutInstallingTheCollector()
    {
        using var directory = new TemporaryDirectory();
        var replacement = new ReplacementMetrics();
        await using CISApplication application = Create(directory,
            builder => builder.Services.AddSingleton<IMetrics>(replacement));
        try
        {
            await application.StartAsync().WaitAsync(Deadline);
            Assert.AreSame(replacement, application.Services.GetRequiredService<IMetrics>());
            Assert.IsNull(application.Services.GetService<MetricsSettings>());
            Assert.IsNull(application.Services.GetService<MeterProvider>());
            using HttpClient client = Client(application);
            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/_metrics")).StatusCode);
        }
        finally { await application.StopAsync().WaitAsync(Deadline); }
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task DisabledDiscoveryOrAbsentCatalogEntryKeepsTheCoreFallback(bool enableDiscovery, bool includeCollector)
    {
        using var directory = new TemporaryDirectory();
        await using CISApplication application = Create(directory,
            builder => builder.EnableOptionalIntegrations = enableDiscovery, includeCollector);
        try
        {
            await application.StartAsync().WaitAsync(Deadline);
            Assert.AreSame(DisabledMetrics.Instance, application.Services.GetRequiredService<IMetrics>());
            Assert.IsNull(application.Services.GetService<MeterProvider>());
            using HttpClient client = Client(application);
            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/_metrics")).StatusCode);
        }
        finally { await application.StopAsync().WaitAsync(Deadline); }
    }

    [TestMethod]
    public async Task TwoAutomaticApplicationsOwnDistinctProvidersAndNeverCollectEachOthersInstruments()
    {
        using var directory = new TemporaryDirectory();
        static void Configure(CISApplicationBuilder builder)
            => builder.ConfigureCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        await using CISApplication first = Create(directory, Configure);
        await using CISApplication second = Create(directory, Configure);
        try
        {
            await first.StartAsync().WaitAsync(Deadline);
            await second.StartAsync().WaitAsync(Deadline);
            Assert.AreNotSame(first.Services.GetRequiredService<MeterProvider>(), second.Services.GetRequiredService<MeterProvider>());
            Assert.AreNotSame(first.Services.GetRequiredService<MetricsCollectionContext>(), second.Services.GetRequiredService<MetricsCollectionContext>());
            first.Services.GetRequiredService<IMetrics>().CreateCounter(new("app.isolation.events")).Add(2);
            second.Services.GetRequiredService<IMetrics>().CreateCounter(new("app.isolation.events")).Add(5);
            using HttpClient firstClient = Client(first), secondClient = Client(second);
            string firstText = await HostingTestSupport.ScrapeAsync(firstClient);
            string secondText = await HostingTestSupport.ScrapeAsync(secondClient);
            Assert.AreEqual(2d, Counter(firstText, "app_isolation_events_total"));
            Assert.AreEqual(5d, Counter(secondText, "app_isolation_events_total"));
        }
        finally
        {
            await Task.WhenAll(first.StopAsync(), second.StopAsync()).WaitAsync(Deadline);
        }
    }

    [TestMethod]
    public async Task CanceledStopWaitDoesNotReleaseAnActualObservableOwnerBeforeItsCallbackSettles()
    {
        using var directory = new TemporaryDirectory();
        var owner = new HostingCallbackOwner();
        await using CISApplication application = Create(directory, builder =>
        {
            builder.ConfigureCisMetrics(options =>
            {
                options.ScrapeCacheDuration = TimeSpan.Zero;
                options.ScrapeTimeout = TimeSpan.FromMilliseconds(200);
            });
            builder.Services.AddSingleton<IHostedService>(root => owner.Bind(root.GetRequiredService<IMetrics>()));
        });
        Task<HttpResponseMessage>? scrape = null;
        try
        {
            await application.StartAsync().WaitAsync(Deadline);
            using HttpClient client = Client(application);
            scrape = client.GetAsync("/_metrics");
            await owner.Entered.Task.WaitAsync(Deadline);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<TaskCanceledException>(() => application.StopAsync(canceled.Token));
            Task drain = application.StopAsync();
            Assert.IsFalse(drain.IsCompleted);
            Assert.IsFalse(owner.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, owner.Disposals);
            owner.Release.TrySetResult();
            await drain.WaitAsync(Deadline);
            Assert.IsTrue(owner.WasUsable);
            Assert.IsTrue(owner.Stopping.Task.IsCompleted);
            await application.DisposeAsync();
            Assert.AreEqual(1, owner.Disposals);
        }
        finally
        {
            owner.Release.TrySetResult();
            await application.StopAsync().WaitAsync(Deadline);
            if (scrape is not null)
            {
                try { using HttpResponseMessage response = await scrape.WaitAsync(Deadline); }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static CISApplication Create(TemporaryDirectory directory, Action<CISApplicationBuilder>? configure = null,
        bool includeCollector = true)
    {
        TypeManagerOptions inventory = Inventory(directory, includeCollector);
        return CISApplication.CreateWeb([], builder =>
        {
            builder.TypeManager = inventory;
            builder.LoggingEnabled = false;
            builder.HealthcheckEnabled = false;
            builder.HeartbeatEnabled = false;
            builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
            configure?.Invoke(builder);
        }, application => application.MapGet("/business", () => Results.Ok("ready")));
    }

    private static TypeManagerOptions Inventory(TemporaryDirectory directory, bool includeCollector)
    {
        Assembly host = typeof(CISApplication).Assembly;
        Assembly[] selected = includeCollector ? [host, typeof(MetricsApplicationIntegration).Assembly] : [host];
        foreach (Assembly assembly in selected)
        { File.Copy(assembly.Location, Path.Combine(directory.Path, assembly.GetName().Name + ".dll"), overwrite: true); }
        string manifest = Path.Combine(directory.Path, "application.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, hostAssembly = host.FullName,
            assemblies = selected.Select(assembly => new
            { assemblyName = assembly.FullName, relativePath = assembly.GetName().Name + ".dll" }).ToArray()
        }));
        return new TypeManagerOptions(host)
        { AssemblyManifestPath = manifest, PrecomputedContracts = [typeof(ICISApplicationIntegration)] };
    }

    private static HttpClient Client(CISApplication application)
    {
        string address = application.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        return new() { BaseAddress = new(address), Timeout = Deadline };
    }

    private static double Counter(string text, string name)
        => CollectionText.Value(CollectionText.Samples(text).Single(line => line.StartsWith(name + "{", StringComparison.Ordinal)));

    private static async Task ObserveStartupAsync(Task startup)
    {
        try { await startup.WaitAsync(Deadline); }
        catch (OperationCanceledException) when (startup.IsCanceled) { }
    }

    private sealed class HeldInitializer : IStartable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ReplacementMetrics : IMetrics
    {
        public ICounter CreateCounter(MetricDefinition definition) => DisabledMetrics.Instance.CreateCounter(definition);
        public IUpDownCounter CreateUpDownCounter(MetricDefinition definition) => DisabledMetrics.Instance.CreateUpDownCounter(definition);
        public IGauge CreateGauge(MetricDefinition definition) => DisabledMetrics.Instance.CreateGauge(definition);
        public IHistogram CreateHistogram(MetricDefinition definition) => DisabledMetrics.Instance.CreateHistogram(definition);
        public CIS.Metrics.ITimer CreateTimer(MetricDefinition definition) => DisabledMetrics.Instance.CreateTimer(definition);
        public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe) { }
    }
}
