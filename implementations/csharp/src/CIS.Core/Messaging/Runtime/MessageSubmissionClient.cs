using System.Data.Common;
using System.Runtime.ExceptionServices;
using CIS.Transactions;

namespace CIS.Messaging;

internal sealed class MessageSubmissionClient(MessageOutbox outbox, ILocalTransactionFactory transactions,
    Action? committedWake = null) : IMessagingClient
{
    public Task<MessageSubmission> PublishAsync<T>(string routeAlias, T value, MessageSendOptions? options = null,
        CancellationToken cancellationToken = default)
        => SubmitAsync(routeAlias, MessageKind.Event, value, options, cancellationToken);

    public Task<MessageSubmission> SendAsync<T>(string routeAlias, T value, MessageSendOptions? options = null,
        CancellationToken cancellationToken = default)
        => SubmitAsync(routeAlias, MessageKind.Command, value, options, cancellationToken);

    public Task<MessageRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(MessageRequest<TRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return outbox.Requests is { } coordinator
            ? coordinator.RequestAsync<TRequest, TResponse>(request, cancellationToken)
            : Task.FromResult(new MessageRequestResult<TResponse>(MessageExchangeOutcome.Unavailable,
                new(MessageSubmissionOutcome.NotSubmitted), error: new("messaging.requests_unavailable", "Typed request support is not selected.")));
    }

    public Task<MessageRequestResult<TResponse>> WaitForReplyAsync<TResponse>(MessageRequestTicket<TResponse> ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        cancellationToken.ThrowIfCancellationRequested();
        return outbox.Requests is { } coordinator
            ? coordinator.WaitForReplyAsync(ticket, cancellationToken)
            : Task.FromResult(new MessageRequestResult<TResponse>(MessageExchangeOutcome.Unavailable,
                new(MessageSubmissionOutcome.NotConfirmed, ticket.Reference.Receipt), ticket.Reference,
                error: new("messaging.requests_unavailable", "Typed request support is not selected.")));
    }

    private async Task<MessageSubmission> SubmitAsync<T>(string routeAlias, MessageKind kind, T value,
        MessageSendOptions? options, CancellationToken cancellationToken)
    {
        using MessagePreparedStage? prepared = outbox.Prepare(routeAlias, kind, value, options, cancellationToken, out MessageStageResult? failure);
        if (prepared is null)
        {
            return new(MessageSubmissionOutcome.NotSubmitted, error: failure!.Error);
        }
        ILocalTransaction owner;
        try
        {
            owner = await transactions.BeginAsync(outbox.DatabaseName,
                new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException)
        {
            return new(MessageSubmissionOutcome.NotSubmitted,
                error: new("messaging.submission_unavailable", "A local submission transaction could not begin."));
        }
        try
        {
            return await MessagingTransaction.UseAsync(owner, outbox.DatabaseName, async current =>
            {
                MessageStageReceipt receipt = await outbox.StagePreparedAsync(current.Context, prepared, cancellationToken).ConfigureAwait(false);
                TransactionCompletion completion = await current.CommitAsync(cancellationToken).ConfigureAwait(false);
                MessageSubmissionOutcome outcome = completion.Outcome switch
                {
                    TransactionOutcome.Committed => MessageSubmissionOutcome.OutboxCommitted,
                    TransactionOutcome.NotCommitted => MessageSubmissionOutcome.NotSubmitted,
                    _ => MessageSubmissionOutcome.Unknown
                };
                if (outcome == MessageSubmissionOutcome.OutboxCommitted)
                {
                    try
                    {
                        committedWake?.Invoke();
                    }
                    catch
                    {
                        // A wake hint never redefines actual committed responsibility.
                    }
                }
                return new MessageSubmission(outcome, receipt, completion);
            }).ConfigureAwait(false);
        }
        catch (MessageStorageCapacityException) when (owner.Completion?.Outcome == TransactionOutcome.NotCommitted)
        {
            return new(MessageSubmissionOutcome.NotSubmitted, completion: owner.Completion,
                error: new("messaging.storage_capacity", "Configured durable messaging storage capacity is occupied."));
        }
        catch (DbException error) when (error.IsTransient && owner.Completion?.Outcome == TransactionOutcome.NotCommitted)
        {
            return new(MessageSubmissionOutcome.NotSubmitted, completion: owner.Completion,
                error: new("messaging.store_unavailable", "Local submission storage is temporarily unavailable."));
        }
    }
}

internal static class MessagingTransaction
{
    internal static async Task<T> UseAsync<T>(ILocalTransaction owner, string databaseName, Func<ILocalTransaction, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(owner);
        T result = default!;
        ExceptionDispatchInfo? primary = null;
        try
        {
            if (owner.Status != LocalTransactionStatus.Active || owner.Context.DatabaseName != databaseName
                || owner.Context.Isolation != TransactionIsolation.ReadCommitted)
            {
                throw new InvalidOperationException("Messaging requires the original active ReadCommitted database owner.");
            }
            result = await work(owner).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary ??= ExceptionDispatchInfo.Capture(error);
        }
        primary?.Throw();
        return result;
    }
}
