using System.Runtime.ExceptionServices;
using CIS.Transactions;

namespace CIS.Messaging;

internal enum MessageDeliveryFailureKind
{
    HandlerFault,
    CanonicalTerminal,
    Rejected,
    Shutdown,
    PersistenceUnknown,
    InfrastructureUnavailable
}

internal enum MessageDispositionHandlingKind
{
    ResponsibilityCommitted,
    Requeue,
    Stop
}

// References are borrowed for this awaited call; no native delivery or retained caller authority enters policy storage.
internal sealed record MessageFailedDelivery(EncodedMessage Message, MessageSubscriptionDefinition Subscription,
    MessageDeliveryFailureKind Kind, string SafeCode, MessageProcessingAttempt? Attempt = null,
    bool CanonicalEligible = false, TransactionCompletion? OriginalCompletion = null,
    ExceptionDispatchInfo? OriginalFault = null, IServiceProvider? Services = null,
    MessageFailureReplyStage? Reply = null);

// Optional Part-B safe protocol response staging, never a business handler or an Operations invocation.
internal delegate Task<Guid> MessageFailureReplyStage(ILocalTransactionContext context, CancellationToken cancellationToken);

internal sealed record MessageDispositionHandling(MessageDispositionHandlingKind Kind,
    TransactionCompletion? Completion = null, MessageProcessingDisposition? Disposition = null,
    Guid? HandoffRecordId = null, ExceptionDispatchInfo? Fault = null);

internal interface IMessageDeliveryDispositionCoordinator
{
    Task<MessageDispositionHandling> HandleAsync(MessageFailedDelivery failure, CancellationToken cancellationToken = default);
}

internal interface IMessageResponseCoordinator
{
    Task<Guid> StageReplyAsync<TRequest, TResponse>(ILocalTransactionContext context,
        MessageRequestSubscriptionDefinition<TRequest, TResponse> definition, EncodedMessage request,
        MessageProcessingAttempt attempt, MessageResponse<TResponse> response, CancellationToken cancellationToken);
    Task<MessageReplyHandling> ReceiveReplyAsync(EncodedMessage reply, IServiceProvider scopedServices,
        CancellationToken cancellationToken);
}

internal enum MessageReplyHandling
{
    Acknowledge,
    Quarantine,
    Stop
}
