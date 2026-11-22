using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using CIS.Messaging;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessagingFootprintTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task FixedDurableWorkloadRecordsFootprintWhileActualPublicationDispatchAndCleanupRemainBounded()
    {
        const int messages = 16;
        const int concurrency = 4;
        const int payloadBytes = 4096;
        const int byteCapacity = 1_048_576;
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        await database.SqlAsync($"CREATE TABLE {database.Table("footprint_effect")} (total integer NOT NULL); INSERT INTO {database.Table("footprint_effect")} VALUES (0)");
        var codec = new FootprintCodec();
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        var handlersEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandlers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int handlerCalls = 0;
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
        {
            var subscription = new MessageSubscriptionDefinition<string>("A", "Messaging", "events", codec,
                _ => new FootprintHandler(async (context, value, _) =>
                {
                    Assert.AreEqual(payloadBytes - 2, value.Length);
                    if (Interlocked.Increment(ref handlerCalls) == concurrency)
                    {
                        handlersEntered.TrySetResult();
                    }
                    // Hold before the one shared counter update, so row locking cannot prevent the first wave's admission.
                    await releaseHandlers.Task;
                    await context.Transaction.UseAdoNetAsync(async (connection, transaction, token) =>
                    {
                        await using DbCommand command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = $"UPDATE {database.Table("footprint_effect")} SET total=total+1";
                        Assert.AreEqual(1, await command.ExecuteNonQueryAsync(token));
                    });
                }), (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(identity.Application, identity.Environment)), ["fixture.handle"]);
            return new MessagingSnapshot(new MessagingOptions
            {
                MaximumPayloadBytes = payloadBytes,
                MaximumConcurrentDeliveries = concurrency,
                PublisherConcurrency = concurrency,
                PublisherBatchSize = messages,
                MaximumEncodedBufferBytes = byteCapacity
            }, [new MessageRouteDefinition<string>("events", MessageKind.Event, codec), initial.GetRoute("commands")],
                [new("Messaging", "Messaging")], [subscription]);
        });
        MessagingSnapshot snapshot = broker.Snapshot;
        await using var resources = new MessagingResourceBudget(snapshot.Settings);
        var outbox = new MessageOutbox(snapshot, database.Store, resources, identity);
        await using var publisher = new MessageOutboxPublisher(snapshot, database.Store, database.Factory,
            broker.Transport, resources, identity);
        await using var dispatcher = new MessageDispatcher(broker.Services, snapshot, database.Store, database.Factory,
            resources, identity, new MessageDeliveryDispositionCoordinator(snapshot, database.Store, database.Factory, identity));
        var expected = new Dictionary<Guid, byte[]>(messages);
        var received = new ConcurrentDictionary<Guid, byte>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbackCount = 0;
        await broker.Transport.StartAsync(async (delivery, token) =>
        {
            try
            {
                Guid recordId = delivery.Metadata.OutboxRecordId ?? throw new InvalidDataException("The native delivery lost its outgoing identity.");
                if (!expected.TryGetValue(recordId, out byte[]? retained))
                {
                    throw new InvalidDataException("The fixed workload received an unrecognized outgoing identity.");
                }
                Assert.IsTrue(delivery.Body.Span.SequenceEqual(retained), "Native publication must preserve the exact committed wire bytes.");
                MessageDispatchResult result = await dispatcher.DispatchAsync(delivery, token);
                Assert.AreEqual(MessageDispatchOutcome.Handled, result.Outcome);
                Assert.AreEqual(TransactionOutcome.Committed, result.Completion!.Outcome);
                Assert.IsTrue(received.TryAdd(recordId, 0));
                int count = Interlocked.Increment(ref callbackCount);
                Assert.IsLessThanOrEqualTo(messages, count);
                if (count == messages)
                {
                    completed.TrySetResult();
                }
            }
            catch (Exception error)
            {
                completed.TrySetException(error);
                handlersEntered.TrySetException(error);
                delivery.RequestConsumerStop("fixture.footprint_failed");
            }
        });
        try
        {
            AssertBudget(resources, byteCapacity, 0);
            // Collection is a managed-memory observation only. Explicit module/native barriers establish cleanup below.
            long beforeManaged = GC.GetTotalMemory(forceFullCollection: true);
            long beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            string value = new('x', payloadBytes - 2);
            await using (ILocalTransaction original = await database.Factory.BeginAsync("Messaging"))
            {
                for (int index = 0; index < messages; index++)
                {
                    MessageStageResult staged = await outbox.PublishAsync(original.Context, "events", value);
                    Assert.AreEqual(MessageStageDisposition.Staged, staged.Disposition);
                    expected.Add(staged.Receipt!.RecordId, await ReadWireAsync(original.Context, database, staged.Receipt.RecordId));
                }
                Assert.AreEqual(TransactionOutcome.Committed, (await original.CommitAsync()).Outcome);
            }
            int wireLength = expected.Values.First().Length;
            Assert.IsTrue(expected.Values.All(bytes => bytes.Length == wireLength));
            MessagePumpResult published = await publisher.PumpAsync();
            Assert.AreEqual(messages, published.Confirmed);
            Assert.IsTrue(published.Attempts.All(attempt => attempt.Result?.Disposition == MessagePublicationDisposition.Confirmed
                && attempt.SettlementCompletion?.Outcome == TransactionOutcome.Committed));
            await handlersEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(concurrency, Volatile.Read(ref handlerCalls));
            int duringBytes = checked(concurrency * 4 * wireLength);
            AssertBudget(resources, byteCapacity, duringBytes);
            long duringManaged = GC.GetTotalMemory(forceFullCollection: false);
            releaseHandlers.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await dispatcher.StopAsync();
            await publisher.StopAsync();
            await outbox.StopAsync();
            await broker.Transport.StopAsync();
            AssertBudget(resources, byteCapacity, 0);
            Assert.AreEqual(messages, handlerCalls);
            Assert.AreEqual(messages, received.Count);
            Assert.AreEqual(messages, await CounterAsync(database));
            Assert.AreEqual((long)messages, await database.CountAsync("processing"));
            Assert.AreEqual((long)messages, await database.CountAsync("outbox"));
            await using IConnection connection = await broker.ConnectAsync();
            await using IChannel channel = await connection.CreateChannelAsync();
            Assert.AreEqual(0U, await channel.MessageCountAsync(broker.Queue));
            long afterManaged = GC.GetTotalMemory(forceFullCollection: true);
            long afterAllocated = GC.GetTotalAllocatedBytes(precise: true);
            TestContext.WriteLine(JsonSerializer.Serialize(new
            {
                workload = "fixed-local-pg-rabbit-publication-dispatch",
                messages,
                payloadBytes,
                concurrency,
                elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds,
                managedBytes = new
                {
                    before = beforeManaged,
                    during = duringManaged,
                    after = afterManaged
                },
                managedRetainedDeltaBytes = afterManaged - beforeManaged,
                managedAllocatedDeltaBytes = afterAllocated - beforeAllocated,
                encodedAdmissionBytes = new
                {
                    capacity = byteCapacity,
                    before = 0,
                    during = duringBytes,
                    after = 0
                },
                committedWireBytes = expected.Values.Sum(bytes => (long)bytes.Length),
                nativeConfirmed = published.Confirmed,
                committedEffects = messages,
                finalReadyQueueMessages = 0,
                limits = "One fixed local workload; managed samples include retained fixture/pool/test state. No throughput or release qualification threshold."
            }));
        }
        finally
        {
            releaseHandlers.TrySetResult();
            await outbox.StopAsync();
            await broker.Transport.StopAsync();
        }
    }

    // Probe only at quiescent phase barriers; no production capacity counters, reflection or polling history is added.
    private static void AssertBudget(MessagingResourceBudget resources, int capacity, int occupied)
    {
        Assert.IsTrue(resources.TryAcquireBytes(capacity - occupied, out IDisposable? available));
        using (available)
        {
            bool admitted = resources.TryAcquireBytes(1, out IDisposable? unexpected);
            unexpected?.Dispose();
            Assert.IsFalse(admitted);
        }
    }

    private static Task<byte[]> ReadWireAsync(ILocalTransactionContext context, PostgresMessageFixture database, Guid recordId)
        => context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT envelope FROM {database.Table("outbox")} WHERE record_id=@id";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = recordId;
            command.Parameters.Add(parameter);
            return (byte[])(await command.ExecuteScalarAsync(token))!;
        });

    private static async Task<int> CounterAsync(PostgresMessageFixture database)
    {
        await using DbConnection connection = await database.Source.OpenConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT total FROM {database.Table("footprint_effect")}";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private sealed class FootprintCodec : IMessageCodec<string>
    {
        public MessageContract Contract { get; } = new("footprint.text", 1);
        public byte[] Encode(string value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out string? value)
        {
            value = JsonSerializer.Deserialize<string>(json.Span);
            return contract == Contract;
        }
    }

    private sealed class FootprintHandler(Func<MessageHandlerContext, string, CancellationToken, Task> handler) : IMessageHandler<string>
    {
        public Task HandleAsync(MessageHandlerContext context, string value, CancellationToken cancellationToken = default)
            => handler(context, value, cancellationToken);
    }
}
