using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Coordination.PostgreSql;

public sealed partial class PostgreSqlCoordinationProvider
{
    /// <inheritdoc />
    public async Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grant);
        CoordinationRejectionReason reason;
        if (grant.AuthorityId != AuthorityId)
        {
            reason = CoordinationRejectionReason.WrongAuthority;
        }
        else if (context.DatabaseName != settings.DatabaseName)
        {
            reason = CoordinationRejectionReason.WrongDatabase;
        }
        else if (context.Isolation != TransactionIsolation.ReadCommitted || context.IsRollbackOnly
            || context is not IAdoNetTransactionContext)
        {
            reason = CoordinationRejectionReason.InvalidTransaction;
        }
        else
        {
            try
            {
                reason = await context.UseAdoNetAsync(async (connection, transaction, token) =>
                {
                    await CheckAuthorityAsync(connection, transaction, token).ConfigureAwait(false);
                    AuthorityRow? row = await ReadRowAsync(connection, transaction, grant.Key, false, token).ConfigureAwait(false);
                    // A separate statement AFTER SELECT FOR UPDATE has returned is required: now() is
                    // transaction-start time and a predicate/projection can precede a row-lock wait.
                    DateTimeOffset now = await ClockAsync(connection, transaction, token).ConfigureAwait(false);
                    return row?.Rejection(grant, now) ?? CoordinationRejectionReason.Missing;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (IsInfrastructure(error))
            {
                reason = CoordinationRejectionReason.AuthorityUnavailable;
            }
        }
        if (reason != CoordinationRejectionReason.None)
        {
            context.MarkRollbackOnly("coordination.guard_rejected");
        }
        return new(reason);
    }
}
