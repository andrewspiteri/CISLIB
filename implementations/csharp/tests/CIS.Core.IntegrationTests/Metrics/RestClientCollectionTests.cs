using System.Diagnostics.Metrics;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class RestClientCollectionTests
{
    [TestMethod]
    [DataRow(false, -1, true, 21, 3328)]
    [DataRow(true, -1, true, 27, 4416)]
    [DataRow(true, 0, true, 25, 4224)]
    [DataRow(false, 1, true, 23, 3520)]
    [DataRow(true, -1, false, 4, 1344)]
    public async Task FinalCompositionPreservesOldProfilesAndCapturesConditionalRestInBothOrders(bool hasRest, int outgoing, bool core, int streams, int capacity)
    {
        foreach (bool metricsFirst in new[] { false, true })
        {
            await using var host = new ProfileHost(hasRest, metricsFirst, outgoing, core);
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(0, host.OwnerResolutions, "Catalog discovery inspects passive metadata, never the REST factory.");
            IReadOnlyList<MetricsStreamSnapshot> catalog = context.CaptureCatalog();
            Assert.AreEqual(streams, catalog.Count);
            Assert.AreEqual(capacity, catalog.Sum(stream => stream.CardinalityLimit));
            Assert.AreEqual(hasRest && core ? 4 : 0, catalog.Count(stream => stream.Name.StartsWith("cis.restclient.", StringComparison.Ordinal)));
            Assert.AreEqual(outgoing == 1 || outgoing == -1 && hasRest, context.Settings.CollectOutgoingHttp);
            await host.StartAsync();
            Assert.AreEqual(hasRest && core ? 1 : 0, host.OwnerResolutions);
            Assert.AreEqual(hasRest && core ? 4 : 0, context.DefinitionCount, "Only actual REST attachment creates its four handles.");
        }
    }

    [TestMethod]
    public async Task NoRestAutoRetainsExplicitNineteenDefinitionAndTwentyOneStreamMinimum()
    {
        await using var host = new ProfileHost(false, true, -1, true, smallest: true);
        await host.StartAsync();
        MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
        Assert.AreEqual(19, context.Settings.MaxDefinitions);
        Assert.AreEqual(21, context.CaptureCatalog().Count);
        Assert.AreEqual(3328, context.CaptureCatalog().Sum(stream => stream.CardinalityLimit));
    }

    [TestMethod]
    public async Task RestKnownCatalogRejectsInsufficientDefinitionCapacityBeforeOwnerResolution()
    {
        await using var host = new ProfileHost(true, true, -1, true, smallest: true);
        Assert.ThrowsExactly<ArgumentException>(() => host.Services.GetRequiredService<MetricsCollectionContext>());
        Assert.AreEqual(0, host.OwnerResolutions);
    }

    [TestMethod]
    public async Task PresenceRejectsAnOpaqueMetadataFactoryWithoutInvokingIt()
    {
        await using var host = new ProfileHost(true, true, -1, true, opaqueMarker: true);
        Assert.ThrowsExactly<InvalidOperationException>(() => host.Services.GetRequiredService<MetricsCollectionContext>());
        Assert.AreEqual(0, host.MarkerResolutions);
        Assert.AreEqual(0, host.OwnerResolutions);
    }

    [TestMethod]
    public async Task AttachmentRejectsAnOwnerFromAnotherRegistration()
    {
        await using var host = new ProfileHost(true, true, -1, true, mismatchedIdentity: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.AreEqual(1, host.OwnerResolutions);
        Assert.AreEqual(0, host.Services.GetRequiredService<MetricsCollectionContext>().DefinitionCount);
    }

    [TestMethod]
    public async Task DisabledCollectionWithRestDoesNotResolveAnOwnerOrProvider()
    {
        await using var host = new ProfileHost(true, true, -1, true, enabled: false);
        await host.StartAsync();
        Assert.AreEqual(0, host.OwnerResolutions);
        Assert.IsNull(host.Services.GetService<MetricsCollectionContext>());
        Assert.IsNull(host.Services.GetService<global::OpenTelemetry.Metrics.MeterProvider>());
        Assert.AreSame(DisabledMetrics.Instance, host.Services.GetRequiredService<IMetrics>());
    }

    [TestMethod]
    public async Task TwoRealRootFactoriesSelectOnlyTheirActualNativeOutgoingStreams()
    {
        await using var first = CollectionTestHost.Create(options => options.CollectOutgoingHttp = true);
        await using var second = CollectionTestHost.Create(options => options.CollectOutgoingHttp = true);
        await first.StartAsync(); await second.StartAsync();
        Assert.AreNotSame(first.Context.OutgoingMeter, second.Context.OutgoingMeter);
        Assert.AreSame(first.Context.OutgoingMeter, first.App.Services.GetRequiredService<IMeterFactory>().Create("System.Net.Http"));
        using var firstHandler = new SocketsHttpHandler { MeterFactory = first.App.Services.GetRequiredService<IMeterFactory>() };
        using var secondHandler = new SocketsHttpHandler { MeterFactory = second.App.Services.GetRequiredService<IMeterFactory>() };
        using var firstClient = new HttpClient(firstHandler);
        using var secondClient = new HttpClient(secondHandler);
        using (HttpResponseMessage response = await firstClient.GetAsync(new Uri(first.Client.BaseAddress!, "/work/first")).WaitAsync(CollectionTestHost.Deadline)) { response.EnsureSuccessStatusCode(); }
        for (int index = 0; index < 2; index++)
        {
            using HttpResponseMessage response = await secondClient.GetAsync(new Uri(second.Client.BaseAddress!, "/work/second")).WaitAsync(CollectionTestHost.Deadline);
            response.EnsureSuccessStatusCode();
        }
        using var foreign = new Meter("System.Net.Http");
        foreign.CreateHistogram<double>("http.client.request.duration", "s").Record(999,
            new KeyValuePair<string, object?>("http.request.method", "FOREIGN"));

        string firstText = await first.ScrapeAsync(); string secondText = await second.ScrapeAsync();
        CollectionText.AssertIdentity(firstText); CollectionText.AssertIdentity(secondText);
        string[] firstSamples = CollectionText.Samples(firstText); string[] secondSamples = CollectionText.Samples(secondText);
        Assert.AreEqual(1d, DurationCount(firstSamples));
        Assert.AreEqual(2d, DurationCount(secondSamples));
        Assert.IsFalse(firstText.Contains("FOREIGN", StringComparison.Ordinal));
        Assert.IsFalse(secondText.Contains("FOREIGN", StringComparison.Ordinal));
        Assert.IsFalse(firstText.Contains("server_address", StringComparison.Ordinal));
        Assert.IsFalse(firstText.Contains("url_full", StringComparison.Ordinal));
        string[] outgoingSamples = firstSamples.Where(line => line.StartsWith("http_client_", StringComparison.Ordinal)).ToArray();
        Assert.IsTrue(outgoingSamples.All(line => line.StartsWith("http_client_request_duration_seconds_", StringComparison.Ordinal)
            || line.StartsWith("http_client_active_requests{", StringComparison.Ordinal)), firstText);
        foreach (string sample in outgoingSamples)
        {
            Assert.IsFalse(sample.Contains("http_response_status_code", StringComparison.Ordinal), sample);
            Assert.IsFalse(sample.Contains("url_scheme", StringComparison.Ordinal), sample);
            Assert.IsFalse(sample.Contains("server_port", StringComparison.Ordinal), sample);
            Assert.IsFalse(sample.Contains("error_type", StringComparison.Ordinal), sample);
        }
        Assert.IsTrue(firstSamples.Any(line => line.StartsWith("http_client_active_requests{", StringComparison.Ordinal)
            && CollectionText.Value(line) == 0), firstText);

        static double DurationCount(string[] samples) => samples.Where(line => line.StartsWith("http_client_request_duration_seconds_count{", StringComparison.Ordinal)).Sum(CollectionText.Value);
    }

    private sealed class ProfileHost : IAsyncDisposable
    {
        private readonly IHost host;
        private int ownerResolutions;
        private int markerResolutions;
        internal ProfileHost(bool hasRest, bool metricsFirst, int outgoing, bool core, bool smallest = false,
            bool opaqueMarker = false, bool mismatchedIdentity = false, bool enabled = true)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            var composition = new RestClientComposition(new object(), [new("catalog", "request"), new("catalog", "read")]);
            if (metricsFirst) { Metrics(); }
            if (hasRest)
            {
                if (opaqueMarker) { builder.Services.AddSingleton(_ => { Interlocked.Increment(ref markerResolutions); return composition; }); }
                else { builder.Services.AddSingleton(composition); }
                builder.Services.AddSingleton<IRestClientRuntime>(_ => { Interlocked.Increment(ref ownerResolutions); return new ProfileOwner(composition,
                    mismatchedIdentity ? new object() : composition.RegistrationIdentity); });
            }
            if (!metricsFirst) { Metrics(); }
            // Idempotence compares captured options, independently of final REST presence.
            Metrics();
            host = builder.Build();
            void Metrics() => builder.Services.AddCisMetrics(options =>
            {
                options.CollectOutgoingHttp = outgoing < 0 ? null : outgoing == 1;
                options.Enabled = enabled;
                options.CollectCoreModules = core;
                if (smallest) { options.MaxDefinitions = 19; options.MaxStreams = hasRest ? 27 : 21; options.TotalCardinalityLimit = hasRest ? 4416 : 3328; }
            });
        }
        internal IServiceProvider Services => host.Services;
        internal int OwnerResolutions => Volatile.Read(ref ownerResolutions);
        internal int MarkerResolutions => Volatile.Read(ref markerResolutions);
        internal Task StartAsync() => host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        public async ValueTask DisposeAsync()
        {
            try { await host.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { if (host is IAsyncDisposable asyncHost) { await asyncHost.DisposeAsync().ConfigureAwait(false); } else { host.Dispose(); } }
        }
    }

    // This proves composition/attachment only; actual operations are verified with RC5.
    private sealed class ProfileOwner(RestClientComposition composition, object registrationIdentity) : IRestClientRuntime
    {
        private readonly MetricsAttachment attachment = new();
        private RestClientMetrics? metrics;
        public object RegistrationIdentity => registrationIdentity;
        public void BeginStopping() { }
        public Task StopAsync() => Task.CompletedTask;
        public void AttachMetrics(MetricsRuntime runtime)
        {
            if (attachment.Attach(runtime)) { Volatile.Write(ref metrics, new RestClientMetrics(runtime, composition)); }
        }
    }
}
