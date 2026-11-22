using System.Data.Common;
using System.Runtime.ExceptionServices;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Messaging;

internal sealed partial class MessageRequestCoordinator : IMessageRequestCoordinator, IMessageResponseCoordinator, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly MessagingSnapshot snapshot;
    private readonly IMessagingStore store;
    private readonly ILocalTransactionFactory transactions;
    private readonly IMessageTransport transport;
    private readonly MessageOutbox outbox;
    private readonly MessagingResourceBudget resources;
    private readonly LocalApplicationIdentity local;
    private readonly MessagingInstrumentation? instrumentation;
    private readonly TimeProvider clock;
    private readonly Action? wake;
    private readonly Dictionary<Guid, IMessageLocalWaiter> waiters = [];
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing;
    private int active;
    private ExceptionDispatchInfo? cleanupFault;

    internal MessageRequestCoordinator(MessagingSnapshot snapshot, IMessagingStore store, ILocalTransactionFactory transactions,
        IMessageTransport transport, MessageOutbox outbox, MessagingResourceBudget resources, LocalApplicationIdentity local,
        TimeProvider? timeProvider = null, MessagingInstrumentation? instrumentation = null, Action? wake = null)
    {
        snapshot.Validate();
        if (snapshot.Requests.Count != 0 && !resources.ReplyIngressEnabled)
        {
            throw new InvalidOperationException("RPC must reserve its reply lane in the shared process budget.");
        }
        this.snapshot = snapshot;
        this.store = store;
        this.transactions = transactions;
        this.transport = transport;
        this.outbox = outbox;
        this.resources = resources;
        this.local = local;
        this.instrumentation = instrumentation;
        clock = timeProvider ?? TimeProvider.System;
        this.wake = wake;
    }

    public async Task<MessageRequestStage<TResponse>> StageRequestAsync<TRequest, TResponse>(ILocalTransactionContext context,
        MessageRequest<TRequest> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enter())
        {
            MessageOutbox.Poison(context, "messaging.stopping");
            return new(MessageStageDisposition.Unavailable, error: Unavailable());
        }
        MessageRpcTicket<TResponse>? ticket = null;
        bool staged = false;
        ExceptionDispatchInfo? primary = null;
        MessageRequestStage<TResponse>? result = null;
        try
        {
            long entry = clock.GetTimestamp();
            using MessagePreparedStage? prepared = Prepare<TRequest, TResponse>(request, entry, true,
                cancellationToken, out ticket, out MessageStageResult? failure);
            if (prepared is null)
            {
                MessageOutbox.Poison(context, "messaging.request_refused");
                result = new(failure!.Disposition, error: failure.Error);
            }
            else
            {
                using CancellationTokenRegistration caller = cancellationToken.UnsafeRegister(static state => ((IMessageLocalWaiter)state!).Cancel(), ticket);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ticket!.DeadlineToken, ticket.LocalToken);
                await outbox.StagePreparedAsync(context, prepared, linked.Token).ConfigureAwait(false);
                staged = true;
                result = new(MessageStageDisposition.Staged, ticket);
            }
        }
        catch (MessageStorageCapacityException)
        {
            MessageOutbox.Poison(context, "messaging.storage_capacity");
            result = new(MessageStageDisposition.Overloaded,
                error: new("messaging.storage_capacity", "Configured durable messaging storage capacity is occupied."));
        }
        catch (Exception error)
        {
            MessageOutbox.Poison(context, "messaging.request_stage_failed");
            primary = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            ticket?.StagingSettled();
            if (!staged && ticket is not null)
            {
                try
                {
                    await ticket.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    primary ??= ExceptionDispatchInfo.Capture(error);
                }
            }
            Exit();
        }
        primary?.Throw();
        return result!;
    }

    public async Task<MessageRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(MessageRequest<TRequest> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        long entry = clock.GetTimestamp();
        if (!Enter())
        {
            return new(MessageExchangeOutcome.Unavailable, new(MessageSubmissionOutcome.NotSubmitted), error: Unavailable());
        }
        using MessageObservation? observation = instrumentation?.Rpc(request.RequestAlias);
        MessageRpcTicket<TResponse>? ticket = null;
        ILocalTransaction? owner = null;
        MessagePreparedStage? prepared = null;
        CancellationToken executionToken = default;
        MessageRequestResult<TResponse>? result = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            prepared = Prepare<TRequest, TResponse>(request, entry, false,
                cancellationToken, out ticket, out MessageStageResult? failure);
            if (prepared is null)
            {
                result = new(failure!.Disposition == MessageStageDisposition.Overloaded ? MessageExchangeOutcome.Overloaded
                    : MessageExchangeOutcome.Unavailable, new(MessageSubmissionOutcome.NotSubmitted), error: failure.Error);
            }
            else
            {
                using CancellationTokenRegistration caller = cancellationToken.UnsafeRegister(static state => ((IMessageLocalWaiter)state!).Cancel(), ticket);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ticket!.DeadlineToken, ticket.LocalToken);
                executionToken = linked.Token;
                owner = await transactions.BeginAsync(outbox.DatabaseName,
                    new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, linked.Token).ConfigureAwait(false);
                TransactionCompletion completion = await MessagingTransaction.UseAsync(owner, outbox.DatabaseName, async current =>
                {
                    await outbox.StagePreparedAsync(current.Context, prepared, linked.Token).ConfigureAwait(false);
                    return await current.CommitAsync(linked.Token).ConfigureAwait(false);
                }).ConfigureAwait(false);
                prepared.Dispose();
                prepared = null;
                ticket.StagingSettled();
                MessageSubmission submission = Submission(completion, ticket.Reference.Receipt);
                if (completion.Outcome == TransactionOutcome.Committed)
                {
                    Hint();
                    result = await AwaitReplyAsync(ticket, submission, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    MessageExchangeOutcome outcome = completion.Outcome == TransactionOutcome.Unknown ? MessageExchangeOutcome.SubmissionUnknown
                        : linked.IsCancellationRequested ? ticket.CancellationOutcome : MessageExchangeOutcome.NotSubmitted;
                    result = new(outcome, submission, ticket.Reference);
                }
            }
        }
        catch (OperationCanceledException error) when (ticket is not null && executionToken.IsCancellationRequested
            && error.CancellationToken == executionToken)
        {
            result = new(ticket.CancellationOutcome, owner?.Completion is { } completion
                ? Submission(completion, ticket.Reference.Receipt) : new(MessageSubmissionOutcome.NotSubmitted), ticket.Reference);
        }
        catch (MessageStorageCapacityException) when (ticket is not null && owner?.Completion?.Outcome == TransactionOutcome.NotCommitted)
        {
            result = new(MessageExchangeOutcome.Overloaded, Submission(owner.Completion, ticket.Reference.Receipt), ticket.Reference);
        }
        catch (DbException error) when (error.IsTransient && (owner is null || owner.Completion?.Outcome == TransactionOutcome.NotCommitted))
        {
            result = new(MessageExchangeOutcome.Unavailable, owner?.Completion is { } completion && ticket is not null
                ? Submission(completion, ticket.Reference.Receipt) : new(MessageSubmissionOutcome.NotSubmitted), ticket?.Reference);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            prepared?.Dispose();
            ticket?.StagingSettled();
            if (ticket is not null)
            {
                try
                {
                    if (result is not null)
                    {
                        ticket.Consume(result.Outcome);
                    }
                    await ticket.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    primary ??= ExceptionDispatchInfo.Capture(error);
                }
            }
            if (result is not null)
            {
                observation?.Complete(OutcomeName(result.Outcome));
            }
            Exit();
        }
        primary?.Throw();
        return result!;
    }

    public async Task<MessageRequestResult<TResponse>> WaitForReplyAsync<TResponse>(MessageRequestTicket<TResponse> ticket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (ticket is not MessageRpcTicket<TResponse> current || !ReferenceEquals(current.Owner, this))
        {
            throw new ArgumentException("Use this runtime's exact typed provisional ticket.", nameof(ticket));
        }
        var unconfirmed = new MessageSubmission(MessageSubmissionOutcome.NotConfirmed, ticket.Reference.Receipt);
        if (!Enter())
        {
            return new(MessageExchangeOutcome.Cancelled, unconfirmed, ticket.Reference);
        }
        bool enteredWait;
        try
        {
            enteredWait = current.BeginWait();
        }
        catch
        {
            Exit();
            throw;
        }
        if (!enteredWait)
        {
            current.EndWait();
            Exit();
            return new(current.CancellationOutcome, unconfirmed, ticket.Reference);
        }
        CancellationToken executionToken = default;
        try
        {
            using CancellationTokenRegistration caller = cancellationToken.UnsafeRegister(static state => ((IMessageLocalWaiter)state!).Cancel(), current);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, current.DeadlineToken, current.LocalToken);
            executionToken = linked.Token;
            ILocalTransaction read = await transactions.BeginAsync(outbox.DatabaseName,
                new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, linked.Token).ConfigureAwait(false);
            MessageSubmissionObservation observed = await MessagingTransaction.UseAsync(read, outbox.DatabaseName, async owner =>
            {
                MessageSubmissionObservation value = await store.ReadSubmissionAsync(owner.Context, current.Reference.Receipt, linked.Token).ConfigureAwait(false);
                if ((await owner.RollbackAsync().ConfigureAwait(false)).Outcome != TransactionOutcome.NotCommitted)
                {
                    throw new InvalidOperationException("Independent receipt observation did not settle definitively.");
                }
                return value;
            }).ConfigureAwait(false);
            if (observed != MessageSubmissionObservation.Found)
            {
                return new(MessageExchangeOutcome.SubmissionNotConfirmed, unconfirmed, ticket.Reference);
            }
            return await AwaitReplyAsync(current, new(MessageSubmissionOutcome.OutboxCommitted, ticket.Reference.Receipt), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (executionToken.IsCancellationRequested && error.CancellationToken == executionToken)
        {
            current.Cancel();
            return new(current.CancellationOutcome, unconfirmed, ticket.Reference);
        }
        finally
        {
            current.EndWait();
            Exit();
        }
    }

    private async Task<MessageRequestResult<T>> AwaitReplyAsync<T>(MessageRpcTicket<T> ticket, MessageSubmission submission, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, ticket.DeadlineToken, ticket.LocalToken);
        MessageReplyCompletion<T> reply;
        try
        {
            await ticket.WaitAsync(linked.Token).ConfigureAwait(false);
            reply = ticket.TakeResponse();
        }
        catch (OperationCanceledException error) when (linked.IsCancellationRequested && error.CancellationToken == linked.Token)
        {
            reply = new(ticket.CancellationOutcome);
        }
        ticket.Consume(reply.Outcome);
        return new(reply.Outcome, submission, ticket.Reference, reply.Response);
    }

    private MessagePreparedStage? Prepare<TRequest, TResponse>(MessageRequest<TRequest> request, long entry, bool observe,
        CancellationToken token, out MessageRpcTicket<TResponse>? ticket, out MessageStageResult? failure)
    {
        ticket = null;
        failure = null;
        token.ThrowIfCancellationRequested();
        if (!transport.IsReady || transport.ReplyEndpoint is not { } endpoint || endpoint.Application != local.Application
            || endpoint.Environment != local.Environment)
        {
            failure = new(MessageStageDisposition.Unavailable, error: Unavailable());
            return null;
        }
        if (!resources.TryAcquireRequest(out IDisposable? permit))
        {
            failure = new(MessageStageDisposition.Overloaded, error: new("messaging.request_capacity", "The bounded request capacity is occupied."));
            return null;
        }
        MessagePreparedStage? prepared = null;
        try
        {
            var definition = snapshot.GetRequest(request.RequestAlias) as MessageRequestDefinition<TRequest, TResponse>
                ?? throw new ArgumentException("Request and response types must match the registered request alias.", nameof(request));
            if (definition.RequestCodec.Contract != definition.RequestContract || definition.ResponseCodec.Contract != definition.ResponseContract
                || (definition.Mode == MessageRequestMode.DurableCommand) != (request.Operation is not null)
                || request.Operation is not null && request.Operation.OperationName != definition.OperationName)
            {
                throw new ArgumentException("The request codec/mode/operation does not match its immutable registration.", nameof(request));
            }
            TimeSpan timeout = request.Timeout ?? snapshot.Settings.DefaultRequestTimeout;
            if (timeout > snapshot.Settings.MaximumRequestTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "The selected request timeout exceeds the captured runtime limit.");
            }
            Guid attempt = Guid.NewGuid();
            var options = new MessageSendOptions(request.Diagnostics.CorrelationId, request.Diagnostics.CausationId,
                request.Diagnostics.Headers, request.Operation);
            prepared = outbox.Prepare(definition.RouteAlias, MessageKind.Request, request.Input, options, token, out failure, attempt, endpoint);
            if (prepared is null)
            {
                permit!.Dispose();
                return null;
            }
            var receipt = new MessageStageReceipt(prepared.Record.RecordId, prepared.Record.Message.Envelope.MessageId,
                outbox.DatabaseName, local.Application, local.Environment, prepared.Record.Message.Digest);
            ticket = new(this, new(definition.Alias, attempt, receipt, endpoint, definition.ResponseContract, request.Operation),
                definition, definition.ResponseCodec, resources, snapshot.Settings.Limits, clock, entry, timeout, permit!,
                observe ? instrumentation?.Rpc(definition.Alias) : null);
            lock (gate)
            {
                if (closing)
                {
                    ticket.Cancel();
                    throw new OperationCanceledException("Request admission closed during preparation.", ticket.LocalToken);
                }
                waiters.Add(attempt, ticket);
            }
            return prepared;
        }
        catch
        {
            prepared?.Dispose();
            if (ticket is null)
            {
                permit!.Dispose();
            }
            else
            {
                ticket.Cancel();
            }
            throw;
        }
    }

    private static MessageSubmission Submission(TransactionCompletion completion, MessageStageReceipt receipt)
        => new(completion.Outcome == TransactionOutcome.Committed ? MessageSubmissionOutcome.OutboxCommitted
            : completion.Outcome == TransactionOutcome.Unknown ? MessageSubmissionOutcome.Unknown : MessageSubmissionOutcome.NotSubmitted,
            receipt, completion);

    private static MessageError Unavailable() => new("messaging.requests_unavailable", "The selected requester endpoint is not available.");

    private void Hint()
    {
        try
        {
            wake?.Invoke();
        }
        catch (Exception)
        {
            // Durable publication has its existing bounded poll; a hint cannot redefine commit.
        }
    }

    internal static string OutcomeName(MessageExchangeOutcome outcome) => outcome switch
    {
        MessageExchangeOutcome.ResponseReceived => "response_received",
        MessageExchangeOutcome.TimedOut => "timed_out",
        MessageExchangeOutcome.Cancelled => "cancelled",
        MessageExchangeOutcome.NotSubmitted => "not_submitted",
        MessageExchangeOutcome.SubmissionUnknown => "submission_unknown",
        MessageExchangeOutcome.SubmissionNotConfirmed => "submission_not_confirmed",
        MessageExchangeOutcome.Unavailable => "unavailable",
        MessageExchangeOutcome.Overloaded => "overloaded",
        _ => "invalid_response"
    };

    private bool Enter()
    {
        lock (gate)
        {
            if (closing)
            {
                return false;
            }
            active++;
            return true;
        }
    }

    private void Exit()
    {
        lock (gate)
        {
            active--;
            Settle();
        }
    }

    internal void Remove(IMessageLocalWaiter ticket, Exception? failure = null)
    {
        lock (gate)
        {
            if (failure is not null)
            {
                cleanupFault ??= ExceptionDispatchInfo.Capture(failure);
            }
            if (waiters.TryGetValue(ticket.Reference.RequestAttemptId, out IMessageLocalWaiter? current) && ReferenceEquals(current, ticket))
            {
                waiters.Remove(ticket.Reference.RequestAttemptId);
            }
            Settle();
        }
    }

    private void Settle()
    {
        if (closing && active == 0 && waiters.Count == 0)
        {
            if (cleanupFault is null)
            {
                drained.TrySetResult();
            }
            else
            {
                drained.TrySetException(cleanupFault.SourceException);
            }
        }
    }

    internal void BeginStopping()
    {
        IMessageLocalWaiter[] pending;
        lock (gate)
        {
            closing = true;
            pending = waiters.Values.ToArray();
            Settle();
        }
        foreach (IMessageLocalWaiter waiter in pending)
        {
            waiter.Cancel();
        }
    }

    internal Task StopAsync()
    {
        BeginStopping();
        return drained.Task;
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
