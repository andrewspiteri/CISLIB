using CIS.CommonTesting;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.Workflows;
using CIS.Workflows.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class PipelineCollectionTests
{
    [TestMethod]
    [DataRow(false, false, 21, 3328)]
    [DataRow(false, true, 27, 4416)]
    [DataRow(true, false, 24, 3776)]
    [DataRow(true, true, 30, 4864)]
    public async Task ExplicitWorkflowOptInPreservesOldCatalogsAndSupportsBothRegistrationOrders(
        bool workflows, bool rest, int expectedStreams, int expectedCapacity)
    {
        foreach (bool metricsFirst in new[] { false, true })
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            if (metricsFirst)
            {
                builder.Services.AddCisMetrics();
            }

            if (workflows)
            {
                builder.Services.AddCisWorkflows();
            }

            if (rest)
            {
                builder.Services.AddCisRestClient(new("catalog")
                {
                    BaseUrl = "http://127.0.0.1:1/"
                });
            }

            if (!metricsFirst)
            {
                builder.Services.AddCisMetrics();
            }

            IHost host = builder.Build();
            try
            {
                MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
                Assert.AreEqual(expectedStreams, context.CaptureCatalog().Count);
                Assert.AreEqual(expectedCapacity, context.CaptureCatalog().Sum(stream => stream.CardinalityLimit));
                Assert.AreEqual(0, context.DefinitionCount, "Presence inspection does not activate producers.");
                await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
                Assert.AreEqual((workflows ? 3 : 0) + (rest ? 4 : 0), context.DefinitionCount);
                Assert.AreEqual(workflows, context.Settings.CollectWorkflows);
                if (workflows)
                {
                    Assert.AreEqual(48, context.CaptureCatalog().Single(stream => stream.Name == "cis.pipeline.execution.duration").CardinalityLimit);
                    Assert.AreEqual(16, context.CaptureCatalog().Single(stream => stream.Name == "cis.pipeline.active.executions").CardinalityLimit);
                    Assert.AreEqual(384, context.CaptureCatalog().Single(stream => stream.Name == "cis.pipeline.step.duration").CardinalityLimit);
                    CollectionAssert.AreEqual(new[] { "outcome", "pipeline" }, context.CaptureCatalog()
                        .Single(stream => stream.Name == "cis.pipeline.execution.duration").DimensionKeys.ToArray());
                    CollectionAssert.AreEqual(new[] { "outcome", "phase", "pipeline", "step" }, context.CaptureCatalog()
                        .Single(stream => stream.Name == "cis.pipeline.step.duration").DimensionKeys.ToArray());
                }
            }
            finally
            {
                await StopAndDispose(host);
            }
        }
    }

    [TestMethod]
    [DataRow("cis.pipeline.execution.duration", 47)]
    [DataRow("cis.pipeline.active.executions", 15)]
    [DataRow("cis.pipeline.step.duration", 383)]
    public async Task InadequateWorkflowCapacityFailsAtFinalCompositionBeforeProducerActivation(string name, int capacity)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.Streams.Add(name, new() { CardinalityLimit = capacity }));
        builder.Services.AddCisWorkflows();
        IHost host = builder.Build();
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Services.GetRequiredService<MetricsCollectionContext>());
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    public async Task ActualPipelineExportsEnteredPhasesExactSecondsAndOwnedCleanupBeforeFinalSettlement()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-11-18T00:00:00Z"));
        var context = new LookupContext(clock);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "PipelineCollection",
            ApplicationName = typeof(PipelineCollectionTests).Assembly.FullName,
        });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(context);
        builder.Services.AddCisWorkflows();
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCisMetrics();
        try
        {
            await app.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            IPipeline<LookupContext> pipeline = app.Services.GetRequiredService<IPipelineFactory>()
                .CreateBuilder("lookup", context)
                .Add<NormalizeStep>("normalize")
                .Add<FindStep>("find")
                .Add<UnenteredStep>("prepare_response")
                .Build();
            await pipeline.Execute().WaitAsync(CollectionTestHost.Deadline);
            Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
            Assert.AreEqual(TimeSpan.FromSeconds(4.125), pipeline.Duration);
            Assert.AreEqual(TimeSpan.FromSeconds(1), pipeline.Steps[0].ExecutionDuration);
            Assert.AreEqual(TimeSpan.FromSeconds(2), pipeline.Steps[1].ExecutionDuration);
            Assert.IsNull(pipeline.Steps[2].ExecutionDuration);
            Assert.AreEqual(3, context.Disposals);

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            using HttpResponseMessage response = await client.GetAsync("/_metrics").WaitAsync(CollectionTestHost.Deadline);
            response.EnsureSuccessStatusCode();
            string text = await response.Content.ReadAsStringAsync();
            CollectionText.AssertIdentity(text);
            string[] samples = CollectionText.Samples(text).Where(line => line.Contains("pipeline=\"lookup\"", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(1d, Sum("cis_pipeline_execution_duration_seconds_count{"));
            Assert.AreEqual(4.125, Sum("cis_pipeline_execution_duration_seconds_sum{"));
            Assert.AreEqual(4d, Sum("cis_pipeline_step_duration_seconds_count{"));
            Assert.AreEqual(3.75, Sum("cis_pipeline_step_duration_seconds_sum{"));
            Assert.AreEqual(0d, Sum("cis_pipeline_active_executions{"));
            Assert.IsTrue(samples.Any(line => line.StartsWith("cis_pipeline_active_executions{", StringComparison.Ordinal)));
            Assert.IsFalse(samples.Any(line => line.Contains("prepare_response", StringComparison.Ordinal)));
            Assert.IsFalse(text.Contains(typeof(NormalizeStep).FullName!, StringComparison.Ordinal));
            string[] buckets = samples.Where(line => line.StartsWith("cis_pipeline_step_duration_seconds_bucket{", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(60, buckets, "Four actual phase series each retain fourteen finite bounds and infinity.");
            MetricsCollectionContext collection = app.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(3, collection.DefinitionCount);
            foreach (MetricsStreamSnapshot stream in collection.CaptureCatalog().Where(stream => stream.Name.StartsWith("cis.pipeline.", StringComparison.Ordinal)))
            {
                Assert.IsTrue(stream.Selected);
                if (stream.Name.EndsWith("duration", StringComparison.Ordinal))
                {
                    CollectionAssert.AreEqual(MetricsStreamCatalog.DurationBounds.ToArray(), stream.HistogramBounds.ToArray());
                }
            }

            double Sum(string prefix) => samples.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Sum(CollectionText.Value);
        }
        finally
        {
            try
            {
                await app.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
    }

    private static async Task StopAndDispose(IHost host)
    {
        try
        {
            await host.StopAsync(CancellationToken.None).WaitAsync(CollectionTestHost.Deadline);
        }
        finally
        {
            if (host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
    }

    public sealed class LookupContext(ManualTimeProvider clock)
    {
        internal ManualTimeProvider Clock { get; } = clock;
        internal int Disposals
        {
            get; set;
        }
    }

    public sealed class NormalizeStep : IPipelineStep<LookupContext>, IAsyncDisposable
    {
        private LookupContext? context;
        public Task<PipelineStepResult> Execute(LookupContext context, CancellationToken cancellationToken)
        {
            this.context = context;
            context.Clock.Advance(TimeSpan.FromSeconds(1));
            return Task.FromResult(PipelineStepResult.Succeeded());
        }
        public Task Error(LookupContext context, PipelineFailure failure, CancellationToken cancellationToken)
        {
            context.Clock.Advance(TimeSpan.FromSeconds(0.25));
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Dispose(context!);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class FindStep : IPipelineStep<LookupContext>, IAsyncDisposable
    {
        private LookupContext? context;
        public Task<PipelineStepResult> Execute(LookupContext context, CancellationToken cancellationToken)
        {
            this.context = context;
            context.Clock.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult(PipelineStepResult.Failed("country.missing"));
        }
        public Task Error(LookupContext context, PipelineFailure failure, CancellationToken cancellationToken)
        {
            context.Clock.Advance(TimeSpan.FromSeconds(0.5));
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Dispose(context!);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class UnenteredStep(LookupContext context) : IPipelineStep<LookupContext>, IAsyncDisposable
    {
        public Task<PipelineStepResult> Execute(LookupContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The final forward method must remain unentered.");
        public Task Error(LookupContext context, PipelineFailure failure, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The final handler must remain unentered.");
        public ValueTask DisposeAsync()
        {
            Dispose(context);
            return ValueTask.CompletedTask;
        }
    }

    private static void Dispose(LookupContext context)
    {
        context.Clock.Advance(TimeSpan.FromSeconds(0.125));
        context.Disposals++;
    }
}
