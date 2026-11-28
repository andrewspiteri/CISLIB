using System.Data.Common;
using System.Globalization;
using System.Text;
using CIS.Management;
using CIS.Management.Wire;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

internal sealed record RuleEngineExecutionFilter(Guid? ExecutionId, Guid? Version, string? Instance, string? Status,
    long? StartedFromTicks, long? StartedThroughTicks);

internal sealed record RuleEngineExecutionPosition(long StartedTicks, Guid ExecutionId)
{
    internal string CursorKey => StartedTicks.ToString(CultureInfo.InvariantCulture) + "." + ExecutionId.ToString("D");

    internal static bool TryRead(string? value, out RuleEngineExecutionPosition? position)
    {
        position = null;
        if (value is null or { Length: < 38 or > 56 })
        {
            return false;
        }
        int separator = value.IndexOf('.');
        if (separator <= 0 || !long.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || ticks <= 0 || ticks > DateTime.MaxValue.Ticks || !ManagementWire.IsUuid(value[(separator + 1)..]))
        {
            return false;
        }
        position = new(ticks, Guid.Parse(value[(separator + 1)..]));
        return position.CursorKey == value;
    }
}

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<RuleEngineResource>> ReadRetainedRuleEngineResourceAsync(Guid application, string ruleset,
        CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            await using DbCommand query = ManagementSql.Command(connection, transaction, RuleEngineResourceSelect
                + "WHERE r.application_id=@application AND r.ruleset_id=@ruleset", ("application", application), ("ruleset", ruleset));
            await using DbDataReader reader = await query.ExecuteReaderAsync(sql);
            return await reader.ReadAsync(sql) ? Candidate<RuleEngineResource>.Existing(ReadRuleEngineResource(reader))
                : Candidate<RuleEngineResource>.Refused("management.rule-engine.not_found");
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementRuleExecutionSummary>>> ListRuleEngineExecutionsAsync(
        RuleEngineResource scope, RuleEngineExecutionFilter filter, RuleEngineExecutionPosition? after, int take, CancellationToken token)
    {
        Page(take);
        return RunCoreAsync<IReadOnlyList<ManagementRuleExecutionSummary>>(async (context, _, cancellation) =>
            await context.UseAdoNetAsync(async (connection, transaction, sql) =>
            {
                await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
                    WITH history AS (
                        SELECT {ExecutionColumns}
                        {ExecutionJoins}
                        WHERE e.ruleset_key=@key AND (CAST(@execution AS uuid) IS NULL OR e.execution_id=CAST(@execution AS uuid))
                            AND (CAST(@version AS uuid) IS NULL OR e.version_id=CAST(@version AS uuid))
                            AND (CAST(@instance AS text) IS NULL OR e.instance_id=@instance)
                            AND (CAST(@from_ticks AS bigint) IS NULL OR e.started_ticks>=CAST(@from_ticks AS bigint))
                            AND (CAST(@through_ticks AS bigint) IS NULL OR e.started_ticks<=CAST(@through_ticks AS bigint))
                            AND (CAST(@after_ticks AS bigint) IS NULL OR
                                (e.started_ticks,e.execution_id)<(CAST(@after_ticks AS bigint),CAST(@after_id AS uuid)))
                    )
                    SELECT * FROM history WHERE CAST(@status AS text) IS NULL OR execution_status=@status
                    ORDER BY started_ticks DESC,execution_id DESC LIMIT @take
                    """, ("key", scope.Key), ("execution", filter.ExecutionId?.ToString("D")), ("version", filter.Version?.ToString("D")),
                    ("instance", filter.Instance), ("from_ticks", filter.StartedFromTicks), ("through_ticks", filter.StartedThroughTicks),
                    ("after_ticks", after?.StartedTicks), ("after_id", after?.ExecutionId.ToString("D")), ("status", filter.Status), ("take", take));
                await using DbDataReader reader = await query.ExecuteReaderAsync(sql);
                var executions = new List<ManagementRuleExecutionSummary>();
                while (await reader.ReadAsync(sql))
                {
                    executions.Add(ReadExecutionSummary(scope, reader));
                }
                return Candidate<IReadOnlyList<ManagementRuleExecutionSummary>>.Existing(executions.AsReadOnly());
            }, cancellation), token, requireReady: true, requireCleanRead: true);
    }

    internal Task<ManagementStoreResult<ManagementRuleExecutionDetail>> ReadRuleEngineExecutionAsync(
        RuleEngineResource scope, Guid execution, CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
                SELECT {ExecutionColumns},v.definition,s.body,f.body
                {ExecutionJoins}
                WHERE e.ruleset_key=@key AND e.execution_id=@execution
                """, ("key", scope.Key), ("execution", execution));
            await using DbDataReader reader = await query.ExecuteReaderAsync(sql);
            if (!await reader.ReadAsync(sql))
            {
                return Candidate<ManagementRuleExecutionDetail>.Refused("management.rule-engine.not_found");
            }
            var decoder = new UTF8Encoding(false, true);
            string? Text(int ordinal) => reader.IsDBNull(ordinal) ? null : decoder.GetString(reader.GetFieldValue<byte[]>(ordinal));
            return Candidate<ManagementRuleExecutionDetail>.Existing(new(ReadExecutionSummary(scope, reader),
                Text(13)!, Text(14), Text(15)));
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    private const string ExecutionColumns = """
        e.execution_id,e.version_id,v.schema_revision,e.producer_application,e.instance_id,e.registration_id,
        e.application_build,e.started_ticks,e.finished_ticks,
        CASE WHEN s.audit_id IS NULL OR f.audit_id IS NULL THEN 'Incomplete' ELSE e.terminal_status END AS execution_status,
        s.audit_id IS NOT NULL AS start_received,f.audit_id IS NOT NULL AS completion_received,
        CASE WHEN s.audit_id IS NOT NULL AND f.audit_id IS NOT NULL THEN e.returned_match_count END
        """;

    private string ExecutionJoins => $"""
        FROM {Table("rule_engine_execution")} e
        JOIN {Table("rule_engine_version")} v ON v.ruleset_key=e.ruleset_key AND v.version_id=e.version_id
        LEFT JOIN {Table("rule_engine_execution_event")} s ON s.ruleset_key=e.ruleset_key AND s.execution_id=e.execution_id AND s.phase=0
        LEFT JOIN {Table("rule_engine_execution_event")} f ON f.ruleset_key=e.ruleset_key AND f.execution_id=e.execution_id AND f.phase=1
        """;

    private static ManagementRuleExecutionSummary ReadExecutionSummary(RuleEngineResource scope, DbDataReader reader)
        => new(scope.Application.ToString("D"), scope.Environment, scope.RulesetId, reader.GetGuid(0).ToString("D"),
            reader.GetGuid(1).ToString("D"), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetGuid(5).ToString("D"),
            reader.GetString(6), ExecutionTimestamp(reader.GetInt64(7)), reader.GetString(9), reader.GetBoolean(10), reader.GetBoolean(11),
            reader.IsDBNull(8) ? null : ExecutionTimestamp(reader.GetInt64(8)))
        {
            ReturnedMatchCount = reader.IsDBNull(12) ? null : reader.GetInt32(12)
        };

    private static string ExecutionTimestamp(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
