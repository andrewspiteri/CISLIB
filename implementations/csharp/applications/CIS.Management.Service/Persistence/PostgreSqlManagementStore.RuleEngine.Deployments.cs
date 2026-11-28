using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

internal sealed record RuleEngineDeploymentEffect(ManagementRuleDeploymentDecision? Decision, string? Refusal = null,
    string? PreviousETag = null, string? PreviousRelease = null);

public sealed partial class PostgreSqlManagementStore
{
    private sealed record RuleEngineVersionForDeployment(string SchemaRevision, byte[] Definition, long EditRevision);

    private sealed record RuleEngineCarrierPublication(Guid Source, Guid Publication, Guid Release, byte[] Payload,
        string Digest, int EnvelopeBytes, long PublicationBytes, long ReleaseBytes);

    internal Task<ManagementStoreResult<ManagementRuleDeployment>> ReadRuleEngineDeploymentAsync(RuleEngineResource target, CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            await LockConfigurationQuotaAsync(connection, transaction, sql, write: false);
            RuleEngineResource? current = await LoadRuleEngineResourceAsync(connection, transaction, target.Application, target.RulesetId, false, sql);
            if (current is null || current.Key != target.Key)
            {
                return Candidate<ManagementRuleDeployment>.Refused("management.rule-engine.not_found");
            }
            ManagementConfigurationBindingResource? binding = await ResolveRuleEngineBindingAsync(connection, transaction, current, false, sql);
            if (binding is null)
            {
                return Candidate<ManagementRuleDeployment>.Refused("management.rule-engine.binding_unavailable");
            }

            ManagementConfigurationAssignmentMap map = await ReadConfigurationMapAsync(connection, transaction, binding, sql);
            ManagementRuleDeploymentSelection? desired = null;
            string? requestedAt = null;
            if (map.DefaultReleaseId is { } release && map.Generation is { } generation)
            {
                await using DbCommand witnessed = ManagementSql.Command(connection, transaction, $"""
                    SELECT v.version_id,v.schema_revision,
                        CASE WHEN e.deployment_requested_generation=@generation THEN e.deployment_requested_at END
                    FROM {Table("rule_engine_deployment_release")} link
                    JOIN {Table("rule_engine_version")} v ON v.ruleset_key=link.ruleset_key AND v.version_id=link.version_id
                    JOIN {Table("configuration_release")} r ON r.release_id=link.release_id
                    JOIN {Table("rule_engine_ruleset")} e ON e.ruleset_key=link.ruleset_key
                    WHERE link.ruleset_key=@key AND link.release_id=@release AND r.binding_id=@binding
                    """, ("key", current.Key), ("release", Guid.Parse(release)), ("binding", Guid.Parse(binding.BindingId)),
                    ("generation", long.Parse(generation, CultureInfo.InvariantCulture)));
                await using DbDataReader reader = await witnessed.ExecuteReaderAsync(sql);
                if (await reader.ReadAsync(sql))
                {
                    desired = new(reader.GetGuid(0).ToString("D"), reader.GetString(1), release, generation);
                    requestedAt = reader.IsDBNull(2) ? null : ManagementSql.Timestamp(reader, 2);
                }
            }
            return Candidate<ManagementRuleDeployment>.Existing(new(current.Application.ToString("D"), current.Environment,
                current.RulesetId, map.ETag, desired) { RequestedAtUtc = requestedAt });
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<ManagementConfigurationBindingResource>> ReadRuleEngineDeploymentBindingAsync(
        RuleEngineResource target, CancellationToken cancellationToken)
        => RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            await LockConfigurationQuotaAsync(connection, transaction, sql, write: false);
            RuleEngineResource? current = await LoadRuleEngineResourceAsync(connection, transaction, target.Application, target.RulesetId, false, sql);
            if (current is null || current.Key != target.Key || current.Environment != target.Environment)
            {
                return Candidate<ManagementConfigurationBindingResource>.Refused("management.rule-engine.not_found");
            }
            ManagementConfigurationBindingResource? binding = await ResolveRuleEngineBindingAsync(connection, transaction, current, false, sql);
            return binding is null ? Candidate<ManagementConfigurationBindingResource>.Refused("management.rule-engine.binding_unavailable")
                : Candidate<ManagementConfigurationBindingResource>.Existing(binding);
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);

