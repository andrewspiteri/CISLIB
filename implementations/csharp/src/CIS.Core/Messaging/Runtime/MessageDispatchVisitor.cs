using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Messaging;

internal enum MessageInvocationKind { Completed, Terminal, Rejected, Retry }
internal sealed record MessageInvocation(MessageInvocationKind Kind, string SafeCode = "messaging.handled", Guid? ReplyRecordId = null);

internal sealed class MessageDispatchPlan
{
    internal CapabilityTarget? Target;
    internal MessageOperationBinding? Binding;
    internal string? Rejection;
    internal bool HandlerStarted;
    internal Func<MessageProcessingRecord, CancellationToken, ValueTask<bool>>? ValidateRetry;
    internal Func<ILocalTransactionContext, MessageProcessingAttempt, CancellationToken, Task<MessageInvocation>>? Invoke;
    internal Func<MessageProcessingAttempt, MessageFailureReplyStage>? TerminalReply;
    internal Func<MessageProcessingAttempt, MessageDeniedReplyStage>? DeniedReply;
}

internal sealed class MessageDispatchVisitor(IServiceProvider services, CallerContext caller, EncodedMessage message,
    LocalApplicationIdentity local, IMessageResponseCoordinator? replies, CancellationToken cancellationToken,
    MessageResponseAdmission? responseAdmission = null) : IMessageSubscriptionVisitor<Task<MessageDispatchPlan>>
{
    public async Task<MessageDispatchPlan> Visit<T>(MessageSubscriptionDefinition<T> definition)
    {
        var plan = new MessageDispatchPlan();
        if (message.Envelope.Kind is not (MessageKind.Event or MessageKind.Command)
            || message.Envelope.Operation is not null || definition.Codec.Contract != definition.Contract
            || !definition.Codec.TryDecode(message.Envelope.Contract, message.Envelope.PayloadMemory, out T? input))
        {
            plan.Rejection = "messaging.schema_incompatible";
            return plan;
        }
        cancellationToken.ThrowIfCancellationRequested();
        plan.Target = await definition.TargetResolver(services, input!, caller, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The message target resolver returned no actual resource.");
        if (definition.RetryValidator is { } validate)
        {
            plan.ValidateRetry = (record, token) => validate(services, input!, caller, record.Provenance, record.OperationBinding, token);
        }
        plan.Invoke = async (context, attempt, token) =>
        {
            IMessageHandler<T> handler = definition.HandlerFactory(services)
                ?? throw new InvalidOperationException("The message handler factory returned no scoped handler.");
            plan.HandlerStarted = true;
            await handler.HandleAsync(new(context, services, message.Envelope, attempt.DeliveryOrdinal, attempt.Generation), input!, token).ConfigureAwait(false);
            return new(MessageInvocationKind.Completed);
        };
        return plan;
    }

    public async Task<MessageDispatchPlan> Visit<TRequest, TResponse>(MessageRequestSubscriptionDefinition<TRequest, TResponse> definition)
    {
        var plan = new MessageDispatchPlan();
        MessageEnvelope envelope = message.Envelope;
        bool request = envelope.Kind == MessageKind.Request;
        bool durable = definition.Request.Mode == MessageRequestMode.DurableCommand;
        if (envelope.Kind is not (MessageKind.Command or MessageKind.Request)
            || !request && !durable || request && replies is null
            || definition.Request.ResponderApplication != local.Application || definition.Request.ResponderEnvironment != local.Environment
            || definition.Request.RequestCodec.Contract != definition.Contract
            || !definition.Request.RequestCodec.TryDecode(envelope.Contract, envelope.PayloadMemory, out TRequest? input)
            || durable != (envelope.Operation is not null)
            || durable && envelope.Operation!.OperationName != definition.Request.OperationName)
        {
            plan.Rejection = "messaging.request_incompatible";
            return plan;
        }
        cancellationToken.ThrowIfCancellationRequested();
        plan.Target = await definition.TargetResolver(services, input!, caller, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The request target resolver returned no actual resource.");
        if (durable)
        {
            plan.Binding = await MessageOperationBindings.ResolveAsync(services, definition, envelope.Operation!, local,
                cancellationToken).ConfigureAwait(false);
            if (plan.Target != plan.Binding.Resource)
            {
                throw new InvalidOperationException("The subscription resource differs from its actual Operations resource.");
            }
        }
        if (definition.RetryValidator is { } validate)
        {
            plan.ValidateRetry = (record, token) => validate(services, input!, caller, record.Provenance, record.OperationBinding, token);
        }
        if (request)
        {
            if (responseAdmission is null)
            {
                throw new InvalidOperationException("Admit the bounded response buffer before request effects.");
            }
            plan.TerminalReply = attempt => (context, token) => replies!.StageReplyAsync(context, definition, message, attempt,
                new(durable ? MessageProtocolDisposition.OutcomeUnknown : MessageProtocolDisposition.Unavailable), responseAdmission, token);
            plan.DeniedReply = attempt => (context, rejection, token) => replies!.StageReplyAsync(context, definition, message, attempt,
                new(MessageProtocolDisposition.Denied), responseAdmission, token, rejection);
        }
        plan.Invoke = async (context, attempt, token) =>
        {
            MessageResponse<TResponse> response;
            if (durable)
            {
                using IDisposable guard = OperationAdmission.ExpectBinding(context, plan.Binding!.Identity, plan.Binding.Resource);
                OperationExecution<TResponse> execution = await services.GetRequiredService<IDurableOperationService>()
                    .ExecuteAsync(context, new OperationRequest<TRequest>(envelope.Operation!.OperationName,
                        envelope.Operation.RequestedScope, envelope.Operation.Key, input!), async (original, normalized, cancellation) =>
                    {
                        IRequestHandler<TRequest, TResponse> handler = definition.HandlerFactory(services)
                            ?? throw new InvalidOperationException("The durable message handler factory returned no scoped handler.");
                        plan.HandlerStarted = true;
                        return await handler.HandleAsync(new(original, services, envelope, attempt.DeliveryOrdinal, attempt.Generation),
                            normalized, cancellation).ConfigureAwait(false);
                    }, token).ConfigureAwait(false);
                if (execution.Disposition == OperationExecutionDisposition.Denied)
                {
                    return new(MessageInvocationKind.Rejected, "messaging.operation_denied");
                }
                if (execution.Disposition is OperationExecutionDisposition.Contended or OperationExecutionDisposition.RetryRequired
                    or OperationExecutionDisposition.RollbackOnly)
                {
                    return new(MessageInvocationKind.Retry, "messaging.operation_retry_required");
                }
                response = execution.Disposition switch
                {
                    OperationExecutionDisposition.Prepared or OperationExecutionDisposition.Replayed => new(MessageProtocolDisposition.Completed, execution.Decision),
                    OperationExecutionDisposition.KeyConflict => new(MessageProtocolDisposition.KeyConflict),
                    OperationExecutionDisposition.ResultExpired => new(MessageProtocolDisposition.ResultExpired),
                    _ => new(MessageProtocolDisposition.Incompatible)
                };
            }
            else
            {
                IRequestHandler<TRequest, TResponse> handler = definition.HandlerFactory(services)
                    ?? throw new InvalidOperationException("The query handler factory returned no scoped handler.");
                plan.HandlerStarted = true;
                OperationDecision<TResponse> decision = await handler.HandleAsync(
                    new(context, services, envelope, attempt.DeliveryOrdinal, attempt.Generation), input!, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The query handler returned no completed business decision.");
                response = new(MessageProtocolDisposition.Completed, decision);
            }
            if (!request)
            {
                return response.Disposition == MessageProtocolDisposition.Completed
                    ? new(MessageInvocationKind.Completed)
                    : new(MessageInvocationKind.Terminal, "messaging.operation_protocol_terminal");
            }
            Guid reply = await replies!.StageReplyAsync(context, definition, message, attempt, response, responseAdmission!, token).ConfigureAwait(false);
            return new(MessageInvocationKind.Completed, ReplyRecordId: reply);
        };
        return plan;
    }
}
