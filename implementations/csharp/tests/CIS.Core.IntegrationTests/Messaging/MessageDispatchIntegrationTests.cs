using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using CIS.Messaging;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Operations.PostgreSql;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[DoNotParallelize]
public sealed partial class MessageDispatchIntegrationTests
{
    [TestMethod]
    public async Task AReplyOnAnOrdinaryRouteCommitsExactQuarantineAndTheSameConsumerHandlesTheLegitimateIdentity()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var effects = new ConcurrentBag<NativeEventHandler>();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            Events(initial, effects, ["A"], (context, _, _) =>
                database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "A")),
            configureServices: services => services.AddScoped(_ => new NativeHandlerScope()));
        await using var resources = new MessagingResourceBudget(broker.Snapshot.Settings);
        await using var dispatcher = new MessageDispatcher(broker.Services, broker.Snapshot, database.Store,
            database.Factory, resources, broker.Local,
            new MessageDeliveryDispositionCoordinator(broker.Snapshot, database.Store, database.Factory, broker.Local));
        await using var publisher = new MessageOutboxPublisher(broker.Snapshot, database.Store, database.Factory,
            broker.Transport, resources, broker.Local);
        var capture = new DispatchCapture(dispatcher, 2);
        await broker.Transport.StartAsync(capture.ReceiveAsync);
        EncodedMessage misrouted = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, new("fixture.number", 1),
            broker.Local.Application, broker.Local.Environment, DateTimeOffset.UtcNow, JsonSerializer.SerializeToUtf8Bytes(7),
            requestAttemptId: Guid.NewGuid(), inReplyToMessageId: Guid.NewGuid()));

        await broker.PublishRawAsync(misrouted.CopyBytes(), "service");
        await capture.WaitForCountAsync();

        Assert.AreEqual(0L, await database.CountAsync("processing"));
        Assert.AreEqual(0L, await database.CountAsync("business_record"));
        Assert.AreEqual(1L, await database.CountAsync("rejections"));
        Assert.AreEqual(1L, await database.CountAsync("outbox"));
        Assert.AreEqual(0, effects.Count);
        Assert.IsTrue(broker.Transport.IsReady);
        MessagePumpResult publication = await publisher.PumpAsync();
        Assert.AreEqual(1, publication.Confirmed);
        Assert.AreEqual(TransactionOutcome.Committed, publication.Attempts.Single().SettlementCompletion!.Outcome);
        await using (var connection = await broker.ConnectAsync())
        {
            await using var channel = await connection.CreateChannelAsync();
            await RabbitMqMessageFixture.UntilAsync(async () => await channel.MessageCountAsync(broker.QuarantineQueue) == 1);
            var quarantined = await channel.BasicGetAsync(broker.QuarantineQueue, false);
            Assert.IsNotNull(quarantined);
            CollectionAssert.AreEqual(misrouted.CopyBytes(), quarantined.Body.ToArray());
            Assert.IsTrue(MessageWire.TryDecode(quarantined.Body, out EncodedMessage? retained, out _));
            Assert.AreEqual(misrouted.Digest, retained!.Digest);
            Assert.AreEqual(MessageKind.Reply, retained.Envelope.Kind);
            Assert.AreEqual(misrouted.Envelope.InReplyToMessageId, retained.Envelope.InReplyToMessageId);
            await channel.BasicAckAsync(quarantined.DeliveryTag, false);
        }

        EncodedMessage legitimate = MessageWire.Encode(new(misrouted.Envelope.MessageId, MessageKind.Event,
            misrouted.Envelope.Contract, broker.Local.Application, broker.Local.Environment,
            misrouted.Envelope.CreatedAtUtc, JsonSerializer.SerializeToUtf8Bytes(7)));
        await broker.PublishRawAsync(legitimate.CopyBytes(), "service");
        MessageDispatchResult[] results = await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.AreEqual(MessageDispatchOutcome.Terminal, results[0].Outcome);
        Assert.AreEqual("messaging.delivery_metadata_invalid", results[0].SafeCode);
        Assert.AreEqual(TransactionOutcome.Committed, results[0].Completion!.Outcome);
        Assert.AreEqual(MessageDispatchOutcome.Handled, results[1].Outcome);
        Assert.AreEqual(1, effects.Count);
        Assert.AreEqual(1L, await database.CountAsync("business_record"));
        Assert.AreEqual(1L, await database.CountAsync("processing"));
        Assert.AreEqual(1L, await database.CountAsync("rejections"));
        Assert.IsTrue(broker.Transport.IsReady);
    }

    [TestMethod]
    public async Task IndependentSubscriptionsAndReconstructedRuntimeSuppressConcurrentAndLaterTransportDuplicates()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        await database.SqlAsync($"CREATE TABLE {database.Table("effect_counter")} (subscription text NOT NULL,total integer NOT NULL); INSERT INTO {database.Table("effect_counter")} VALUES ('A',0),('B',0)");
        var effects = new ConcurrentBag<NativeEventHandler>();
        MessageOutbox? outgoing = null;
        Func<MessagingSnapshot, MessagingSnapshot> compose = initial => Events(initial, effects, ["A", "B"],
            async (context, _, _) =>
            {
                await IncrementCounterAsync(context.Transaction, database,
                    context.Services.GetRequiredService<NativeHandlerScope>().Subscription);
                Assert.AreEqual(MessageStageDisposition.Staged,
                    (await outgoing!.SendAsync(context.Transaction, "commands", 7)).Disposition);
            });
        await using RabbitMqMessageFixture first = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: compose,
            configureServices: services => services.AddScoped(_ => new NativeHandlerScope()));
        await using RabbitMqMessageFixture competing = first.CreateSibling(withRequests: false);
        await using var resources = new MessagingResourceBudget(first.Snapshot.Settings);
        outgoing = new(first.Snapshot, database.Store, resources, first.Local);
        var disposition = new MessageDeliveryDispositionCoordinator(first.Snapshot, database.Store, database.Factory, first.Local);
        await using var dispatcher = new MessageDispatcher(first.Services, first.Snapshot, database.Store,
            database.Factory, resources, first.Local, disposition);
        await using var peerResources = new MessagingResourceBudget(competing.Snapshot.Settings);
        await using var peerDispatcher = new MessageDispatcher(competing.Services, competing.Snapshot, database.Store,
            database.Factory, peerResources, competing.Local,
            new MessageDeliveryDispositionCoordinator(competing.Snapshot, database.Store, database.Factory, competing.Local));
        var firstCapture = new DispatchCapture(dispatcher, 4);
        await first.Transport.StartAsync(firstCapture.ReceiveAsync);
        await competing.Transport.StartAsync((delivery, token) => firstCapture.ReceiveWithAsync(peerDispatcher, delivery, token));
        EncodedMessage message = first.Message(7);
        await Task.WhenAll(first.PublishRawAsync(message.CopyBytes(), "service"), first.PublishRawAsync(message.CopyBytes(), "service"));
        MessageDispatchResult[] initial = await firstCapture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.AreEqual(2, initial.Count(item => item.Outcome == MessageDispatchOutcome.Handled));
        Assert.AreEqual(2, initial.Count(item => item.Outcome == MessageDispatchOutcome.Duplicate));
        Assert.AreEqual(2, await CounterTotalAsync(database));
        Assert.AreEqual(2L, await database.CountAsync("processing"));
        Assert.AreEqual(2L, await database.CountAsync("outbox"));
        await first.Transport.StopAsync();
        await competing.Transport.StopAsync();
        await dispatcher.StopAsync();
        await peerDispatcher.StopAsync();
        Assert.IsTrue(effects.All(item => item.Disposed));

        await using RabbitMqMessageFixture restarted = first.CreateSibling(withRequests: false);
        await using var secondResources = new MessagingResourceBudget(restarted.Snapshot.Settings);
        await using var second = new MessageDispatcher(restarted.Services, restarted.Snapshot, database.Store,
            database.Factory, secondResources, restarted.Local,
            new MessageDeliveryDispositionCoordinator(restarted.Snapshot, database.Store, database.Factory, restarted.Local));
        var recoveredCapture = new DispatchCapture(second, 2);
        await restarted.Transport.StartAsync(recoveredCapture.ReceiveAsync);
        await restarted.PublishRawAsync(message.CopyBytes(), "service");
        Assert.IsTrue((await recoveredCapture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30)))
            .All(item => item.Outcome == MessageDispatchOutcome.Duplicate));
        Assert.AreEqual(2, effects.Count);
        Assert.AreEqual(2, await CounterTotalAsync(database));
        Assert.AreEqual(2L, await database.CountAsync("outbox"));
        await outgoing.StopAsync();
    }

    [TestMethod]
    public async Task UnknownAfterRealCommitLeavesSourceUnacknowledgedAndFreshRuntimeRecoversOneEffect()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var effects = new ConcurrentBag<NativeEventHandler>();
        await using RabbitMqMessageFixture first = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            Events(initial, effects, ["A"], (context, _, _) =>
                database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "A")),
            configureServices: services => services.AddScoped(_ => new NativeHandlerScope()));
        await using var resources = new MessagingResourceBudget(first.Snapshot.Settings);
        await using var dispatcher = new MessageDispatcher(first.Services, first.Snapshot, database.Store, database.Factory,
            resources, first.Local, new MessageDeliveryDispositionCoordinator(first.Snapshot, database.Store, database.Factory, first.Local));
        var capture = new DispatchCapture(dispatcher, 1);
        await first.Transport.StartAsync(capture.ReceiveAsync);
        database.Boundary.FailAfterNextCommit();
        await first.PublishRawAsync(first.Message(7).CopyBytes(), "service");
        MessageDispatchResult uncertain = (await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30))).Single();
        Assert.AreEqual(MessageDispatchOutcome.Unknown, uncertain.Outcome);
        Assert.AreEqual(TransactionOutcome.Unknown, uncertain.Completion!.Outcome);
        Assert.AreEqual(1L, await database.CountAsync("business_record"));
        Assert.AreEqual(1L, await database.CountAsync("processing"));
        Assert.AreEqual(0L, await database.CountAsync("outbox"));
        await first.Transport.StopAsync();
        await dispatcher.StopAsync();
        await using RabbitMqMessageFixture recovered = first.CreateSibling(withRequests: false);
        await using var recoveryResources = new MessagingResourceBudget(recovered.Snapshot.Settings);
        await using var recovery = new MessageDispatcher(recovered.Services, recovered.Snapshot, database.Store,
            database.Factory, recoveryResources, recovered.Local,
            new MessageDeliveryDispositionCoordinator(recovered.Snapshot, database.Store, database.Factory, recovered.Local));
        var duplicate = new DispatchCapture(recovery, 1);
        await recovered.Transport.StartAsync(duplicate.ReceiveAsync);
        Assert.AreEqual(MessageDispatchOutcome.Duplicate,
            (await duplicate.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30))).Single().Outcome);
        Assert.AreEqual(1, effects.Count);
        Assert.AreEqual(1L, await database.CountAsync("business_record"));
    }

    [TestMethod]
    public async Task DeniedProducerAndChangedWireUseSeparateRejectionsWithoutPoisoningTheLegitimateCanonicalIdentity()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        var effects = new ConcurrentBag<NativeEventHandler>();
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            Events(initial, effects, ["A"], (context, _, _) =>
                database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "A")),
            configureServices: services => services.AddScoped(_ => new NativeHandlerScope()));
        await using var resources = new MessagingResourceBudget(broker.Snapshot.Settings);
        await using var dispatcher = new MessageDispatcher(broker.Services, broker.Snapshot, database.Store, database.Factory,
            resources, broker.Local, new MessageDeliveryDispositionCoordinator(broker.Snapshot, database.Store, database.Factory, broker.Local));
        var capture = new DispatchCapture(dispatcher, 3);
        await broker.Transport.StartAsync(capture.ReceiveAsync);
        EncodedMessage original = broker.Message(7);
        await broker.PublishRawAsync(original.CopyBytes(), "other");
        await RabbitMqMessageFixture.UntilAsync(async () => await database.CountAsync("rejections") == 1);
        Assert.AreEqual(0L, await database.CountAsync("processing"));
        await broker.PublishRawAsync(original.CopyBytes(), "service");
        await RabbitMqMessageFixture.UntilAsync(async () => await database.CountAsync("business_record") == 1);
        EncodedMessage changed = MessageWire.Encode(new(original.Envelope.MessageId, MessageKind.Event,
            original.Envelope.Contract, broker.Local.Application, broker.Local.Environment, original.Envelope.CreatedAtUtc,
            JsonSerializer.SerializeToUtf8Bytes(8)));
        Assert.AreNotEqual(original.Digest, changed.Digest);
        await broker.PublishRawAsync(changed.CopyBytes(), "service");
        await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.AreEqual(1, effects.Count);
        Assert.AreEqual(1L, await database.CountAsync("business_record"));
        Assert.AreEqual(1L, await database.CountAsync("processing"));
        Assert.AreEqual(2L, await database.CountAsync("rejections"));
        await using ILocalTransaction read = await database.Factory.BeginAsync("Messaging");
        MessageProcessingRecord retained = (await database.Store.ReadProcessingAsync(read.Context,
            new(broker.Local.Application, broker.Local.Environment, "A", original.Envelope.MessageId)))!;
        Assert.AreEqual(original.Digest, retained.Digest);
        Assert.AreEqual(MessageProcessingDisposition.Handled, retained.Disposition);
        await read.RollbackAsync();
    }

    [TestMethod]
    public async Task NewMessageIdReplaysTheOriginalOperationWhileChangedBusinessInputTerminatesWithoutAnotherEffect()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        string operationSchema = database.Schema + "_op";
        int effects = 0;
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var codec = new NativeNumberCodec();
        var operation = new OperationDefinition<int, string>("Submit", "Messaging", new("dispatch.input", 1),
            new NativeNormalizer(), new NativeResultCodec(), _ => new NativeScopeResolver(),
            new()
            {
                ExecuteCapabilities = ["dispatch.execute"],
                LookupCapabilities = ["dispatch.lookup"]
            });
        var request = new MessageRequestDefinition<int, string>("SubmitRequest", "commands", codec,
            new NativeResponseCodec(), MessageRequestMode.DurableCommand, local.Application, local.Environment, "Submit");
        try
        {
            await database.SqlAsync(PostgreSqlOperationSchema.GetInstallationSql(operationSchema));
            await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(
                transformSnapshot: initial => new(new MessagingOptions(), initial.Routes, [new("Messaging", "Messaging")],
                    [new MessageRequestSubscriptionDefinition<int, string>("A", "Messaging", request,
                        _ => new NativeCommandHandler(async (context, value, _) =>
                        {
                            Interlocked.Increment(ref effects);
                            await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "operation");
                            return OperationDecision<string>.Succeed("original:" + value);
                        }), (_, _, _, _) => ValueTask.FromResult(Resource(local)), ["fixture.handle"])], [request]),
                configureServices: services =>
                {
                    services.AddSingleton<ILocalTransactionFactory>(database.Factory);
                    services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal(new()
                    {
                        Schema = operationSchema
                    }));
                    services.AddCisOperations();
                    services.AddCisOperation(operation);
                }, configureSecurity: options =>
                {
                    SecurityProfile profile = options.Profiles.Single();
                    SecuritySubject service = options.Grants.Single().Subject!;
                    options.Capabilities.Add("dispatch.execute");
                    options.Capabilities.Add("dispatch.lookup");
                    foreach (string capability in new[] { "fixture.handle", "dispatch.execute", "dispatch.lookup" })
                    {
                        options.Grants.Add(new(profile.Alias, capability, local.Application, local.Environment, subject: service,
                            resourceSelection: CapabilityResourceSelection.ExactResource, resourceType: "ledger", resourceIdentifier: "account"));
                    }
                });
            await using var resources = new MessagingResourceBudget(broker.Snapshot.Settings);
            await using var dispatcher = new MessageDispatcher(broker.Services, broker.Snapshot, database.Store,
                database.Factory, resources, local,
                new MessageDeliveryDispositionCoordinator(broker.Snapshot, database.Store, database.Factory, local));
            var capture = new DispatchCapture(dispatcher, 4);
            await broker.Transport.StartAsync(capture.ReceiveAsync);
            EncodedMessage first = Command(local, codec, 7);
            foreach (EncodedMessage message in new[] { first, Command(local, codec, 7), Command(local, codec, 8), first })
            {
                Assert.AreEqual(MessagePublicationDisposition.Confirmed,
                    (await broker.Transport.PublishAsync(new(message, new("commands"), MessageRecordPurpose.Application, 1, Guid.NewGuid()))).Disposition);
                await capture.WaitForCountAsync();
            }
            MessageDispatchResult[] results = await capture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(2, results.Count(item => item.Outcome == MessageDispatchOutcome.Handled));
            Assert.AreEqual(1, results.Count(item => item.Outcome == MessageDispatchOutcome.Terminal));
            Assert.AreEqual(1, results.Count(item => item.Outcome == MessageDispatchOutcome.Duplicate));
            Assert.AreEqual(1, effects);
            Assert.AreEqual(1L, await database.CountAsync("business_record"));
            Assert.AreEqual(3L, await database.CountAsync("processing"));
            await database.SqlAsync($"DO $check$ BEGIN IF (SELECT count(*) FROM \"{operationSchema}\".operation_journal WHERE finalized) <> 1 THEN RAISE EXCEPTION 'Expected one original operation'; END IF; END $check$");
        }
        finally
        {
            // This second generated schema belongs only to this case; the database fixture owns the first.
            await database.SqlAsync($"DROP SCHEMA IF EXISTS \"{operationSchema}\" CASCADE");
        }
    }

    private static MessagingSnapshot Events(MessagingSnapshot initial,
        ConcurrentBag<NativeEventHandler> effects, string[] aliases, Func<MessageHandlerContext, int, CancellationToken, Task> handler)
        => new(new MessagingOptions(), initial.Routes, [new("Messaging", "Messaging")], aliases.Select(alias =>
            new MessageSubscriptionDefinition<int>(alias, "Messaging", "events", new NativeNumberCodec(),
                services =>
                {
                    var scoped = new NativeEventHandler(alias, handler);
                    effects.Add(scoped);
                    return services.GetRequiredService<NativeHandlerScope>().Own(scoped);
                }, (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(LocalApplicationIdentity.Capture().Application,
                    LocalApplicationIdentity.Capture().Environment)), ["fixture.handle"])));

    private static CapabilityTarget Resource(LocalApplicationIdentity local) => new(local.Application, local.Environment, "ledger", "account");

    private static Task IncrementCounterAsync(ILocalTransactionContext context, PostgresMessageFixture database, string subscription)
        => context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"UPDATE {database.Table("effect_counter")} SET total=total+1 WHERE subscription=@subscription";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "subscription";
            parameter.Value = subscription;
            command.Parameters.Add(parameter);
            Assert.AreEqual(1, await command.ExecuteNonQueryAsync(token));
        });

    private static async Task<int> CounterTotalAsync(PostgresMessageFixture database)
    {
        await using DbConnection connection = await database.Source.OpenConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT sum(total)::integer FROM {database.Table("effect_counter")}";
        return (int)(await command.ExecuteScalarAsync())!;
    }
    private static EncodedMessage Command(LocalApplicationIdentity local, NativeNumberCodec codec, int value)
        => MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, codec.Contract, local.Application, local.Environment,
            DateTimeOffset.UtcNow, codec.Encode(value), operation: new("Submit", "requested-account", "stable-key")));

    private sealed class DispatchCapture(MessageDispatcher dispatcher, int expected)
    {
        private readonly object gate = new();
        private readonly MessageDispatchResult[] results = new MessageDispatchResult[expected];
        private int observed;
        private int expectedNext;
        internal TaskCompletionSource<MessageDispatchResult[]> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task ReceiveAsync(IMessageDelivery delivery, CancellationToken token) => ReceiveWithAsync(dispatcher, delivery, token);
        internal async Task ReceiveWithAsync(MessageDispatcher selected, IMessageDelivery delivery, CancellationToken token)
        {
            try
            {
                MessageDispatchResult result = await selected.DispatchAsync(delivery, token);
                lock (gate)
                {
                    if (observed >= results.Length)
                    {
                        throw new InvalidOperationException("The fixed publication set produced more callbacks than this case permits.");
                    }
                    results[observed++] = result;
                    if (observed == expected)
                    {
                        Completed.TrySetResult(results.ToArray());
                    }
                }
            }
            catch (Exception error)
            {
                Completed.TrySetException(error);
                delivery.RequestConsumerStop("fixture.dispatch_failed");
            }
        }
        internal Task WaitForCountAsync()
        {
            int next = ++expectedNext;
            return RabbitMqMessageFixture.UntilAsync(() =>
            {
                if (Completed.Task.IsFaulted)
                {
                    return Task.FromException<bool>(Completed.Task.Exception!.InnerException!);
                }
                return Task.FromResult(Volatile.Read(ref observed) >= next);
            });
        }
    }

    private sealed class NativeNumberCodec : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("fixture.number", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }
    private sealed class NativeResponseCodec : IMessageCodec<string>
    {
        public MessageContract Contract { get; } = new("fixture.response", 1);
        public byte[] Encode(string value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out string? value)
        {
            value = JsonSerializer.Deserialize<string>(json.Span);
            return contract == Contract;
        }
    }
    private sealed class NativeEventHandler(string subscription, Func<MessageHandlerContext, int, CancellationToken, Task> handler) : IMessageHandler<int>, IAsyncDisposable
    {
        internal string Subscription => subscription;
        internal bool Disposed;
        public Task HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default) => handler(context, value, cancellationToken);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
    private sealed class NativeHandlerScope : IAsyncDisposable
    {
        private NativeEventHandler? handler;
        internal string Subscription => handler!.Subscription;
        internal NativeEventHandler Own(NativeEventHandler owned) => handler = owned;
        public ValueTask DisposeAsync() => handler?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
    private sealed class NativeCommandHandler(Func<MessageHandlerContext, int, CancellationToken, Task<OperationDecision<string>>> handler) : IRequestHandler<int, string>
    {
        public Task<OperationDecision<string>> HandleAsync(MessageHandlerContext context, int value, CancellationToken cancellationToken = default)
            => handler(context, value, cancellationToken);
    }
    private sealed class NativeNormalizer : IOperationRequestNormalizer<int>
    {
        public NormalizedOperationInput<int> Normalize(int input, OperationScope scope)
            => new(input, [new(new("dispatch.input", 1), OperationCanonicalValue.Integer(input))]);
    }
    private sealed class NativeResultCodec : IOperationResultCodec<string>
    {
        public OperationContract CurrentContract { get; } = new("dispatch.result", 1);
        public IReadOnlyList<OperationContract> ReadableContracts { get; } = [new("dispatch.result", 1)];
        public byte[] Encode(string? value) => Encoding.UTF8.GetBytes(value!);
        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out string? decoded)
        {
            decoded = Encoding.UTF8.GetString(value.Span);
            return contract == CurrentContract;
        }
    }
    private sealed class NativeScopeResolver : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new OperationScope("trusted-account", "ledger", "account"));
    }
}
