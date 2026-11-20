using Microsoft.Extensions.Logging;

namespace CIS.Transactions;

// Operation-local flags and immutable safe metadata only; no native handles or exception history.
internal sealed class TransactionObservationScope(string database, TransactionIsolation isolation, bool logCalls,
    ILogger? logger, TransactionMetrics? metrics) : ITransactionObservation
{
    private static readonly EventId BeginFailed = new(15000, "transaction.begin.failed");
    private static readonly EventId BeginCleanupFailed = new(15001, "transaction.begin.cleanup.failed");
    private static readonly EventId Terminal = new(15002, "transaction.completed");
    private int published;
    private int completed;

    public void OwnerPublished()
    {
        if (Interlocked.Exchange(ref published, 1) == 0)
        {
            metrics?.Published(database);
        }
    }

    public void NativeFailure(TransactionNativePhase phase, bool expectedCancellation = false)
    {
        metrics?.NativeFailure(database, phase);
        if (phase == TransactionNativePhase.Begin && !expectedCancellation)
        {
            Log(LogLevel.Warning, current => current.LogWarning(BeginFailed,
                "Local transaction {Database} {Isolation} native begin failed.", database, isolation));
        }
    }

    public void BeginCleanupFailure(TransactionError error)
    {
        Log(LogLevel.Warning, current => current.LogWarning(BeginCleanupFailed,
            "Local transaction {Database} {Isolation} begin cleanup failed at {Phase} ({ErrorCode}).",
            database, isolation, error.Phase, error.Code));
    }

    public void Completed(TransactionCompletion completion, bool rollbackOnly = false)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref published) != 0)
        {
            metrics?.Completed(database, completion);
        }

        bool failure = rollbackOnly || completion.Outcome == TransactionOutcome.Unknown || completion.CleanupErrors.Count != 0
            || completion.Error is not null && completion.EndReason != TransactionEndReason.Cancellation;
        if (failure || logCalls)
        {
            LogLevel level = failure ? LogLevel.Warning : LogLevel.Debug;
            Log(level, current => current.Log(level, Terminal,
                "Local transaction {Database} {Isolation} settled {Outcome} for {EndReason} in {DurationSeconds} seconds; rollback-only {RollbackOnly}, commit attempted {CommitAttempted}, rollback attempted {RollbackAttempted}, rollback confirmed {RollbackConfirmed}, cleanup errors {CleanupErrorCount} ({ErrorCode}; rollback {RollbackErrorCode}, transaction dispose {TransactionDisposeErrorCode}, connection dispose {ConnectionDisposeErrorCode}).",
                database, isolation, completion.Outcome, completion.EndReason, completion.DurationSeconds,
                rollbackOnly, completion.CommitAttempted, completion.RollbackAttempted, completion.RollbackConfirmed,
                completion.CleanupErrors.Count, completion.Error?.Code,
                CleanupCode(TransactionErrorPhase.Rollback), CleanupCode(TransactionErrorPhase.TransactionDispose),
                CleanupCode(TransactionErrorPhase.ConnectionDispose)));
        }

        string? CleanupCode(TransactionErrorPhase phase) => completion.CleanupErrors.FirstOrDefault(error => error.Phase == phase)?.Code;
    }

    private void Log(LogLevel level, Action<ILogger> write)
    {
        if (logger is null)
        {
            return;
        }

        TransactionObservation.Attempt(() =>
        {
            if (logger.IsEnabled(level))
            {
                write(logger);
            }
        });
    }
}
