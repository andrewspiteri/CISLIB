using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using CIS.Configuration;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Configuration;
using CIS.Tests.IntegrationTests.Startup;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
[DoNotParallelize]
public sealed class MetricsFootprintTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PerformanceObservation")]
    public async Task OneDefaultCatalogObservationRetainsBoundedMemoryAndPlateausAfterNewDimensionsSaturate()
    {
        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        using var directory = new TemporaryDirectory();
        ITypeManager manager = await ConfigurationHostTestSupport.ManagerAsync(directory);
        await using var store = new ConfigurationHostStore();
        await using ConfigurationRuntime configuration = await ConfigurationRuntime.PrepareAsync(manager, ConfigurationHostTestSupport.Options(store));
        var writer = new QuietWriter();
        CoreLogRuntime logging = ModuleObservationSupport.QuietLogging(writer);
        using var provider = new CoreLoggerProvider(logging);
        StartupHost? host = null;
        try
        {
            host = ModuleObservationSupport.Host(manager, configuration, provider, defaultCache: true);
            await host.StartAsync().WaitAsync(StartupHost.Deadline);
            using HttpClient client = HostingTestSupport.Client(host);
            MetricsCollectionContext context = host.App.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(19, context.DefinitionCount);
            IMetrics facade = host.App.Services.GetRequiredService<IMetrics>();
            ICounter counter = facade.CreateCounter(new("app.footprint.events", allowedDimensionKeys: ["category"]));
            ITimer timer = facade.CreateTimer(new("app.footprint.duration", MetricUnit.Seconds, allowedDimensionKeys: ["category"]));
            Assert.AreEqual(96, context.Settings.MaxDefinitions);
            Assert.AreEqual(128, context.Settings.MaxStreams);
            Assert.AreEqual(128, context.Settings.CardinalityLimit);
            Assert.AreEqual(64, context.Settings.HistogramCardinalityLimit);
            Assert.AreEqual(1_024, context.Settings.HttpDurationCardinalityLimit);
            Assert.AreEqual(16_384, context.Settings.TotalCardinalityLimit);
            Assert.AreEqual(4_194_304, context.Settings.MaxScrapeResponseSizeBytes);
            Assert.AreEqual(2, context.Settings.MaxConcurrentScrapes);
            Assert.AreEqual(5_000, context.Settings.ScrapeTimeoutMilliseconds);
            Assert.AreEqual(300, context.Settings.ScrapeCacheMilliseconds);
            IReadOnlyList<MetricsStreamSnapshot> catalog = context.CaptureCatalog();
            Assert.AreEqual(21, context.DefinitionCount);
            Assert.AreEqual(23, catalog.Count);
            Assert.AreEqual(128, catalog.Single(stream => stream.Name == "app.footprint.events").CardinalityLimit);
            Assert.AreEqual(64, catalog.Single(stream => stream.Name == "app.footprint.duration").CardinalityLimit);
            Assert.AreEqual(1_024, catalog.Single(stream => stream.Name == "http.server.request.duration").CardinalityLimit);
            foreach (MetricsStreamSnapshot stream in catalog.Where(stream => stream.HistogramBounds.Count != 0))
            { CollectionAssert.AreEqual(new[] { .005, .01, .025, .05, .075, .1, .25, .5, .75, 1, 2.5, 5, 7.5, 10 }, stream.HistogramBounds.ToArray()); }

            provider.CreateLogger("Footprint").LogInformation(new EventId(72, "metrics.footprint.warm"), "Warm the real logging owner.");
            Assert.IsTrue((await logging.FlushAsync(StartupHost.Deadline, CancellationToken.None)).Succeeded);
            using (HttpResponseMessage response = await client.GetAsync("/footprint").WaitAsync(StartupHost.Deadline))
            { Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); }
            counter.Add(1, new(("category", "warm")));
            timer.Record(.25, new(("category", "warm")));
            string[] warm = await ScrapesAsync();
            long warmed = GC.GetTotalMemory(forceFullCollection: true);
            Fill("first", counter, timer);
            string[] filled = await ScrapesAsync();
            long firstRetained = GC.GetTotalMemory(forceFullCollection: true);
            Fill("second", counter, timer);
            string[] plateau = await ScrapesAsync();
            long secondRetained = GC.GetTotalMemory(forceFullCollection: true);
            long retainedDelta = Math.Max(warmed, Math.Max(firstRetained, secondRetained)) - baseline;
            TestContext.WriteLine("METRICS-FOOTPRINT " + JsonSerializer.Serialize(new
            {
                baselineBytes = baseline, warmedBytes = warmed, firstRetainedBytes = firstRetained,
                secondRetainedBytes = secondRetained, retainedDeltaBytes = retainedDelta,
                secondRoundGrowthBytes = secondRetained - firstRetained,
                workload = new { coreDefinitions = 19, applicationDefinitions = 2, rounds = 2, newCategoriesPerRound = 4_096,
                    nativeGetCategories = 1, nativeRouteTemplate = (string?)null, requestedConcurrentScrapes = 2,
                    formats = new[] { "text/plain;version=0.0.4", "application/openmetrics-text;version=1.0.0" },
                    cacheExpiryWaitMilliseconds = 350, ordinaryDimensions = new[] { "category" }, exemplars = "AlwaysOff; recorded activity has no emitted exemplar" },
                warm = Summary(warm), first = Summary(filled), plateau = Summary(plateau),
                streams = catalog, selectedStreams = catalog.Count(stream => stream.Selected),
                reservedOnlyStreams = catalog.Count(stream => !stream.Selected),
                limits = new { definitions = 96, streams = 128, ordinary = 128, genericHistogram = 64, httpDuration = 1_024,
                    configuredCapacitySum = catalog.Sum(stream => stream.CardinalityLimit), totalCapacity = 16_384,
                    finiteDurationBounds = 14, sdkZeroDimensionAndOverflowPoints = "additional to configured dimension capacities",
                    responseBytesPerFormat = 4_194_304, concurrentHandlers = 2, queue = 0, timeoutMilliseconds = 5_000, cacheMilliseconds = 300 },
                interpretation = "Actual static-catalog/two-application-stream workload; not saturation of every native/module combination. Each complete response must fit the 4-MiB cap; retained managed memory is distinct from RSS and response size."
            }));
            for (int index = 0; index < filled.Length; index++)
            {
                Assert.IsTrue(filled[index].Contains("otel_metric_overflow=\"true\"", StringComparison.Ordinal));
                Assert.AreEqual(CollectionText.Samples(filled[index]).Length, CollectionText.Samples(plateau[index]).Length,
                    "Another 4,096 new categories must not add exported series after saturation.");
            }
            Assert.IsTrue(retainedDelta < 64L * 1_024 * 1_024, "Record the actual incremental retained memory against the approved 64-MiB observation target.");

            async Task<string[]> ScrapesAsync()
            {
                // The default cache is part of this observation. Let both format
                // caches expire before the two real requests are issued together.
                await Task.Delay(TimeSpan.FromMilliseconds(350));
                string[] texts = await Task.WhenAll(ScrapeAsync("text/plain; version=0.0.4"),
                    ScrapeAsync("application/openmetrics-text; version=1.0.0"));
                foreach (string text in texts)
                {
                    CollectionText.AssertIdentity(text);
                    Assert.IsTrue(Encoding.UTF8.GetByteCount(text) <= 4_194_304);
                    Assert.IsFalse(text.Contains(" # {", StringComparison.Ordinal), "The selected profile disables exemplars.");
                }
                return texts;
            }
            async Task<string> ScrapeAsync(string accept)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/_metrics");
                request.Headers.Accept.ParseAdd(accept);
                using HttpResponseMessage response = await client.SendAsync(request).WaitAsync(StartupHost.Deadline);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual(request.Headers.Accept.Single().MediaType, response.Content.Headers.ContentType?.MediaType);
                Assert.AreEqual(request.Headers.Accept.Single().Parameters.Single(parameter => parameter.Name == "version").Value,
                    response.Content.Headers.ContentType?.Parameters.Single(parameter => parameter.Name == "version").Value);
                return await response.Content.ReadAsStringAsync();
            }
        }
        finally
        {
            if (host is not null) { await host.StopAsync(CancellationToken.None).WaitAsync(StartupHost.Deadline); }
            await ModuleObservationSupport.StopLoggingAsync(logging);
            if (host is not null) { await host.DisposeAsync(); }
            await writer.DisposeAsync();
        }
    }

    private static void Fill(string round, ICounter counter, ITimer timer)
    {
        using var activity = new Activity("MetricsFootprint").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        for (int index = 0; index < 4_096; index++)
        {
            var dimensions = new MetricDimensions(("category", round + "-" + index.ToString(CultureInfo.InvariantCulture)));
            counter.Add(1, dimensions);
            timer.Record(.25, dimensions);
        }
    }

    private static object[] Summary(string[] texts) => texts.Select(text => (object)new
    { responseBytes = Encoding.UTF8.GetByteCount(text), samples = CollectionText.Samples(text).Length }).ToArray();
}
