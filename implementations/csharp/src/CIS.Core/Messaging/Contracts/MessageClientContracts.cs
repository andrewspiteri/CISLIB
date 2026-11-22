using CIS.Operations;
using CIS.Transactions;

namespace CIS.Messaging;

/// <summary>Expected local staging admission, without commit or broker evidence.</summary>
public enum MessageStageDisposition
{
    /// <summary>Immutable outgoing responsibility was staged in the active transaction.</summary>
    Staged,
    /// <summary>Known finite local capacity prevented acceptance.</summary>
    Overloaded,
    /// <summary>Known local configuration/lifecycle prevented acceptance.</summary>
    Unavailable
}

/// <summary>Submission persistence, independent of exchange and business outcomes.</summary>
public enum MessageSubmissionOutcome
{
    /// <summary>Submission is known not to have committed.</summary>
    NotSubmitted,
    /// <summary>Known committed outgoing responsibility; no broker or business-success claim.</summary>
    OutboxCommitted,
    /// <summary>Native submission commit may have happened.</summary>
    Unknown,
    /// <summary>A staged ticket has no independently confirmed committed submission.</summary>
    NotConfirmed
}

/// <summary>Process-local typed exchange completion, distinct from remote persistence.</summary>
public enum MessageExchangeOutcome
{
    /// <summary>A validated expected response arrived.</summary>
    ResponseReceived,
    /// <summary>The finite local wait elapsed without a definitive response.</summary>
    TimedOut,
    /// <summary>The caller or local lifetime stopped waiting; remote rollback is not implied.</summary>
    Cancelled,
    /// <summary>Submission is known not to have committed.</summary>
    NotSubmitted,
    /// <summary>Submission commit remains uncertain.</summary>
    SubmissionUnknown,
    /// <summary>Committed submission has not been independently confirmed.</summary>
    SubmissionNotConfirmed,
    /// <summary>A known local unavailable condition prevented acceptance.</summary>
    Unavailable,
    /// <summary>A known finite admission limit prevented acceptance.</summary>
    Overloaded,
    /// <summary>The exact expected authenticated attempt has invalid typed response content.</summary>
    InvalidResponse
}

/// <summary>A typed response protocol fact, distinct from business rejection.</summary>
public enum MessageProtocolDisposition
{
    /// <summary>Inspect the independent typed Succeeded/Rejected business decision.</summary>
    Completed,
    /// <summary>The retained operation identity has different business meaning.</summary>
    KeyConflict,
    /// <summary>The original result expired while duplicate protection remains.</summary>
    ResultExpired,
    /// <summary>The retained or response representation is incompatible.</summary>
    Incompatible,
    /// <summary>Current target/result authorization denied the request.</summary>
    Denied,
    /// <summary>No definitive original operation outcome was established.</summary>
    OutcomeUnknown,
    /// <summary>A defined availability condition prevented obtaining the requested result.</summary>
    Unavailable
}

/// <summary>Bounded optional transport diagnostics; no field grants authority or changes business idempotency.</summary>
public sealed class MessageSendOptions
{
    /// <summary>Copies optional diagnostic metadata and an explicit durable-command reference.</summary>
    public MessageSendOptions(string? correlationId = null, Guid? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null, MessageOperationReference? operation = null)
    {
        CorrelationId = correlationId is null ? null : MessageText.Unicode(correlationId, 128, nameof(correlationId));
        CausationId = causationId is { } cause ? MessageText.Id(cause, nameof(causationId)) : null;
        Headers = MessageEnvelope.CopyHeaders(headers);
        Operation = operation;
    }

    /// <summary>Gets optional bounded diagnostics.</summary>
    public string? CorrelationId
    {
        get;
    }
    /// <summary>Gets an optional causing logical message identity.</summary>
    public Guid? CausationId
    {
        get;
    }
    /// <summary>Gets copied bounded string headers.</summary>
    public IReadOnlyDictionary<string, string> Headers
    {
        get;
    }
    /// <summary>Gets an untrusted durable command reference when the registered binding requires it.</summary>
    public MessageOperationReference? Operation
    {
        get;
    }
}

