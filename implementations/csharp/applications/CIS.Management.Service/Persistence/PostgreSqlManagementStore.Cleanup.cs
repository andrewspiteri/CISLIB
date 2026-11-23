using System.Data.Common;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Deletes only old disposable telemetry in an idempotent shared-database batch.</summary>
    public Task<ManagementStoreResult<ManagementCleanupResult>> CleanupAsync(int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (maximumRows < 1 || maximumRows > Options.CleanupBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        }

        return RunAsync(async (context, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            _ = await LockCapacityAsync(connection, transaction, sqlToken).ConfigureAwait(false);
            int count;
            long bytes;
            await using (var cleanup = ManagementSql.Command(connection, transaction, $"""
                WITH candidates AS (
                    SELECT registration_id FROM {Table("management_registration")}
                    WHERE GREATEST(registered_at,observation_received_at,inventory_received_at,closed_at)
                        < clock_timestamp()-(@retention_seconds*interval '1 second')
                    ORDER BY registration_id LIMIT @maximum_rows FOR UPDATE SKIP LOCKED
                ), removed AS (
                    DELETE FROM {Table("management_registration")} WHERE registration_id IN (SELECT registration_id FROM candidates)
                    RETURNING inventory_bytes
                ) SELECT count(*)::integer,COALESCE(sum(inventory_bytes),0)::bigint FROM removed
                """, ("retention_seconds", Options.RegistrationRetention.TotalSeconds), ("maximum_rows", maximumRows)))
            {
                await using DbDataReader reader = await cleanup.ExecuteReaderAsync(sqlToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(sqlToken).ConfigureAwait(false))
                {
                    throw new ManagementSchemaException("The bounded management cleanup did not return its facts.");
                }

                count = reader.GetInt32(0);
                bytes = reader.GetInt64(1);
            }

            await using (var release = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("management_capacity")} SET registration_count=registration_count-@count,inventory_bytes=inventory_bytes-@bytes
                WHERE singleton=true
                """, ("count", count), ("bytes", bytes)))
            {
                await release.ExecuteNonQueryAsync(sqlToken).ConfigureAwait(false);
            }

            return Candidate<ManagementCleanupResult>.Written(new(count, bytes));
        }, token).ConfigureAwait(false), cancellationToken);
    }
}
