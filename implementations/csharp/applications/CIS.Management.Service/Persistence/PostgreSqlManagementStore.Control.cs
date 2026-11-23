using CIS.Management.Service.Backends;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Confirms current primary authority and the exact live registration without renewing observation presence.</summary>
    public Task<ManagementStoreResult<ManagementControlConfirmation>> ConfirmControlAsync(ManagementRegistrationOwner owner,
        string registrationId, string attemptId, int validForMs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid registration = ManagementSql.Uuid(registrationId, nameof(registrationId));
        _ = ManagementSql.Uuid(attemptId, nameof(attemptId));
        if (validForMs is < 1 or > 15000 || authority is null)
        {
            throw new ArgumentException("Control requires the complete bounded authoritative provider composition.");
        }
        return RunCoreAsync(async (context, state, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            ManagementAuthoritySnapshot confirmed = state
                ?? throw new ManagementBackendException("management.authority.unavailable");
            RegistrationRow? row = await ReadRegistrationAsync(connection, transaction, registration, "FOR SHARE OF r", sqlToken).ConfigureAwait(false);
            if (row is null || !Owned(row, owner))
            {
                return Candidate<ManagementControlConfirmation>.Refused("management.not_found");
            }
            if (row.ClosedAtUtc is not null)
            {
                return Candidate<ManagementControlConfirmation>.Refused("management.registration.closed");
            }
            return Candidate<ManagementControlConfirmation>.Existing(new(confirmed.AuthorityId, registrationId,
                confirmed.ControlRevision, true, validForMs, attemptId));
        }, token).ConfigureAwait(false), cancellationToken, requireReady: true, requireCleanRead: true);
    }
}