/// <summary>An expected local staging result. A receipt is present only when immutable work was staged.</summary>
public sealed class MessageStageResult
{
    /// <summary>Creates consistent staged/admission facts.</summary>
    public MessageStageResult(MessageStageDisposition disposition, MessageStageReceipt? receipt = null, MessageError? error = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if ((disposition == MessageStageDisposition.Staged) != (receipt is not null))
        {
            throw new ArgumentException("Only staged work carries a staging receipt.", nameof(receipt));
        }
        Disposition = disposition;
        Receipt = receipt;
        Error = error;
    }

    /// <summary>Gets local admission/staging facts.</summary>
    public MessageStageDisposition Disposition
    {
        get;
    }
    /// <summary>Gets stable staged identifiers, without a commit claim.</summary>
    public MessageStageReceipt? Receipt
    {
        get;
    }
    /// <summary>Gets safe optional context.</summary>
    public MessageError? Error
    {
        get;
    }
}

/// <summary>Submission persistence and actual local completion, independent of broker or business results.</summary>
public sealed class MessageSubmission
{
    /// <summary>Creates consistent persistence facts; independent receipt reads may confirm without owning original completion.</summary>
    public MessageSubmission(MessageSubmissionOutcome outcome, MessageStageReceipt? receipt = null,
        TransactionCompletion? completion = null, MessageError? error = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        if (outcome is MessageSubmissionOutcome.OutboxCommitted or MessageSubmissionOutcome.Unknown && receipt is null
            || outcome == MessageSubmissionOutcome.Unknown && completion?.Outcome != TransactionOutcome.Unknown
            || outcome == MessageSubmissionOutcome.OutboxCommitted && completion is not null && completion.Outcome != TransactionOutcome.Committed
            || outcome == MessageSubmissionOutcome.NotSubmitted && completion is not null && completion.Outcome != TransactionOutcome.NotCommitted
            || outcome == MessageSubmissionOutcome.NotConfirmed && (receipt is null || completion is not null))
        {
            throw new ArgumentException("Submission outcome contradicts its receipt or actual transaction completion.", nameof(completion));
        }
        Outcome = outcome;
        Receipt = receipt;
        Completion = completion;
        Error = error;
    }

    /// <summary>Gets known submission persistence only.</summary>
    public MessageSubmissionOutcome Outcome
    {
        get;
    }
    /// <summary>Gets stable identifiers even after uncertain commit.</summary>
    public MessageStageReceipt? Receipt
    {
        get;
    }
    /// <summary>Gets actual method-owned transaction cleanup/completion, where available.</summary>
    public TransactionCompletion? Completion
    {
        get;
    }
    /// <summary>Gets bounded safe diagnostic context.</summary>
    public MessageError? Error
    {
        get;
    }
}

/// <summary>One typed query or durable-command input. The codec creates its owned staging snapshot.</summary>
public sealed class MessageRequest<TRequest>
{
    /// <summary>Creates request data without selecting arbitrary reply queues or granting operation scope.</summary>
    public MessageRequest(string requestAlias, TRequest input, MessageOperationReference? operation = null,
        TimeSpan? timeout = null, MessageSendOptions? diagnostics = null)
    {
        RequestAlias = MessageText.Alias(requestAlias, nameof(requestAlias));
        Input = input;
        Operation = operation;
        Timeout = timeout is { } selected
            ? MessageText.Duration(selected, TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(5), nameof(timeout)) : null;
        Diagnostics = diagnostics ?? new();
        if (Diagnostics.Operation is not null)
        {
            throw new ArgumentException("Supply the operation reference once through the request, not diagnostic metadata.", nameof(diagnostics));
        }
    }

    /// <summary>Gets the registered request alias.</summary>
    public string RequestAlias
    {
        get;
    }
    /// <summary>Gets input that must be encoded/owned before successful staging.</summary>
    public TRequest Input
    {
        get;
    }
    /// <summary>Gets an untrusted original operation reference for durable mode.</summary>
    public MessageOperationReference? Operation
    {
        get;
    }
    /// <summary>Gets optional finite whole-entry wait selection.</summary>
    public TimeSpan? Timeout
    {
        get;
    }
    /// <summary>Gets bounded diagnostics excluded from normalized business fingerprints.</summary>
    public MessageSendOptions Diagnostics
    {
        get;
    }
}

