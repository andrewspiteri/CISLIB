using System.Collections.Concurrent;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.Messaging;
using CIS.Security;
using CIS.Startup.Hosting;
using CIS.Tests.IntegrationTests.Messaging;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[DoNotParallelize]
public sealed class AuditDeliveryIntegrationTests
{
    [TestMethod]
    public async Task ActualGenericHostStartsAuditAfterNativeMessagingAndJoinsItOnStop()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var limits = new AuditLimits(4096);
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(withSubscription: false,
            transformSnapshot: initial => new(initial.Settings,
                initial.Routes.Append(new MessageRouteDefinition<AuditRecord>(AuditNativeTestSupport.Route,
                    MessageKind.Event, new AuditRecordCodec(limits))), [new("Messaging", "Messaging")], [], []));
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        AuditSecurityPressureIntegrationTests.RegisterNativeApplication(builder.Services, broker, database, limits);
        builder.Services.AddCisStartup();
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsTrue(host.Services.GetRequiredService<MessagingRuntime>().IsAccepting);
            await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            {
                CapturedAuditRecord record = scope.ServiceProvider.GetRequiredService<IAuditCapture>()
                    .CaptureLocalSystem(AuditNativeTestSupport.Definition, 17, new(AuditOutcome.Succeeded));
                AuditSubmissionResult submitted = await scope.ServiceProvider.GetRequiredService<IAuditWriter>().SubmitAsync(record);
                Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted, submitted.Outcome);
                Assert.AreEqual(TransactionOutcome.Committed, submitted.Completion?.Outcome);
                Assert.IsNotNull(submitted.Receipt);
                Assert.AreEqual(record.AuditId, submitted.AuditId);
                Assert.AreEqual(1L, await database.CountAsync("outbox"));
            }

            await host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsTrue(host.Services.GetRequiredService<AuditRuntime>().IsDrained);
            Assert.IsFalse(host.Services.GetRequiredService<MessagingRuntime>().IsAccepting);
        }
        finally
        {
            try
            {
                await host.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
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
    }

    [TestMethod]
    public async Task ReconstructedSourceRetainsRetryBytesAndDeliberateSameAuditSubmissionGetsANewMessageIdentity()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var limits = new AuditLimits(4096);
        var codec = new AuditRecordCodec(limits);
        var received = new ConcurrentQueue<AuditRecord>();
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            new(initial.Settings,
                initial.Routes.Where(route => route.Alias != "events")
                    .Append(new MessageRouteDefinition<AuditRecord>("events", MessageKind.Event, codec)),
                [new("Messaging", "Messaging")],
                [new MessageSubscriptionDefinition<AuditRecord>("A", "Messaging", "events", codec,
                    _ => new Receiver(database, received),
                    (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(local.Application, local.Environment)),
                    ["fixture.handle"])], []));

        CapturedAuditRecord captured;
        MessageStageReceipt firstReceipt;
        byte[] sourceWire;
        await using (var firstResources = new MessagingResourceBudget(broker.Snapshot.Settings))
        await using (var firstCapture = new AuditCaptureEngine(local, limits, TimeProvider.System, firstResources))
        {
            var firstOutbox = new MessageOutbox(broker.Snapshot, database.Store, firstResources, local);
            await using var firstWriter = new AuditWriter(new("events", limits), local, firstOutbox,
                new MessageSubmissionClient(firstOutbox, database.Factory));
            firstWriter.BeginAccepting();
            try
            {
                captured = AuditNativeTestSupport.Capture(firstCapture);
                AuditSubmissionResult committed = await firstWriter.SubmitAsync(captured);
                Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted, committed.Outcome);
                Assert.AreEqual(TransactionOutcome.Committed, committed.Completion?.Outcome);
                Assert.IsNotNull(committed.Receipt);
                firstReceipt = committed.Receipt;
                sourceWire = await AuditNativeTestSupport.ReadStoredWireAsync(database, firstReceipt.RecordId);
                Assert.AreEqual(1L, await database.CountAsync("outbox"));
                Assert.AreEqual(0L, await database.CountAsync("processing"));
            }
            finally
            {
                await firstOutbox.StopAsync();
            }
        }

        // Fresh source owners reuse only the committed store and the application's retained capture.
        // This is runtime reconstruction, not a process-death or broker-failover simulation.
        await using var resources = new MessagingResourceBudget(broker.Snapshot.Settings);
        var outbox = new MessageOutbox(broker.Snapshot, database.Store, resources, local);
        await using var writer = new AuditWriter(new("events", limits), local, outbox,
            new MessageSubmissionClient(outbox, database.Factory));
        writer.BeginAccepting();
        await using var dispatcher = new MessageDispatcher(broker.Services, broker.Snapshot, database.Store,
            database.Factory, resources, local,
            new MessageDeliveryDispositionCoordinator(broker.Snapshot, database.Store, database.Factory, local));
        var simulatedLoss = new LoseFirstConfirmation(broker.Transport);
        await using var publisher = new MessageOutboxPublisher(broker.Snapshot, database.Store, database.Factory,
            simulatedLoss, resources, local);
        TaskCompletionSource<DeliveryObservation>[] delivered = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<DeliveryObservation>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        int deliveries = 0;
        await broker.Transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                int index = Interlocked.Increment(ref deliveries) - 1;
                if (index >= delivered.Length)
                {
                    throw new InvalidOperationException("The bounded Audit workload permits exactly three native deliveries.");
                }
                byte[] original = delivery.Body.ToArray();
                MessageDispatchResult result = await dispatcher.DispatchAsync(delivery, token);
                delivered[index].TrySetResult(new(original, result));
            }
            catch (Exception error)
            {
                foreach (TaskCompletionSource<DeliveryObservation> pending in delivered)
                {
                    pending.TrySetException(error);
                }
                delivery.RequestConsumerStop("fixture.audit_delivery_failed");
            }
        });
        try
        {
            MessagePumpResult uncertainPublication = await publisher.PumpAsync();
            Assert.AreEqual(1, uncertainPublication.Unknown);
            Assert.AreEqual(1, simulatedLoss.ActualConfirmations);
            DeliveryObservation first = await delivered[0].Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(MessageDispatchOutcome.Handled, first.Result.Outcome);
            Assert.AreEqual(TransactionOutcome.Committed, first.Result.Completion?.Outcome);
            CollectionAssert.AreEqual(sourceWire, first.Wire);

            await RabbitMqMessageFixture.UntilAsync(() => EligibleAsync(database, firstReceipt.RecordId));
            Assert.AreEqual(1, (await publisher.PumpAsync()).Confirmed);
            DeliveryObservation retried = await delivered[1].Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(MessageDispatchOutcome.Duplicate, retried.Result.Outcome);
            Assert.AreEqual(2, simulatedLoss.ActualConfirmations);
            CollectionAssert.AreEqual(first.Wire, retried.Wire);
            Assert.IsTrue(MessageWire.TryDecode(retried.Wire, out EncodedMessage? retriedEnvelope, out _));
            Assert.AreEqual(firstReceipt.MessageId, retriedEnvelope!.Envelope.MessageId);
            Assert.AreEqual(firstReceipt.Digest, retriedEnvelope.Digest);

            AuditSubmissionResult deliberate = await writer.SubmitAsync(captured);
            Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted, deliberate.Outcome);
            Assert.IsNotNull(deliberate.Receipt);
            Assert.AreEqual(captured.AuditId, deliberate.AuditId);
            Assert.AreNotEqual(firstReceipt.MessageId, deliberate.Receipt.MessageId);
            Assert.AreNotEqual(firstReceipt.RecordId, deliberate.Receipt.RecordId);
            Assert.AreEqual(1, (await publisher.PumpAsync()).Confirmed);
            DeliveryObservation secondOccurrenceDelivery = await delivered[2].Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(MessageDispatchOutcome.Handled, secondOccurrenceDelivery.Result.Outcome);
            Assert.IsTrue(MessageWire.TryDecode(secondOccurrenceDelivery.Wire, out EncodedMessage? deliberateEnvelope, out _));
            Assert.AreEqual(deliberate.Receipt.MessageId, deliberateEnvelope!.Envelope.MessageId);
            CollectionAssert.AreEqual(captured.OriginalBytes.ToArray(), deliberateEnvelope.Envelope.CopyPayload());

            AuditRecord[] interpreted = received.ToArray();
            Assert.AreEqual(2, interpreted.Length, "Messaging suppresses its retry, while deliberate distinct MessageIds remain separate deliveries.");
            Assert.IsTrue(interpreted.All(record => record.AuditId == captured.AuditId));
            foreach (AuditRecord record in interpreted)
            {
                CollectionAssert.AreEqual(captured.OriginalBytes.ToArray(), record.OriginalBytes.ToArray());
            }
            Assert.AreEqual(2L, await database.CountAsync("processing"));
            Assert.AreEqual(2L, await database.CountAsync("business_record"));
            Assert.AreEqual(2L, await database.CountAsync("outbox"));
            Assert.IsTrue(broker.Transport.IsReady);
            // These are test-handler effects keyed by MessageId, not a destination AuditId repository.
            // The independent vectors separately declare future same-AuditId duplicate/conflict obligations.
        }
        finally
        {
            await broker.Transport.StopAsync();
            await dispatcher.StopAsync();
            await outbox.StopAsync();
        }
    }

    private static async Task<bool> EligibleAsync(PostgresMessageFixture database, Guid recordId)
    {
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT state=0 AND eligible_at<=clock_timestamp() FROM {database.Table("outbox")} WHERE record_id=@id", connection);
        command.Parameters.AddWithValue("id", recordId);
        return await command.ExecuteScalarAsync() is true;
    }

    private sealed record DeliveryObservation(byte[] Wire, MessageDispatchResult Result);

    private sealed class Receiver(PostgresMessageFixture database, ConcurrentQueue<AuditRecord> received) : IMessageHandler<AuditRecord>
    {
        public async Task HandleAsync(MessageHandlerContext context, AuditRecord record, CancellationToken cancellationToken = default)
        {
            // RM-04 has authenticated initial ingress or checked retained original provenance before this callback.
            Assert.AreEqual(context.Envelope.ProducerApplication, record.Producer.Application);
            Assert.AreEqual(context.Envelope.ProducerEnvironment, record.Producer.Environment);
            await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "audit-receiver-probe");
            received.Enqueue(record);
        }
    }

    // This seam follows an actual broker confirmation; it is not a network-fault claim.
    private sealed class LoseFirstConfirmation(IMessagePublisher actual) : IMessagePublisher
    {
        internal int ActualConfirmations
        {
            get; private set;
        }

        public async Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
        {
            MessagePublicationResult result = await actual.PublishAsync(publication, cancellationToken);
            if (result.Disposition == MessagePublicationDisposition.Confirmed && ++ActualConfirmations == 1)
            {
                return new(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient,
                    "fixture.simulated_confirmation_loss");
            }
            return result;
        }
    }
}
