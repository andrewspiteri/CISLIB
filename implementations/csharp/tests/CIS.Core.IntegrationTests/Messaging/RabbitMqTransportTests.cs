using System.Security.Claims;
using System.Text;
using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class RabbitMqTransportTests
{
    [TestMethod]
    public async Task ConfirmedPublicationRetainsExactBytesAndProjectsOnlyCurrentBrokerEvidence()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        var received = new TaskCompletionSource<MessageNativeEvidence>(TaskCreationOptions.RunContinuationsAsynchronously);
        EncodedMessage outgoing = fixture.Message();
        byte[] expected = outgoing.CopyBytes();
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            CollectionAssert.AreEqual(expected, delivery.Body.ToArray());
            MessageNativeEvidence? evidence = await delivery.GetNativeEvidenceAsync(token);
            Assert.IsNotNull(evidence);
            await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(fixture.Services,
                evidence.Profile, evidence.Authentication, token);
            Assert.IsNotNull(scope.Caller);
            Assert.AreEqual(fixture.ServiceUser, scope.Caller.Subject.Identifier);
            Assert.AreEqual(fixture.Local.Application, scope.Caller.Application);
            AuthorizationDecision decision = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
                .AuthorizeAsync("fixture.handle", new(fixture.Local.Application, fixture.Local.Environment), token);
            Assert.IsTrue(decision.IsAllowed);
            await delivery.AcknowledgeAsync(token);
            received.TrySetResult(evidence);
        });

        Task<MessagePublicationResult> publication = fixture.Transport.PublishAsync(fixture.Publication(outgoing));
        byte[] detached = outgoing.CopyBytes();
        detached.AsSpan().Fill(0);
        Assert.AreEqual(MessagePublicationDisposition.Confirmed, (await publication).Disposition);
        MessageNativeEvidence retained = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await fixture.Transport.StopConsumersAsync();

        await using SecurityHostContextScope expired = await SecurityHostContextScope.CreateAsync(fixture.Services,
            retained.Profile, retained.Authentication);
        Assert.IsNull(expired.Caller, "A retained native ticket cannot establish authority after its delivery lifetime.");
        var fabricated = AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("rabbitmq-user-id")), "cis-rabbitmq"));
        await using SecurityHostContextScope forged = await SecurityHostContextScope.CreateAsync(fixture.Services,
            retained.Profile, fabricated);
        Assert.IsNull(forged.Caller);
        CollectionAssert.AreEqual(expected, outgoing.CopyBytes());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnotherMappedPrincipalCannotGainGrantsFromClaimedProducerMetadata(bool crossApplication)
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(configureSecurity: crossApplication
            ? security =>
            {
                SecurityProfile original = security.Profiles.Single();
                security.Profiles[0] = new(original.Alias, original.Kind, original.Roles, original.AuthenticationScheme,
                    [original.IdentityMappings[0], new(original.IdentityMappings[1].Subject, "external-producer", "external-environment")]);
            }
            : null);
        string expectedApplication = crossApplication ? "external-producer" : fixture.Local.Application;
        string expectedEnvironment = crossApplication ? "external-environment" : fixture.Local.Environment;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            MessageNativeEvidence? evidence = await delivery.GetNativeEvidenceAsync(token);
            Assert.IsNotNull(evidence);
            Assert.AreEqual(fixture.OtherUser, evidence.Provenance.UserId);
            Assert.AreEqual(expectedApplication, evidence.Provenance.Application);
            Assert.AreEqual(expectedEnvironment, evidence.Provenance.Environment);
            Assert.IsTrue(MessageWire.TryDecode(delivery.Body, out EncodedMessage? parsed, out _));
            Assert.IsNotNull(parsed);
            Assert.AreEqual("forged-claimed-producer", parsed.Envelope.ProducerApplication);
            await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(fixture.Services,
                evidence.Profile, evidence.Authentication, token);
            Assert.IsNotNull(scope.Caller);
            Assert.AreEqual(expectedApplication, scope.Caller.Application);
            Assert.AreEqual(expectedEnvironment, scope.Caller.Environment);
            AuthorizationDecision decision = await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
                .AuthorizeAsync("fixture.handle", new(fixture.Local.Application, fixture.Local.Environment), token);
            Assert.IsFalse(decision.IsAllowed);
            Assert.IsTrue(await delivery.QuarantineMalformedAsync("fixture.denied", token));
            observed.TrySetResult();
        });
        await fixture.PublishRawAsync(fixture.Message(claimedProducer: "forged-claimed-producer").CopyBytes());
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public async Task QueuedBeforeStartupDeliveryHasAvailableCurrentNativeAuthorityBeforeDispatch()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        await fixture.PublishRawAsync(fixture.Message().CopyBytes(), role: "service");
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            MessageNativeEvidence? evidence = await delivery.GetNativeEvidenceAsync(token);
            Assert.IsNotNull(evidence);
            await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(fixture.Services,
                evidence.Profile, evidence.Authentication, token);
            Assert.IsNotNull(scope.Caller);
            Assert.AreEqual(fixture.ServiceUser, scope.Caller.Subject.Identifier);
            Assert.IsTrue((await scope.Services.GetRequiredService<ICapabilityAuthorizer>()
                .AuthorizeAsync("fixture.handle", new(fixture.Local.Application, fixture.Local.Environment), token)).IsAllowed);
            await delivery.AcknowledgeAsync(token);
            observed.TrySetResult();
        });
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public async Task MissingValidatedUserIdDoesNotCreateAnAuthenticatedScope()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            Assert.IsNull(await delivery.GetNativeEvidenceAsync(token));
            Assert.IsTrue(await delivery.QuarantineMalformedAsync("fixture.missing-user-id", token));
            observed.TrySetResult();
        });
        await fixture.PublishRawAsync(fixture.Message().CopyBytes(), includeUserId: false);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public async Task BrokerRejectsForgedUserIdAndUnauthorizedQueueAccessForAnotherRealPrincipal()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        OperationInterruptedException forgery = await ProtocolFailureAsync(
            () => fixture.PublishRawAsync(fixture.Message().CopyBytes(), userId: fixture.ServiceUser));
        Assert.AreEqual<ushort?>(406, forgery.ShutdownReason?.ReplyCode);

        await using IConnection rogue = await fixture.ConnectAsync("rogue");
        await using IChannel channel = await rogue.CreateChannelAsync();
        OperationInterruptedException denied = await ProtocolFailureAsync(() => channel.BasicGetAsync(fixture.Queue, false));
        Assert.AreEqual<ushort?>(403, denied.ShutdownReason?.ReplyCode);
    }

    [TestMethod]
    public async Task ATrustedRootDoesNotBypassTheExpectedTlsHostname()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(wrongHost: true);
        await Assert.ThrowsExactlyAsync<BrokerUnreachableException>(
            () => fixture.Transport.StartAsync((_, _) => Task.CompletedTask));
        Assert.IsFalse(fixture.Transport.IsReady);
    }

    [TestMethod]
    public async Task MandatoryUnroutablePublicationRemainsDistinctFromConfirmation()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(maximumQueueMessages: 1);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            await delivery.AcknowledgeAsync(token);
        });

        MessagePublicationResult unroute = await fixture.Transport.PublishAsync(fixture.Publication(fixture.Message(kind: MessageKind.Command), "commands"));
        Assert.AreEqual(MessagePublicationDisposition.Rejected, unroute.Disposition);
        Assert.AreEqual(MessagePublicationFailure.Unroutable, unroute.Failure);
        await fixture.Transport.StopConsumersAsync();
    }

    [TestMethod]
    public async Task FullQuorumQueueRejectsNewWorkInsteadOfEvictingAnAcceptedRecord()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(maximumQueueMessages: 1);
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        List<byte[]> accepted = await FillUntilRejectedAsync(channel, fixture.EventExchange, "",
            index => fixture.Message(index + 1).CopyBytes());
        var received = new List<string>();
        for (int index = 0; index < accepted.Count; index++)
        {
            BasicGetResult? retained = await channel.BasicGetAsync(fixture.Queue, false);
            Assert.IsNotNull(retained, "Every confirmed publication must remain available after overflow rejection.");
            received.Add(Convert.ToBase64String(retained.Body.Span));
            await channel.BasicAckAsync(retained.DeliveryTag, false);
        }
        CollectionAssert.AreEquivalent(accepted.Select(Convert.ToBase64String).ToArray(), received.ToArray());
        Assert.IsNull(await channel.BasicGetAsync(fixture.Queue, false), "The rejected publication must not replace accepted content.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NoSubscriberAcceptanceRequiresAnExplicitSeparatelyNamedPolicy(bool allowed)
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(withSubscription: false,
            allowNoSubscribers: allowed);
        await fixture.Transport.StartAsync((_, _) => Task.CompletedTask);
        MessagePublicationResult result = await fixture.Transport.PublishAsync(fixture.Publication());
        Assert.AreEqual(allowed ? MessagePublicationDisposition.Confirmed : MessagePublicationDisposition.Rejected, result.Disposition);
        Assert.AreEqual(allowed ? "messaging.publish.no-subscriber-policy" : "messaging.publish.unroutable", result.SafeCode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MalformedAndOversizeBodiesUseVerifiedSafeNativeQuarantine(bool oversize)
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        byte[] body = oversize ? new byte[fixture.Snapshot.Settings.Limits.MaximumEnvelopeBytes + 1] : "not-json"u8.ToArray();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            Assert.AreEqual(body.Length, delivery.Body.Length);
            Assert.IsTrue(await delivery.QuarantineMalformedAsync("fixture.invalid-framing", token));
            observed.TrySetResult();
        });
        await fixture.PublishRawAsync(body);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        await RabbitMqMessageFixture.UntilAsync(async () => await channel.MessageCountAsync(fixture.QuarantineQueue) == 1);
        BasicGetResult? quarantined = await channel.BasicGetAsync(fixture.QuarantineQueue, false);
        Assert.IsNotNull(quarantined);
        CollectionAssert.AreEqual(body, quarantined.Body.ToArray());
        await channel.BasicAckAsync(quarantined.DeliveryTag, false);
    }

    [TestMethod]
    public async Task SafeDeadLetteringRetainsTheSourceWhileItsQuarantineQueueCannotAccept()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(maximumQueueMessages: 1);
        string terminalExchange = fixture.Options.Namespace + ".terminal.local.A";
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        List<byte[]> accepted = await FillUntilRejectedAsync(channel, terminalExchange, "A",
            index => Encoding.UTF8.GetBytes("held-quarantine-" + index));
        int targetAcceptedCount = accepted.Count;
        var callbacks = new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            Assert.IsTrue(await delivery.QuarantineMalformedAsync("fixture.invalid", token));
            callbacks[Encoding.UTF8.GetString(delivery.Body.Span)].TrySetResult();
        });
        bool retainedSource = false;
        for (int index = 0; index < 8 && !retainedSource; index++)
        {
            string value = "malformed-" + index;
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.IsTrue(callbacks.TryAdd(value, observed));
            byte[] body = Encoding.UTF8.GetBytes(value);
            try
            {
                await fixture.PublishRawAsync(body);
            }
            catch (PublishException error)
            {
                Assert.IsFalse(error.IsReturn, "Only actual source capacity rejection establishes backpressure.");
                Assert.IsTrue(accepted.Count > targetAcceptedCount, "A known confirmed source message must precede source backpressure.");
                await RabbitMqMessageFixture.UntilAsync(async () => await fixture.TotalQueuedAsync(fixture.Queue) > 0);
                retainedSource = true;
                break;
            }
            accepted.Add(body);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            retainedSource = await fixture.TotalQueuedAsync(fixture.Queue) > 0;
        }
        Assert.IsTrue(retainedSource, "A confirmed source message must remain retained while the actual DLX target refuses capacity.");
        string[] expected = accepted.Select(Convert.ToBase64String).ToArray();
        var recovered = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await RabbitMqMessageFixture.UntilAsync(async () =>
            {
                BasicGetResult? message = await channel.BasicGetAsync(fixture.QuarantineQueue, false);
                if (message is not null)
                {
                    string value = Convert.ToBase64String(message.Body.Span);
                    CollectionAssert.Contains(expected, value, "Restoration must recover only known exact accepted bytes; repeated DLX delivery is allowed.");
                    recovered.Add(value);
                    await channel.BasicAckAsync(message.DeliveryTag, false);
                }
                return recovered.Count == expected.Length && await fixture.TotalQueuedAsync(fixture.Queue) == 0
                    && await channel.MessageCountAsync(fixture.QuarantineQueue) == 0;
            }, TimeSpan.FromSeconds(210));
        }
        catch (AssertFailedException)
        {
            long source = await fixture.TotalQueuedAsync(fixture.Queue);
            uint target = await channel.MessageCountAsync(fixture.QuarantineQueue);
            string missing = string.Join(",", Enumerable.Range(0, expected.Length).Where(index => !recovered.Contains(expected[index])));
            Assert.Fail($"Safe DLX did not restore all confirmed fixture records: targetAccepted={targetAcceptedCount}, sourceAccepted={accepted.Count - targetAcceptedCount}, recovered={recovered.Count}, missingAcceptedOrdinals=[{missing}], sourceRetained={source}, targetReady={target}, providerCode={fixture.Transport.FailureCode ?? "none"}.");
        }
        CollectionAssert.AreEquivalent(expected, recovered.ToArray());
    }

    [TestMethod]
    public async Task UnverifiedQuarantineStopsTheConsumerAndLeavesTheOriginalResponsibility()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(safeQuarantine: false);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            Assert.IsFalse(await delivery.QuarantineMalformedAsync("fixture.invalid", token));
            failed.TrySetResult();
        });
        byte[] body = "invalid"u8.ToArray();
        await fixture.PublishRawAsync(body);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await fixture.Transport.StopConsumersAsync();
        Assert.IsFalse(fixture.Transport.IsReady);
        Assert.AreEqual("messaging.quarantine.unavailable", fixture.Transport.FailureCode);
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        BasicGetResult? retained = await channel.BasicGetAsync(fixture.Queue, false);
        Assert.IsNotNull(retained);
        CollectionAssert.AreEqual(body, retained.Body.ToArray());
        await channel.BasicAckAsync(retained.DeliveryTag, false);
    }

    [TestMethod]
    public async Task RequeuePreservesTheSameImmutableEnvelopeAndScheduledOrdinal()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        EncodedMessage outgoing = fixture.Message();
        int calls = 0;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            CollectionAssert.AreEqual(outgoing.CopyBytes(), delivery.Body.ToArray());
            Assert.AreEqual(1, delivery.Metadata.DeliveryOrdinal);
            if (Interlocked.Increment(ref calls) == 1)
            {
                await delivery.RequeueAsync(token);
            }
            else
            {
                await delivery.AcknowledgeAsync(token);
                finished.TrySetResult();
            }
        });
        Assert.AreEqual(MessagePublicationDisposition.Confirmed,
            (await fixture.Transport.PublishAsync(fixture.Publication(outgoing))).Disposition);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task ShutdownJoinsAnAlreadyAdmittedNativeCallbackWithoutAcknowledgingIt()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Transport.StartAsync(async (delivery, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            delivery.RequestConsumerStop("fixture.shutdown");
        });
        await fixture.PublishRawAsync(fixture.Message().CopyBytes());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Task stopping = fixture.Transport.StopConsumersAsync();
        try
        {
            Assert.IsFalse(stopping.IsCompleted);
            Assert.IsFalse(fixture.Transport.IsReady);
        }
        finally
        {
            release.TrySetResult();
            await stopping;
        }
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        BasicGetResult? retained = await channel.BasicGetAsync(fixture.Queue, false);
        Assert.IsNotNull(retained);
        await channel.BasicAckAsync(retained.DeliveryTag, false);
    }

    [TestMethod]
    public async Task ARealOwnedBrokerRestartRedeliversPersistentUnacknowledgedWorkThroughNativeRecovery()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repeated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EncodedMessage message = fixture.Message();
        EncodedMessage afterRecovery = fixture.Message(2);
        int calls = 0;
        await fixture.Transport.StartAsync(async (delivery, token) =>
        {
            bool original = delivery.Body.Span.SequenceEqual(message.CopyBytes());
            if (!original)
            {
                CollectionAssert.AreEqual(afterRecovery.CopyBytes(), delivery.Body.ToArray());
            }
            if (original && Interlocked.Increment(ref calls) == 1)
            {
                first.TrySetResult();
                await release.Task;
            }
            await delivery.AcknowledgeAsync(token);
            if (original && Volatile.Read(ref calls) > 1)
            {
                repeated.TrySetResult();
            }
            else if (!original)
            {
                healthy.TrySetResult();
            }
        });
        Assert.AreEqual(MessagePublicationDisposition.Confirmed,
            (await fixture.Transport.PublishAsync(fixture.Publication(message))).Disposition);
        try
        {
            await first.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await fixture.RestartOwnedBrokerAsync();
        }
        finally
        {
            release.TrySetResult();
        }
        try
        {
            await repeated.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (TimeoutException)
        {
            Assert.Fail($"Native recovery did not redeliver the retained original: callbacks={Volatile.Read(ref calls)}, ready={fixture.Transport.IsReady}, code={fixture.Transport.FailureCode ?? "none"}.");
        }
        Assert.IsTrue(calls >= 2, "This is broker redelivery evidence; SQL committed-effect protection belongs to the dispatcher acceptance.");
        await RabbitMqMessageFixture.UntilAsync(() => Task.FromResult(fixture.Transport.IsReady));
        Assert.AreEqual(MessagePublicationDisposition.Confirmed,
            (await fixture.Transport.PublishAsync(fixture.Publication(afterRecovery))).Disposition);
        await healthy.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public async Task TwoRequesterInstancesUseDistinctQueuesAndReceiveOnlyTheirOwnEndpointReplies()
    {
        await using RabbitMqMessageFixture first = await RabbitMqMessageFixture.CreateAsync(withRequests: true);
        await using RabbitMqMessageFixture second = first.CreateSibling();
        var firstReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int firstCount = 0;
        int secondCount = 0;
        await first.Transport.StartAsync(async (delivery, token) =>
        {
            Assert.AreEqual(RabbitMqTransport.ReplySubscriptionAlias, delivery.SubscriptionAlias);
            Assert.AreEqual(MessageRecordPurpose.Reply, delivery.Metadata.Purpose);
            Interlocked.Increment(ref firstCount);
            await delivery.AcknowledgeAsync(token);
            firstReply.TrySetResult();
        });
        await second.Transport.StartAsync(async (delivery, token) =>
        {
            Assert.AreEqual(RabbitMqTransport.ReplySubscriptionAlias, delivery.SubscriptionAlias);
            Interlocked.Increment(ref secondCount);
            await delivery.AcknowledgeAsync(token);
            secondReply.TrySetResult();
        });
        Assert.IsNotNull(first.Transport.ReplyEndpoint);
        Assert.IsNotNull(second.Transport.ReplyEndpoint);
        Assert.AreNotEqual(first.Transport.ReplyEndpoint.EndpointId, second.Transport.ReplyEndpoint.EndpointId);
        EncodedMessage response = Reply(first);
        MessagePublicationResult accepted = await second.Transport.PublishAsync(new(response,
            new("commands", first.Transport.ReplyEndpoint), MessageRecordPurpose.Reply, 1, Guid.NewGuid()));
        Assert.AreEqual(MessagePublicationDisposition.Confirmed, accepted.Disposition);
        await firstReply.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.AreEqual(0, secondCount);
        accepted = await first.Transport.PublishAsync(new(Reply(first),
            new("commands", second.Transport.ReplyEndpoint), MessageRecordPurpose.Reply, 1, Guid.NewGuid()));
        Assert.AreEqual(MessagePublicationDisposition.Confirmed, accepted.Disposition);
        await secondReply.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.AreEqual(1, firstCount);
        Assert.AreEqual(1, secondCount);
    }

    [TestMethod]
    public async Task ActualBrokerConsumerCancellationRemainsVisibleWithoutStartingAnotherRecoveryOwner()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync();
        await fixture.Transport.StartAsync((_, _) => Task.CompletedTask);
        Assert.IsTrue(fixture.Transport.IsReady);
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync();
        Assert.AreEqual(0u, await channel.MessageCountAsync(fixture.Queue));
        // Delete only this fresh empty owned queue to produce a real broker basic.cancel;
        // no accepted work or external topology is removed by this fault injection.
        await channel.QueueDeleteAsync(fixture.Queue, ifUnused: false, ifEmpty: false);
        await RabbitMqMessageFixture.UntilAsync(() => Task.FromResult(!fixture.Transport.IsReady
            && fixture.Transport.FailureCode == "messaging.consumer.cancelled"));
    }

    [TestMethod]
    public async Task ExplicitSetupRejectsAnExistingMismatchedQueueWithoutReplacingItsAcceptedContents()
    {
        await using RabbitMqMessageFixture fixture = await RabbitMqMessageFixture.CreateAsync(installTopology: false);
        await using IConnection connection = await fixture.ConnectAsync();
        await using IChannel channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await channel.ExchangeDeclareAsync(fixture.EventExchange, ExchangeType.Fanout, true, false);
        await channel.QueueDeclareAsync(fixture.Queue, true, false, false, new Dictionary<string, object?> { ["x-queue-type"] = "classic" });
        await channel.QueueBindAsync(fixture.Queue, fixture.EventExchange, "");
        await channel.BasicPublishAsync(fixture.EventExchange, "", true, new BasicProperties { Persistent = true }, "preserved"u8.ToArray());

        OperationInterruptedException incompatible = await ProtocolFailureAsync(() => RabbitMqTopology.InstallAsync(fixture.SetupOptions, fixture.Snapshot));
        Assert.AreEqual<ushort?>(406, incompatible.ShutdownReason?.ReplyCode);
        await channel.QueueDeclareAsync(fixture.Queue, true, false, false, new Dictionary<string, object?> { ["x-queue-type"] = "classic" });
        BasicGetResult? retained = await channel.BasicGetAsync(fixture.Queue, false);
        Assert.IsNotNull(retained);
        CollectionAssert.AreEqual("preserved"u8.ToArray(), retained.Body.ToArray());
        await channel.BasicAckAsync(retained.DeliveryTag, false);
    }

    private static EncodedMessage Reply(RabbitMqMessageFixture fixture)
        => MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, new("fixture.number", 1), fixture.Local.Application,
            fixture.Local.Environment, DateTimeOffset.UtcNow, "42"u8.ToArray(), Guid.NewGuid(), inReplyToMessageId: Guid.NewGuid()));

    private static async Task<List<byte[]>> FillUntilRejectedAsync(IChannel channel, string exchange, string routingKey,
        Func<int, byte[]> body)
    {
        var accepted = new List<byte[]>();
        // Quorum limits permit in-flight overshoot. Exercise actual backpressure rather than
        // assuming the first publication beyond a threshold is already rejected.
        for (int index = 0; index < 16; index++)
        {
            byte[] next = body(index);
            try
            {
                await channel.BasicPublishAsync(exchange, routingKey, true,
                    new BasicProperties { Persistent = true }, next);
                accepted.Add(next);
            }
            catch (PublishException error)
            {
                Assert.IsFalse(error.IsReturn, "The configured queue must reject capacity, not fail routing.");
                Assert.IsTrue(accepted.Count > 0);
                return accepted;
            }
        }
        Assert.Fail("The finite quorum queue did not establish reject-publish backpressure within sixteen serial attempts.");
        throw new InvalidOperationException("Unreachable after the failed assertion.");
    }

    private static async Task<OperationInterruptedException> ProtocolFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationInterruptedException error)
        {
            return error;
        }
        Assert.Fail("Expected a conclusive native broker protocol rejection.");
        throw new InvalidOperationException("Unreachable after the failed assertion.");
    }
}