/// <summary>Immutable recovery/correlation metadata, retained independently of a process-local waiter.</summary>
public sealed record MessageRequestReference
{
    /// <summary>Copies exact identities and expected contract without a broker-acceptance claim.</summary>
    public MessageRequestReference(string requestAlias, Guid requestAttemptId, MessageStageReceipt receipt,
        MessageReplyRoute replyRoute, MessageContract responseContract, MessageOperationReference? operation = null)
    {
        RequestAlias = MessageText.Alias(requestAlias, nameof(requestAlias));
        RequestAttemptId = MessageText.Id(requestAttemptId, nameof(requestAttemptId));
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(replyRoute);
        ArgumentNullException.ThrowIfNull(responseContract);
        MessageText.Id(receipt.MessageId, nameof(receipt));
        MessageText.Id(receipt.RecordId, nameof(receipt));
        Receipt = receipt;
        ReplyRoute = replyRoute;
        ResponseContract = responseContract;
        Operation = operation;
    }

    /// <summary>Gets the finite registered request category.</summary>
    public string RequestAlias
    {
        get;
    }
    /// <summary>Gets this caller attempt's identity.</summary>
    public Guid RequestAttemptId
    {
        get;
    }
    /// <summary>Gets the original request MessageId.</summary>
    public Guid MessageId => Receipt.MessageId;
    /// <summary>Gets stable submission identifiers and digest.</summary>
    public MessageStageReceipt Receipt
    {
        get;
    }
    /// <summary>Gets the permitted process requester endpoint.</summary>
    public MessageReplyRoute ReplyRoute
    {
        get;
    }
    /// <summary>Gets the expected response schema.</summary>
    public MessageContract ResponseContract
    {
        get;
    }
    /// <summary>Gets the unchanged operation identity input for fresh recovery attempts.</summary>
    public MessageOperationReference? Operation
    {
        get;
    }
}

/// <summary>A bounded process-local waiter reservation, never a persisted Task or transaction owner.</summary>
/// <remarks>Production tickets belong to one runtime. Public test consumers can derive a substitute without internal bypasses.</remarks>
public abstract class MessageRequestTicket<TResponse> : IAsyncDisposable
{
    /// <summary>Captures immutable recovery/correlation data.</summary>
    protected MessageRequestTicket(MessageRequestReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Reference = reference;
    }

    /// <summary>Gets immutable request/operation identities after local waiting ends.</summary>
    public MessageRequestReference Reference
    {
        get;
    }
    /// <summary>Releases owned local capacity once; it neither commits nor deletes the staged request.</summary>
    public abstract ValueTask DisposeAsync();
}

/// <summary>Expected provisional-ticket admission, without hidden caller completion or waiting.</summary>
public sealed class MessageRequestStage<TResponse>
{
    /// <summary>Creates consistent local staging facts.</summary>
    public MessageRequestStage(MessageStageDisposition disposition, MessageRequestTicket<TResponse>? ticket = null,
        MessageError? error = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if ((disposition == MessageStageDisposition.Staged) != (ticket is not null))
        {
            throw new ArgumentException("Only staged requests carry a provisional ticket.", nameof(ticket));
        }
        Disposition = disposition;
        Ticket = ticket;
        Error = error;
    }

    /// <summary>Gets expected local staging/admission.</summary>
    public MessageStageDisposition Disposition
    {
        get;
    }
    /// <summary>Gets the disposable provisional waiter, only for staged work.</summary>
    public MessageRequestTicket<TResponse>? Ticket
    {
        get;
    }
    /// <summary>Gets bounded safe context.</summary>
    public MessageError? Error
    {
        get;
    }
}

/// <summary>A validated typed protocol reply, distinct from its Succeeded/Rejected business decision.</summary>
public sealed class MessageResponse<TResponse>
{
    /// <summary>Creates a completed decision or explicit nonbusiness protocol outcome.</summary>
    public MessageResponse(MessageProtocolDisposition disposition, OperationDecision<TResponse>? decision = null,
        MessageError? error = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if ((disposition == MessageProtocolDisposition.Completed) != (decision is not null))
        {
            throw new ArgumentException("Only Completed carries a typed business decision.", nameof(decision));
        }
        Disposition = disposition;
        Decision = decision;
        Error = error;
    }

    /// <summary>Gets the independent protocol fact.</summary>
    public MessageProtocolDisposition Disposition
    {
        get;
    }
    /// <summary>Gets the original typed business decision when Completed.</summary>
    public OperationDecision<TResponse>? Decision
    {
        get;
    }
    /// <summary>Gets bounded safe protocol context.</summary>
    public MessageError? Error
    {
        get;
    }
}

