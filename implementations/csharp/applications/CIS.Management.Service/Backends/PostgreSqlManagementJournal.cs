using CIS.Operations;
using CIS.Operations.PostgreSql;
using CIS.Transactions;

namespace CIS.Management.Service.Backends;

// Provider qualification participates in the coordinator's actual original context,
// including its fresh result-read context. The existing journal owns all arbitration/data semantics.
internal sealed class PostgreSqlManagementJournal(PostgreSqlOperationJournal journal, PostgreSqlManagementAuthority authority) : IOperationJournal
{
    public async Task<OperationClaim> TryClaimAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        _ = await authority.ReadAsync(context, true, cancellationToken).ConfigureAwait(false);
        return await journal.TryClaimAsync(context, identity, fingerprint, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationJournalRecord?> ReadAsync(ILocalTransactionContext context, OperationIdentity identity,
        CancellationToken cancellationToken = default)
    {
        _ = await authority.ReadAsync(context, false, cancellationToken).ConfigureAwait(false);
        return await journal.ReadAsync(context, identity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationJournalTimes> WriteCompletedAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, OperationStoredResult result, OperationRetentionPolicy retention,
        CancellationToken cancellationToken = default)
    {
        _ = await authority.ReadAsync(context, true, cancellationToken).ConfigureAwait(false);
        return await journal.WriteCompletedAsync(context, identity, fingerprint, result, retention, cancellationToken).ConfigureAwait(false);
    }
}
