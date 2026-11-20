using CIS.Transactions;

namespace CIS.CommonTesting;

/// <summary>A recorded portable transaction owner, without native handles, work execution or timers.</summary>
/// <remarks>Request flags record calls, including rejected repeated completion requests.
/// Completion and its native-attempt flags are simulated facts supplied or normalized by the script.</remarks>
public sealed class ScriptedLocalTransaction : ILocalTransaction
{
    private readonly object gate = new();
    private readonly TransactionCompletion script;
    private LocalTransactionStatus status;
    private TransactionCompletion? completion;
    private string? rollbackReason;
    private bool commitRequested;
    private bool rollbackRequested;
    private bool disposeRequested;

    internal ScriptedLocalTransaction(string databaseName, TransactionIsolation isolation, TransactionCompletion script)
    {
        this.script = script;
        Context = new PortableContext(this, databaseName, isolation);
    }

    /// <inheritdoc />
    public ILocalTransactionContext Context
    {
        get;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>Gets whether a caller requested commit, including a rejected repeated request.</summary>
    public bool CommitRequested
    {
        get
        {
            lock (gate)
            {
                return commitRequested;
            }
        }
    }

    /// <summary>Gets whether a caller explicitly requested rollback, excluding simulated cleanup rollback.</summary>
    public bool RollbackRequested
    {
        get
        {
            lock (gate)
            {
                return rollbackRequested;
            }
        }
    }

    /// <summary>Gets whether a caller requested disposal, including disposal after completion.</summary>
    public bool DisposeRequested
    {
        get
        {
            lock (gate)
            {
                return disposeRequested;
            }
        }
    }

    /// <inheritdoc />
    public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            commitRequested = true;
            RequireActive();
            TransactionCompletion result;
            if (rollbackReason is not null)
            {
                result = WithoutCommit(TransactionEndReason.RollbackOnly,
                    new(TransactionErrorPhase.Commit, "transaction.rollback_only",
                        "A participant marked this transaction rollback-only."));
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                result = WithoutCommit(TransactionEndReason.Cancellation,
                    new(TransactionErrorPhase.Commit, "transaction.cancelled_before_commit",
                        "Cancellation was observed before the scripted commit."));
            }
            else
            {
                result = script;
            }
            Publish(result);
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<TransactionCompletion> RollbackAsync()
    {
        lock (gate)
        {
            rollbackRequested = true;
            RequireActive();
            TransactionCompletion result = WithoutCommit(TransactionEndReason.RollbackRequested);
            Publish(result);
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposeRequested = true;
            if (status != LocalTransactionStatus.Completed)
            {
                Publish(WithoutCommit(TransactionEndReason.UncommittedDisposal));
            }
            return ValueTask.CompletedTask;
        }
    }

    private TransactionCompletion WithoutCommit(TransactionEndReason reason, TransactionError? error = null)
    {
        bool rollbackConfirmed = !script.CleanupErrors.Any(item => item.Phase == TransactionErrorPhase.Rollback);
        return new(TransactionOutcome.NotCommitted, reason, commitAttempted: false, rollbackAttempted: true,
            rollbackConfirmed, error, script.CleanupErrors, script.DurationSeconds);
    }

    private void Publish(TransactionCompletion result)
    {
        completion = result;
        status = LocalTransactionStatus.Completed;
    }

    private void RequireActive()
    {
        if (status != LocalTransactionStatus.Active)
        {
            throw new InvalidOperationException("A scripted owner can complete only once.");
        }
    }

    private void MarkRollbackOnly(string reasonCode)
    {
        if (reasonCode is null || reasonCode.Length is < 1 or > 128
            || reasonCode.Any(character => character is < ' ' or > '~') || string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Use a nonempty printable ASCII reason of at most 128 characters.", nameof(reasonCode));
        }
        lock (gate)
        {
            RequireActive();
            rollbackReason ??= reasonCode;
        }
    }

    private sealed class PortableContext(ScriptedLocalTransaction owner, string databaseName, TransactionIsolation isolation)
        : ILocalTransactionContext
    {
        public string DatabaseName { get; } = databaseName;
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
    }
}
