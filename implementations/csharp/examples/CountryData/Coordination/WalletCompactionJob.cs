using CIS.Coordination;
using CIS.Jobs;
using CIS.Transactions;

namespace CIS.Examples.CountryData.Coordination;

/// <summary>Application persistence port for one finite partition batch.</summary>
/// <remarks>Read and advance the durable checkpoint with business writes in this same context.
/// Keep its identity stable across generations; do not send broker/HTTP effects from this transaction callback.</remarks>
public interface IWalletCompactionWork
{
    Task<bool> CompactNextBatchAsync(ILocalTransactionContext context, string partitionId,
        CancellationToken cancellationToken = default);
}

/// <summary>One finite guarded batch. The CIS continuous runner owns repetition, idle waiting and independent renewal.</summary>
public sealed class WalletCompactionJob(ILocalTransactionFactory transactions, ICoordinationGuard guard,
    IWalletCompactionWork work) : IContinuousJobHandler
{
    public async Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Definition.ExecutionScope != CountryMaintenanceJobs.WalletScope
            || context.Definition.JobId != CountryMaintenanceJobs.WalletJob || context.Settings.Mode != JobExecutionMode.Continuous)
        {
            throw new ArgumentException("Wallet compaction requires its exact registered continuous target.", nameof(context));
        }

        await using ILocalTransaction transaction = await transactions.BeginAsync(CountryMaintenanceJobs.Database, cancellationToken);
        CoordinationGuardResult admission = await guard.ValidateAsync(transaction.Context, context.Lease.Grant, cancellationToken);
        if (!admission.Admitted)
        {
            TransactionCompletion refused = await transaction.RollbackAsync();
            return new(ContinuousIterationOutcome.Failed, "wallet.ownership_guard_refused", refused);
        }

        bool more = await work.CompactNextBatchAsync(transaction.Context, context.ResourceId, cancellationToken);
        TransactionCompletion completion = await transaction.CommitAsync(cancellationToken);
        return completion.Outcome switch
        {
            TransactionOutcome.Committed => new(more ? ContinuousIterationOutcome.MoreWork : ContinuousIterationOutcome.Idle,
                completion: completion),
            TransactionOutcome.Unknown => new(ContinuousIterationOutcome.Indeterminate, "wallet.commit_unknown", completion),
            _ => new(ContinuousIterationOutcome.Failed, "wallet.not_committed", completion)
        };
    }
}
