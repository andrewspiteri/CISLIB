using System.Data.Common;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    // The caller already owns management_capacity then configuration_catalog and passes its remaining row allowance.
    // Every reference installer and catalog pin change takes the same catalog lock before touching these rows.
    private async Task<ConfigurationCleanup> CleanupConfigurationAsync(DbConnection connection, DbTransaction transaction,
        int remainingRows, CancellationToken token)
    {
        if (remainingRows == 0)
        {
            return new(0, 0, 0);
        }
        int keep;
        await using (DbCommand policy = ManagementSql.Command(connection, transaction,
            $"SELECT retained_releases FROM {Table("configuration_catalog")} WHERE singleton=true AND manifest_digest IS NOT NULL"))
        {
            object? selected = await policy.ExecuteScalarAsync(token);
            if (selected is not int retained)
            {
                return new(0, 0, 0);
            }
            keep = retained;
        }
        int releases;
        long releasedBytes;
        await using (DbCommand prune = ManagementSql.Command(connection, transaction, $"""
            WITH eligible AS MATERIALIZED (
                SELECT r.release_id,r.binding_id,r.created_at FROM {Table("configuration_release")} r
                WHERE NOT r.pinned
                    AND NOT EXISTS(SELECT 1 FROM {Table("configuration_assignment")} a WHERE a.default_release_id=r.release_id)
                    AND NOT EXISTS(SELECT 1 FROM {Table("configuration_override")} o WHERE o.release_id=r.release_id)
            ), ranked AS (
                SELECT release_id,row_number() OVER(PARTITION BY binding_id ORDER BY created_at DESC,release_id DESC) AS position
                FROM eligible
            ), candidates AS (
                SELECT r.release_id FROM {Table("configuration_release")} r JOIN ranked h ON h.release_id=r.release_id
                WHERE h.position>@keep ORDER BY r.created_at,r.release_id LIMIT @maximum_rows FOR UPDATE OF r SKIP LOCKED
            ), removed AS (
                DELETE FROM {Table("configuration_release")} WHERE release_id IN(SELECT release_id FROM candidates) RETURNING logical_bytes
            ) SELECT count(*)::integer,COALESCE(sum(logical_bytes),0)::bigint FROM removed
            """, ("keep", keep), ("maximum_rows", remainingRows)))
        await using (DbDataReader reader = await prune.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
            {
                throw new ManagementSchemaException("The bounded configuration release cleanup did not return its facts.");
            }
            releases = reader.GetInt32(0);
            releasedBytes = reader.GetInt64(1);
        }
        remainingRows -= releases;
        if (remainingRows == 0)
        {
            return new(releases, 0, releasedBytes);
        }
        int publications;
        await using (DbCommand prune = ManagementSql.Command(connection, transaction, $"""
            WITH eligible AS MATERIALIZED (
                SELECT p.publication_id,p.source_id,p.created_at FROM {Table("configuration_publication")} p
                WHERE NOT EXISTS(SELECT 1 FROM {Table("configuration_release")} r
                    WHERE r.common_publication_id=p.publication_id OR r.application_publication_id=p.publication_id)
            ), ranked AS (
                SELECT publication_id,row_number() OVER(PARTITION BY source_id ORDER BY created_at DESC,publication_id DESC) AS position
                FROM eligible
            ), candidates AS (
                SELECT p.publication_id FROM {Table("configuration_publication")} p JOIN ranked h ON h.publication_id=p.publication_id
                WHERE h.position>@keep ORDER BY p.created_at,p.publication_id LIMIT @maximum_rows FOR UPDATE OF p SKIP LOCKED
            ), removed AS (
                DELETE FROM {Table("configuration_publication")} WHERE publication_id IN(SELECT publication_id FROM candidates) RETURNING logical_bytes
            ) SELECT count(*)::integer,COALESCE(sum(logical_bytes),0)::bigint FROM removed
            """, ("keep", keep), ("maximum_rows", remainingRows)))
        await using (DbDataReader reader = await prune.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
            {
                throw new ManagementSchemaException("The bounded configuration publication cleanup did not return its facts.");
            }
            publications = reader.GetInt32(0);
            releasedBytes = checked(releasedBytes + reader.GetInt64(1));
        }
        return new(releases, publications, releasedBytes);
    }

    private sealed record ConfigurationCleanup(int Releases, int Publications, long ReleasedBytes);
}
