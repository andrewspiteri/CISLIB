using System.Diagnostics.Metrics;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Metrics;
using CIS.Security;
using CIS.Startup.Hosting;
using CIS.Tests.UnitTests.Metrics;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagingLifetimeTests
{
    [TestMethod]
    public async Task SettledMessagingFaultStillDrainsTheExistingStartupBridgeMetricsOwner()
    {
        var snapshot = new MessagingSnapshot(new MessagingOptions(),
            [new MessageRouteDefinition<int>("events", MessageKind.Event, new PublicationNumberCodec())], [new("Messaging", "Ledger")]);
        var store = new PublicationStore();
        var transactions = new ScriptedLocalTransactionFactory();
        var identity = LocalApplicationIdentity.Capture();
        await using ServiceProvider dispatcherServices = new ServiceCollection().BuildServiceProvider();
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, store, resources, identity);
        var original = new InvalidOperationException("The settled messaging cutoff failed.");
        var transport = new HeldStopTransport(original);
        transport.ReleaseCleanup.TrySetResult();
        await using var publisher = new MessageOutboxPublisher(snapshot, store, transactions, transport, resources, identity);
        await using var dispatcher = new MessageDispatcher(dispatcherServices, snapshot, store, transactions, resources, identity);
        var messaging = new MessagingRuntime(snapshot, new(snapshot), store, transactions, transport, resources,
            outbox, publisher, dispatcher, new(snapshot), identity);
        using var meter = new Meter("Messaging.BridgeDrain");
        await using var metrics = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metrics.RegisterObservableGauge(new("app.messaging.held"), () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("The controlled metric callback was not released.");
            }
            return [new MetricObservation(1)];
        });
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(messaging);
        builder.Services.AddSingleton<IMetrics>(metrics);
        builder.Services.AddCisStartup();
        IHost host = builder.Build();
        Task? collection = null;
        Task? stopping = null;
        try
        {
            await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<AggregateException>(() => messaging.StopAsync());
            collection = Task.Run(listener.Collect);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stopping = host.StopAsync(CancellationToken.None);
            Assert.IsTrue(metrics.IsStopping, "A settled messaging fault cannot bypass the bridge's remaining Metrics drain.");
            Assert.IsFalse(stopping.IsCompleted);
            release.Set();
            await collection.WaitAsync(TimeSpan.FromSeconds(10));
            AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsTrue(failure.Flatten().InnerExceptions.Contains(original));
        }
        finally
        {
            release.Set();
            if (collection is not null)
            {
                await collection;
            }
            try
            {
                await (stopping ?? host.StopAsync(CancellationToken.None));
            }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Contains(original))
            {
            }
            try
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
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Contains(original))
            {
            }
            try
            {
                await messaging.DisposeAsync();
            }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Contains(original))
            {
            }
        }
    }

    [TestMethod]
    public async Task SynchronousConsumerStopFaultCannotBypassAcceptedStagingOrActualNativeCleanup()
    {
        var codec = new PublicationNumberCodec();
        var snapshot = new MessagingSnapshot(new MessagingOptions(),
            [new MessageRouteDefinition<int>("events", MessageKind.Event, codec)], [new("Messaging", "Ledger")]);
        var store = new PublicationStore();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        var identity = LocalApplicationIdentity.Capture();
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, store, resources, identity);
        var original = new InvalidOperationException("The scripted adapter rejected consumer stop synchronously.");
        var transport = new HeldStopTransport(original);
        await using var publisher = new MessageOutboxPublisher(snapshot, store, transactions, transport, resources, identity);
        await using var dispatcher = new MessageDispatcher(services, snapshot, store, transactions, resources, identity);
        var runtime = new MessagingRuntime(snapshot, new(snapshot), store, transactions, transport, resources,
            outbox, publisher, dispatcher, new(snapshot), identity);
        var stageEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.StageHold = () =>
        {
            stageEntered.TrySetResult();
            return releaseStage.Task;
        };
        await using ILocalTransaction owner = await transactions.BeginAsync("Ledger");
        Task<MessageStageResult> stage = outbox.PublishAsync(owner.Context, "events", 7);
        Task? stop = null;
        try
        {
            await stageEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop = runtime.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(transport.CleanupEntered.Task.IsCompleted);
            Assert.AreEqual(1, transport.ConsumerStops);
            releaseStage.TrySetResult();
            Assert.AreEqual(MessageStageDisposition.Staged, (await stage).Disposition);
            await transport.CleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(stop.IsCompleted, "Actual native cleanup remains owned after a synchronous consumer cutoff failure.");
            Assert.IsTrue(resources.TryAcquireDelivery(out IDisposable? stillOwned));
            stillOwned!.Dispose();
            transport.ReleaseCleanup.TrySetResult();
            AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => stop.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsTrue(failure.Flatten().InnerExceptions.Contains(original));
            Assert.AreSame(stop, runtime.StopAsync());
            Assert.IsFalse(resources.TryAcquireDelivery(out _));
        }
        finally
        {
            releaseStage.TrySetResult();
            transport.ReleaseCleanup.TrySetResult();
            await stage;
            try
            {
                await runtime.DisposeAsync();
            }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Contains(original))
            {
            }
        }
    }

    // A private lifetime substitute only; it establishes no broker or connection-recovery evidence.
    private sealed class HeldStopTransport(Exception stopFailure) : IMessageTransport
    {
        internal readonly TaskCompletionSource CleanupEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ConsumerStops;
        public MessageReplyRoute? ReplyEndpoint => null;
        public bool IsReady => false;
        public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No startup is expected in this lifetime case.");
        public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No publication is expected in this lifetime case.");
        public Task StopConsumersAsync()
        {
            ConsumerStops++;
            throw stopFailure;
        }
        public Task StopAsync()
        {
            CleanupEntered.TrySetResult();
            return ReleaseCleanup.Task;
        }
        public ValueTask DisposeAsync() => new(StopAsync());
    }
}
