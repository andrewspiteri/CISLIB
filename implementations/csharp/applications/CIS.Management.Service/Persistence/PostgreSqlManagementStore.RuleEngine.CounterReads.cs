using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CIS.Management;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementRuleCounterPage>> ReadRuleEngineCountersAsync(RuleEngineResource scope,
        string? after, int take, bool ingressReady, CancellationToken token)
    {
        Page(take);
        return RunCoreAsync(async (context, _, cancellation) =>
        {
            try
            {
                return await context.UseAdoNetAsync(async (connection, transaction, sql) =>
                {
                    await LockRuleCountersAsync(connection, transaction, scope.Key, sql, readOnly: true);
                    string availability;
                    string observed;
                    await using (DbCommand state = ManagementSql.Command(connection, transaction, $"""
                        SELECT clock_timestamp(),EXISTS(
                            SELECT 1 FROM {Table("rule_engine_execution_event")} received
                            WHERE received.ruleset_key=@key AND received.phase=1
                              AND NOT EXISTS(SELECT 1 FROM {Table("rule_engine_counter_execution")} accepted
                                  WHERE accepted.ruleset_key=received.ruleset_key AND accepted.execution_id=received.execution_id)),
                            EXISTS(SELECT 1 FROM {Table("rule_engine_counter")} c WHERE c.ruleset_key=@key)
                                OR EXISTS(SELECT 1 FROM {Table("rule_engine_counter_scope")} s WHERE s.ruleset_key=@key)
                        """, ("key", scope.Key)))
                    await using (DbDataReader reader = await state.ExecuteReaderAsync(sql))
                    {
                        if (!await reader.ReadAsync(sql))
                        {
                            throw new ManagementSchemaException("The scoped counter observation is incomplete.");
                        }
                        observed = ManagementSql.Timestamp(reader, 0);
                        bool pending = reader.GetBoolean(1);
                        bool known = reader.GetBoolean(2);
                        availability = ingressReady && !pending ? "fresh" : ingressReady || known ? "stale" : "unavailable";
                    }
                    await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
                        SELECT i.rule_id,
                            CASE WHEN c.scope_epoch=COALESCE(s.reset_epoch,0) THEN c.hit_count
                                 WHEN c.rule_id IS NOT NULL OR i.counter_visible_from_revision<=s.all_reset_revision THEN 0 END,
                            CASE WHEN c.scope_epoch=COALESCE(s.reset_epoch,0) AND c.reset_at IS NOT NULL THEN c.reset_at
                                 WHEN i.counter_visible_from_revision<=s.all_reset_revision THEN s.all_reset_at END
                        FROM {Table("rule_engine_identity")} i
                        LEFT JOIN {Table("rule_engine_counter")} c ON c.ruleset_key=i.ruleset_key AND c.rule_id=i.rule_id
                        LEFT JOIN {Table("rule_engine_counter_scope")} s ON s.ruleset_key=i.ruleset_key
                        WHERE i.ruleset_key=@key AND (CAST(@after AS text) IS NULL OR i.rule_id>@after)
                        ORDER BY i.rule_id LIMIT @take
                        """, ("key", scope.Key), ("after", after), ("take", take));
                    await using DbDataReader rows = await query.ExecuteReaderAsync(sql);
                    var items = new List<ManagementRuleCounterItem>();
                    while (await rows.ReadAsync(sql))
                    {
                        string? count = rows.IsDBNull(1) ? availability == "fresh" ? "0" : null
                            : rows.GetInt64(1).ToString(CultureInfo.InvariantCulture);
                        items.Add(new(rows.GetString(0), count, rows.IsDBNull(2) ? null : ManagementSql.Timestamp(rows, 2)));
                    }
                    return Candidate<ManagementRuleCounterPage>.Existing(new(scope.Application.ToString("D"), scope.Environment,
                        scope.RulesetId, availability, items.AsReadOnly(), observed));
                }, cancellation).ConfigureAwait(false);
            }
            catch (DbException)
            {
                return Candidate<ManagementRuleCounterPage>.Refused("management.rule-engine.counters.unavailable");
            }
        }, token, requireReady: true, requireCleanRead: true);
    }

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementRuleCounterResetOutcome>>> ReadRuleCounterResetOutcomesAsync(
        RuleEngineResource scope, ManagementOperationReference operation, string? after, int take, CancellationToken token)
    {
        Page(take);
        return RunCoreAsync<IReadOnlyList<ManagementRuleCounterResetOutcome>>(async (context, _, cancellation) =>
        {
            try
            {
                return await context.UseAdoNetAsync(async (connection, transaction, sql) =>
                {
                    string selection;
                    long basis;
                    byte[]? stored;
                    await using (DbCommand effect = ManagementSql.Command(connection, transaction, $"""
                        SELECT selection,basis_revision,outcomes FROM {Table("rule_engine_counter_reset")}
                        WHERE ruleset_key=@key AND operation_scope=@scope AND operation_key=@operation
                        """, ("key", scope.Key), ("scope", operation.Scope), ("operation", operation.Key)))
                    await using (DbDataReader reader = await effect.ExecuteReaderAsync(sql))
                    {
                        if (!await reader.ReadAsync(sql))
                        {
                            return Candidate<IReadOnlyList<ManagementRuleCounterResetOutcome>>.Refused("management.rule-engine.not_found");
                        }
                        selection = reader.GetString(0);
                        basis = reader.GetInt64(1);
                        stored = reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2);
                    }
                    if (selection != "all")
                    {
                        ManagementRuleCounterResetOutcome[] captured = JsonSerializer.Deserialize<ManagementRuleCounterResetOutcome[]>(stored!, Json)
                            ?? throw new ManagementSchemaException("The retained selected counter reset outcomes are unreadable.");
                        return Candidate<IReadOnlyList<ManagementRuleCounterResetOutcome>>.Existing(Array.AsReadOnly(captured
                            .Where(item => after is null || StringComparer.Ordinal.Compare(item.RuleId, after) > 0).Take(take).ToArray()));
                    }
                    await using DbCommand identities = ManagementSql.Command(connection, transaction, $"""
                        SELECT rule_id FROM {Table("rule_engine_identity")}
                        WHERE ruleset_key=@key AND counter_visible_from_revision<=@basis
                          AND (CAST(@after AS text) IS NULL OR rule_id>@after)
                        ORDER BY rule_id LIMIT @take
                        """, ("key", scope.Key), ("basis", basis), ("after", after), ("take", take));
                    await using DbDataReader rows = await identities.ExecuteReaderAsync(sql);
                    var items = new List<ManagementRuleCounterResetOutcome>();
                    while (await rows.ReadAsync(sql))
                    {
                        items.Add(new(rows.GetString(0), "reset"));
                    }
                    return Candidate<IReadOnlyList<ManagementRuleCounterResetOutcome>>.Existing(items.AsReadOnly());
                }, cancellation).ConfigureAwait(false);
            }
            catch (DbException)
            {
                return Candidate<IReadOnlyList<ManagementRuleCounterResetOutcome>>.Refused("management.rule-engine.counters.unavailable");
            }
        }, token, requireReady: true, requireCleanRead: true);
    }
}
