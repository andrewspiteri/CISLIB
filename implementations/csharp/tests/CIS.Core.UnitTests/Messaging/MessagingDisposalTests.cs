using System.Data.Common;
using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagingDisposalTests
{
    [TestMethod]
    [DataRow("staging", true, true)]
    [DataRow("staging", false, true)]
    [DataRow("subscription", true, false)]
    [DataRow("subscription", false, true)]
    [DataRow("caller", true, false)]
    [DataRow("caller", false, true)]
    [DataRow("responder", true, false)]
    [DataRow("responder", false, true)]
    public async Task ReadinessUsesTheCapturedBrokerPolicyForEachConfiguredResponsibility(
        string profile, bool requireLiveBroker, bool readyWithoutBroker)
    {
        await WithReadinessRuntimeAsync(profile, requireLiveBroker, async (runtime, store, transport) =>
        {
            Assert.IsFalse(runtime.IsReady, "The broker policy cannot bypass startup.");
            await runtime.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await store.ReserveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, transport.Starts, "Every profile still establishes its selected transport at startup.");
            Assert.IsTrue(runtime.IsReady);

            transport.Ready = false;
            Assert.AreEqual(readyWithoutBroker, runtime.IsReady);
            transport.Ready = true;
            Assert.IsTrue(runtime.IsReady);

            runtime.BeginStopping();
            Assert.IsFalse(runtime.IsReady, "An optional live broker cannot bypass module cutoff.");
            await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(runtime.IsReady);
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AFailedStoreRefreshMakesReadinessFalseForEitherBrokerPolicy(bool requireLiveBroker)
    {
        await WithReadinessRuntimeAsync("responder", requireLiveBroker, async (runtime, store, transport) =>
        {
            await runtime.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await store.ReserveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(runtime.IsReady);
            store.FailObservation = true;
            store.ReleaseReservation.TrySetResult();
            await store.ObservationFailed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (runtime.IsReady)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token);
            }
            Assert.IsTrue(transport.IsReady, "This case loses only the store observation.");
            Assert.AreEqual(2, store.Observations);
            await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AFailedInitialStoreObservationCannotBeBypassedByTheBrokerPolicy(bool requireLiveBroker)
    {
        await WithReadinessRuntimeAsync("caller", requireLiveBroker, async (runtime, store, transport) =>
        {
            store.FailObservation = true;
            await Assert.ThrowsExactlyAsync<ReadinessObservationException>(
                () => runtime.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsFalse(runtime.IsReady);
            Assert.AreEqual(0, transport.Starts, "Sequential startup must reject the failed store before native transport startup.");
        });
    }

    [TestMethod]
    public async Task ConcurrentDisposalJoinsOneHeldMonitorCleanupAndPreservesStopAndCleanupFaultsAfterResourceDrain()
    {
        var codec = new PublicationNumberCodec();
        var snapshot = new MessagingSnapshot(new MessagingOptions(),
            [new MessageRouteDefinition<int>("events", MessageKind.Event, codec)], [new("Messaging", "Ledger")]);
        var store = new ReadinessStore();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        transactions.Enqueue();
        var identity = LocalApplicationIdentity.Capture();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, store, resources, identity);
        var stopFault = new InvalidOperationException("owned-transport-stop-fault");
        var cleanupFault = new IOException("owned-monitor-cleanup-fault");
        var transport = new DisposalTransport(stopFault);
        var health = new HeldConditionFactory(cleanupFault);
        await using var publisher = new MessageOutboxPublisher(snapshot, store, transactions, transport, resources, identity);
        await using var dispatcher = new MessageDispatcher(services, snapshot, store, transactions, resources, identity);
        var runtime = new MessagingRuntime(snapshot, new MessagingComposition(snapshot), store, transactions, transport,
            resources, outbox, publisher, dispatcher, new MessagingInstrumentation(snapshot), identity, health);
        try
        {
            await runtime.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await store.ReserveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, health.Created);
            Assert.IsTrue(runtime.IsReady);
            Task first = runtime.DisposeAsync().AsTask();
            await health.Monitor.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task second = runtime.DisposeAsync().AsTask();
            Assert.AreSame(first, second, "Concurrent callers must join the exact same owned disposal barrier.");
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse(runtime.IsAccepting);
            Assert.AreEqual(1, health.Monitor.Disposals);
            Assert.AreEqual(1, transport.Stops);
            Assert.AreEqual(1, transport.ConsumerStops);
            Assert.IsFalse(resources.TryAcquireDelivery(out _), "Shared resource drain precedes held monitor cleanup.");
            Assert.AreEqual(2, transactions.CreatedTransactions.Count);
            Assert.IsTrue(transactions.CreatedTransactions.All(owner => owner.DisposeRequested
                && owner.Completion?.Outcome == TransactionOutcome.NotCommitted));

            health.Monitor.ReleaseDispose.TrySetResult();
            AggregateException firstFault = await Assert.ThrowsExactlyAsync<AggregateException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
            AggregateException secondFault = await Assert.ThrowsExactlyAsync<AggregateException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)));
            foreach (AggregateException observed in new[] { firstFault, secondFault })
            {
                Exception[] failures = observed.Flatten().InnerExceptions.ToArray();
                Assert.HasCount(2, failures);
                Assert.IsTrue(failures.Any(error => ReferenceEquals(error, stopFault)));
                Assert.IsTrue(failures.Any(error => ReferenceEquals(error, cleanupFault)));
            }
            Assert.AreEqual(1, health.Monitor.Disposals);
            Assert.AreEqual(1, store.Reservations);
            Assert.AreEqual(1, store.Observations);
        }
        finally
        {
            health.Monitor.ReleaseDispose.TrySetResult();
            try
            {
                await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.All(fault => ReferenceEquals(fault, stopFault)
                || ReferenceEquals(fault, cleanupFault)))
            {
            }
        }
    }

    private static async Task WithReadinessRuntimeAsync(string profile, bool requireLiveBroker,
        Func<MessagingRuntime, ReadinessStore, ReadinessTransport, Task> check)
    {
        var codec = new PublicationNumberCodec();
        var request = new MessageRequestDefinition<int, int>("Query", "commands", codec, codec,
            MessageRequestMode.Query, "responder", "test");
        MessageSubscriptionDefinition[] subscriptions = profile switch
        {
            "subscription" => [new MessageSubscriptionDefinition<int>("A", "Ledger", "events", codec,
                _ => throw new InvalidOperationException("A readiness test must not construct a business handler."),
                (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("responder", "test")), ["fixture.handle"])],
            "responder" => [new MessageRequestSubscriptionDefinition<int, int>("A", "Ledger", request,
                _ => throw new InvalidOperationException("A readiness test must not construct a request handler."),
                (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget("responder", "test")), ["fixture.handle"])],
            _ => []
        };
        var snapshot = new MessagingSnapshot(new MessagingOptions
        {
            RequireLiveBrokerForReadiness = requireLiveBroker,
            IdlePollInterval = TimeSpan.FromMinutes(1)
        }, [new MessageRouteDefinition<int>("events", MessageKind.Event, codec),
            new MessageRouteDefinition<int>("commands", MessageKind.Command, codec)], [new("Messaging", "Ledger")],
            subscriptions, profile is "caller" or "responder" ? [request] : []);
        var store = new ReadinessStore();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        transactions.Enqueue();
        transactions.Enqueue();
        var identity = LocalApplicationIdentity.Capture();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        await using var resources = new MessagingResourceBudget(snapshot.Settings, snapshot.Requests.Count != 0);
        var outbox = new MessageOutbox(snapshot, store, resources, identity);
        var transport = new ReadinessTransport();
        await using var publisher = new MessageOutboxPublisher(snapshot, store, transactions, transport, resources, identity);
        await using var dispatcher = new MessageDispatcher(services, snapshot, store, transactions, resources, identity);
        await using var runtime = new MessagingRuntime(snapshot, new(snapshot), store, transactions, transport,
            resources, outbox, publisher, dispatcher, new(snapshot), identity);
        await check(runtime, store, transport);
    }

    // These private bounded substitutes establish readiness/disposal ownership, never native broker or SQL evidence.
    private sealed class ReadinessTransport : IMessageTransport
    {
        internal bool Ready = true;
        internal int Starts;
        public MessageReplyRoute? ReplyEndpoint => null;
        public bool IsReady => Ready;
        public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default)
        {
            Starts++;
            return Task.CompletedTask;
        }
        public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No publication is expected in a readiness case.");
        public Task StopConsumersAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisposalTransport(Exception stopFault) : IMessageTransport
    {
        internal int Stops;
        internal int ConsumerStops;
        public MessageReplyRoute? ReplyEndpoint => null;
        public bool IsReady => true;
        public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No publication is expected in the disposal case.");
        public Task StopConsumersAsync()
        {
            ConsumerStops++;
            return Task.CompletedTask;
        }
        public Task StopAsync()
        {
            Stops++;
            return Task.FromException(stopFault);
        }
        public ValueTask DisposeAsync() => new(StopAsync());
    }

    private sealed class HeldConditionFactory(Exception cleanupFault) : IHealthMonitorFactory
    {
        internal readonly HeldCondition Monitor = new(cleanupFault);
        internal int Created;
        public IConditionMonitor CreateCondition(ConditionMonitorOptions options)
        {
            Created++;
            Assert.AreEqual("Messaging", options.Name);
            return Monitor;
        }
        public IConsecutiveFailureMonitor CreateConsecutiveFailures(ConsecutiveFailureMonitorOptions options) => throw new NotSupportedException();
        public INumericThresholdMonitor CreateNumericThreshold(NumericThresholdMonitorOptions options) => throw new NotSupportedException();
        public IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options) => throw new NotSupportedException();
    }

    private sealed class HeldCondition(Exception cleanupFault) : IConditionMonitor
    {
        internal readonly TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals;
        public Guid MonitorId { get; } = Guid.NewGuid();
        public string? Name => "Messaging";
        public bool IsCritical => true;
        public void Flag(HealthDiagnostic? diagnostic = null)
        {
        }
        public void Clear()
        {
        }
        public void ReportMeasurement(HealthMeasurement measurement) => throw new NotSupportedException();
        public void Withdraw()
        {
        }
        public Task WaitForDisposalAsync(CancellationToken waitCancellation = default) => ReleaseDispose.Task.WaitAsync(waitCancellation);
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            DisposeEntered.TrySetResult();
            await ReleaseDispose.Task;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFault).Throw();
        }
    }

    private sealed class ReadinessStore : IMessagingStore
    {
        public Task<bool> AttachReplyAsync(ILocalTransactionContext context, MessageReplyAttachment attachment,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        internal readonly TaskCompletionSource ReserveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseReservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ObservationFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailObservation;
        internal int Reservations;
        internal int Observations;
        public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request, CancellationToken cancellationToken = default)
        {
            Observations++;
            if (FailObservation)
            {
                ObservationFailed.TrySetResult();
                return Task.FromException<MessageStoreObservation>(new ReadinessObservationException());
            }
            return Task.FromResult(new MessageStoreObservation(DateTimeOffset.UnixEpoch, 0, null, true));
        }
        public async Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request, CancellationToken cancellationToken = default)
        {
            Reservations++;
            ReserveEntered.TrySetResult();
            await ReleaseReservation.Task.WaitAsync(cancellationToken);
            return [];
        }
        public Task<MessageStageReceipt> StageAsync(ILocalTransactionContext context, MessageOutgoingRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EncodedMessage?> ReadReservedMessageAsync(ILocalTransactionContext context, MessageReservation reservation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RecordPublicationAsync(ILocalTransactionContext context, MessagePublicationUpdate update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageSubmissionObservation> ReadSubmissionAsync(ILocalTransactionContext context, MessageStageReceipt receipt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageProcessingRecord?> ReadProcessingAsync(ILocalTransactionContext context, MessageProcessingKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageProcessingClaim> ClaimProcessingAsync(ILocalTransactionContext context, MessageProcessingAttempt attempt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CompleteProcessingAsync(ILocalTransactionContext context, MessageProcessingCompletion completion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageDispositionResult> SecureDispositionAsync(ILocalTransactionContext context, MessageDispositionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordRejectionAsync(ILocalTransactionContext context, MessageRejection rejection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageRedriveResult> RedriveAsync(ILocalTransactionContext context, MessageRedriveTransition transition, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MessageInspectionRecord>> InspectAsync(ILocalTransactionContext context, MessageInspectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MessageMaintenanceCounts> MaintainAsync(ILocalTransactionContext context, MessageMaintenanceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ReadinessObservationException : DbException
    {
        internal ReadinessObservationException() : base("The bounded readiness substitute lost its store observation.")
        {
        }
    }
}
