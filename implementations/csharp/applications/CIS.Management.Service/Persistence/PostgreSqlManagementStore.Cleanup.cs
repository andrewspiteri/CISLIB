using System.Data.Common;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Deletes old telemetry and eligible configuration history in one bounded original transaction, never operation results.</summary>
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
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction,
                $"SELECT catalog_id FROM {Table("configuration_catalog")} WHERE singleton=true FOR UPDATE"))
            {
                await catalog.ExecuteScalarAsync(sqlToken).ConfigureAwait(false);
            }
            int count;
            long bytes;
            long configurationBytes;
            await using (var cleanup = ManagementSql.Command(connection, transaction, $"""
                WITH candidates AS (
                    SELECT r.registration_id,COALESCE(s.logical_bytes,0)+COALESCE(a.logical_bytes,0) AS configuration_bytes
                    FROM {Table("management_registration")} r
                    LEFT JOIN {Table("configuration_session")} s ON s.registration_id=r.registration_id
                    LEFT JOIN {Table("configuration_adoption")} a ON a.registration_id=r.registration_id
                    WHERE GREATEST(r.registered_at,r.observation_received_at,r.inventory_received_at,r.closed_at)
                        < clock_timestamp()-(@retention_seconds*interval '1 second')
                    ORDER BY r.registration_id LIMIT @maximum_rows FOR UPDATE OF r SKIP LOCKED
                ), removed AS (
                    DELETE FROM {Table("management_registration")} WHERE registration_id IN (SELECT registration_id FROM candidates)
                    RETURNING registration_id,inventory_bytes
                ) SELECT count(*)::integer,COALESCE(sum(removed.inventory_bytes),0)::bigint,
                    COALESCE(sum(candidates.configuration_bytes),0)::bigint FROM removed JOIN candidates USING(registration_id)
                """, ("retention_seconds", Options.RegistrationRetention.TotalSeconds), ("maximum_rows", maximumRows)))
            {
                await using DbDataReader reader = await cleanup.ExecuteReaderAsync(sqlToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(sqlToken).ConfigureAwait(false))
                {
                    throw new ManagementSchemaException("The bounded management cleanup did not return its facts.");
                }

                count = reader.GetInt32(0);
                bytes = reader.GetInt64(1);
                configurationBytes = reader.GetInt64(2);
            }

            ConfigurationCleanup configuration = await CleanupConfigurationAsync(connection, transaction, maximumRows - count, sqlToken).ConfigureAwait(false);
            configurationBytes = checked(configurationBytes + configuration.ReleasedBytes);
            await using (var release = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("management_capacity")} SET registration_count=registration_count-@count,inventory_bytes=inventory_bytes-@bytes
                WHERE singleton=true;
                UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes-@configuration_bytes WHERE singleton=true
                """, ("count", count), ("bytes", bytes), ("configuration_bytes", configurationBytes)))
            {
                await release.ExecuteNonQueryAsync(sqlToken).ConfigureAwait(false);
            }

            return Candidate<ManagementCleanupResult>.Written(new(count, bytes)
            {
                RemovedConfigurationReleases = configuration.Releases,
                RemovedConfigurationPublications = configuration.Publications,
                ReleasedConfigurationBytes = configurationBytes
            });
        }, token).ConfigureAwait(false), cancellationToken);
    }
}
