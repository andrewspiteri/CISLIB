using System.Data.Common;
using System.Text;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.RuleEngine;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    private sealed record PendingRuleCounterProjection(Guid Ruleset, Guid Execution, byte[] Completion, byte[] Definition);

    // Projection owns a short subsequent transaction. A counter-specific failure
    // leaves execution history pending and never changes global control readiness.
    internal async Task<bool> ProjectRuleEngineCountersAsync(int maximum, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 16);
        try
        {
            await using ILocalTransaction owner = await factory.BeginAsync("management", new()
            {
                Isolation = TransactionIsolation.ReadCommitted
            }, token).ConfigureAwait(false);
            _ = await (authority ?? throw new ManagementBackendException("management.authority.unavailable"))
                .ReadAsync(owner.Context, requireReady: true, token).ConfigureAwait(false);
            bool attempted = await owner.Context.UseAdoNetAsync(async (connection, transaction, cancellation) =>
            {
                List<PendingRuleCounterProjection> pending = [];
                await using (DbCommand query = ManagementSql.Command(connection, transaction, $"""
                    SELECT f.ruleset_key,f.execution_id,f.body,v.definition
                    FROM {Table("rule_engine_execution_event")} f
                    JOIN {Table("rule_engine_execution")} e ON e.ruleset_key=f.ruleset_key AND e.execution_id=f.execution_id
                    JOIN {Table("rule_engine_version")} v ON v.ruleset_key=e.ruleset_key AND v.version_id=e.version_id
                    WHERE f.phase=1 AND NOT EXISTS(SELECT 1 FROM {Table("rule_engine_counter_execution")} done
                        WHERE done.ruleset_key=f.ruleset_key AND done.execution_id=f.execution_id)
                    ORDER BY f.received_at,f.execution_id LIMIT @maximum
                    """, ("maximum", maximum)))
                await using (DbDataReader reader = await query.ExecuteReaderAsync(cancellation))
                {
                    while (await reader.ReadAsync(cancellation))
                    {
                        pending.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<byte[]>(2), reader.GetFieldValue<byte[]>(3)));
                    }
                }
                var locked = new Dictionary<Guid, bool>();
                long charge = 0;
                foreach (PendingRuleCounterProjection projection in pending)
                {
                    if (!locked.TryGetValue(projection.Ruleset, out bool acquired))
                    {
                        acquired = await LockRuleCountersAsync(connection, transaction, projection.Ruleset, cancellation, tryOnly: true);
                        locked.Add(projection.Ruleset, acquired);
                    }
                    if (acquired)
                    {
                        charge = checked(charge + await ApplyRuleCounterProjectionAsync(connection, transaction, projection, cancellation));
                    }
                }
                if (charge == 0)
                {
                    return false;
                }
                if (!await ChargeRuleCountersAsync(connection, transaction, charge, cancellation))
                {
                    throw new ManagementBackendException("management.rule-engine.counters.capacity_exceeded");
                }
                return true;
            }, token).ConfigureAwait(false);
            TransactionCompletion completion = attempted
                ? await owner.CommitAsync(token).ConfigureAwait(false) : await owner.RollbackAsync().ConfigureAwait(false);
            return attempted ? completion.Outcome == TransactionOutcome.Committed && completion.CleanupErrors.Count == 0
                : completion.RollbackConfirmed && completion.CleanupErrors.Count == 0;
        }
        catch (Exception error) when (error is DbException or ManagementBackendException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<long> ApplyRuleCounterProjectionAsync(DbConnection connection, DbTransaction transaction,
        PendingRuleCounterProjection projection, CancellationToken token)
    {
        await using DbCommand prior = ManagementSql.Command(connection, transaction, $"""
            SELECT EXISTS(SELECT 1 FROM {Table("rule_engine_counter_execution")} WHERE ruleset_key=@key AND execution_id=@execution)
            """, ("key", projection.Ruleset), ("execution", projection.Execution));
        if (await prior.ExecuteScalarAsync(token) is true)
        {
            return 0;
        }
        var codec = new RuleEngineAuditObservationCodec();
        if (!codec.TryDecode(codec.Contract, projection.Completion, out RuleEngineAuditObservation? observation)
            || observation is null || !observation.Status.HasValue)
        {
            throw new ManagementBackendException("management.rule-engine.counters.observation_invalid");
        }
        RuleDefinition definition = RuleDefinitionCodec.Decode(projection.Definition);
        string[] hits = observation.Details.GetProperty("rows").EnumerateArray()
            .Where(row => row.GetProperty("outcome").GetString() == "Matched")
            .Select(row => definition.Rows[row.GetProperty("rowIndex").GetInt32()].RuleId).ToArray();
        await using DbCommand epochQuery = ManagementSql.Command(connection, transaction, $"""
            SELECT reset_epoch FROM {Table("rule_engine_counter_scope")} WHERE ruleset_key=@key
            """, ("key", projection.Ruleset));
        long epoch = (long?)await epochQuery.ExecuteScalarAsync(token) ?? 0;
        long charge = 64;
        if (hits.Length != 0)
        {
            string encoded = JsonSerializer.Serialize(hits);
            await using DbCommand missing = ManagementSql.Command(connection, transaction, $"""
                SELECT id FROM json_array_elements_text(CAST(@ids AS json)) AS listed(id)
                WHERE NOT EXISTS(SELECT 1 FROM {Table("rule_engine_counter")} c WHERE c.ruleset_key=@key AND c.rule_id=id)
                """, ("ids", encoded), ("key", projection.Ruleset));
            await using (DbDataReader reader = await missing.ExecuteReaderAsync(token))
            {
                while (await reader.ReadAsync(token))
                {
                    charge = checked(charge + RuleCounterRowBytes(reader.GetString(0)));
                }
            }
            await using DbCommand increment = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("rule_engine_counter")} AS target(ruleset_key,rule_id,scope_epoch,hit_count)
                SELECT @key,id,@epoch,1 FROM json_array_elements_text(CAST(@ids AS json)) AS listed(id)
                ON CONFLICT (ruleset_key,rule_id) DO UPDATE SET
                    hit_count=CASE WHEN target.scope_epoch=EXCLUDED.scope_epoch THEN target.hit_count+1 ELSE 1 END,
                    reset_at=CASE WHEN target.scope_epoch=EXCLUDED.scope_epoch THEN target.reset_at END,
                    scope_epoch=EXCLUDED.scope_epoch
                """, ("key", projection.Ruleset), ("ids", encoded), ("epoch", epoch));
            await increment.ExecuteNonQueryAsync(token);
        }
        await using DbCommand accepted = ManagementSql.Command(connection, transaction, $"""
            INSERT INTO {Table("rule_engine_counter_execution")}(ruleset_key,execution_id,scope_epoch)
            VALUES(@key,@execution,@epoch)
            """, ("key", projection.Ruleset), ("execution", projection.Execution), ("epoch", epoch));
        await accepted.ExecuteNonQueryAsync(token);
        return charge;
    }

    private static async Task<bool> LockRuleCountersAsync(DbConnection connection, DbTransaction transaction, Guid key,
        CancellationToken token, bool tryOnly = false, bool readOnly = false)
    {
        // The advisory key serializes only operations for this retained ruleset.
        // Trying the key lets a batch skip a busy scope while it holds other keys.
        string operation = tryOnly ? "pg_try_advisory_xact_lock" : readOnly ? "pg_advisory_xact_lock_shared" : "pg_advisory_xact_lock";
        await using DbCommand command = ManagementSql.Command(connection, transaction,
            $"SELECT {operation}(hashtextextended(CAST(@key AS text),451165229))", ("key", key));
        if (tryOnly)
        {
            return await command.ExecuteScalarAsync(token) is true;
        }
        await command.ExecuteNonQueryAsync(token);
        return true;
    }

    private async Task<bool> ChargeRuleCountersAsync(DbConnection connection, DbTransaction transaction, long charge, CancellationToken token)
    {
        await using DbCommand allocate = ManagementSql.Command(connection, transaction, $"""
            UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@charge
            WHERE singleton=true AND manifest_digest IS NOT NULL AND @charge<=quota_bytes-used_bytes
            """, ("charge", charge));
        return await allocate.ExecuteNonQueryAsync(token) == 1;
    }

    private static long RuleCounterRowBytes(string ruleId) => 96 + Encoding.UTF8.GetByteCount(ruleId);
}
