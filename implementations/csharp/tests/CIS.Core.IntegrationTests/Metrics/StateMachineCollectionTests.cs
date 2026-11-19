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
public sealed class StateMachineCollectionTests
{
    [TestMethod]
    [DataRow(false, false, 24, 6288)]
    [DataRow(false, true, 30, 7376)]
    [DataRow(true, false, 27, 6736)]
    [DataRow(true, true, 33, 7824)]
    public async Task ExplicitStateMachineCompositionReservesItsOwnProfileInEitherRegistrationOrder(
        bool pipeline, bool rest, int streams, int capacity)
    {
        foreach (bool metricsFirst in new[] { false, true })
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            if (metricsFirst)
            {
                builder.Services.AddCisMetrics();
            }

            builder.Services.AddCisStateMachines();
            if (pipeline)
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
                Assert.AreEqual(streams, context.CaptureCatalog().Count);
                Assert.AreEqual(capacity, context.CaptureCatalog().Sum(stream => stream.CardinalityLimit));
                Assert.AreEqual(0, context.DefinitionCount);
                Assert.AreEqual(pipeline, context.Settings.CollectWorkflows);
                Assert.IsTrue(context.Settings.CollectStateMachines);
                await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
                Assert.AreEqual(3 + (pipeline ? 3 : 0) + (rest ? 4 : 0), context.DefinitionCount);
                CollectionAssert.AreEqual(new[] { "machine", "outcome", "source", "trigger" }, context.CaptureCatalog()
                    .Single(stream => stream.Name == "cis.statemachine.trigger.duration").DimensionKeys.ToArray());
                CollectionAssert.AreEqual(new[] { "handler", "machine", "outcome", "phase" }, context.CaptureCatalog()
                    .Single(stream => stream.Name == "cis.statemachine.handler.duration").DimensionKeys.ToArray());
            }
            finally
            {
                await StopAndDispose(host);
            }
        }
    }

    [TestMethod]
    [DataRow("cis.statemachine.trigger.duration", 2559)]
    [DataRow("cis.statemachine.active.attempts", 15)]
    [DataRow("cis.statemachine.handler.duration", 383)]
    public async Task InadequateStateMachineCapacityFailsBeforeProducerActivation(string name, int capacity)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisMetrics(options => options.Streams.Add(name, new() { CardinalityLimit = capacity }));
        builder.Services.AddCisStateMachines();
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
    [DataRow(false, 19, 21, 3328)]
    [DataRow(true, 22, 24, 3776)]
    public async Task NoStateMachineOptInPreservesExistingExplicitSmallProfiles(bool pipeline, int definitions, int streams, int capacity)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (pipeline)
        {
            builder.Services.AddCisWorkflows();
        }
        builder.Services.AddCisMetrics(options =>
        {
            options.MaxDefinitions = definitions;
            options.MaxStreams = streams;
            options.TotalCardinalityLimit = capacity;
        });
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.IsFalse(context.Settings.CollectStateMachines);
            Assert.AreEqual(streams, context.CaptureCatalog().Count);
            Assert.AreEqual(capacity, context.CaptureCatalog().Sum(stream => stream.CardinalityLimit));
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task DisabledOrUnselectedAutomaticCollectionDoesNotChangeARealDirectTransition(bool enabled, bool modules)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddCisStateMachines();
        builder.Services.AddCisMetrics(options =>
        {
            options.Enabled = enabled;
            options.CollectCoreModules = modules;
        });
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            var definition = host.Services.GetRequiredService<IStateMachineFactory>()
                .CreateBuilder<OrderState, OrderTrigger, object>("direct", new object());
            definition.State(OrderState.Draft).On(OrderTrigger.Submit, OrderState.Submitted);
            IStateMachine<OrderState, OrderTrigger> machine = definition.Build(OrderState.Draft);
            Assert.IsTrue((await machine.Trigger(OrderTrigger.Submit)).Success);
            Assert.AreEqual(OrderState.Submitted, machine.CurrentState);
            MetricsCollectionContext? context = host.Services.GetService<MetricsCollectionContext>();
            if (enabled)
            {
                Assert.IsNotNull(context);
                Assert.AreEqual(0, context.DefinitionCount);
            }
            else
            {
                Assert.IsNull(context);
                Assert.IsNull(host.Services.GetService<global::OpenTelemetry.Metrics.MeterProvider>());
            }
        }
        finally
        {
            await StopAndDispose(host);
        }
    }

    [TestMethod]
    public async Task ActualPublicCallsExportAllOutcomesAndKeepActiveThroughCompensationAndCleanup()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero));
        var context = new StateContext(clock);
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(context);
        builder.Services.AddCisStateMachines();
        builder.Services.AddCisMetrics(options => options.ScrapeCacheDuration = TimeSpan.Zero);
        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseCisMetrics();
        Task<StateTransitionResult<OrderState, OrderTrigger>>? pending = null;
        try
        {
            await app.StartAsync().WaitAsync(CollectionTestHost.Deadline);
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = CollectionTestHost.Deadline };
            var definition = app.Services.GetRequiredService<IStateMachineFactory>()
                .CreateBuilder<OrderState, OrderTrigger, StateContext>("orders", context);
            definition.State(OrderState.Draft).On<SubmitHandler>(OrderTrigger.Submit, OrderState.Submitted, "submit")
                .On(OrderTrigger.invalid, OrderState.Draft);
            definition.State(OrderState.Submitted).On(OrderTrigger.Finish, OrderState.Done);
            IStateMachine<OrderState, OrderTrigger> machine = definition.Build(OrderState.Draft);
            pending = machine.Trigger(OrderTrigger.Submit);
            await context.Entered.Task.WaitAsync(CollectionTestHost.Deadline);
            Assert.AreEqual(StateTransitionOutcome.Busy, (await machine.Trigger(OrderTrigger.Submit)).Outcome);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.AreEqual(StateTransitionOutcome.Cancelled, (await machine.Trigger(OrderTrigger.Submit, cancellation.Token)).Outcome);
            string held = await Scrape(client);
            Assert.AreEqual(1d, Sum(held, "cis_statemachine_active_attempts"));
            Assert.AreEqual(OrderState.Draft, machine.CurrentState);
            context.Release.TrySetResult();
            StateTransitionResult<OrderState, OrderTrigger> failure = await pending.WaitAsync(CollectionTestHost.Deadline);
            Assert.AreEqual(StateTransitionOutcome.Failed, failure.Outcome);
            Assert.AreEqual(TimeSpan.FromSeconds(2.75), failure.Duration);
            Assert.AreEqual(StateTransitionOutcome.Rejected, (await machine.Trigger((OrderTrigger)int.MaxValue)).Outcome);
            Assert.IsTrue((await machine.Trigger(OrderTrigger.invalid)).Success);
            context.Fail = false;
            StateTransitionResult<OrderState, OrderTrigger> success = await machine.Trigger(OrderTrigger.Submit);
            Assert.AreEqual(TimeSpan.FromSeconds(2.25), success.Duration);
            Assert.IsTrue((await machine.Trigger(OrderTrigger.Finish)).Success);
            Assert.AreEqual(OrderState.Done, machine.CurrentState);
            Assert.AreEqual(2, context.Disposals);
            Assert.AreEqual(1, context.Compensations);

            string text = await Scrape(client);
            CollectionText.AssertIdentity(text);
            Assert.AreEqual(7d, Sum(text, "cis_statemachine_trigger_duration_seconds_count"));
            Assert.AreEqual(5d, Sum(text, "cis_statemachine_trigger_duration_seconds_sum"));
            Assert.AreEqual(3d, Sum(text, "cis_statemachine_handler_duration_seconds_count"));
            Assert.AreEqual(4.5, Sum(text, "cis_statemachine_handler_duration_seconds_sum"));
            Assert.AreEqual(0d, Sum(text, "cis_statemachine_active_attempts"));
            foreach (string outcome in new[] { "success", "rejected", "busy", "error", "cancelled" })
            {
                Assert.IsTrue(CollectionText.Samples(text).Any(line => line.StartsWith("cis_statemachine_trigger_duration_seconds_count{", StringComparison.Ordinal)
                    && line.Contains("outcome=\"" + outcome + "\"", StringComparison.Ordinal)), text);
            }
            Assert.IsTrue(text.Contains("trigger=\"enum:invalid\"", StringComparison.Ordinal));
            Assert.IsTrue(text.Contains("trigger=\"invalid\"", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            MetricsCollectionContext collection = app.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(3, collection.DefinitionCount);
            foreach (MetricsStreamSnapshot stream in collection.CaptureCatalog().Where(stream => stream.Name.StartsWith("cis.statemachine.", StringComparison.Ordinal)))
            {
                Assert.IsTrue(stream.Selected);
                if (stream.Name.EndsWith("duration", StringComparison.Ordinal))
                {
                    CollectionAssert.AreEqual(MetricsStreamCatalog.DurationBounds.ToArray(), stream.HistogramBounds.ToArray());
                }
            }
        }
        finally
        {
            context.Release.TrySetResult();
            try
            {
                if (pending is not null)
                {
                    await pending.WaitAsync(CollectionTestHost.Deadline);
                }
            }
            finally
            {
                await StopAndDispose(app);
            }
        }
    }

    private static double Sum(string text, string name) => CollectionText.Samples(text)
        .Where(line => line.StartsWith(name + "{", StringComparison.Ordinal) && line.Contains("machine=\"orders\"", StringComparison.Ordinal)).Sum(CollectionText.Value);

    private static async Task<string> Scrape(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/_metrics").WaitAsync(CollectionTestHost.Deadline);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
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

    public enum OrderState
    {
        Draft, Submitted, Done
    }
    public enum OrderTrigger
    {
        Submit, Finish, invalid
    }

    public sealed class StateContext(ManualTimeProvider clock)
    {
        internal ManualTimeProvider Clock { get; } = clock;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Fail { get; set; } = true;
        internal int Disposals
        {
            get; set;
        }
        internal int Compensations
        {
            get; set;
        }
    }

    public sealed class SubmitHandler(StateContext owner) : IStateTransitionHandler<OrderState, OrderTrigger, StateContext>, IAsyncDisposable
    {
        public async Task<TransitionActionResult> Execute(StateContext context, TransitionInfo<OrderState, OrderTrigger> transition, CancellationToken cancellationToken)
        {
            context.Clock.Advance(TimeSpan.FromSeconds(2));
            context.Entered.TrySetResult();
            await context.Release.Task;
            return context.Fail ? TransitionActionResult.Failed("orders.declined", "Controlled expected failure.") : TransitionActionResult.Succeeded();
        }
        public Task Error(StateContext context, TransitionInfo<OrderState, OrderTrigger> transition, StateTransitionFailure failure, CancellationToken cancellationToken)
        {
            context.Clock.Advance(TimeSpan.FromSeconds(0.5));
            context.Compensations++;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            owner.Clock.Advance(TimeSpan.FromSeconds(0.25));
            owner.Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
