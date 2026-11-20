using CIS.Transactions;

namespace CIS.CommonTesting;

/// <summary>Consumes bounded portable transaction scripts without a database or native ADO.NET capability.</summary>
/// <remarks>Scripted completion facts are test inputs, not evidence that native commit or cleanup occurred.
/// Create a new factory when its retained-owner capacity is exhausted.</remarks>
public sealed class ScriptedLocalTransactionFactory : ILocalTransactionFactory
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedLocalTransaction> created = [];
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;

    /// <summary>Bounds pending scripts and retained owners independently, both defaulting to sixteen.</summary>
    public ScriptedLocalTransactionFactory(int scriptCapacity = 16, int recordingCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);
        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <summary>Gets a detached bounded collection of live, inspectable portable owner recordings.</summary>
    public IReadOnlyList<ScriptedLocalTransaction> CreatedTransactions
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(created.ToArray());
            }
        }
    }

    /// <summary>Gets how many scripts remain, without consuming one.</summary>
    public int PendingScripts
    {
        get
        {
            lock (gate)
            {
                return scripts.Count;
            }
        }
    }

    /// <summary>Queues a successful begin and immutable facts for a subsequent commit request.</summary>
    /// <remarks>Omitting completion scripts a clean committed result. The completion must describe
    /// CommitRequested; rollback-only and caller cancellation can still refuse that commit.
    /// Cleanup errors must describe each applicable cleanup phase at most once, in native order.</remarks>
    public void Enqueue(TransactionCompletion? commitCompletion = null,
        TransactionIsolation defaultIsolation = TransactionIsolation.ReadCommitted)
    {
        if (!Enum.IsDefined(defaultIsolation))
        {
            throw new ArgumentOutOfRangeException(nameof(defaultIsolation));
        }
        TransactionCompletion selected = commitCompletion ?? new(TransactionOutcome.Committed,
            TransactionEndReason.CommitRequested, commitAttempted: true, rollbackAttempted: false,
            rollbackConfirmed: false);
        ValidateCompletion(selected);
        Add(new(null, selected, defaultIsolation));
    }

    /// <summary>Queues the original begin exception without creating an owner.</summary>
    public void EnqueueBeginFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(exception, null, TransactionIsolation.ReadCommitted));
    }

    private void Add(Step step)
    {
        lock (gate)
        {
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The configured transaction script capacity has been reached.");
            }
            scripts.Enqueue(step);
        }
    }

    /// <inheritdoc />
    public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
        => BeginAsync(databaseName, new LocalTransactionOptions(), cancellationToken);

    /// <inheritdoc />
    public Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateAlias(databaseName);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Isolation is { } requested && !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (created.Count == recordingCapacity)
            {
                throw new InvalidOperationException("The configured transaction recording capacity has been reached.");
            }
            if (scripts.Count == 0)
            {
                throw new InvalidOperationException("No transaction begin is scripted for this call.");
            }
            Step step = scripts.Dequeue();
            if (step.BeginException is { } failure)
            {
                return Task.FromException<ILocalTransaction>(failure);
            }
            var owner = new ScriptedLocalTransaction(databaseName, options.Isolation ?? step.DefaultIsolation,
                step.CommitCompletion!);
            created.Add(owner);
            return Task.FromResult<ILocalTransaction>(owner);
        }
    }

    private static void ValidateCompletion(TransactionCompletion completion)
    {
        if (completion.EndReason != TransactionEndReason.CommitRequested)
        {
            throw new ArgumentException("Script commit-request facts; the helper selects other terminal reasons.", nameof(completion));
        }
        TransactionErrorPhase previous = TransactionErrorPhase.Commit;
        foreach (TransactionError error in completion.CleanupErrors)
        {
            if ((int)error.Phase <= (int)previous)
            {
                throw new ArgumentException("Script distinct cleanup phases in rollback, transaction, connection order.", nameof(completion));
            }
            previous = error.Phase;
        }
        if (completion.CleanupErrors.Any(error => error.Phase == TransactionErrorPhase.Rollback)
            && (!completion.RollbackAttempted || completion.RollbackConfirmed))
        {
            throw new ArgumentException("A rollback cleanup error requires an attempted but unconfirmed rollback.", nameof(completion));
        }
        if (completion.Outcome == TransactionOutcome.Committed
            && (completion.RollbackAttempted || completion.Error is not null))
        {
            throw new ArgumentException("A confirmed commit has no rollback attempt or primary commit error; script release errors separately.", nameof(completion));
        }
    }

    private static void ValidateAlias(string value)
    {
        if (value is null || value.Length is < 1 or > 64 || !IsAlphaNumeric(value[0])
            || value.Any(character => !IsAlphaNumeric(character) && character is not '_' and not '.' and not '-'))
        {
            throw new ArgumentException("Use a database alias matching [A-Za-z0-9][A-Za-z0-9_.-]{0,63}.", nameof(value));
        }
    }

    private static bool IsAlphaNumeric(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private sealed record Step(Exception? BeginException, TransactionCompletion? CommitCompletion,
        TransactionIsolation DefaultIsolation);
}
