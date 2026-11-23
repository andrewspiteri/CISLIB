using System.Text.Json;
using System.Diagnostics.Metrics;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.Messaging;
using CIS.Metrics.OpenTelemetry;
using CIS.Security;
using CIS.Tests.IntegrationTests.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditMetricsIntegrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalAuditMetadataReservesOneNineCombinationCounterInEitherOrder(bool metricsFirst)
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
            MetricsStreamSnapshot stream = context.CaptureCatalog().Single(item => item.Name == AuditInstrumentation.MetricName);
            Assert.AreEqual(9, stream.CardinalityLimit);
            CollectionAssert.AreEquivalent(new[] { "category", "outcome" }, stream.DimensionKeys.ToArray());
            Assert.AreEqual(96, context.Settings.MaxDefinitions);
            Assert.AreEqual(128, context.Settings.MaxStreams);
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            Assert.IsTrue(context.Settings.CollectAudit);
            Assert.AreEqual(1, context.DefinitionCount);
            Assert.IsTrue(context.CaptureCatalog().Single(item => item.Name == AuditInstrumentation.MetricName).Selected);
        }
        finally
        {
            await StopAndDisposeAsync(host);
        }
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task AbsentAuditOrDisabledCollectionAddsNoAuditReservation(bool audit, bool collect)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.CollectCoreModules = collect);
        if (audit)
        {
            RegisterMetadata(builder.Services);
        }
        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.IsFalse(context.Settings.CollectAudit);
            Assert.IsFalse(context.CaptureCatalog().Any(item => item.Name == AuditInstrumentation.MetricName));
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            Assert.AreEqual(0, context.DefinitionCount);
        }
        finally
        {
            await StopAndDisposeAsync(host);
        }
    }

    [TestMethod]
    public void AuditProfileCannotShrinkAwayItsAcceptedFiniteCombinations()
    {
        var options = new MetricsOptions();
        options.Streams.Add(AuditInstrumentation.MetricName, new()
        {
            CardinalityLimit = 8
        });
        MetricsSettings settings = new MetricsSettings(options).ForComposition(false, hasAudit: true);

        Assert.ThrowsExactly<ArgumentException>(() => new MetricsStreamCatalog(settings));
    }

    [TestMethod]
    public async Task ActualCaptureAndHttpExporterKeepCentralIdentityAndExcludeSourceData()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AuditMetricsIntegrationTests).Assembly.FullName,
            EnvironmentName = "AuditMetrics"
        });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        RegisterMetadata(builder.Services);
        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCisMetrics();
        await app.StartAsync().WaitAsync(CollectionTestHost.Deadline);
        try
        {
            AuditInstrumentation instrumentation = app.Services.GetRequiredService<AuditInstrumentation>();
            await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
            await using var capture = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), TimeProvider.System, budget, instrumentation);
            var identities = new List<string>();
            foreach (AuditCategory category in Enum.GetValues<AuditCategory>())
            {
                var definition = new AuditEventDefinition<int>("private.event.name", 1, category, new Codec());
                CapturedAuditRecord record = capture.Capture(definition, 42, new(AuditOutcome.Succeeded,
                    correlationId: "private-correlation", target: new("app", "test", "secret-resource", "private-resource")), null);
                identities.Add(record.AuditId.ToString("D"));
            }
            Assert.ThrowsExactly<ArgumentException>(() => capture.Capture(new AuditEventDefinition<int>("invalid.event", 1,
                AuditCategory.Business, new Codec { Fault = new ArgumentException("invalid selected input") }), 42, null, null));
            Assert.ThrowsExactly<InvalidOperationException>(() => capture.Capture(new AuditEventDefinition<int>("faulted.event", 1,
                AuditCategory.Security, new Codec { Fault = new InvalidOperationException("original details fault") }), 42, null, null));
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = CollectionTestHost.Deadline };

            string text = await client.GetStringAsync("/_metrics");

            CollectionText.AssertIdentity(text);
            string[] samples = CollectionText.Samples(text).Where(line => line.StartsWith("cis_audit_capture_results_total{", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(5, samples);
            Assert.AreEqual(5d, samples.Sum(CollectionText.Value));
            Assert.IsTrue(samples.Any(line => line.Contains("category=\"security\"", StringComparison.Ordinal)
                && line.Contains("outcome=\"faulted\"", StringComparison.Ordinal)));
            foreach (string value in identities.Concat(["private.event.name", "private-correlation", "secret-resource", "private-resource", "original details fault"]))
            {
                Assert.IsFalse(text.Contains(value, StringComparison.Ordinal));
            }
            Assert.IsFalse(text.Contains("audit_delivery", StringComparison.Ordinal));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
    }

    [TestMethod]
    public async Task DefinitionOnlyHostStopJoinsActualCaptureBeforeClosingItsCollector()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        RegisterMetadata(builder.Services);
        builder.Services.AddCisMetrics();
        IHost host = builder.Build();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            AuditInstrumentation instrumentation = host.Services.GetRequiredService<AuditInstrumentation>();
            AuditHostingRegistration registration = host.Services.GetRequiredService<AuditHostingRegistration>();
            await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
            await using var capture = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), TimeProvider.System, budget, instrumentation);
            registration.Bind(capture);
            long measurements = 0;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, current) =>
            {
                if (ReferenceEquals(instrument.Meter, context.CisMeter) && instrument.Name == AuditInstrumentation.MetricName)
                {
                    current.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref measurements, value));
            listener.Start();
            var definition = new AuditEventDefinition<int>("app.fact", 1, AuditCategory.Business, new HoldingCodec(entered, release));
            Task<CapturedAuditRecord> pending = Task.Run(() => capture.Capture(definition, 42, null, null));
            try
            {
                Assert.IsTrue(entered.Wait(CollectionTestHost.Deadline));
                Task stop = host.StopAsync(CancellationToken.None);
                Assert.IsFalse(stop.IsCompleted);
                Assert.IsFalse(context.IsStopping, "The provider must remain available for its admitted capture's terminal observation.");
                release.Set();
                Assert.AreNotEqual(Guid.Empty, (await pending.WaitAsync(CollectionTestHost.Deadline)).AuditId);
                await stop.WaitAsync(CollectionTestHost.Deadline);
                Assert.AreEqual(1L, Volatile.Read(ref measurements));
                Assert.IsTrue(context.IsStopping);
            }
            finally
            {
                release.Set();
                await pending.WaitAsync(CollectionTestHost.Deadline);
            }
        }
        finally
        {
            release.Set();
            await StopAndDisposeAsync(host);
        }
    }

    private static void RegisterMetadata(IServiceCollection services)
    {
        // Only the optional collector boundary is selected; no production writer/store/broker is replaced.
        var registration = new AuditHostingRegistration(new("audit"));
        services.AddSingleton(registration);
        services.AddSingleton(root => new AuditInstrumentation(registration, root.GetService<TimeProvider>(), root.GetService<ILoggerFactory>()));
    }

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

    private class Codec : IAuditDetailsCodec<int>
    {
        internal Exception? Fault
        {
            get; init;
        }
        public void Validate(int details)
        {
            if (Fault is { } fault)
            {
                throw fault;
            }
        }
        public virtual void Write(Utf8JsonWriter writer, int details)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", details);
            writer.WriteEndObject();
        }
    }

    private sealed class HoldingCodec(ManualResetEventSlim entered, ManualResetEventSlim release) : Codec
    {
        public override void Write(Utf8JsonWriter writer, int details)
        {
            entered.Set();
            release.Wait();
            base.Write(writer, details);
        }
    }
}
