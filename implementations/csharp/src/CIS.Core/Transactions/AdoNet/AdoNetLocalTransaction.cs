using System.Data.Common;

namespace CIS.Transactions.AdoNet;

internal sealed class AdoNetLocalTransaction : ILocalTransaction
{
    private static readonly AsyncLocal<Invocation?> CurrentInvocation = new();
    private readonly object gate = new();
    private readonly TimeSpan rollbackTimeout;
    private readonly TimeProvider clock;
    private readonly ITransactionObservation observation;
    private DbConnection? connection;
    private DbTransaction? transaction;
    private LocalTransactionStatus status;
    private TransactionCompletion? completion;
    private Task<TransactionCompletion>? terminal;
    private TaskCompletionSource? callbackSettled;
    private bool callbackActive;
    private bool disposalWaitingForCallback;
    private string? rollbackReason;
    private long? started;

    internal AdoNetLocalTransaction(string database, TransactionIsolation isolation, DbConnection connection,
        DbTransaction transaction, TimeSpan rollbackTimeout, TimeProvider clock, ITransactionObservation observation)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.rollbackTimeout = rollbackTimeout;
        this.clock = clock;
        this.observation = observation;
        Context = new Participant(this, database, isolation);
    }

    internal void Publish()
    {
        try
        {
            started = clock.GetTimestamp();
        }
        catch (Exception error) when (TransactionText.IsOrdinary(error))
        {
        }
        TransactionObservation.Attempt(observation.OwnerPublished);
    }

    public ILocalTransactionContext Context
    {
        get;
    }
    public LocalTransactionStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }
    public TransactionCompletion? Completion
    {
        get
        {
            lock (gate)
            {
                return completion;
            }
        }
    }

    public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default)
        => ClaimCompletion(TransactionEndReason.CommitRequested, cancellationToken);

    public Task<TransactionCompletion> RollbackAsync()
        => ClaimCompletion(TransactionEndReason.RollbackRequested, CancellationToken.None);

    private Task<TransactionCompletion> ClaimCompletion(TransactionEndReason reason, CancellationToken token)
    {
        TaskCompletionSource<TransactionCompletion> begin;
        lock (gate)
        {
            if (status != LocalTransactionStatus.Active || callbackActive)
            {
                throw new InvalidOperationException("Complete an active transaction only when no native callback is admitted.");
            }
            status = LocalTransactionStatus.Completing;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            terminal = begin.Task;
        }
        _ = CompleteAsync(begin, reason, token, null);
        return begin.Task;
    }

    public ValueTask DisposeAsync()
    {
        if (InsideCallback())
        {
            throw new InvalidOperationException("A native callback cannot dispose the transaction owner it is borrowing.");
        }
        TaskCompletionSource<TransactionCompletion> begin;
        Task? waiting;
        lock (gate)
        {
            if (status == LocalTransactionStatus.Completed)
            {
                return ValueTask.CompletedTask;
            }
            if (terminal is not null)
            {
                return new(terminal);
            }
            status = LocalTransactionStatus.Completing;
            disposalWaitingForCallback = callbackActive;
            waiting = callbackActive ? callbackSettled!.Task : null;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            terminal = begin.Task;
        }
        _ = CompleteAsync(begin, TransactionEndReason.UncommittedDisposal, CancellationToken.None, waiting);
        return new(begin.Task);
    }

    private async Task CompleteAsync(TaskCompletionSource<TransactionCompletion> published,
        TransactionEndReason reason, CancellationToken token, Task? callback)
    {
        try
        {
            if (callback is not null)
            {
                await callback.ConfigureAwait(false);
            }
            bool commitAttempted = false;
            TransactionOutcome outcome = TransactionOutcome.NotCommitted;
            TransactionError? primary = null;
            lock (gate)
            {
                disposalWaitingForCallback = false;
                if (reason == TransactionEndReason.CommitRequested && rollbackReason is not null)
                {
                    reason = TransactionEndReason.RollbackOnly;
                    primary = new(TransactionErrorPhase.Commit, "transaction.rollback_only",
                        "A participant marked this transaction rollback-only.");
                }
            }
            if (reason == TransactionEndReason.CommitRequested)
            {
                if (token.IsCancellationRequested)
                {
                    reason = TransactionEndReason.Cancellation;
                    primary = new(TransactionErrorPhase.Commit, "transaction.cancelled_before_commit",
                        "Cancellation was observed before invoking native commit.");
                }
                else
                {
                    commitAttempted = true;
                    try
                    {
                        await transaction!.CommitAsync(token).ConfigureAwait(false);
                        outcome = TransactionOutcome.Committed;
                    }
                    catch (Exception error) when (TransactionText.IsOrdinary(error))
                    {
                        outcome = TransactionOutcome.Unknown;
                        primary = new(TransactionErrorPhase.Commit, "transaction.commit_outcome_unknown",
                            "Native commit was invoked without confirmed successful completion.");
                        TransactionObservation.Attempt(() => observation.NativeFailure(TransactionNativePhase.Commit,
                            error is OperationCanceledException && token.IsCancellationRequested));
                    }
                }
            }

            TransactionCleanupResult cleanup = await TransactionCleanup.RunAsync(transaction, connection,
                rollback: outcome != TransactionOutcome.Committed, rollbackTimeout, observation).ConfigureAwait(false);
            var result = new TransactionCompletion(outcome, reason, commitAttempted, cleanup.RollbackAttempted,
                cleanup.RollbackConfirmed, primary, cleanup.Errors, DurationSeconds());
            lock (gate)
            {
                transaction = null;
                connection = null;
                completion = result;
                status = LocalTransactionStatus.Completed;
            }
            TransactionObservation.Attempt(() => observation.Completed(result));
            published.TrySetResult(result);
        }
        catch (Exception error)
        {
            // Fatal/runtime faults remain actual task faults, never ordinary completion snapshots.
            published.TrySetException(error);
        }
    }

    private double? DurationSeconds()
    {
        if (started is not { } origin)
        {
            return null;
        }
        try
        {
            double seconds = clock.GetElapsedTime(origin, clock.GetTimestamp()).TotalSeconds;
            return double.IsFinite(seconds) && seconds >= 0 ? seconds : null;
        }
        catch (Exception error) when (TransactionText.IsOrdinary(error))
        {
            return null;
        }
    }

    private void MarkRollbackOnly(string reason)
    {
        TransactionText.Code(reason, nameof(reason));
        lock (gate)
        {
            if (status != LocalTransactionStatus.Active
                && !(disposalWaitingForCallback && callbackActive && InsideCallback()))
            {
                throw new InvalidOperationException("Rollback-only marking is closed after completion has claimed ownership.");
            }
            rollbackReason ??= reason;
        }
    }

    private Task<T> UseAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(work);
        DbConnection selectedConnection;
        DbTransaction selectedTransaction;
        lock (gate)
        {
            if (status != LocalTransactionStatus.Active || callbackActive || rollbackReason is not null)
            {
                throw new InvalidOperationException("Admit one native callback only on an active transaction that is not rollback-only.");
            }
            callbackActive = true;
            callbackSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            selectedConnection = connection!;
            selectedTransaction = transaction!;
        }
        return RunWorkAsync(work, selectedConnection, selectedTransaction, token);
    }

    private async Task<T> RunWorkAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work,
        DbConnection selectedConnection, DbTransaction selectedTransaction, CancellationToken token)
    {
        Invocation? previous = CurrentInvocation.Value;
        var invocation = new Invocation(this, previous);
        CurrentInvocation.Value = invocation;
        try
        {
            token.ThrowIfCancellationRequested();
            Task<T> pending = work(selectedConnection, selectedTransaction, token)
                ?? throw new InvalidOperationException("The native callback returned no task.");
            T result = await pending.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error) when (TransactionText.IsOrdinary(error))
        {
            lock (gate)
            {
                rollbackReason ??= error is OperationCanceledException && token.IsCancellationRequested
                    ? "transaction.work_cancelled" : "transaction.work_failed";
            }
            throw;
        }
        finally
        {
            invocation.Active = false;
            CurrentInvocation.Value = previous;
            TaskCompletionSource notify;
            lock (gate)
            {
                callbackActive = false;
                notify = callbackSettled!;
            }
            notify.TrySetResult();
        }
    }

    private bool InsideCallback()
    {
        for (Invocation? current = CurrentInvocation.Value; current is not null; current = current.Parent)
        {
            if (current.Active && ReferenceEquals(current.Owner, this))
            {
                return true;
            }
        }
        return false;
    }

    private sealed class Invocation(AdoNetLocalTransaction owner, Invocation? parent)
    {
        internal AdoNetLocalTransaction Owner { get; } = owner;
        internal Invocation? Parent { get; } = parent;
        internal volatile bool Active = true;
    }

    private sealed class Participant(AdoNetLocalTransaction owner, string database, TransactionIsolation isolation)
        : IAdoNetTransactionContext
    {
        public string DatabaseName { get; } = database;
        public TransactionIsolation Isolation { get; } = isolation;
        public bool IsRollbackOnly
        {
            get
            {
                lock (owner.gate)
                {
                    return owner.rollbackReason is not null;
                }
            }
        }
        public string? RollbackReasonCode
        {
            get
            {
                lock (owner.gate)
                {
                    return owner.rollbackReason;
                }
            }
        }

        public void MarkRollbackOnly(string reasonCode) => owner.MarkRollbackOnly(reasonCode);
        public Task<T> UseAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default) => owner.UseAsync(work, cancellationToken);

        public Task UseAsync(Func<DbConnection, DbTransaction, CancellationToken, Task> work,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);
            return owner.UseAsync<object?>(async (connection, transaction, token) =>
            {
                await work(connection, transaction, token).ConfigureAwait(false);
                return null;
            }, cancellationToken);
        }
    }
}
