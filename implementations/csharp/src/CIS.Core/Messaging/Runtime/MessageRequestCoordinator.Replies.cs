using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Messaging;

internal sealed partial class MessageRequestCoordinator
{
    public async Task<Guid> StageReplyAsync<TRequest, TResponse>(ILocalTransactionContext context,
        MessageRequestSubscriptionDefinition<TRequest, TResponse> definition, EncodedMessage request,
        MessageProcessingAttempt attempt, MessageResponse<TResponse> response, MessageResponseAdmission admission,
        CancellationToken cancellationToken, Guid? rejectionId = null)
    {
        admission.Require(resources);
        if (request.Envelope.Kind != MessageKind.Request || request.Envelope.ReplyRoute is null
            || !ReferenceEquals(snapshot.Subscriptions.SingleOrDefault(item => item.Alias == definition.Alias), definition)
            || !attempt.Message.Digest.Equals(request.Digest) || attempt.Key.MessageId != request.Envelope.MessageId
            || definition.Request.ResponderApplication != local.Application || definition.Request.ResponderEnvironment != local.Environment)
        {
            throw new InvalidOperationException("Stage a response only for the exact admitted request/subscription.");
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] payload = MessageReplyProtocol.Encode(definition.Request, request.Digest, definition.Request.ResponseCodec,
                response, snapshot.Settings.Limits);
            var envelope = new MessageEnvelope(Guid.NewGuid(), MessageKind.Reply, definition.Request.ResponseContract,
                local.Application, local.Environment, clock.GetUtcNow(), payload, request.Envelope.RequestAttemptId,
                null, request.Envelope.MessageId, null, request.Envelope.CorrelationId, request.Envelope.MessageId, null, false);
            EncodedMessage encoded = MessageWire.Encode(envelope, snapshot.Settings.Limits);
            var outgoing = new MessageOutgoingRecord(Guid.NewGuid(), local.Application, local.Environment,
                new(definition.RouteAlias, request.Envelope.ReplyRoute), encoded, MessageRecordPurpose.Reply,
                ProcessingKey: rejectionId is null ? attempt.Key : null,
                DeliveryOrdinal: rejectionId is null ? attempt.DeliveryOrdinal : 1,
                Generation: rejectionId is null ? attempt.Generation : 0, RejectionId: rejectionId);
            MessageStageReceipt receipt = await store.StageAsync(context, outgoing, cancellationToken).ConfigureAwait(false);
            if (receipt.RecordId != outgoing.RecordId || receipt.MessageId != encoded.Envelope.MessageId
                || receipt.DatabaseName != definition.DatabaseName || receipt.Application != local.Application
                || receipt.Environment != local.Environment || !receipt.Digest.Equals(encoded.Digest))
            {
                throw new InvalidOperationException("The store returned a receipt for different reply responsibility.");
            }
            return outgoing.RecordId;
        }
        catch
        {
            MessageOutbox.Poison(context, "messaging.reply_stage_failed");
            throw;
        }
    }

    public async Task<MessageReplyHandling> ReceiveReplyAsync(EncodedMessage reply, IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        if (!Enter())
        {
            return MessageReplyHandling.Stop;
        }
        try
        {
            IMessageLocalWaiter? waiter;
            lock (gate)
            {
                waiters.TryGetValue(reply.Envelope.RequestAttemptId ?? Guid.Empty, out waiter);
            }
            if (waiter is null || reply.Envelope.Kind != MessageKind.Reply
                || reply.Envelope.InReplyToMessageId != waiter.Reference.MessageId
                || waiter.Reference.ReplyRoute != transport.ReplyEndpoint)
            {
                return MessageReplyHandling.Quarantine;
            }
            CallerContext? caller = scopedServices.GetRequiredService<ICallerContextAccessor>().Current;
            if (caller is null || caller.Application != waiter.Definition.ResponderApplication || caller.Environment != waiter.Definition.ResponderEnvironment
                || reply.Envelope.ProducerApplication != caller.Application || reply.Envelope.ProducerEnvironment != caller.Environment)
            {
                return MessageReplyHandling.Quarantine;
            }
            var target = new CapabilityTarget(local.Application, local.Environment, "messaging.reply", waiter.Definition.Alias);
            if (!(await scopedServices.GetRequiredService<ICapabilityAuthorizer>().AuthorizeAsync(
                waiter.Definition.ResponseCapabilities, target, cancellationToken).ConfigureAwait(false)).IsAllowed)
            {
                return MessageReplyHandling.Quarantine;
            }
            return waiter.Receive(reply);
        }
        finally
        {
            Exit();
        }
    }
}
