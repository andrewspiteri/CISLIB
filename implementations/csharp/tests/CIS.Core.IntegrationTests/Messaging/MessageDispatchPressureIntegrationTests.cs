using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.Operations;
using CIS.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;

namespace CIS.Tests.IntegrationTests.Messaging;

public sealed partial class MessageDispatchIntegrationTests
{
    [TestMethod]
    public async Task MinimumRpcResponsePressureWaitsBeforeDecodingAndTheNativeConsumerContinues()
    {
        await using PostgresMessageFixture database = await PostgresMessageFixture.CreateAsync();
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var codec = new NativeNumberCodec();
        var request = new MessageRequestDefinition<int, string>("PressureQuery", "commands", codec,
            new NativeResponseCodec(), MessageRequestMode.Query, local.Application, local.Environment,
            responseCapabilities: ["fixture.handle"]);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<MessageDispatchResult>[] completed = Enumerable.Range(0, 3)
            .Select(_ => new TaskCompletionSource<MessageDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var options = new MessagingOptions
        {
            MaximumPayloadBytes = 1024,
            MaximumConcurrentDeliveries = 3,
            MaximumOutstandingRequests = 4,
            PublisherConcurrency = 1,
            MaximumEncodedBufferBytes = 12 * new MessagingLimits(1024).MaximumEnvelopeBytes
        };
        await using RabbitMqMessageFixture broker = await RabbitMqMessageFixture.CreateAsync(transformSnapshot: initial =>
            new(options, initial.Routes, [new("Messaging", "Messaging")],
                [new MessageRequestSubscriptionDefinition<int, string>("A", "Messaging", request,
                    _ => new NativeCommandHandler(async (context, value, token) =>
                    {
                        if (value == 1)
                        {
                            firstEntered.TrySetResult();
                            await releaseFirst.Task.WaitAsync(token);
                        }
                        await database.InsertBusinessAsync(context.Transaction, context.Envelope.MessageId, "pressure");
                        return OperationDecision<string>.Succeed("response:" + value);
                    }), (_, _, _, _) => ValueTask.FromResult(new CapabilityTarget(local.Application, local.Environment)),
                    ["fixture.handle"], new() { Concurrency = 2, Prefetch = 2, ReplyCapabilities = ["fixture.handle"] })], [request]),
            configureSecurity: security =>
            {
                security.Grants.Add(new(security.Profiles.Single().Alias, "fixture.handle", local.Application, local.Environment,
                    subject: security.Grants.Single().Subject, resourceSelection: CapabilityResourceSelection.ExactResource,
                    resourceType: "messaging.reply", resourceIdentifier: "PressureQuery"));
            });
        await using var resources = new MessagingResourceBudget(broker.Snapshot.Settings, reserveReplyIngress: true);
        var outbox = new MessageOutbox(broker.Snapshot, database.Store, resources, local);
        await using var responses = new MessageRequestCoordinator(broker.Snapshot, database.Store, database.Factory,
            broker.Transport, outbox, resources, local);
        await using var dispatcher = new MessageDispatcher(broker.Services, broker.Snapshot, database.Store,
            database.Factory, resources, local,
            new MessageDeliveryDispositionCoordinator(broker.Snapshot, database.Store, database.Factory, local));
        dispatcher.BindReplies(responses);
        int callbacks = 0;
        await broker.Transport.StartAsync(async (delivery, token) =>
        {
            int index = Interlocked.Increment(ref callbacks) - 1;
            if (index >= completed.Length)
            {
                delivery.RequestConsumerStop("fixture.pressure_extra_delivery");
                throw new InvalidOperationException("The fixed pressure workload permits three native callbacks.");
            }
            try
            {
                Task<MessageDispatchResult> pending = dispatcher.DispatchAsync(delivery, token);
                if (index == 1)
                {
                    Assert.IsFalse(pending.IsCompleted,
                        "A second request under temporary response pressure must wait without stopping its consumer.");
                    secondWaiting.TrySetResult();
                }
                completed[index].TrySetResult(await pending);
            }
            catch (Exception error)
            {
                completed[index].TrySetException(error);
                secondWaiting.TrySetException(error);
                delivery.RequestConsumerStop("fixture.pressure_failed");
            }
        });
        try
        {
            MessageReplyRoute reply = broker.Transport.ReplyEndpoint!;
            string commands = RabbitMqTopology.GetRouteExchange(broker.Options.Capture(), "commands");
            async Task PublishAsync(int value)
            {
                EncodedMessage message = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Request, codec.Contract,
                    local.Application, local.Environment, DateTimeOffset.UtcNow, codec.Encode(value), Guid.NewGuid(), reply),
                    broker.Snapshot.Settings.Limits);
                await broker.PublishRawAsync(message.CopyBytes(), role: "service", exchange: commands, routingKey: "message");
            }
            await PublishAsync(1);
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await PublishAsync(2);
            await secondWaiting.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsFalse(completed[0].Task.IsCompleted);
            Assert.IsFalse(completed[1].Task.IsCompleted);
            Assert.IsTrue(broker.Transport.IsReady);
            releaseFirst.TrySetResult();
            foreach (TaskCompletionSource<MessageDispatchResult> delivery in completed.Take(2))
            {
                MessageDispatchResult result = await delivery.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.AreEqual(MessageDispatchOutcome.Handled, result.Outcome);
            }
            await using (IConnection inspection = await broker.ConnectAsync())
            {
                await using IChannel channel = await inspection.CreateChannelAsync();
                Assert.AreEqual(1u, (await channel.QueueDeclarePassiveAsync(broker.Queue)).ConsumerCount);
            }
            await PublishAsync(3);
            Assert.AreEqual(MessageDispatchOutcome.Handled,
                (await completed[2].Task.WaitAsync(TimeSpan.FromSeconds(15))).Outcome);
            Assert.AreEqual(3L, await database.CountAsync("business_record"));
            Assert.AreEqual(3L, await database.CountAsync("processing"));
            Assert.AreEqual(3L, await database.CountAsync("outbox"));
            await database.SqlAsync($"DO $check$ BEGIN IF (SELECT count(*) FROM {database.Table("outbox")} o JOIN {database.Table("processing")} p ON p.reply_id=o.record_id WHERE o.purpose=3 AND o.reply_to_message_id=p.message_id AND p.state=1) <> 3 THEN RAISE EXCEPTION 'Expected three atomically secured current replies'; END IF; END $check$");
            Assert.IsTrue(broker.Transport.IsReady);
            int normalCapacity = checked((int)options.MaximumEncodedBufferBytes - 4 * broker.Snapshot.Settings.Limits.MaximumEnvelopeBytes);
            Assert.IsTrue(resources.TryAcquireBytes(normalCapacity, out IDisposable? recovered));
            recovered!.Dispose();
        }
        finally
        {
            releaseFirst.TrySetResult();
            await broker.Transport.StopAsync();
            await dispatcher.StopAsync();
            await outbox.StopAsync();
        }
    }
}
