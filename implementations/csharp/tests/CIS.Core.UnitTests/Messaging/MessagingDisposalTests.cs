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

    // These private bounded substitutes establish disposal ownership, never native broker or SQL evidence.
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
        public void Flag(HealthDiagnostic? diagnostic = null) { }
        public void Clear() { }
        public void ReportMeasurement(HealthMeasurement measurement) => throw new NotSupportedException();
        public void Withdraw() { }
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
        internal int Reservations;
        internal int Observations;
        public Task<MessageStoreObservation> ObserveAsync(ILocalTransactionContext context, MessageObservationRequest request, CancellationToken cancellationToken = default)
        {
            Observations++;
            return Task.FromResult(new MessageStoreObservation(DateTimeOffset.UnixEpoch, 0, null, true));
        }
        public async Task<IReadOnlyList<MessageReservation>> ReserveAsync(ILocalTransactionContext context, MessageReservationRequest request, CancellationToken cancellationToken = default)
        {
            Reservations++;
            ReserveEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
}
