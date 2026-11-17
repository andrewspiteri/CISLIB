using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using global::OpenTelemetry.Exporter;
using global::OpenTelemetry.Metrics;
using global::OpenTelemetry.Resources;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class CollectionOwnershipTests
{
    [TestMethod]
    [DataRow("zero_streams")]
    [DataRow("definition_budget")]
    [DataRow("known_capacity")]
    [DataRow("identity_override")]
    [DataRow("resource_collision")]
    [DataRow("exporter_minimum")]
    [DataRow("native_nonhistogram_bounds")]
    [DataRow("nonfinite_bounds")]
    public void InvalidKnownProfilesFailBeforeTheyChangeTheServiceCollection(string problem)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddCisMetrics(options =>
        {
            switch (problem)
            {
                case "zero_streams": options.MaxStreams = 0; break;
                case "definition_budget": options.MaxDefinitions = 129; break;
                case "known_capacity": options.TotalCardinalityLimit = 1_000; break;
                case "identity_override": options.ResourceAttributes.Add("application", "cannot-override"); break;
                case "resource_collision": options.ResourceAttributes.Add("app.a", "x"); options.ResourceAttributes.Add("app_a", "y"); break;
                case "exporter_minimum": options.MaxScrapeResponseSizeBytes = 84_999; break;
                case "native_nonhistogram_bounds":
                    options.Streams.Add("http.server.active_requests", new() { HistogramBounds = new[] { 1d } }); break;
                case "nonfinite_bounds":
                    options.Streams.Add("http.server.request.duration", new() { HistogramBounds = new[] { double.NaN } }); break;
                default: Assert.Fail(problem); break;
            }
        }));
        Assert.AreEqual(0, services.Count, "Invalid captured profile must fail before owned DI descriptors are added.");
    }

    [TestMethod]
    public void ExistingDeferredMetricsAndGlobalResourceConfigurationAreRejectedAtRegistration()
    {
        var services = new ServiceCollection();
        services.ConfigureOpenTelemetryMeterProvider(static (_, _) => { });
        Assert.Throws<InvalidOperationException>(() => services.AddCisMetrics());

        var resource = new ServiceCollection();
        resource.AddOpenTelemetry().ConfigureResource(builder => builder.AddAttributes([new("external", "uncontrolled")]));
        Assert.Throws<InvalidOperationException>(() => resource.AddCisMetrics());
    }

    [TestMethod]
    [DataRow("view")]
    [DataRow("resource")]
    [DataRow("exporter")]
    [DataRow("provider")]
    public async Task ForeignPostRegistrationConfigurationFailsBeforeItsCallbackCanRun(string kind)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.CollectCoreModules = false);
        bool foreignRan = false;
        switch (kind)
        {
            case "view":
                builder.Services.ConfigureOpenTelemetryMeterProvider((_, provider) =>
                { foreignRan = true; provider.AddView("*", MetricStreamConfiguration.Drop); });
                break;
            case "resource":
                builder.Services.ConfigureOpenTelemetryMeterProvider((_, provider) =>
                { foreignRan = true; provider.SetResourceBuilder(ResourceBuilder.CreateEmpty()); });
                break;
            case "exporter":
                builder.Services.Configure<PrometheusAspNetCoreOptions>(options =>
                { foreignRan = true; options.ResourceConstantLabels = null; });
                break;
            case "provider":
                builder.Services.RemoveAll<MeterProvider>();
                builder.Services.AddSingleton<MeterProvider>(_ => new ForeignProvider());
                break;
            default: Assert.Fail(kind); break;
        }
        using IHost host = builder.Build();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync().WaitAsync(CollectionTestHost.Deadline));
            Assert.IsFalse(foreignRan, "The guard must execute before any foreign deferred callback or exporter option callback.");
        }
        finally { await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline); }
    }

    [TestMethod]
    public async Task SharedOpenTelemetryHostAndTracingKeepExactlyOneMetricsProviderAndAnImmutableProfile()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenTelemetry().WithTracing();
        MetricsOptions? retained = null;
        builder.Services.AddCisMetrics(options => { retained = options; options.CollectCoreModules = false; });
        builder.Services.AddCisMetrics(options => options.CollectCoreModules = false);
        using IHost host = builder.Build();
        await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        try
        {
            Assert.AreSame(host.Services.GetRequiredService<MeterProvider>(), host.Services.GetRequiredService<MeterProvider>());
            Assert.AreEqual(1, host.Services.GetServices<MeterProvider>().Count());
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.IsTrue(context.IsActive);
            retained!.MaxStreams = 1;
            retained.ResourceAttributes.Add("late", "ignored");
            Assert.AreEqual(128, context.Settings.MaxStreams);
            Assert.AreEqual(0, context.Settings.ResourceAttributes.Count);
        }
        finally { await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline); }
    }

    [TestMethod]
    public async Task DisabledIntegrationInstallsNoProviderAndNeverCallsAnObservable()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddCisMetrics(options => options.Enabled = false);
        using IHost host = builder.Build();
        IMetrics metrics = host.Services.GetRequiredService<IMetrics>();
        metrics.RegisterObservableGauge(new("app.disabled.state"), () => throw new InvalidOperationException("Must not execute."));
        metrics.CreateCounter(new("app.disabled.events")).Add();
        await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        try { Assert.IsNull(host.Services.GetService<MeterProvider>()); }
        finally { await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline); }
    }

    [TestMethod]
    public async Task StreamExhaustionKeepsExistingDataAndDoesNotRetainRejectedDefinitionNames()
    {
        await using var host = CollectionTestHost.Create(options =>
        { options.CollectIncomingHttp = false; options.MaxDefinitions = 1; options.MaxStreams = 1; });
        ICounter counter = host.Metrics.CreateCounter(new("app.capacity.events"));
        await host.StartAsync();
        counter.Add(4);
        int reserved = host.Context.ReservedNameCount;
        for (int index = 0; index < 100; index++)
        { host.Metrics.CreateCounter(new("app.rejected." + index)).Add(100); }
        counter.Add(2);
        string text = await host.ScrapeAsync();
        CollectionText.AssertIdentity(text);
        Assert.IsFalse(text.Contains("rejected", StringComparison.Ordinal));
        Assert.AreEqual(1, host.Context.DefinitionCount);
        Assert.AreEqual(reserved, host.Context.ReservedNameCount);
        Assert.AreEqual(6d, CollectionText.Value(CollectionText.Samples(text).Single(line => line.StartsWith("app_capacity_events_total{", StringComparison.Ordinal))));
    }

    [TestMethod]
    public async Task OversizedExporterResponseFailsWithoutPublishingATruncatedSuccessfulDocument()
    {
        await using var host = CollectionTestHost.Create(options =>
        { options.CollectIncomingHttp = false; options.MaxScrapeResponseSizeBytes = 85_000; });
        IHistogram histogram = host.Metrics.CreateHistogram(new("app.response.duration", MetricUnit.Seconds, allowedDimensionKeys: ["category"]));
        await host.StartAsync();
        for (int index = 0; index < 64; index++)
        { histogram.Record(0.25, new(("category", new string('x', 250) + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)))); }
        using HttpResponseMessage response = await host.Client.GetAsync("/_metrics").WaitAsync(CollectionTestHost.Deadline);
        string body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(System.Net.HttpStatusCode.InternalServerError, response.StatusCode, body);
        Assert.IsFalse(response.IsSuccessStatusCode);
        Assert.IsTrue(host.Context.IsActive, "Exporter capacity failure cannot stop the application runtime.");
    }

    private sealed class ForeignProvider : MeterProvider { }
}