    // Resolves the observed release identities in a bounded adoption page, including pruned carriers.
    internal Task<ManagementStoreResult<IReadOnlyDictionary<Guid, Guid>>> ReadRuleEngineReleaseVersionsAsync(
        RuleEngineResource target, IReadOnlyList<Guid> releaseIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(releaseIds);
        // Each of up to 500 adoption observations can reference four distinct releases.
        if (releaseIds.Count > 2000 || releaseIds.Contains(Guid.Empty))
        {
            throw new ArgumentException("Select at most 2000 nonempty configuration release identities.", nameof(releaseIds));
        }
        string captured = "{" + string.Join(",", releaseIds.Distinct().Select(release => release.ToString("D"))) + "}";
        return RunCoreAsync<IReadOnlyDictionary<Guid, Guid>>(async (context, _, token) =>
            await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            await LockConfigurationQuotaAsync(connection, transaction, sql, write: false);
            await using DbCommand select = ManagementSql.Command(connection, transaction, $"""
                SELECT h.release_id,h.version_id
                FROM {Table("rule_engine_release_history")} h
                JOIN {Table("rule_engine_ruleset")} e ON e.ruleset_key=h.ruleset_key AND e.enabled
                JOIN {Table("management_application")} a ON a.application_id=e.application_id
                WHERE e.ruleset_key=@key AND e.ruleset_id=@ruleset AND a.application_id=@application AND a.environment=@environment
                    AND h.release_id=ANY(CAST(@releases AS uuid[]))
                """, ("key", target.Key), ("ruleset", target.RulesetId), ("application", target.Application),
                ("environment", target.Environment), ("releases", captured));
            var versions = new Dictionary<Guid, Guid>();
            await using DbDataReader reader = await select.ExecuteReaderAsync(sql);
            while (await reader.ReadAsync(sql))
            {
                versions.Add(reader.GetGuid(0), reader.GetGuid(1));
            }
            return Candidate<IReadOnlyDictionary<Guid, Guid>>.Existing(versions.AsReadOnly());
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    internal Task<RuleEngineDeploymentEffect> AssignRuleEngineVersionAsync(ILocalTransactionContext context, RuleEngineResource target,
        string ifMatch, ManagementRequestRuleDeploymentCommand command, string actor, CancellationToken token)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync<RuleEngineDeploymentEffect>(async (connection, transaction, sql) =>
        {
            (long used, long quota) = await LockConfigurationQuotaAsync(connection, transaction, sql);
            RuleEngineResource? current = await LoadRuleEngineResourceAsync(connection, transaction, target.Application, target.RulesetId, true, sql);
            if (current is null || current.Key != target.Key)
            {
                return new(null, "management.rule-engine.not_found");
            }
            ManagementConfigurationBindingResource? binding = await ResolveRuleEngineBindingAsync(connection, transaction, current, true, sql);
            if (binding is null || binding.ApplicationSourceId is null || binding.CommonSourceId is not null)
            {
                return new(null, "management.rule-engine.binding_unavailable");
            }

            ManagementConfigurationAssignmentMap before = await ReadConfigurationMapAsync(connection, transaction, binding, sql, write: true);
            if (before.ETag != ifMatch)
            {
                return Refuse("management.rule-engine.precondition_failed");
            }
            long revision = long.Parse(before.ETag["\"configuration.map.".Length..^1], CultureInfo.InvariantCulture);
            long generation = before.Generation is null ? 0 : long.Parse(before.Generation, CultureInfo.InvariantCulture);
            if (revision == long.MaxValue || generation == long.MaxValue)
            {
                return Refuse("management.rule-engine.generation_exhausted");
            }

            Guid version = Guid.Parse(command.Version);
            RuleEngineVersionForDeployment? selected = await FindRuleEngineVersionForDeploymentAsync(connection, transaction, current.Key, version, sql);
            if (selected is null)
            {
                return Refuse("management.rule-engine.version_not_found");
            }
            if (command.Intent == "rollback")
            {
                await using DbCommand active = ManagementSql.Command(connection, transaction, $"""
                    SELECT v.edit_revision FROM {Table("rule_engine_deployment_release")} link
                    JOIN {Table("rule_engine_version")} v ON v.ruleset_key=link.ruleset_key AND v.version_id=link.version_id
                    WHERE link.ruleset_key=@key AND link.release_id=CAST(@release AS uuid)
                    """, ("key", current.Key), ("release", before.DefaultReleaseId));
                if (await active.ExecuteScalarAsync(sql) is not long assignedRevision || selected.EditRevision >= assignedRevision)
                {
                    return Refuse("management.rule-engine.rollback_target_not_earlier");
                }
            }
            bool witnessed;
            await using (DbCommand find = ManagementSql.Command(connection, transaction, $"""
                SELECT EXISTS(SELECT 1 FROM {Table("rule_engine_deployed_version")} WHERE ruleset_key=@key AND version_id=@version)
                """, ("key", current.Key), ("version", version)))
            {
                witnessed = await find.ExecuteScalarAsync(sql) is true;
            }
            Guid? previousCarrier;
            await using (DbCommand read = ManagementSql.Command(connection, transaction, $"""
                SELECT release_id FROM {Table("rule_engine_deployment_release")} WHERE ruleset_key=@key AND version_id=@version
                """, ("key", current.Key), ("version", version)))
            {
                previousCarrier = (Guid?)await read.ExecuteScalarAsync(sql);
            }
            Guid release;
            RuleEngineCarrierPublication? materialized = null;
            try
            {
                if (previousCarrier is { } retained)
                {
                    release = retained;
                    await ValidateConfigurationAssignmentAsync(connection, transaction, binding, new(1, "replace", retained.ToString("D"),
                        [], command.Reason), sql);
                }
                else
                {
                    materialized = await PrepareRuleEngineCarrierAsync(connection, transaction, binding, selected, actor, command.Reason, sql);
                    release = materialized.Release;
                }
            }
            catch (ConfigurationReferenceException error)
            {
                return Refuse(error.Code);
            }
            catch (ArgumentException)
            {
                return Refuse("management.rule-engine.carrier_incompatible");
            }

            Guid bindingId = Guid.Parse(binding.BindingId);
            long previousOverrides;
            await using (DbCommand sum = ManagementSql.Command(connection, transaction, $"""
                SELECT COALESCE(sum(logical_bytes),0)::bigint FROM {Table("configuration_override")} WHERE binding_id=@binding
                """, ("binding", bindingId)))
            {
                previousOverrides = (long)(await sum.ExecuteScalarAsync(sql))!;
            }
            long delta = (materialized is null ? 0 : materialized.PublicationBytes + materialized.ReleaseBytes + ManagementSchema.RuleEngineReleaseHistoryBytes)
                + (witnessed ? 0 : 96) - previousOverrides;
            if (delta > quota - used)
            {
                return Refuse("management.rule-engine.capacity_exceeded");
            }

            if (materialized is not null)
            {
                await WriteRuleEngineCarrierAsync(connection, transaction, bindingId, materialized, actor, command.Reason, sql);
            }
            string timestamp = await ResetConfigurationAssignmentAsync(connection, transaction, bindingId, generation + 1, release, sql);
            if (!witnessed)
            {
                await using DbCommand witness = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_deployed_version")}(ruleset_key,version_id,first_assigned_at)
                    VALUES(@key,@version,clock_timestamp())
                    """, ("key", current.Key), ("version", version));
                await witness.ExecuteNonQueryAsync(sql);
            }
            if (materialized is not null)
            {
                await using DbCommand link = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_deployment_release")}(release_id,ruleset_key,version_id)
                    VALUES(@release,@key,@version);
                    INSERT INTO {Table("rule_engine_release_history")}(release_id,ruleset_key,version_id)
                    VALUES(@release,@key,@version)
                    """, ("release", release), ("key", current.Key), ("version", version));
                await link.ExecuteNonQueryAsync(sql);
            }
            await using (DbCommand update = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@delta WHERE singleton=true;
                UPDATE {Table("rule_engine_ruleset")} SET configuration_binding_id=COALESCE(configuration_binding_id,@binding),
                    deployment_requested_generation=@generation,deployment_requested_at=CAST(@requested AS timestamptz)
                    WHERE ruleset_key=@key
                """, ("delta", delta), ("binding", bindingId), ("key", current.Key),
                ("generation", generation + 1), ("requested", timestamp)))
            {
                await update.ExecuteNonQueryAsync(sql);
            }
            return new(new(command.Intent, ConfigurationETag("map", revision + 1), timestamp,
                new(version.ToString("D"), selected.SchemaRevision, release.ToString("D"), (generation + 1).ToString(CultureInfo.InvariantCulture))),
                PreviousETag: before.ETag, PreviousRelease: before.DefaultReleaseId);

            RuleEngineDeploymentEffect Refuse(string code) => new(null, code, before.ETag, before.DefaultReleaseId);
        }, token);
    }

    private async Task<ManagementConfigurationBindingResource?> ResolveRuleEngineBindingAsync(DbConnection connection,
        DbTransaction transaction, RuleEngineResource resource, bool write, CancellationToken token)
    {
        await using DbCommand query = ManagementSql.Command(connection, transaction, ConfigurationBindingSelect + """
            WHERE b.application_id=@app AND d.configuration_id=@config AND b.enabled AND d.enabled
                AND (CAST(@native AS uuid) IS NULL OR b.binding_id=CAST(@native AS uuid))
            """ + (write ? " FOR UPDATE OF b" : ""),
            ("app", resource.Application), ("config", resource.ConfigurationId), ("native", resource.ConfigurationBinding?.ToString("D")));
        await using DbDataReader reader = await query.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            return null;
        }
        ManagementConfigurationBindingResource binding = ReadConfigurationBinding(reader);
        return binding.Environment != resource.Environment || await reader.ReadAsync(token) ? null : binding;
    }

    private async Task<RuleEngineVersionForDeployment?> FindRuleEngineVersionForDeploymentAsync(DbConnection connection,
        DbTransaction transaction, Guid key, Guid version, CancellationToken token)
    {
        await using DbCommand select = ManagementSql.Command(connection, transaction, $"""
            SELECT schema_revision,definition,edit_revision FROM {Table("rule_engine_version")} WHERE ruleset_key=@key AND version_id=@version
            """, ("key", key), ("version", version));
        await using DbDataReader reader = await select.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(reader.GetString(0), reader.GetFieldValue<byte[]>(1), reader.GetInt64(2)) : null;
    }

    private async Task<RuleEngineCarrierPublication> PrepareRuleEngineCarrierAsync(DbConnection connection, DbTransaction transaction,
        ManagementConfigurationBindingResource binding, RuleEngineVersionForDeployment version, string actor, string reason, CancellationToken token)
    {
        Guid source = Guid.Parse(binding.ApplicationSourceId!);
        await using (DbCommand lockSource = ManagementSql.Command(connection, transaction, $"""
            SELECT revision FROM {Table("configuration_source")}
            WHERE source_id=@source AND binding_id=@binding AND enabled FOR UPDATE
            """, ("source", source), ("binding", Guid.Parse(binding.BindingId))))
        {
            object? revision = await lockSource.ExecuteScalarAsync(token);
            if (revision is not long revisionValue || revisionValue == long.MaxValue)
            {
                throw new ConfigurationReferenceException("management.rule-engine.carrier_unavailable");
            }
        }

        string json = ManagementRuleDefinitionCarrier.Encode(version.Definition);
        if (!ManagementConfigurationStructure.Validate(binding.Shape, json, complete: true).Valid)
        {
            throw new ConfigurationReferenceException("management.rule-engine.carrier_incompatible");
        }
        byte[] payload = Encoding.UTF8.GetBytes(json);
        string digest = Convert.ToHexStringLower(SHA256.HashData(payload));
        Guid publication = Guid.NewGuid();
        var pair = new ConfigurationSourcePair(new("absent"), new("present", publication.ToString("D"), digest, json,
            source.ToString("D"), binding.ConfigurationId, binding.SchemaVersion, binding.Environment, "application", binding.ApplicationId));
        string composed = ManagementConfigurationComposition.Compose(binding, pair).ToJsonString();
        if (!ManagementRuleDefinitionCarrier.Decode(composed).GetDefinitionBytes().AsSpan().SequenceEqual(version.Definition))
        {
            throw new ConfigurationReferenceException("management.rule-engine.carrier_incompatible");
        }
        int envelope = WorstConfigurationEnvelopeBytes(pair);
        long actorAndReason = Encoding.UTF8.GetByteCount(actor) + Encoding.UTF8.GetByteCount(reason);
        return new(source, publication, Guid.NewGuid(), payload, digest, envelope, payload.LongLength + 128 + actorAndReason,
            192 + actorAndReason);
    }

    private async Task WriteRuleEngineCarrierAsync(DbConnection connection, DbTransaction transaction, Guid binding,
        RuleEngineCarrierPublication carrier, string actor, string reason, CancellationToken token)
    {
        await using DbCommand insert = ManagementSql.Command(connection, transaction, $"""
            INSERT INTO {Table("configuration_publication")}(publication_id,source_id,payload,sha256,actor,created_at,reason,logical_bytes)
            VALUES(@publication,@source,@payload,@sha256,@actor,clock_timestamp(),@reason,@publication_bytes);
            UPDATE {Table("configuration_source")} SET revision=revision+1 WHERE source_id=@source;
            INSERT INTO {Table("configuration_release")}(release_id,binding_id,common_publication_id,application_publication_id,
                actor,created_at,reason,encoded_bytes,logical_bytes)
            VALUES(@release,@binding,NULL,@publication,@actor,clock_timestamp(),@reason,@envelope,@release_bytes)
            """, ("publication", carrier.Publication), ("source", carrier.Source), ("payload", carrier.Payload), ("sha256", carrier.Digest),
            ("actor", actor), ("reason", reason), ("publication_bytes", carrier.PublicationBytes), ("release", carrier.Release),
            ("binding", binding), ("envelope", carrier.EnvelopeBytes), ("release_bytes", carrier.ReleaseBytes));
        await insert.ExecuteNonQueryAsync(token);
    }
}
