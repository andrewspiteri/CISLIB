using System.Data.Common;
using CIS.Messaging;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using RabbitMQ.Client;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagePublicationIntegrationTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ActualOriginalTransactionControlsVisibilityAndBrokerPublication(bool commit)
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync();
        MessagingSnapshot snapshot = Snapshot(broker);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, broker.Local);
        await using var publisher = new MessageOutboxPublisher(snapshot, database.Store, database.Factory,
            broker.Transport, resources, broker.Local);
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await broker.Transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                byte[] bytes = delivery.Body.ToArray();
                Assert.IsNotNull(await delivery.GetNativeEvidenceAsync(token));
                await delivery.AcknowledgeAsync(token);
                received.TrySetResult(bytes);
            }
            catch (Exception error)
            {
                received.TrySetException(error);
                delivery.RequestConsumerStop("fixture.publication_capture_failed");
            }
        });
        try
        {
            await using ILocalTransaction original = await database.Factory.BeginAsync("Messaging");
            MessageStageResult staged = await outbox.PublishAsync(original.Context, "events", 42);
            Assert.AreEqual(MessageStageDisposition.Staged, staged.Disposition);
            MessageStageReceipt receipt = staged.Receipt!;
            await database.InsertBusinessAsync(original.Context, receipt.MessageId, "publication");
            byte[] expected = await ReadWireAsync(original.Context, database, receipt.RecordId);
            Assert.AreEqual(0L, await database.CountAsync("outbox"));
            Assert.AreEqual(0L, await database.CountAsync("business_record"));
            Assert.AreEqual(0, (await publisher.PumpAsync()).Attempted);
            Assert.IsFalse(received.Task.IsCompleted, "An active original transaction cannot publish its outgoing row.");

            TransactionCompletion completion = commit ? await original.CommitAsync() : await original.RollbackAsync();
            Assert.AreEqual(commit ? TransactionOutcome.Committed : TransactionOutcome.NotCommitted, completion.Outcome);
            MessagePumpResult pumped = await publisher.PumpAsync();
            if (commit)
            {
                Assert.AreEqual(1, pumped.Confirmed);
                Assert.AreEqual(TransactionOutcome.Committed, pumped.ReservationCompletion!.Outcome);
                Assert.AreEqual(TransactionOutcome.Committed, pumped.Attempts.Single().SettlementCompletion!.Outcome);
                CollectionAssert.AreEqual(expected, await received.Task.WaitAsync(TimeSpan.FromSeconds(20)));
                Assert.AreEqual(1L, await database.CountAsync("business_record"));
                await AssertStateAsync(database, broker, receipt, "outbox.published");
            }
            else
            {
                Assert.AreEqual(0, pumped.Attempted);
                Assert.AreEqual(0L, await database.CountAsync("outbox"));
                Assert.AreEqual(0L, await database.CountAsync("business_record"));
                await using IConnection connection = await broker.ConnectAsync();
                await using IChannel channel = await connection.CreateChannelAsync();
                Assert.AreEqual(0U, await channel.MessageCountAsync(broker.Queue));
                Assert.IsFalse(received.Task.IsCompleted);
            }
        }
        finally
        {
            await outbox.StopAsync();
        }
    }

    [TestMethod]
    public async Task SimulatedLostConfirmationAfterRealBrokerAcceptanceRetainsAndRepublishesIdenticalWire()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync();
        MessagingSnapshot snapshot = Snapshot(broker);
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, broker.Local);
        var client = new MessageSubmissionClient(outbox, database.Factory);
        var simulatedLoss = new LoseFirstConfirmation(broker.Transport);
        await using var publisher = new MessageOutboxPublisher(snapshot, database.Store, database.Factory,
            simulatedLoss, resources, broker.Local);
        var first = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        int deliveries = 0;
        await broker.Transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                byte[] bytes = delivery.Body.ToArray();
                await delivery.AcknowledgeAsync(token);
                (Interlocked.Increment(ref deliveries) == 1 ? first : second).TrySetResult(bytes);
            }
            catch (Exception error)
            {
                first.TrySetException(error);
                second.TrySetException(error);
                delivery.RequestConsumerStop("fixture.publication_capture_failed");
            }
        });
        try
        {
            MessageSubmission submission = await client.PublishAsync("events", 7);
            Assert.AreEqual(MessageSubmissionOutcome.OutboxCommitted, submission.Outcome);
            MessageStageReceipt receipt = submission.Receipt!;
            MessagePumpResult uncertain = await publisher.PumpAsync();
            Assert.AreEqual(1, simulatedLoss.ActualConfirmations);
            Assert.AreEqual(1, uncertain.Unknown);
            byte[] acceptedBytes = await first.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await AssertStateAsync(database, broker, receipt, "outbox.pending");
            await RabbitMqMessageFixture.UntilAsync(() => EligibleAsync(database, receipt.RecordId));
            MessagePumpResult recovered = await publisher.PumpAsync();
            Assert.AreEqual(1, recovered.Confirmed);
            Assert.AreEqual(2, simulatedLoss.ActualConfirmations);
            CollectionAssert.AreEqual(acceptedBytes, await second.Task.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.IsTrue(MessageWire.TryDecode(acceptedBytes, out EncodedMessage? envelope, out _));
            Assert.AreEqual(receipt.MessageId, envelope!.Envelope.MessageId);
            Assert.AreEqual(receipt.Digest, envelope.Digest);
            await AssertStateAsync(database, broker, receipt, "outbox.published");
        }
        finally
        {
            await outbox.StopAsync();
        }
    }

    private static MessagingSnapshot Snapshot(RabbitMqMessageFixture broker)
        => new(new MessagingOptions(), broker.Snapshot.Routes, [new("Messaging", "Messaging")]);

    private static async Task<byte[]> ReadWireAsync(ILocalTransactionContext context, PostgresMessageFixture database, Guid recordId)
        => await context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT envelope FROM {database.Table("outbox")} WHERE record_id=@id";
            DbParameter id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = recordId;
            command.Parameters.Add(id);
            return (byte[])(await command.ExecuteScalarAsync(token))!;
        });

    private static async Task AssertStateAsync(PostgresMessageFixture database, RabbitMqMessageFixture broker,
        MessageStageReceipt receipt, string state)
    {
        await using ILocalTransaction read = await database.Factory.BeginAsync("Messaging");
        Assert.AreEqual(MessageSubmissionObservation.Found, await database.Store.ReadSubmissionAsync(read.Context, receipt));
        IReadOnlyList<MessageInspectionRecord> rows = await database.Store.InspectAsync(read.Context,
            new(broker.Local.Application, broker.Local.Environment, RouteAlias: "events"));
        MessageInspectionRecord record = rows.Single(item => item.RecordId == receipt.RecordId);
        Assert.AreEqual(state, record.State);
        Assert.AreEqual(receipt.MessageId, record.MessageId);
        await read.RollbackAsync();
    }

    private static async Task<bool> EligibleAsync(PostgresMessageFixture database, Guid recordId)
    {
        await using NpgsqlConnection connection = await database.Source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT state=0 AND eligible_at<=clock_timestamp() FROM {database.Table("outbox")} WHERE record_id=@id", connection);
        command.Parameters.AddWithValue("id", recordId);
        return await command.ExecuteScalarAsync() is true;
    }

    // A narrow test seam after an actual native confirmation, not a network-fault or broker-failover proof.
    private sealed class LoseFirstConfirmation(IMessagePublisher actual) : IMessagePublisher
    {
        internal int ActualConfirmations { get; private set; }
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
