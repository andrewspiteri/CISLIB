using System.Text.Json;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Metrics.OpenTelemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class MessagingCollectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalMessagingMetadataReservesSevenStreamsAnd435CombinationsInEitherRegistrationOrder(bool metricsFirst)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }
        RegisterMetadata(builder.Services);
        if (!metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }
        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            MetricsStreamSnapshot[] messaging = context.CaptureCatalog().Where(item => item.Name.StartsWith("cis.messaging.", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(7, messaging);
            Assert.AreEqual(435, messaging.Sum(item => item.CardinalityLimit));
            Assert.AreEqual(96, context.Settings.MaxDefinitions);
            Assert.AreEqual(128, context.Settings.MaxStreams);
            Assert.AreEqual(16_384, context.Settings.TotalCardinalityLimit);
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            Assert.IsTrue(context.Settings.CollectMessaging);
            Assert.AreEqual(7, context.DefinitionCount);
            Assert.IsTrue(context.CaptureCatalog().Where(item => item.Name.StartsWith("cis.messaging.", StringComparison.Ordinal)).All(item => item.Selected));
            foreach (MetricsStreamSnapshot timer in messaging.Where(item => item.Name.EndsWith(".duration", StringComparison.Ordinal)))
            {
                CollectionAssert.AreEqual(MetricsStreamCatalog.DurationBounds.ToArray(), timer.HistogramBounds.ToArray());
            }
        }
        finally
        {
            await StopAndDisposeAsync(host);
        }
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task AbsentCompositionOrDisabledModuleCollectionAddsNoMessagingReservations(bool composition, bool collect)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.CollectCoreModules = collect);
        if (composition)
        {
            RegisterMetadata(builder.Services);
        }
        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.IsFalse(context.Settings.CollectMessaging);
            Assert.IsFalse(context.CaptureCatalog().Any(item => item.Name.StartsWith("cis.messaging.", StringComparison.Ordinal)));
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            Assert.AreEqual(0, context.DefinitionCount);
        }
        finally
        {
            await StopAndDisposeAsync(host);
        }
    }

    [TestMethod]
    [DataRow("cis.messaging.publish.duration", 79)]
    [DataRow("cis.messaging.delivery.duration", 111)]
    [DataRow("cis.messaging.rpc.duration", 159)]
    [DataRow("cis.messaging.active", 2)]
    [DataRow("cis.messaging.dispositions", 47)]
    [DataRow("cis.messaging.outbox.pending", 15)]
    [DataRow("cis.messaging.outbox.oldest.age", 15)]
    public void ExplicitProfilesCannotShrinkAwayAcceptedAliasOutcomeCombinations(string name, int capacity)
    {
        var options = new MetricsOptions();
        options.Streams.Add(name, new()
        {
            CardinalityLimit = capacity
        });
        MetricsSettings selected = new MetricsSettings(options).ForComposition(false, hasMessaging: true);
        Assert.ThrowsExactly<ArgumentException>(() => new MetricsStreamCatalog(selected));
    }

    [TestMethod]
    public async Task ActualExporterProjectsCentralIdentityAndFiniteMessagingFactsWithoutCallingStorageAtScrapeTime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MessagingCollectionTests).Assembly.FullName,
            EnvironmentName = "MessagingCollection"
        });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        RegisterMetadata(builder.Services);
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCisMetrics();
        await app.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        try
        {
            MessagingInstrumentation owner = app.Services.GetRequiredService<MessagingInstrumentation>();
            using MessageObservation publication = owner.Publish("events");
            using (MessageObservation delivery = owner.Delivery("A"))
            {
                delivery.Complete("unknown");
            }
            using (MessageObservation rpc = owner.Rpc("Query"))
            {
                // This controls instrumentation input; actual typed RPC belongs to Part B.
                rpc.Complete("unavailable");
            }
            owner.Disposition("A", "retry");
            owner.Observe(new(DateTimeOffset.UnixEpoch, 5, 2, true));
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            string pending = await client.GetStringAsync("/_metrics");
            CollectionText.AssertIdentity(pending);
            Assert.AreEqual(1d, Sum(pending, "cis_messaging_active{"));
            Assert.AreEqual(0d, Sum(pending, "cis_messaging_publish_duration_seconds_count{"));
            clock.Advance(TimeSpan.FromSeconds(1.25));
            publication.Complete("confirmed");
            string text = await client.GetStringAsync("/_metrics");
            CollectionText.AssertIdentity(text);
            Assert.AreEqual(1d, Sum(text, "cis_messaging_publish_duration_seconds_count{"));
            Assert.AreEqual(1.25d, Sum(text, "cis_messaging_publish_duration_seconds_sum{"));
            Assert.AreEqual(1d, Sum(text, "cis_messaging_delivery_duration_seconds_count{"));
            Assert.AreEqual(1d, Sum(text, "cis_messaging_rpc_duration_seconds_count{"));
            Assert.AreEqual(0d, Sum(text, "cis_messaging_active{"));
            Assert.AreEqual(1d, Sum(text, "cis_messaging_dispositions_total{"));
            Assert.AreEqual(5d, Sum(text, "cis_messaging_outbox_pending{"));
            Assert.AreEqual(2d, Sum(text, "cis_messaging_outbox_oldest_age_seconds{"));
            Assert.HasCount(15, CollectionText.Samples(text).Where(line => line.StartsWith("cis_messaging_publish_duration_seconds_bucket{", StringComparison.Ordinal)).ToArray());
            Assert.IsFalse(text.Contains(MessagingSystemSubscriptions.Replies, StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("message_id", StringComparison.Ordinal));
            clock.Advance(TimeSpan.FromSeconds(6));
            string stale = await client.GetStringAsync("/_metrics");
            Assert.AreEqual(0, CollectionText.Samples(stale).Count(line => line.StartsWith("cis_messaging_outbox_pending{", StringComparison.Ordinal)));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
    }

    private static void RegisterMetadata(IServiceCollection services)
    {
        var codec = new NumberCodec();
        var snapshot = new MessagingSnapshot(new MessagingOptions(),
            [new MessageRouteDefinition<int>("events", MessageKind.Event, codec), new MessageRouteDefinition<int>("commands", MessageKind.Command, codec)],
            [new("Messaging", "Ledger")],
            [new MessageSubscriptionDefinition<int>("A", "Ledger", "events", codec,
                _ => throw new InvalidOperationException("Passive collection metadata must not resolve a handler."),
                (_, _, _, _) => throw new InvalidOperationException("Passive collection metadata must not resolve a target."), ["metric.read"])],
            requests:
            [new MessageRequestDefinition<int, int>("Query", "commands", codec, codec, MessageRequestMode.Query, "server", "test")]);
        services.AddSingleton(snapshot);
        services.AddSingleton(new MessagingComposition(snapshot));
        services.AddSingleton(root => new MessagingInstrumentation(snapshot, root.GetService<TimeProvider>()));
    }

    private static double Sum(string text, string prefix) => CollectionText.Samples(text)
        .Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Sum(CollectionText.Value);

    private static async Task StopAndDisposeAsync(IHost host)
    {
        try
        {
            await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
        finally
        {
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    private sealed class NumberCodec : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("metric.number", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }
}
