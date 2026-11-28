using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Backends;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

internal sealed record RuleEngineCounterResetEffect(ManagementRuleCounterResetDecision? Decision, string? Refusal = null);

public sealed partial class PostgreSqlManagementStore
{
    internal async Task<RuleEngineCounterResetEffect> ResetRuleEngineCountersAsync(ILocalTransactionContext context,
        RuleEngineResource requested, ManagementRuleCounterResetCommand command, ManagementOperationReference operation,
        CancellationToken token)
    {
        ManagementSql.RequireContext(context);
        try
        {
            return await context.UseAdoNetAsync<RuleEngineCounterResetEffect>(async (connection, transaction, sql) =>
            {
                await LockRuleCountersAsync(connection, transaction, requested.Key, sql);
                RuleEngineResource? current = await LoadRuleEngineResourceAsync(connection, transaction, requested.Application,
                    requested.RulesetId, write: false, sql);
                if (current is null || current.Key != requested.Key)
                {
                    return new(null, "management.rule-engine.not_found");
                }
                await using DbCommand scope = ManagementSql.Command(connection, transaction, $"""
                    SELECT reset_epoch FROM {Table("rule_engine_counter_scope")} WHERE ruleset_key=@key
                    """, ("key", current.Key));
                long? previousEpoch = (long?)await scope.ExecuteScalarAsync(sql);
                long epoch = previousEpoch ?? 0;
                bool all = command.Selection == "all";
                if (all && epoch == long.MaxValue)
                {
                    return new(null, "management.rule-engine.counters.capacity_exceeded");
                }

                var eligible = new List<string>();
                byte[]? storedOutcomes = null;
                long applied;
                long refused = 0;
                long charge = 192 + Encoding.UTF8.GetByteCount(operation.Scope) + Encoding.UTF8.GetByteCount(operation.Key);
                if (all)
                {
                    await using DbCommand count = ManagementSql.Command(connection, transaction, $"""
                        SELECT count(*) FROM {Table("rule_engine_identity")}
                        WHERE ruleset_key=@key AND counter_visible_from_revision<=@basis
                        """, ("key", current.Key), ("basis", current.EditRevision));
                    applied = (long)(await count.ExecuteScalarAsync(sql))!;
                    if (!previousEpoch.HasValue)
                    {
                        charge += 96;
                    }
                }
                else
                {
                    string encoded = JsonSerializer.Serialize(command.RuleIds);
                    var existing = new Dictionary<string, bool>(StringComparer.Ordinal);
                    await using (DbCommand query = ManagementSql.Command(connection, transaction, $"""
                        SELECT i.rule_id,c.rule_id IS NOT NULL
                        FROM {Table("rule_engine_identity")} i
                        LEFT JOIN {Table("rule_engine_counter")} c ON c.ruleset_key=i.ruleset_key AND c.rule_id=i.rule_id
                        WHERE i.ruleset_key=@key AND i.counter_visible_from_revision<=@basis
                          AND i.rule_id IN(SELECT json_array_elements_text(CAST(@ids AS json)))
                        """, ("key", current.Key), ("basis", current.EditRevision), ("ids", encoded)))
                    await using (DbDataReader reader = await query.ExecuteReaderAsync(sql))
                    {
                        while (await reader.ReadAsync(sql))
                        {
                            existing.Add(reader.GetString(0), reader.GetBoolean(1));
                        }
                    }
                    var outcomes = new List<ManagementRuleCounterResetOutcome>();
                    foreach (string id in command.RuleIds.Order(StringComparer.Ordinal))
                    {
                        if (existing.TryGetValue(id, out bool hasCounter))
                        {
                            eligible.Add(id);
                            outcomes.Add(new(id, "reset"));
                            if (!hasCounter)
                            {
                                charge = checked(charge + RuleCounterRowBytes(id));
                            }
                        }
                        else
                        {
                            outcomes.Add(new(id, "notFound"));
                            refused++;
                        }
                    }
                    applied = eligible.Count;
                    storedOutcomes = JsonSerializer.SerializeToUtf8Bytes(outcomes, Json);
                    charge = checked(charge + storedOutcomes.LongLength);
                }
                if (!await ChargeRuleCountersAsync(connection, transaction, charge, sql))
                {
                    return new(null, "management.rule-engine.counters.capacity_exceeded");
                }

                await using DbCommand time = ManagementSql.Command(connection, transaction, "SELECT clock_timestamp()");
                string timestamp = ManagementSql.Timestamp((DateTime)(await time.ExecuteScalarAsync(sql))!);
                if (all)
                {
                    // A scope generation covers every identity present at the
                    // captured edit revision, including inactive historical rules.
                    await using DbCommand reset = ManagementSql.Command(connection, transaction, $"""
                        INSERT INTO {Table("rule_engine_counter_scope")}(ruleset_key,reset_epoch,all_reset_at,all_reset_revision)
                        VALUES(@key,@epoch,CAST(@time AS timestamptz),@basis)
                        ON CONFLICT(ruleset_key) DO UPDATE SET reset_epoch=EXCLUDED.reset_epoch,all_reset_at=EXCLUDED.all_reset_at,
                            all_reset_revision=EXCLUDED.all_reset_revision
                        """, ("key", current.Key), ("epoch", epoch + 1), ("time", timestamp), ("basis", current.EditRevision));
                    await reset.ExecuteNonQueryAsync(sql);
                }
                else if (eligible.Count != 0)
                {
                    await using DbCommand reset = ManagementSql.Command(connection, transaction, $"""
                        INSERT INTO {Table("rule_engine_counter")}(ruleset_key,rule_id,scope_epoch,hit_count,reset_at)
                        SELECT @key,id,@epoch,0,CAST(@time AS timestamptz) FROM json_array_elements_text(CAST(@ids AS json)) AS selected(id)
                        ON CONFLICT(ruleset_key,rule_id) DO UPDATE SET
                            scope_epoch=EXCLUDED.scope_epoch,hit_count=0,reset_at=EXCLUDED.reset_at
                        """, ("key", current.Key), ("epoch", epoch), ("time", timestamp), ("ids", JsonSerializer.Serialize(eligible)));
                    await reset.ExecuteNonQueryAsync(sql);
                }
                await using DbCommand settle = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_counter_reset")}(operation_scope,operation_key,ruleset_key,selection,basis_revision,outcomes,reset_at)
                    VALUES(@scope,@operation,@key,@selection,@basis,CAST(@outcomes AS bytea),CAST(@time AS timestamptz))
                    """, ("scope", operation.Scope), ("operation", operation.Key), ("key", current.Key), ("selection", command.Selection),
                    ("basis", current.EditRevision), ("outcomes", storedOutcomes), ("time", timestamp));
                await settle.ExecuteNonQueryAsync(sql);
                return new(new(command.Selection, timestamp, applied.ToString(CultureInfo.InvariantCulture), refused.ToString(CultureInfo.InvariantCulture)));
            }, token).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // The usage of the original transaction still settles normally. A
            // counter relation failure carries no decision and no control-state change.
            context.MarkRollbackOnly("management.rule-engine.counters.unavailable");
            throw new ManagementBackendException("management.rule-engine.counters.unavailable");
        }
    }
}
