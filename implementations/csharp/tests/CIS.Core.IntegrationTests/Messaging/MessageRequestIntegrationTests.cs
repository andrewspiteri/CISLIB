using System.Text.Json;
using CIS.Messaging;
using CIS.Messaging.RabbitMQ;
using CIS.Tests.MessagingCrashFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;

namespace CIS.Tests.IntegrationTests.Messaging;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[DoNotParallelize]
public sealed class MessageRequestIntegrationTests
{
    [TestMethod]
    [DataRow(7, "Succeeded", "original:7", 1L)]
    [DataRow(-1, "Rejected", null, 0L)]
    public async Task ActualHostedQueryAndCommandReturnOriginalDecisionUnderTheMinimumSharedBudget(
        int input, string business, string? value, long effects)
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync(minimum: true);
        MessageRpcProcessFixture.RpcChild process = await fixture.StartAsync(fixture.Descriptor("roundtrip", "minimum", minimum: true, input: input));
        CrashReport result = await fixture.ReportAsync(process);
        AssertResponse(result, "Completed");
        Assert.AreEqual(business, result.Business);
        Assert.AreEqual(value, result.Value);
        Assert.AreEqual(value, result.ReplayedValue);
        Assert.AreEqual(input < 0 ? null : "query:7", result.QueryValue);
        Assert.AreEqual(effects, result.EffectCount);
        Assert.AreEqual(1L, result.JournalCount);
        Assert.AreNotEqual(result.MessageId, result.SecondMessageId);
        Assert.AreNotEqual(result.AttemptId, result.SecondAttemptId);
        Assert.AreEqual(fixture.Broker.Local.Application, result.Application);
        Assert.AreEqual(fixture.Broker.Local.Environment, result.Environment);
    }

    [TestMethod]
    [DataRow(false, "TimedOut")]
    [DataRow(true, "Cancelled")]
    public async Task LocalWaitEndsAfterTheVerifiedRemoteCommitWithoutUndoingItsEffect(bool cancel, string expected)
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync();
        MessageRpcProcessFixture.RpcChild responder = await fixture.StartAsync(fixture.Descriptor("responder-hold", "server"));
        await fixture.MarkerAsync(responder, "ready");
        MessageRpcProcessFixture.RpcChild caller = await fixture.StartAsync(fixture.Descriptor("caller-hold", "caller", timeout: cancel ? 30 : 2));
        await fixture.MarkerAsync(caller, "caller-committed");
        JsonElement held = await fixture.MarkerAsync(responder, "reply-held");
        await AssertCommittedEffectAsync(fixture);
        if (cancel)
        {
            await File.WriteAllTextAsync(caller.Descriptor.CancelPath, "cancel");
        }
        JsonElement result = await fixture.MarkerAsync(caller, "", result: true);
        Assert.AreEqual(expected, result.GetProperty("Exchange").GetString());
        Assert.AreEqual("OutboxCommitted", result.GetProperty("Submission").GetString());
        Assert.AreEqual("Committed", result.GetProperty("Completion").GetString());
        await File.WriteAllTextAsync(responder.Descriptor.MarkerPath + ".release", "release");
        await RabbitMqMessageFixture.UntilAsync(() => fixture.ReplyPublishedAsync(held.GetProperty("OutboxRecordId").GetGuid()));
        JsonElement unchanged = await fixture.MarkerAsync(caller, "", result: true);
        Assert.AreEqual(expected, unchanged.GetProperty("Exchange").GetString());
        await AssertCommittedEffectAsync(fixture);
    }

    [TestMethod]
    public async Task ActualCallerAndResponderProcessDeathRecoverTheCommittedOriginalResultAtANewEndpoint()
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync();
        MessageRpcProcessFixture.RpcChild firstResponder = await fixture.StartAsync(fixture.Descriptor("responder-hold", "old-server"));
        await fixture.MarkerAsync(firstResponder, "ready");
        MessageRpcProcessFixture.RpcChild firstCaller = await fixture.StartAsync(fixture.Descriptor("caller-hold", "old-caller"));
        JsonElement original = await fixture.MarkerAsync(firstCaller, "caller-committed");
        JsonElement held = await fixture.MarkerAsync(firstResponder, "reply-held");
        await AssertCommittedEffectAsync(fixture);
        Assert.AreEqual(original.GetProperty("EndpointId").GetGuid(), held.GetProperty("RecipientEndpointId").GetGuid());
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("processing")} WHERE subscription='A' AND state=1 AND reply_id IS NOT NULL"));

        await firstResponder.StopAsync(requireCrash: true);
        await firstCaller.StopAsync(requireCrash: true);

        MessageRpcProcessFixture.RpcChild recoveredResponder = await fixture.StartAsync(fixture.Descriptor("responder", "new-server"));
        await fixture.MarkerAsync(recoveredResponder, "ready");
        MessageRpcProcessFixture.RpcChild recoveredCaller = await fixture.StartAsync(fixture.Descriptor("caller", "new-caller"));
        CrashReport recovered = await fixture.ReportAsync(recoveredCaller);
        AssertResponse(recovered, "Completed");
        Assert.AreEqual("original:7", recovered.Value);
        Assert.AreEqual("original:7", recovered.ReplayedValue);
        Assert.AreNotEqual(original.GetProperty("EndpointId").GetGuid(), recovered.EndpointId);
        Assert.AreNotEqual(original.GetProperty("MessageId").GetGuid(), recovered.MessageId);
        Assert.AreNotEqual(original.GetProperty("RequestAttemptId").GetGuid(), recovered.AttemptId);
        await RabbitMqMessageFixture.UntilAsync(() => fixture.ReplyPublishedAsync(held.GetProperty("OutboxRecordId").GetGuid()));
        await AssertCommittedEffectAsync(fixture);
    }

    [TestMethod]
    [DataRow("conflict", "KeyConflict")]
    [DataRow("expired", "ResultExpired")]
    [DataRow("incompatible", "Incompatible")]
    [DataRow("denied", "Denied")]
    public async Task ANewAuthorizedAttemptCannotReexecuteChangedExpiredIncompatibleOrDeniedOriginalWork(string condition, string protocol)
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync();
        MessageRpcProcessFixture.RpcChild responder = await fixture.StartAsync(fixture.Descriptor("responder", "server"));
        await fixture.MarkerAsync(responder, "ready");
        CrashReport original = await fixture.ReportAsync(await fixture.StartAsync(fixture.Descriptor("caller", "first")));
        AssertResponse(original, "Completed");
        Assert.AreEqual("original:7", original.Value);
        if (condition == "expired")
        {
            await fixture.Server.SqlAsync($"UPDATE \"{fixture.OperationSchema}\".operation_journal SET recorded_at=clock_timestamp()-interval '2 seconds', result_expires_at=clock_timestamp()-interval '1 second', result_payload=NULL");
        }
        else if (condition == "incompatible")
        {
            await fixture.Server.SqlAsync($"UPDATE \"{fixture.OperationSchema}\".operation_journal SET result_version=2");
        }
        else if (condition == "denied")
        {
            await responder.StopAsync(requireCrash: true);
            responder = await fixture.StartAsync(fixture.Descriptor("responder", "denied-server", deny: true));
            await fixture.MarkerAsync(responder, "ready");
        }
        CrashReport result = await fixture.ReportAsync(await fixture.StartAsync(fixture.Descriptor("caller", "next", input: condition == "conflict" ? 8 : 7)));
        AssertResponse(result, protocol);
        Assert.AreNotEqual(original.MessageId, result.MessageId);
        Assert.AreNotEqual(original.AttemptId, result.AttemptId);
        Assert.IsNull(result.Value);
        await AssertCommittedEffectAsync(fixture);
        if (condition == "denied")
        {
            Assert.AreEqual(2L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("rejections")} WHERE reply_id IS NOT NULL"));
            Assert.AreEqual(2L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("processing")} WHERE subscription='A'"));
        }
    }

    [TestMethod]
    public async Task TwoLiveCallerEndpointsAndAResponderGeneratedChangedDigestDenialCannotCrossComplete()
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync();
        MessageRpcProcessFixture.RpcChild responder = await fixture.StartAsync(fixture.Descriptor("responder-hold", "server"));
        await fixture.MarkerAsync(responder, "ready");
        MessageRpcProcessFixture.RpcChild first = await fixture.StartAsync(fixture.Descriptor("caller-hold", "first"));
        JsonElement one = await fixture.MarkerAsync(first, "caller-committed");
        JsonElement held = await fixture.MarkerAsync(responder, "reply-held");
        MessageRpcProcessFixture.RpcChild second = await fixture.StartAsync(fixture.Descriptor("caller-hold", "second"));
        JsonElement two = await fixture.MarkerAsync(second, "caller-committed");
        Assert.AreNotEqual(one.GetProperty("EndpointId").GetGuid(), two.GetProperty("EndpointId").GetGuid());
        Assert.AreNotEqual(one.GetProperty("RequestAttemptId").GetGuid(), two.GetProperty("RequestAttemptId").GetGuid());

        EncodedMessage actual = await fixture.ReadRequestAsync(one.GetProperty("MessageId").GetGuid());
        MessageEnvelope request = actual.Envelope;
        string commands = RabbitMqTopology.GetRouteExchange(fixture.Broker.Options.Capture(), "commands");
        await fixture.Broker.PublishRawAsync(actual.CopyBytes(), role: "other", exchange: commands, routingKey: "message");
        await AssertCommittedEffectAsync(fixture);
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("outbox")} WHERE purpose=3 AND reply_to_message_id='{request.MessageId}'"));

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            responseVersion = 1,
            requestAlias = "SubmitRequest",
            routeAlias = "commands",
            requestDigest = one.GetProperty("Digest").GetString(),
            protocol = "completed",
            decision = "succeeded",
            value = "forged"
        });
        EncodedMessage wrongResponder = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, new("fixture.response", 1),
            "rpc-foreign", request.ProducerEnvironment, DateTimeOffset.UtcNow, payload,
            request.RequestAttemptId, inReplyToMessageId: request.MessageId));
        string replies = fixture.Broker.Options.Namespace + ".reply.local";
        string endpoint = one.GetProperty("EndpointId").GetGuid().ToString("N");
        await fixture.Broker.PublishRawAsync(wrongResponder.CopyBytes(), role: "setup", exchange: replies, routingKey: endpoint);
        EncodedMessage wrongAttempt = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, new("fixture.response", 1),
            request.ProducerApplication, request.ProducerEnvironment, DateTimeOffset.UtcNow, payload,
            two.GetProperty("RequestAttemptId").GetGuid(), inReplyToMessageId: request.MessageId));
        await fixture.Broker.PublishRawAsync(wrongAttempt.CopyBytes(), role: "service", exchange: replies, routingKey: endpoint);
        await using (IConnection inspect = await fixture.Broker.ConnectAsync())
        {
            await using IChannel channel = await inspect.CreateChannelAsync();
            await RabbitMqMessageFixture.UntilAsync(async () =>
                await channel.MessageCountAsync(fixture.Broker.Options.Namespace + ".quarantine.local.replies") == 2);
        }
        Assert.IsFalse(File.Exists(first.Descriptor.ResultPath), "Rejected native replies must be observed before permitting a legitimate response.");
        Assert.IsFalse(File.Exists(second.Descriptor.ResultPath));
        EncodedMessage changed = MessageWire.Encode(new(request.MessageId, MessageKind.Request, request.Contract,
            request.ProducerApplication, request.ProducerEnvironment, request.CreatedAtUtc, "999"u8.ToArray(),
            request.RequestAttemptId, request.ReplyRoute, operation: new("Submit", "forbidden", "rpc-key")));
        await fixture.Broker.PublishRawAsync(changed.CopyBytes(), role: "other",
            exchange: commands, routingKey: "message");
        await RabbitMqMessageFixture.UntilAsync(async () => await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("rejections")} WHERE reply_id IS NOT NULL") == 1);
        await File.WriteAllTextAsync(responder.Descriptor.MarkerPath + ".release", "release");

        JsonElement firstResult = await fixture.MarkerAsync(first, "", result: true);
        JsonElement secondResult = await fixture.MarkerAsync(second, "", result: true);
        Assert.AreEqual("ResponseReceived", firstResult.GetProperty("Exchange").GetString());
        Assert.AreEqual("ResponseReceived", secondResult.GetProperty("Exchange").GetString());
        Assert.AreEqual("Completed", firstResult.GetProperty("Protocol").GetString());
        Assert.AreEqual("Completed", secondResult.GetProperty("Protocol").GetString());
        Assert.AreEqual("original:7", firstResult.GetProperty("Value").GetString());
        Assert.AreEqual("original:7", secondResult.GetProperty("Value").GetString());
        Assert.AreEqual(one.GetProperty("RequestAttemptId").GetGuid(), firstResult.GetProperty("AttemptId").GetGuid());
        Assert.AreEqual(two.GetProperty("RequestAttemptId").GetGuid(), secondResult.GetProperty("AttemptId").GetGuid());
        await AssertCommittedEffectAsync(fixture);
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("processing")} WHERE message_id='{request.MessageId}' AND reply_id='{held.GetProperty("OutboxRecordId").GetGuid()}'"));
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM {fixture.Server.Table("outbox")} WHERE purpose=3 AND reply_to_message_id='{request.MessageId}' AND processing_application IS NOT NULL"));
    }

    [TestMethod]
    public async Task AnIndependentReceiptReadCannotConfirmAnActiveOriginalOwnerAndTheSameTicketWorksAfterCommit()
    {
        await using MessageRpcProcessFixture fixture = await MessageRpcProcessFixture.CreateAsync();
        MessageRpcProcessFixture.RpcChild responder = await fixture.StartAsync(fixture.Descriptor("responder", "server"));
        await fixture.MarkerAsync(responder, "ready");
        CrashDescriptor descriptor = fixture.Descriptor("caller-hold", "provisional");
        descriptor.ObserveBeforeCommit = true;
        MessageRpcProcessFixture.RpcChild caller = await fixture.StartAsync(descriptor);
        JsonElement report = await fixture.MarkerAsync(caller, "", result: true);
        Assert.AreEqual("SubmissionNotConfirmed", report.GetProperty("PreCommitExchange").GetString());
        Assert.AreEqual("OutboxCommitted", report.GetProperty("Submission").GetString());
        Assert.AreEqual("Committed", report.GetProperty("Completion").GetString());
        Assert.AreEqual("ResponseReceived", report.GetProperty("Exchange").GetString());
        Assert.AreEqual("Completed", report.GetProperty("Protocol").GetString());
        Assert.AreEqual("original:7", report.GetProperty("Value").GetString());
        await AssertCommittedEffectAsync(fixture);
    }

    private static void AssertResponse(CrashReport result, string protocol)
    {
        Assert.AreEqual("OutboxCommitted", result.Submission);
        Assert.AreEqual("Committed", result.Completion);
        Assert.AreEqual("ResponseReceived", result.Exchange);
        Assert.AreEqual(protocol, result.Protocol);
        Assert.AreNotEqual(Guid.Empty, result.EndpointId);
    }

    private static async Task AssertCommittedEffectAsync(MessageRpcProcessFixture fixture)
    {
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT total::bigint FROM {fixture.Server.Table("rpc_effect_counter")}"));
        Assert.AreEqual(1L, await fixture.ScalarAsync($"SELECT count(*) FROM \"{fixture.OperationSchema}\".operation_journal WHERE finalized"));
    }
}
