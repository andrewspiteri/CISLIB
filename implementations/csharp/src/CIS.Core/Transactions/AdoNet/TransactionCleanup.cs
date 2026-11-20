using System.Data.Common;

namespace CIS.Transactions.AdoNet;

internal sealed record TransactionCleanupResult(bool RollbackAttempted, bool RollbackConfirmed,
    IReadOnlyList<TransactionError> Errors);

internal static class TransactionCleanup
{
    internal static async Task<TransactionCleanupResult> RunAsync(DbTransaction? transaction, DbConnection? connection,
        bool rollback, TimeSpan rollbackTimeout, ITransactionObservation observation, bool failedBegin = false)
    {
        bool attempted = false;
        bool confirmed = false;
        var errors = new List<TransactionError>(3);
        if (rollback && transaction is not null)
        {
            using var cancellation = new CancellationTokenSource(rollbackTimeout);
            attempted = true;
            try
            {
                await transaction.RollbackAsync(cancellation.Token).ConfigureAwait(false);
                confirmed = true;
            }
            catch (Exception error) when (TransactionText.IsOrdinary(error))
            {
                Failed(TransactionNativePhase.Rollback, new(TransactionErrorPhase.Rollback,
                    "transaction.rollback_failed", "The native rollback cleanup did not complete successfully."),
                    error is OperationCanceledException && cancellation.IsCancellationRequested);
            }
        }
        if (transaction is not null)
        {
            try
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (TransactionText.IsOrdinary(error))
            {
                Failed(TransactionNativePhase.Dispose, new(TransactionErrorPhase.TransactionDispose,
                    "transaction.transaction_disposal_failed", "The native transaction disposal failed."));
            }
        }
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (TransactionText.IsOrdinary(error))
            {
                Failed(TransactionNativePhase.Dispose, new(TransactionErrorPhase.ConnectionDispose,
                    "transaction.connection_disposal_failed", "The connection lease disposal failed."));
            }
        }
        return new(attempted, confirmed, errors.AsReadOnly());

        void Failed(TransactionNativePhase phase, TransactionError error, bool expectedCancellation = false)
        {
            errors.Add(error);
            TransactionObservation.Attempt(() => observation.NativeFailure(phase, expectedCancellation));
            if (failedBegin)
            {
                TransactionObservation.Attempt(() => observation.BeginCleanupFailure(error));
            }
        }
    }
}