/// <summary>A final caller result retaining submission, exchange, protocol and business facts separately.</summary>
public sealed class MessageRequestResult<TResponse>
{
    /// <summary>Creates consistent final local facts without inferring remote rollback from a wait result.</summary>
    public MessageRequestResult(MessageExchangeOutcome outcome, MessageSubmission submission,
        MessageRequestReference? reference = null, MessageResponse<TResponse>? response = null, MessageError? error = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        ArgumentNullException.ThrowIfNull(submission);
        if ((outcome == MessageExchangeOutcome.ResponseReceived) != (response is not null))
        {
            throw new ArgumentException("Only an authenticated matching received response carries typed content.", nameof(response));
        }
        if (response is not null && reference is null)
        {
            throw new ArgumentException("A received response requires its exact request reference.", nameof(reference));
        }
        if (outcome == MessageExchangeOutcome.ResponseReceived
                && submission.Outcome is MessageSubmissionOutcome.NotSubmitted or MessageSubmissionOutcome.NotConfirmed
            || outcome == MessageExchangeOutcome.NotSubmitted && submission.Outcome != MessageSubmissionOutcome.NotSubmitted
            || outcome == MessageExchangeOutcome.SubmissionUnknown && submission.Outcome != MessageSubmissionOutcome.Unknown
            || outcome == MessageExchangeOutcome.SubmissionNotConfirmed && submission.Outcome != MessageSubmissionOutcome.NotConfirmed
            || reference is not null && submission.Receipt is not null && reference.Receipt != submission.Receipt)
        {
            throw new ArgumentException("Exchange outcome or request reference contradicts submission evidence.", nameof(submission));
        }
        Outcome = outcome;
        Submission = submission;
        Reference = reference;
        Response = response;
        Error = error;
    }

    /// <summary>Gets final process-local exchange completion.</summary>
    public MessageExchangeOutcome Outcome
    {
        get;
    }
    /// <summary>Gets independent outgoing persistence facts.</summary>
    public MessageSubmission Submission
    {
        get;
    }
    /// <summary>Gets stable identities even after uncertain submission or waiting stops.</summary>
    public MessageRequestReference? Reference
    {
        get;
    }
    /// <summary>Gets a validated typed protocol/business result, when received.</summary>
    public MessageResponse<TResponse>? Response
    {
        get;
    }
    /// <summary>Gets bounded safe local context.</summary>
    public MessageError? Error
    {
        get;
    }
}

/// <summary>Explicit original-context immutable staging; this port never completes the caller's owner.</summary>
public interface IMessageOutbox
{
    /// <summary>Stages an event; success means uncommitted outgoing responsibility only.</summary>
    Task<MessageStageResult> PublishAsync<T>(ILocalTransactionContext context, string routeAlias, T value,
        MessageSendOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Stages a command; expected serialization/store failures prohibit an apparently successful atomic commit.</summary>
    Task<MessageStageResult> SendAsync<T>(ILocalTransactionContext context, string routeAlias, T value,
        MessageSendOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reserves/registers a provisional local waiter before staging and returns without waiting.</summary>
    Task<MessageRequestStage<TResponse>> StageRequestAsync<TRequest, TResponse>(ILocalTransactionContext context,
        MessageRequest<TRequest> request, CancellationToken cancellationToken = default);
}

/// <summary>Convenience submission and bounded typed waits on the same explicit outbox machinery.</summary>
public interface IMessagingClient
{
    /// <summary>Owns and settles a short event submission transaction.</summary>
    Task<MessageSubmission> PublishAsync<T>(string routeAlias, T value, MessageSendOptions? options = null,
        CancellationToken cancellationToken = default);
    /// <summary>Owns and settles a short command submission transaction.</summary>
    Task<MessageSubmission> SendAsync<T>(string routeAlias, T value, MessageSendOptions? options = null,
        CancellationToken cancellationToken = default);
    /// <summary>Admits, stages/settles, then waits within the remaining monotonic entry deadline.</summary>
    Task<MessageRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(MessageRequest<TRequest> request,
        CancellationToken cancellationToken = default);
    /// <summary>Verifies exact committed submission through an independent read before waiting; never commits an owner.</summary>
    Task<MessageRequestResult<TResponse>> WaitForReplyAsync<TResponse>(MessageRequestTicket<TResponse> ticket,
        CancellationToken cancellationToken = default);
}
