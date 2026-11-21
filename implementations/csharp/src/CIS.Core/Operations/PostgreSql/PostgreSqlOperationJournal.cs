using System.Data;
using System.Data.Common;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Operations.PostgreSql;

/// <summary>Production PostgreSQL v1 journal using only the owner's public BCL ADO.NET capability.</summary>
/// <remarks>The caller installs the versioned schema explicitly and owns its data source and transaction completion.
/// Commands are sequential and awaited. A claim conflict is followed by a new ReadCommitted statement snapshot.</remarks>
public sealed class PostgreSqlOperationJournal : IOperationJournal
{
    private readonly PostgreSqlOperationSettings settings;

    /// <summary>Copies schema and command limits without opening a database or performing migrations.</summary>
    public PostgreSqlOperationJournal(PostgreSqlOperationOptions? options = null)
    {
        settings = new(options);
    }

    internal PostgreSqlOperationSettings Settings => settings;

    /// <inheritdoc />
    public async Task<OperationClaim> TryClaimAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, CancellationToken cancellationToken = default)
    {
        Validate(context, identity);
        ArgumentNullException.ThrowIfNull(fingerprint);
        bool failedClaimStatement = false;
        try
        {
            return await context.UseAdoNetAsync(async (connection, transaction, token) =>
            {
                await CheckVersionAsync(connection, transaction, token).ConfigureAwait(false);
                await using DbCommand command = Command(connection, transaction, $"""
                    INSERT INTO {settings.Journal}
                        (application, environment, scope, operation_name, idempotency_key,
                         fingerprint_contract, fingerprint_version, fingerprint)
                    VALUES (@application, @environment, @scope, @operation, @key, @fingerprint_contract, @fingerprint_version, @fingerprint)
                    ON CONFLICT (application, environment, scope, operation_name, idempotency_key) DO NOTHING
                    RETURNING true
                    """);
                AddIdentity(command, identity);
                AddFingerprint(command, fingerprint);
                object? inserted;
                try
                {
                    inserted = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                }
                catch (DbException error) when (error.SqlState is "55P03" or "40001" or "40P01")
                {
                    failedClaimStatement = true;
                    throw;
                }
                if (inserted is true)
                {
                    return new OperationClaim(OperationClaimDisposition.Acquired);
                }

                // A new statement is essential: the conflicting committed winner need not be visible
                // in the INSERT statement's snapshot under PostgreSQL ReadCommitted.
                OperationJournalRecord? winner = await ReadRowAsync(connection, transaction, identity, token).ConfigureAwait(false);
                return winner is null ? new(OperationClaimDisposition.MissingAfterConflict)
                    : new(OperationClaimDisposition.Existing, winner);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException error) when (failedClaimStatement && context.IsRollbackOnly
            && !cancellationToken.IsCancellationRequested && (error.SqlState is "55P03" or "40001" or "40P01"))
        {
            // This catch is deliberately outside UseAdoNetAsync, after RM-02 has poisoned the owner.
            return new(error.SqlState == "55P03" ? OperationClaimDisposition.Contended : OperationClaimDisposition.RetryRequired);
        }
    }

    /// <inheritdoc />
    public Task<OperationJournalRecord?> ReadAsync(ILocalTransactionContext context, OperationIdentity identity,
        CancellationToken cancellationToken = default)
    {
        Validate(context, identity);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await CheckVersionAsync(connection, transaction, token).ConfigureAwait(false);
            return await ReadRowAsync(connection, transaction, identity, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationJournalTimes> WriteCompletedAsync(ILocalTransactionContext context, OperationIdentity identity,
        OperationFingerprint fingerprint, OperationStoredResult result, OperationRetentionPolicy retention,
        CancellationToken cancellationToken = default)
    {
        Validate(context, identity);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(retention);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await CheckVersionAsync(connection, transaction, token).ConfigureAwait(false);
            await using DbCommand command = Command(connection, transaction, $"""
                WITH recording AS (SELECT clock_timestamp() AS at)
                UPDATE {settings.Journal} AS journal
                SET finalized = true, result_contract = @result_contract, result_version = @result_version,
                    business_decision = @business_decision, result_payload = @payload,
                    error_code = @error_code, error_message = @error_message, recorded_at = recording.at,
                    result_expires_at = CASE WHEN @result_ticks IS NULL THEN NULL
                        ELSE recording.at + ceil(@result_ticks::numeric / 10) * INTERVAL '1 microsecond' END,
                    protection_expires_at = CASE WHEN @protection_ticks IS NULL THEN NULL
                        ELSE recording.at + ceil(@protection_ticks::numeric / 10) * INTERVAL '1 microsecond' END
                FROM recording
                WHERE application = @application AND environment = @environment AND scope = @scope
                    AND operation_name = @operation AND idempotency_key = @key
                    AND fingerprint_contract = @fingerprint_contract AND fingerprint_version = @fingerprint_version
                    AND fingerprint = @fingerprint AND NOT finalized AND claim_xid = pg_current_xact_id()
                RETURNING clock_timestamp(), recorded_at, result_expires_at, protection_expires_at
                """);
            AddIdentity(command, identity);
            AddFingerprint(command, fingerprint);
            Parameter(command, "result_contract", DbType.String, result.Contract.Identifier);
            Parameter(command, "result_version", DbType.Int32, result.Contract.Version);
            Parameter(command, "business_decision", DbType.Int16, (short)result.Decision);
            Parameter(command, "payload", DbType.Binary, result.Value?.ToArray());
            Parameter(command, "error_code", DbType.String, result.Error?.Code);
            Parameter(command, "error_message", DbType.String, result.Error?.Message);
            Parameter(command, "result_ticks", DbType.Int64, retention.ResultRetention?.Ticks);
            Parameter(command, "protection_ticks", DbType.Int64, retention.DuplicateProtectionRetention?.Ticks);
            await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The original transaction must finalize its own incomplete operation claim exactly once.");
            }
            OperationJournalTimes times = Times(reader, 0);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Operation finalization affected an invalid number of rows.");
            }
            return times;
        }, cancellationToken);
    }

    internal async Task CheckVersionAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction,
            $"SELECT version FROM {settings.VersionTable} WHERE singleton");
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is not int version || version != PostgreSqlOperationSchema.Version)
        {
            throw new InvalidOperationException("Install the supported CIS.Operations PostgreSQL schema version before accessing its journal.");
        }
    }

    private async Task<OperationJournalRecord?> ReadRowAsync(DbConnection connection, DbTransaction transaction,
        OperationIdentity identity, CancellationToken token)
    {
        await using DbCommand command = Command(connection, transaction, $"""
            SELECT clock_timestamp(), recorded_at, result_expires_at, protection_expires_at,
                application, environment, scope, operation_name, idempotency_key,
                fingerprint_contract, fingerprint_version, fingerprint, finalized,
                result_contract, result_version, business_decision, result_payload, error_code, error_message
            FROM {settings.Journal}
            WHERE application = @application AND environment = @environment AND scope = @scope
                AND operation_name = @operation AND idempotency_key = @key
                AND claim_xid IS DISTINCT FROM pg_current_xact_id_if_assigned()
            """);
        AddIdentity(command, identity);
        await using DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return null;
        }
        return ReadRecord(reader, identity);
    }

    private static OperationJournalRecord ReadRecord(DbDataReader reader, OperationIdentity requested)
    {
        DateTimeOffset observed = Timestamp(reader, 0);
        OperationJournalTimes times;
        try
        {
            times = Times(reader, 0);
        }
        catch (ArgumentException)
        {
            return new(requested, null, null, new(observed), reader.GetBoolean(12), isCorrupt: true);
        }

        OperationFingerprint? fingerprint = null;
        bool finalized = reader.GetBoolean(12);
        try
        {
            var actual = new OperationIdentity(reader.GetString(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7), reader.GetString(8));
            if (actual != requested)
            {
                return new(actual, null, null, times, finalized, isCorrupt: true);
            }
            fingerprint = new(new(reader.GetString(9), reader.GetInt32(10)), reader.GetFieldValue<byte[]>(11));
            if (!finalized)
            {
                return new(actual, fingerprint, null, times, isFinalized: false);
            }
            if (times.RecordedAtUtc is null || reader.IsDBNull(13) || reader.IsDBNull(14) || reader.IsDBNull(15))
            {
                return new(actual, fingerprint, null, times, finalized, isCorrupt: true);
            }
            var contract = new OperationContract(reader.GetString(13), reader.GetInt32(14));
            var decision = (OperationBusinessDecision)reader.GetInt16(15);
            if (reader.IsDBNull(16) && reader.IsDBNull(17) && reader.IsDBNull(18))
            {
                return new(actual, fingerprint, null, times, finalized, isCorrupt: !times.IsResultExpired);
            }
            ReadOnlyMemory<byte>? payload = reader.IsDBNull(16) ? (ReadOnlyMemory<byte>?)null
                : new ReadOnlyMemory<byte>(reader.GetFieldValue<byte[]>(16));
            OperationError? error = reader.IsDBNull(17) || reader.IsDBNull(18) ? null
                : new(reader.GetString(17), reader.GetString(18));
            var result = new OperationStoredResult(contract, decision, payload, error);
            return new(actual, fingerprint, result, times, finalized);
        }
        catch (ArgumentException)
        {
            // Invalid retained contract data blocks execution. It is never interpreted as an absent key.
            return new(requested, fingerprint, null, times, finalized, isCorrupt: true);
        }
    }

    internal DbCommand Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        return command;
    }

    private static void Validate(ILocalTransactionContext context, OperationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(identity);
        if (context.Isolation != TransactionIsolation.ReadCommitted)
        {
            throw new NotSupportedException("The PostgreSQL operation journal v1 supports ReadCommitted only.");
        }
    }

    private static void AddIdentity(DbCommand command, OperationIdentity identity)
    {
        Parameter(command, "application", DbType.String, identity.Application);
        Parameter(command, "environment", DbType.String, identity.Environment);
        Parameter(command, "scope", DbType.String, identity.Scope);
        Parameter(command, "operation", DbType.String, identity.OperationName);
        Parameter(command, "key", DbType.String, identity.Key);
    }

    private static void AddFingerprint(DbCommand command, OperationFingerprint fingerprint)
    {
        Parameter(command, "fingerprint_contract", DbType.String, fingerprint.Contract.Identifier);
        Parameter(command, "fingerprint_version", DbType.Int32, fingerprint.Contract.Version);
        Parameter(command, "fingerprint", DbType.Binary, fingerprint.Digest.ToArray());
    }

    internal static void Parameter(DbCommand command, string name, DbType type, object? value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static OperationJournalTimes Times(DbDataReader reader, int offset) => new(
        Timestamp(reader, offset), OptionalTimestamp(reader, offset + 1),
        OptionalTimestamp(reader, offset + 2), OptionalTimestamp(reader, offset + 3));

    private static DateTimeOffset Timestamp(DbDataReader reader, int ordinal) => reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static DateTimeOffset? OptionalTimestamp(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Timestamp(reader, ordinal);
}
