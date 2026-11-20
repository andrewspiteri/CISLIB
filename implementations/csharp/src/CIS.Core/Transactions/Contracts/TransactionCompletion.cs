namespace CIS.Transactions;

/// <summary>Immutable persistence evidence, published only after all owned cleanup attempts settle.</summary>
public sealed class TransactionCompletion
{
    /// <summary>Copies bounded terminal facts. Cleanup diagnostics never redefine persistence outcome.</summary>
    public TransactionCompletion(TransactionOutcome outcome, TransactionEndReason endReason,
        bool commitAttempted, bool rollbackAttempted, bool rollbackConfirmed,
        TransactionError? error = null, IReadOnlyList<TransactionError>? cleanupErrors = null,
        double? durationSeconds = null)
    {
        if (!Enum.IsDefined(outcome) || !Enum.IsDefined(endReason))
        {
            throw new ArgumentException("Use declared transaction outcomes and end reasons.");
        }
        if (rollbackConfirmed && !rollbackAttempted
            || outcome is TransactionOutcome.Committed or TransactionOutcome.Unknown && !commitAttempted
            || outcome == TransactionOutcome.NotCommitted && commitAttempted)
        {
            throw new ArgumentException("Completion flags must agree with the stated native persistence evidence.");
        }
        if (durationSeconds is { } duration && (!double.IsFinite(duration) || duration < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }
        if (cleanupErrors?.Count > 3)
        {
            throw new ArgumentException("At most three owned cleanup attempts can fail.", nameof(cleanupErrors));
        }
        TransactionError[] copied = cleanupErrors?.ToArray() ?? [];
        if (copied.Any(item => item is null))
        {
            throw new ArgumentException("Cleanup errors cannot contain null.", nameof(cleanupErrors));
        }
        Outcome = outcome;
        EndReason = endReason;
        CommitAttempted = commitAttempted;
        RollbackAttempted = rollbackAttempted;
        RollbackConfirmed = rollbackConfirmed;
        Error = error;
        CleanupErrors = Array.AsReadOnly(copied);
        DurationSeconds = durationSeconds;
    }

    /// <summary>Gets the decisive persistence fact.</summary>
    public TransactionOutcome Outcome
    {
        get;
    }
    /// <summary>Gets why this terminal path was selected.</summary>
    public TransactionEndReason EndReason
    {
        get;
    }
    /// <summary>Gets whether native commit was invoked.</summary>
    public bool CommitAttempted
    {
        get;
    }
    /// <summary>Gets whether cleanup explicitly invoked native rollback.</summary>
    public bool RollbackAttempted
    {
        get;
    }
    /// <summary>Gets whether that rollback returned successfully, without resolving an unknown commit.</summary>
    public bool RollbackConfirmed
    {
        get;
    }
    /// <summary>Gets the primary completion problem, separately from resource cleanup.</summary>
    public TransactionError? Error
    {
        get;
    }
    /// <summary>Gets at most three cleanup errors in actual attempt order.</summary>
    public IReadOnlyList<TransactionError> CleanupErrors
    {
        get;
    }
    /// <summary>Gets the one monotonic owner-lifetime measurement, or null if unavailable.</summary>
    public double? DurationSeconds
    {
        get;
    }
}
