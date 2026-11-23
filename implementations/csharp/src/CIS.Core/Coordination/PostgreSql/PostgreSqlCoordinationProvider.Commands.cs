using System.Data;
using System.Data.Common;

namespace CIS.Coordination.PostgreSql;

public sealed partial class PostgreSqlCoordinationProvider
{
    private const string KeyPredicate = "environment = @environment AND execution_scope = @scope AND duty_id = @duty AND resource_id = @resource";
    private const string ExactPredicate = KeyPredicate + " AND owner_application = @owner AND instance_id = @instance AND acquisition_id = @acquisition AND generation = @generation";

    private async Task CheckAuthorityAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            SELECT metadata.version, metadata.authority_id,
                current_setting('server_encoding') = 'UTF8', current_setting('block_size')::integer >= 8192,
                current_setting('server_version_num')::integer >= 180000,
                EXISTS
                (
                    SELECT 1 FROM pg_class AS allocator JOIN pg_sequence AS sequence ON sequence.seqrelid = allocator.oid
                    WHERE allocator.oid = to_regclass(@sequence) AND allocator.relkind = 'S' AND allocator.relpersistence = 'p'
                        AND sequence.seqtypid = 'bigint'::regtype AND sequence.seqstart = 1 AND sequence.seqincrement = 1
                        AND sequence.seqmin = 1 AND sequence.seqmax = 9223372036854775807
                        AND sequence.seqcache = 1 AND NOT sequence.seqcycle
                        AND NOT EXISTS (SELECT 1 FROM pg_depend AS dependency
                            WHERE dependency.classid = 'pg_class'::regclass AND dependency.objid = allocator.oid
                                AND dependency.deptype IN ('a', 'i'))
                ),
                EXISTS (SELECT 1 FROM pg_class AS grants WHERE grants.oid = to_regclass(@grants)
                    AND grants.relkind = 'r' AND grants.relpersistence = 'p'),
                NOT pg_is_in_recovery()
            FROM {settings.Metadata} AS metadata WHERE metadata.singleton
            """);
        Parameter(command, "sequence", DbType.String, settings.Sequence);
        Parameter(command, "grants", DbType.String, settings.Grants);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)
            || reader.GetInt32(0) != PostgreSqlCoordinationSchema.Version || reader.GetGuid(1) != AuthorityId
            || Enumerable.Range(2, 6).Any(index => !reader.GetBoolean(index)))
        {
            throw new InvalidOperationException("The selected coordination authority, writer, schema or durable generation allocator is unsupported. Never recreate its allocator to repair acknowledged history.");
        }
    }

    private async Task LockOrCreateAsync(DbConnection connection, DbTransaction transaction, CoordinationKey key, CancellationToken token)
    {
        // The unique upsert also serializes absent-row creation and recreation after exact pruning.
        // No expiry predicate or generation allocation is evaluated before this lock is held.
        await using DbCommand command = Command(connection, transaction, $"""
            INSERT INTO {settings.Grants} AS current (environment, execution_scope, duty_id, resource_id)
            VALUES (@environment, @scope, @duty, @resource)
            ON CONFLICT (environment, execution_scope, duty_id, resource_id)
            DO UPDATE SET environment = current.environment
            """);
        AddKey(command, key);
        if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("Coordination key serialization did not lock exactly one row.");
        }
    }

    private async Task<AuthorityRow?> ReadRowAsync(DbConnection connection, DbTransaction transaction,
        CoordinationKey key, bool sharedLock, CancellationToken token)
    {
        string locking = sharedLock ? "FOR SHARE" : "FOR UPDATE";
        await using DbCommand command = Command(connection, transaction, $"""
            SELECT owner_application, instance_id, acquisition_id, generation, expires_at, released
            FROM {settings.Grants} WHERE {KeyPredicate} {locking}
            """);
        AddKey(command, key);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }
        var row = new AuthorityRow(reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), reader.GetBoolean(5));
        if (row.OwnerApplication is null
            ? !row.Released || row.InstanceId is not null || row.AcquisitionId is not null || row.Generation is not null || row.ExpiresAtUtc is not null
            : row.InstanceId is null || row.AcquisitionId is null || row.Generation is null || row.ExpiresAtUtc is null)
        {
            throw new InvalidOperationException("The coordination authority contains an incomplete ownership row.");
        }
        return row;
    }

    private async Task<DateTimeOffset> ClockAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, "SELECT clock_timestamp()");
        object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        return value switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("The coordination authority returned no current timestamp.")
        };
    }

    private async Task<CoordinationGrant> WriteAcquisitionAsync(DbConnection connection, DbTransaction transaction,
        CoordinationAcquireRequest request, DateTimeOffset now, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            UPDATE {settings.Grants}
            SET owner_application = @owner, instance_id = @instance, acquisition_id = @acquisition,
                generation = nextval(@sequence::regclass), expires_at = @now + ceil(@ticks::numeric / 10) * INTERVAL '1 microsecond',
                released = false
            WHERE {KeyPredicate} AND (released OR expires_at <= @now)
            RETURNING generation, expires_at
            """);
        AddAcquisition(command, request.Acquisition);
        Parameter(command, "sequence", DbType.String, settings.Sequence);
        Parameter(command, "now", DbType.DateTimeOffset, now);
        Parameter(command, "ticks", DbType.Int64, request.LeaseDuration.Ticks);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A serialized coordination acquisition did not update its eligible row.");
        }
        return new(AuthorityId, request.Acquisition, reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task<CoordinationGrant> WriteRenewalAsync(DbConnection connection, DbTransaction transaction,
        CoordinationGrant grant, TimeSpan duration, DateTimeOffset now, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            UPDATE {settings.Grants}
            SET expires_at = @now + ceil(@ticks::numeric / 10) * INTERVAL '1 microsecond'
            WHERE {ExactPredicate} AND NOT released AND expires_at > @now
            RETURNING expires_at
            """);
        AddGrant(command, grant);
        Parameter(command, "now", DbType.DateTimeOffset, now);
        Parameter(command, "ticks", DbType.Int64, duration.Ticks);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw new InvalidOperationException("A serialized renewal did not update its exact live grant.");
        }
        return new(AuthorityId, grant.Acquisition, grant.Generation, reader.GetFieldValue<DateTimeOffset>(0));
    }

    private DbCommand Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        return command;
    }

    private static void AddKey(DbCommand command, CoordinationKey key)
    {
        Parameter(command, "environment", DbType.String, key.Environment);
        Parameter(command, "scope", DbType.String, key.ExecutionScope);
        Parameter(command, "duty", DbType.String, key.DutyId);
        Parameter(command, "resource", DbType.String, key.ResourceId);
    }

    private static void AddAcquisition(DbCommand command, CoordinationAcquisition acquisition)
    {
        AddKey(command, acquisition.Key);
        Parameter(command, "owner", DbType.String, acquisition.OwnerApplication);
        Parameter(command, "instance", DbType.String, acquisition.InstanceId);
        Parameter(command, "acquisition", DbType.Guid, acquisition.AcquisitionId);
    }

    private static void AddGrant(DbCommand command, CoordinationGrant grant)
    {
        AddAcquisition(command, grant.Acquisition);
        Parameter(command, "generation", DbType.Int64, grant.Generation);
    }

    private static void Parameter(DbCommand command, string name, DbType type, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record AuthorityRow(string? OwnerApplication, string? InstanceId, Guid? AcquisitionId,
        long? Generation, DateTimeOffset? ExpiresAtUtc, bool Released)
    {
        internal bool Matches(CoordinationAcquisition acquisition) => OwnerApplication == acquisition.OwnerApplication
            && InstanceId == acquisition.InstanceId && AcquisitionId == acquisition.AcquisitionId;

        internal CoordinationRejectionReason Rejection(CoordinationGrant grant, DateTimeOffset now)
        {
            if (!Matches(grant.Acquisition) || Generation != grant.Generation)
            {
                return CoordinationRejectionReason.Superseded;
            }
            if (Released)
            {
                return CoordinationRejectionReason.Released;
            }
            return ExpiresAtUtc <= now ? CoordinationRejectionReason.Expired : CoordinationRejectionReason.None;
        }
    }
}
