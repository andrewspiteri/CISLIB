using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.RuleEngine;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    private sealed record ExecutionStartFacts(Guid Version, string Producer, string Instance, Guid Registration, string Build,
        string? Correlation, long StartedTicks);

    private sealed class RuleExecutionRefusal(string code) : Exception
    {
        internal string Code { get; } = code;
    }

    // The broker dispatcher already owns the named transaction and its inbox.
    // The execution and exact source payload settle with that original owner.
    internal async Task AcceptRuleEngineExecutionAsync(ILocalTransactionContext context, RuleEngineAuditObservation observation,
        string managedApplication, CancellationToken cancellationToken)
    {
        ManagementSql.RequireContext(context);
        ArgumentNullException.ThrowIfNull(observation);
        ManagementAuthoritySnapshot confirmed = await (authority
            ?? throw new ManagementBackendException("management.authority.unavailable"))
            .ReadAsync(context, requireReady: true, cancellationToken).ConfigureAwait(false);
        byte[] payload = observation.Record.OriginalBytes.ToArray();
        if (payload.Length is < 1 or > 128 * 1024)
        {
            throw new ManagementBackendException("management.rule-engine.execution_invalid");
        }

        await context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            (long used, long quota) = await LockConfigurationQuotaAsync(connection, transaction, token);
            Guid auditId = observation.Record.AuditId;
            byte[] digest = SHA256.HashData(payload);
            await using (DbCommand prior = ManagementSql.Command(connection, transaction, $"""
                SELECT EXISTS(SELECT 1 FROM {Table("rule_engine_execution_rejection")} rejected WHERE rejected.audit_id=@audit
                    AND (rejected.payload_digest=@digest OR NOT EXISTS(
                        SELECT 1 FROM {Table("rule_engine_execution_event")} accepted WHERE accepted.audit_id=@audit)))
                """, ("audit", auditId), ("digest", digest)))
            {
                if (await prior.ExecuteScalarAsync(token) is true)
                {
                    return 0;
                }
            }
            try
            {
                if (confirmed.AuthorityId != observation.AuthorityId)
                {
                    throw new RuleExecutionRefusal("management.rule-engine.execution_wrong_authority");
                }

                Guid rulesetKey = await FindExecutionRulesetAsync(connection, transaction, observation, managedApplication, token);
                var facts = new ExecutionStartFacts(observation.Version, observation.Application, observation.InstanceId,
                    observation.RegistrationId, observation.ApplicationBuild, observation.CorrelationId, observation.StartedAtUtc.UtcTicks);
                ExecutionStartFacts? retained = await ReadExecutionStartFactsAsync(connection, transaction, rulesetKey, observation.ExecutionId, token);
                if (retained is not null && retained != facts)
                {
                    throw new RuleExecutionRefusal("management.rule-engine.execution_conflict");
                }
                int phase = observation.Status.HasValue ? 1 : 0;
                if (retained is not null && await CompareExecutionEventsAsync(connection, transaction, rulesetKey, observation, phase, payload, token))
                {
                    return 0;
                }
                await using (DbCommand conflictingAudit = ManagementSql.Command(connection, transaction,
                    $"SELECT EXISTS(SELECT 1 FROM {Table("rule_engine_execution_event")} WHERE audit_id=@audit)", ("audit", auditId)))
                {
                    if (await conflictingAudit.ExecuteScalarAsync(token) is true)
                    {
                        throw new RuleExecutionRefusal("management.rule-engine.execution_conflict");
                    }
                }

                long allocation = 96 + payload.LongLength;
                if (retained is null)
                {
                    allocation += 256 + Encoding.UTF8.GetByteCount(facts.Producer) + Encoding.UTF8.GetByteCount(facts.Instance)
                        + Encoding.UTF8.GetByteCount(facts.Build) + Encoding.UTF8.GetByteCount(facts.Correlation ?? string.Empty);
                }
                if (allocation > quota - used)
                {
                    throw new ManagementBackendException("management.configuration.capacity_exceeded");
                }
                if (retained is null)
                {
                    await using DbCommand insert = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_execution")}(ruleset_key,execution_id,version_id,producer_application,instance_id,
                        registration_id,application_build,correlation_id,started_ticks,logical_bytes)
                    VALUES(@ruleset,@execution,@version,@producer,@instance,@registration,@build,@correlation,@started,@allocation)
                    """, ("ruleset", rulesetKey), ("execution", observation.ExecutionId), ("version", facts.Version),
                        ("producer", facts.Producer), ("instance", facts.Instance), ("registration", facts.Registration), ("build", facts.Build),
                        ("correlation", facts.Correlation), ("started", facts.StartedTicks), ("allocation", allocation));
                    await insert.ExecuteNonQueryAsync(token);
                }
                await using DbCommand save = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("rule_engine_execution_event")}(ruleset_key,execution_id,phase,audit_id,causation_audit_id,body)
                VALUES(@ruleset,@execution,@phase,@audit,CAST(@cause AS uuid),@body) ON CONFLICT(audit_id) DO NOTHING
                """, ("ruleset", rulesetKey), ("execution", observation.ExecutionId), ("phase", phase),
                    ("audit", observation.Record.AuditId), ("cause", observation.Record.CausationAuditId?.ToString("D")), ("body", payload));
                if (await save.ExecuteNonQueryAsync(token) != 1)
                {
                    throw new ManagementBackendException("management.rule-engine.execution_conflict");
                }
                int? matchCount = observation.Status == RuleExecutionStatus.Completed
                    ? observation.Details.GetProperty("selectedRowIndices").GetArrayLength() : null;
                await using DbCommand settle = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("rule_engine_execution")} SET
                    finished_ticks=COALESCE(CAST(@finished AS bigint),finished_ticks),
                    terminal_status=COALESCE(@status,terminal_status),
                    returned_match_count=COALESCE(CAST(@matches AS integer),returned_match_count),
                    last_received_at=clock_timestamp(),logical_bytes=logical_bytes+@additional
                WHERE ruleset_key=@ruleset AND execution_id=@execution;
                UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@charge WHERE singleton=true
                """, ("finished", observation.FinishedAtUtc?.UtcTicks), ("status", observation.Status?.ToString()),
                    ("matches", matchCount), ("additional", retained is null ? 0L : allocation), ("ruleset", rulesetKey), ("execution", observation.ExecutionId),
                    ("charge", allocation));
                await settle.ExecuteNonQueryAsync(token);
                return 0;
            }
            catch (RuleExecutionRefusal refusal)
            {
                long charge = 128 + Encoding.UTF8.GetByteCount(refusal.Code);
                if (charge > quota - used)
                {
                    throw new ManagementBackendException("management.configuration.capacity_exceeded");
                }
                await using DbCommand reject = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_execution_rejection")}(audit_id,payload_digest,safe_code)
                    VALUES(@audit,@digest,@code);
                    UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@charge WHERE singleton=true
                    """, ("audit", auditId), ("digest", digest), ("code", refusal.Code), ("charge", charge));
                await reject.ExecuteNonQueryAsync(token);
                return 0;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid> FindExecutionRulesetAsync(DbConnection connection, DbTransaction transaction,
        RuleEngineAuditObservation observation, string managedApplication, CancellationToken token)
    {
        await using DbCommand select = ManagementSql.Command(connection, transaction, $"""
            SELECT r.ruleset_key,v.definition
            FROM {Table("rule_engine_ruleset")} r
            JOIN {Table("management_application")} a ON a.application_id=r.application_id
            JOIN {Table("rule_engine_deployed_version")} w ON w.ruleset_key=r.ruleset_key AND w.version_id=@version
            JOIN {Table("rule_engine_version")} v ON v.ruleset_key=w.ruleset_key AND v.version_id=w.version_id
            WHERE r.application_id=@application AND a.application=@managed AND a.environment=@environment
                AND r.ruleset_id=@ruleset AND v.schema_revision=@schema
                AND NOT EXISTS(SELECT 1 FROM {Table("management_registration")} other
                    WHERE other.registration_id=@registration AND other.application_id<>r.application_id)
            """, ("version", observation.Version), ("application", observation.ApplicationId), ("managed", managedApplication),
            ("environment", observation.Environment), ("ruleset", observation.RulesetId), ("schema", observation.SchemaRevision),
            ("registration", observation.RegistrationId));
        await using DbDataReader reader = await select.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)
            || !observation.MatchesDefinition(RuleDefinitionCodec.Decode(reader.GetFieldValue<byte[]>(1))))
        {
            throw new RuleExecutionRefusal("management.rule-engine.execution_invalid");
        }
        return reader.GetGuid(0);
    }

    private async Task<ExecutionStartFacts?> ReadExecutionStartFactsAsync(DbConnection connection, DbTransaction transaction,
        Guid ruleset, Guid execution, CancellationToken token)
    {
        await using DbCommand select = ManagementSql.Command(connection, transaction, $"""
            SELECT version_id,producer_application,instance_id,registration_id,application_build,correlation_id,started_ticks
            FROM {Table("rule_engine_execution")} WHERE ruleset_key=@ruleset AND execution_id=@execution FOR UPDATE
            """, ("ruleset", ruleset), ("execution", execution));
        await using DbDataReader reader = await select.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.GetGuid(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6)) : null;
    }

    private async Task<bool> CompareExecutionEventsAsync(DbConnection connection, DbTransaction transaction, Guid ruleset,
        RuleEngineAuditObservation observation, int phase, byte[] payload, CancellationToken token)
    {
        await using DbCommand select = ManagementSql.Command(connection, transaction, $"""
            SELECT phase,audit_id,causation_audit_id,CASE WHEN phase=@phase THEN body END
            FROM {Table("rule_engine_execution_event")} WHERE ruleset_key=@ruleset AND execution_id=@execution
            """, ("phase", phase), ("ruleset", ruleset), ("execution", observation.ExecutionId));
        await using DbDataReader reader = await select.ExecuteReaderAsync(token);
        bool duplicate = false;
        while (await reader.ReadAsync(token))
        {
            if (reader.GetInt16(0) == phase)
            {
                duplicate = reader.GetGuid(1) == observation.Record.AuditId
                    && CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(3), payload);
                if (!duplicate)
                {
                    throw new RuleExecutionRefusal("management.rule-engine.execution_conflict");
                }
            }
            else if (phase == 1 ? observation.Record.CausationAuditId != reader.GetGuid(1)
                : reader.IsDBNull(2) || reader.GetGuid(2) != observation.Record.AuditId)
            {
                throw new RuleExecutionRefusal("management.rule-engine.execution_conflict");
            }
        }
        return duplicate;
    }
}
