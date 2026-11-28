using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.RuleEngine;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

internal sealed record RuleEngineResource(Guid Key, Guid Application, string Environment, string RulesetId, string ConfigurationId,
    string SchemaRevision, string DisplayName, long EditRevision, Guid? LatestVersion, Guid? ConfigurationBinding = null,
    string? ManagedApplication = null);

internal sealed record RuleEngineCatalogEntry(RuleEngineResource Resource, byte[] Schema, bool NewSchema);

internal sealed record RuleEngineCatalogUpdate(IReadOnlyList<RuleEngineCatalogEntry> Entries, long AdditionalBytes, string? Refusal = null);

internal sealed record RuleEngineSaveEffect(ManagementRuleVersionDecision? Decision, string? Refusal = null,
    string? PreviousETag = null, string? Digest = null);

public sealed partial class PostgreSqlManagementStore
{
    private async Task<RuleEngineCatalogUpdate> PrepareRuleEngineCatalogAsync(DbConnection connection, DbTransaction transaction,
        ManagementConfigurationManifest manifest, IReadOnlyDictionary<(string, string), Guid> applications,
        IReadOnlyDictionary<(Guid, string), (Guid Binding, Guid Definition)> nativeBindings, CancellationToken token)
    {
        var entries = new List<RuleEngineCatalogEntry>();
        long additionalBytes = 0;
        foreach (ManagementRuleEngineRegistration registration in manifest.RuleEngine ?? [])
        {
            Guid app = applications[(registration.Application, registration.Environment)];
            (Guid nativeBinding, Guid nativeDefinition) = nativeBindings[(app, registration.ConfigurationId)];
            byte[] schema = Encoding.UTF8.GetBytes(registration.SchemaJson);
            RuleDefinition definition = RuleDefinitionCodec.Decode(schema);
            Guid key = Guid.NewGuid();
            long editRevision = 1;
            Guid? latest = null;
            bool existing = false;
            bool bindingRecorded = false;
            await using (DbCommand find = ManagementSql.Command(connection, transaction, $"""
                SELECT ruleset_key,ruleset_id,configuration_id,active_schema_revision,edit_revision,latest_version,configuration_binding_id
                FROM {Table("rule_engine_ruleset")} WHERE application_id=@application
                  AND (ruleset_id=@ruleset OR configuration_id=@configuration) FOR UPDATE
                """, ("application", app), ("ruleset", definition.RulesetId), ("configuration", registration.ConfigurationId)))
            await using (DbDataReader reader = await find.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token))
                {
                    if (reader.GetString(1) != definition.RulesetId || reader.GetString(2) != registration.ConfigurationId
                        || !reader.IsDBNull(6) && reader.GetGuid(6) != nativeBinding)
                    {
                        return new([], 0, "management.configuration.rule_engine_binding_immutable");
                    }
                    existing = true;
                    bindingRecorded = !reader.IsDBNull(6);
                    key = reader.GetGuid(0);
                    editRevision = reader.GetInt64(4);
                    latest = reader.IsDBNull(5) ? null : reader.GetGuid(5);
                    if (reader.GetString(3) != definition.SchemaRevision)
                    {
                        if (editRevision == long.MaxValue)
                        {
                            return new([], 0, "management.configuration.revision_exhausted");
                        }
                        editRevision++;
                    }
                    if (await reader.ReadAsync(token))
                    {
                        return new([], 0, "management.configuration.rule_engine_binding_immutable");
                    }
                }
            }

            await using (DbCommand compatible = ManagementSql.Command(connection, transaction, $"""
                SELECT NOT EXISTS(SELECT 1 FROM {Table("configuration_source")} WHERE definition_id=@definition
                    AND environment=@environment AND application_id IS NULL)
                   AND (@recorded OR NOT EXISTS(SELECT 1 FROM {Table("configuration_assignment")} WHERE binding_id=@binding
                    AND generation IS NOT NULL))
                """, ("definition", nativeDefinition), ("environment", registration.Environment), ("recorded", bindingRecorded),
                ("binding", nativeBinding)))
            {
                if (await compatible.ExecuteScalarAsync(token) is not true)
                {
                    return new([], 0, "management.configuration.rule_engine_binding_incompatible");
                }
            }

            await using DbCommand read = ManagementSql.Command(connection, transaction, $"""
                SELECT definition FROM {Table("rule_engine_schema")} WHERE ruleset_key=@key AND schema_revision=@revision
                """, ("key", key), ("revision", definition.SchemaRevision));
            byte[]? retained = (byte[]?)await read.ExecuteScalarAsync(token);
            if (retained is not null && !schema.AsSpan().SequenceEqual(retained))
            {
                return new([], 0, "management.configuration.rule_engine_schema_immutable");
            }
            string display = definition.DisplayName ?? (ManagementWire.IsScalarText(definition.RulesetId, 512) ? definition.RulesetId : "Ruleset");
            if (!existing)
            {
                additionalBytes = checked(additionalBytes + 128 + Encoding.UTF8.GetByteCount(definition.RulesetId)
                    + Encoding.UTF8.GetByteCount(registration.ConfigurationId));
            }
            if (retained is null)
            {
                additionalBytes = checked(additionalBytes + schema.LongLength + 64);
            }
            entries.Add(new(new(key, app, registration.Environment, definition.RulesetId, registration.ConfigurationId,
                definition.SchemaRevision, display, editRevision, latest, nativeBinding), schema, retained is null));
        }

        return new(entries, additionalBytes);
    }

    private async Task ApplyRuleEngineCatalogAsync(DbConnection connection, DbTransaction transaction,
        IReadOnlyList<RuleEngineCatalogEntry> entries, CancellationToken token)
    {
        await using (DbCommand disable = ManagementSql.Command(connection, transaction,
            $"UPDATE {Table("rule_engine_ruleset")} SET enabled=false WHERE enabled"))
        {
            await disable.ExecuteNonQueryAsync(token);
        }
        foreach (RuleEngineCatalogEntry entry in entries)
        {
            RuleEngineResource resource = entry.Resource;
            await using DbCommand upsert = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("rule_engine_ruleset")}(ruleset_key,application_id,ruleset_id,configuration_id,active_schema_revision,
                    display_name,enabled,edit_revision,configuration_binding_id) VALUES(@key,@app,@ruleset,@config,@revision,@display,true,@edit,@native)
                ON CONFLICT(ruleset_key) DO UPDATE SET active_schema_revision=EXCLUDED.active_schema_revision,
                    display_name=EXCLUDED.display_name,enabled=true,edit_revision=EXCLUDED.edit_revision,
                    configuration_binding_id=EXCLUDED.configuration_binding_id
                """, ("key", resource.Key), ("app", resource.Application), ("ruleset", resource.RulesetId),
                ("config", resource.ConfigurationId), ("revision", resource.SchemaRevision), ("display", resource.DisplayName),
                ("edit", resource.EditRevision), ("native", resource.ConfigurationBinding!.Value));
            await upsert.ExecuteNonQueryAsync(token);
            if (entry.NewSchema)
            {
                await using DbCommand insert = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_schema")}(ruleset_key,schema_revision,definition) VALUES(@key,@revision,@definition)
                    """, ("key", resource.Key), ("revision", resource.SchemaRevision), ("definition", entry.Schema));
                await insert.ExecuteNonQueryAsync(token);
            }
        }
    }

    private async Task<bool> IsRuleEngineConfigurationAsync(DbConnection connection, DbTransaction transaction,
        string? application, string environment, string configuration, CancellationToken token)
    {
        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            SELECT EXISTS(SELECT 1 FROM {Table("rule_engine_ruleset")} r
                JOIN {Table("management_application")} a ON a.application_id=r.application_id
                WHERE r.configuration_id=@config AND a.environment=@environment
                  AND (CAST(@application AS uuid) IS NULL OR a.application_id=CAST(@application AS uuid)))
            """, ("config", configuration), ("environment", environment), ("application", application));
        return await command.ExecuteScalarAsync(token) is true;
    }

    internal Task<ManagementStoreResult<RuleEngineResource>> ReadRuleEngineResourceAsync(Guid application, string ruleset, CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            RuleEngineResource? resource = await LoadRuleEngineResourceAsync(connection, transaction, application, ruleset, false, sql);
            return resource is null ? Candidate<RuleEngineResource>.Refused("management.rule-engine.not_found")
                : Candidate<RuleEngineResource>.Existing(resource);
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementRuleSetSummary>>> ListRuleEngineResourcesAsync(Guid application,
        string? after, int take, CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => Candidate<IReadOnlyList<ManagementRuleSetSummary>>.Existing(
            await context.UseAdoNetAsync(async (connection, transaction, sql) =>
            {
                await using DbCommand query = ManagementSql.Command(connection, transaction, RuleEngineResourceSelect + """
                    WHERE r.application_id=@application AND r.enabled AND r.ruleset_id>@after ORDER BY r.ruleset_id LIMIT @take
                    """, ("application", application), ("after", after ?? string.Empty), ("take", take));
                await using DbDataReader reader = await query.ExecuteReaderAsync(sql);
                var items = new List<ManagementRuleSetSummary>();
                while (await reader.ReadAsync(sql))
                {
                    RuleEngineResource resource = ReadRuleEngineResource(reader);
                    items.Add(new(resource.Application.ToString("D"), resource.Environment, resource.RulesetId, resource.DisplayName,
                        resource.SchemaRevision, RuleEngineEditETag(resource.EditRevision)));
                }
                return (IReadOnlyList<ManagementRuleSetSummary>)items.AsReadOnly();
            }, cancellation)), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<ManagementRuleSchema>> ReadRuleEngineSchemaAsync(RuleEngineResource resource, string? revision,
        CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            string selected = revision ?? resource.SchemaRevision;
            byte[]? definition = await RuleEngineSchemaBytesAsync(connection, transaction, resource.Key, selected, sql);
            return definition is null ? Candidate<ManagementRuleSchema>.Refused("management.rule-engine.not_found")
                : Candidate<ManagementRuleSchema>.Existing(new(resource.Application.ToString("D"), resource.Environment,
                    resource.RulesetId, selected, Encoding.UTF8.GetString(definition)));
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementRuleVersionSummary>>> ListRuleEngineVersionsAsync(RuleEngineResource resource,
        Guid? after, int take, CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => Candidate<IReadOnlyList<ManagementRuleVersionSummary>>.Existing(
            await context.UseAdoNetAsync(async (connection, transaction, sql) =>
            {
                await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
                    SELECT version_id,schema_revision,saved_at,version_note FROM {Table("rule_engine_version")}
                    WHERE ruleset_key=@key AND (CAST(@after AS uuid) IS NULL OR edit_revision <
                        (SELECT edit_revision FROM {Table("rule_engine_version")} WHERE ruleset_key=@key AND version_id=CAST(@after AS uuid)))
                    ORDER BY edit_revision DESC LIMIT @take
                    """, ("key", resource.Key), ("after", after?.ToString("D")), ("take", take));
                await using DbDataReader reader = await query.ExecuteReaderAsync(sql);
                var items = new List<ManagementRuleVersionSummary>();
                while (await reader.ReadAsync(sql))
                {
                    items.Add(new(resource.Application.ToString("D"), resource.Environment, resource.RulesetId,
                        reader.GetGuid(0).ToString("D"), reader.GetString(1), ManagementSql.Timestamp(reader, 2))
                    {
                        VersionNote = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
                }
                return (IReadOnlyList<ManagementRuleVersionSummary>)items.AsReadOnly();
            }, cancellation)), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<ManagementRuleVersion>> ReadRuleEngineVersionAsync(RuleEngineResource resource, Guid version,
        CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sql) =>
        {
            await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
                SELECT schema_revision,definition,saved_at,version_note
                FROM {Table("rule_engine_version")} WHERE ruleset_key=@key AND version_id=@version
                """, ("key", resource.Key), ("version", version));
            await using DbDataReader reader = await query.ExecuteReaderAsync(sql);
            return await reader.ReadAsync(sql)
                ? Candidate<ManagementRuleVersion>.Existing(new(resource.Application.ToString("D"), resource.Environment,
                    resource.RulesetId, version.ToString("D"), reader.GetString(0), ManagementSql.Timestamp(reader, 2),
                    Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(1)))
                    {
                        VersionNote = reader.IsDBNull(3) ? null : reader.GetString(3)
                    })
                : Candidate<ManagementRuleVersion>.Refused("management.rule-engine.not_found");
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    internal Task<RuleEngineSaveEffect> SaveRuleEngineVersionAsync(ILocalTransactionContext context, RuleEngineResource target,
        string editETag, ManagementSaveRuleVersionCommand command, CancellationToken token)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync<RuleEngineSaveEffect>(async (connection, transaction, sql) =>
        {
            (long used, long quota) = await LockConfigurationQuotaAsync(connection, transaction, sql);
            RuleEngineResource? current = await LoadRuleEngineResourceAsync(connection, transaction, target.Application, target.RulesetId, true, sql);
            if (current is null || current.Key != target.Key)
            {
                return new(null, "management.rule-engine.not_found");
            }

            string previous = RuleEngineEditETag(current.EditRevision);
            if (editETag != previous)
            {
                return new(null, "management.rule-engine.precondition_failed", previous);
            }
            if (current.EditRevision == long.MaxValue)
            {
                return new(null, "management.rule-engine.revision_exhausted", previous);
            }
            if (command.SchemaRevision != current.SchemaRevision)
            {
                return new(null, "management.rule-engine.schema_changed", previous);
            }

            byte[]? trusted = await RuleEngineSchemaBytesAsync(connection, transaction, current.Key, current.SchemaRevision, sql);
            if (trusted is null)
            {
                throw new ManagementSchemaException("The active Rule Engine schema is absent.");
            }
            RuleDefinition schema = RuleDefinitionCodec.Decode(trusted);
            RuleDefinition draft = RuleDefinitionCodec.Decode(Encoding.UTF8.GetBytes(command.DefinitionJson));
            if (draft.RulesetId != current.RulesetId || draft.SchemaRevision != current.SchemaRevision
                || !RuleDefinitionCodec.Encode(new(draft.RulesetId, draft.SchemaRevision, schema.Version, schema.Mode, draft.Columns,
                    [], schema.DisplayName, draft.MetadataShape)).AsSpan().SequenceEqual(trusted))
            {
                return new(null, "management.rule-engine.schema_mismatch", previous);
            }

            Guid? baseId = current.LatestVersion;
            if (command.BaseVersion is { } requestedBase)
            {
                if (!ManagementWire.IsUuid(requestedBase))
                {
                    return new(null, "management.rule-engine.base_version_not_found", previous);
                }
                baseId = Guid.Parse(requestedBase);
            }
            var inherited = new HashSet<string>(StringComparer.Ordinal);
            if (baseId is { } baseVersion)
            {
                await using DbCommand basis = ManagementSql.Command(connection, transaction, $"""
                    SELECT definition FROM {Table("rule_engine_version")} WHERE ruleset_key=@key AND version_id=@version
                    """, ("key", current.Key), ("version", baseVersion));
                if (await basis.ExecuteScalarAsync(sql) is not byte[] baseline)
                {
                    return new(null, "management.rule-engine.base_version_not_found", previous);
                }
                inherited.UnionWith(RuleDefinitionCodec.Decode(baseline).Rows.Select(row => row.RuleId));
            }

            string[] introduced = draft.Rows.Where(row => !inherited.Contains(row.RuleId)).Select(row => row.RuleId).ToArray();
            foreach (string identity in introduced)
            {
                await using DbCommand known = ManagementSql.Command(connection, transaction, $"""
                    SELECT EXISTS(SELECT 1 FROM {Table("rule_engine_identity")} WHERE ruleset_key=@key AND rule_id=@rule)
                    """, ("key", current.Key), ("rule", identity));
                if (await known.ExecuteScalarAsync(sql) is true)
                {
                    return new(null, "management.rule-engine.rule_identity_reuse", previous);
                }
            }

            Guid version = Guid.NewGuid();
            byte[] encoded;
            try
            {
                encoded = RuleDefinitionCodec.Encode(new(current.RulesetId, current.SchemaRevision, version.ToString("D"), draft.Mode,
                    schema.Columns, draft.Rows, current.DisplayName, schema.MetadataShape));
            }
            catch (ArgumentException)
            {
                return new(null, "management.rule-engine.definition_limit", previous);
            }
            long bytes = encoded.LongLength + 128 + Encoding.UTF8.GetByteCount(command.Reason)
                + introduced.Sum(identity => 48L + Encoding.UTF8.GetByteCount(identity));
            if (bytes > quota - used)
            {
                return new(null, "management.rule-engine.capacity_exceeded", previous);
            }

            string savedAt;
            await using (DbCommand insert = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("rule_engine_version")}(ruleset_key,version_id,schema_revision,edit_revision,definition,saved_at,version_note)
                    VALUES(@key,@version,@schema,@edit,@definition,clock_timestamp(),@note) RETURNING saved_at
                """, ("key", current.Key), ("version", version), ("schema", current.SchemaRevision),
                ("edit", current.EditRevision + 1), ("definition", encoded), ("note", command.Reason)))
            {
                savedAt = ManagementSql.Timestamp((DateTime)(await insert.ExecuteScalarAsync(sql))!);
            }
            foreach (string identity in introduced)
            {
                await using DbCommand add = ManagementSql.Command(connection, transaction, $"""
                    INSERT INTO {Table("rule_engine_identity")}(ruleset_key,rule_id,counter_visible_from_revision)
                    VALUES(@key,@rule,@revision)
                    """, ("key", current.Key), ("rule", identity), ("revision", current.EditRevision + 1));
                await add.ExecuteNonQueryAsync(sql);
            }
            await using DbCommand charge = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("rule_engine_ruleset")} SET edit_revision=edit_revision+1,latest_version=@version WHERE ruleset_key=@key;
                UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@bytes WHERE singleton=true
                """, ("key", current.Key), ("version", version), ("bytes", bytes));
            await charge.ExecuteNonQueryAsync(sql);
            return new(new(version.ToString("D"), current.SchemaRevision, RuleEngineEditETag(current.EditRevision + 1), savedAt),
                PreviousETag: previous, Digest: Convert.ToHexStringLower(SHA256.HashData(encoded)));
        }, token);
    }

    private string RuleEngineResourceSelect => $"""
        SELECT r.ruleset_key,r.application_id,a.environment,r.ruleset_id,r.configuration_id,
            r.active_schema_revision,r.display_name,r.edit_revision,r.latest_version,r.configuration_binding_id,a.application
        FROM {Table("rule_engine_ruleset")} r JOIN {Table("management_application")} a ON a.application_id=r.application_id
        """ + " ";

    private static RuleEngineResource ReadRuleEngineResource(DbDataReader reader)
        => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), reader.GetInt64(7), reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetString(10));

    private async Task<RuleEngineResource?> LoadRuleEngineResourceAsync(DbConnection connection, DbTransaction transaction,
        Guid application, string ruleset, bool write, CancellationToken token)
    {
        await using DbCommand query = ManagementSql.Command(connection, transaction, RuleEngineResourceSelect
            + "WHERE r.application_id=@application AND r.ruleset_id=@ruleset AND r.enabled" + (write ? " FOR UPDATE OF r" : ""),
            ("application", application), ("ruleset", ruleset));
        await using DbDataReader reader = await query.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadRuleEngineResource(reader) : null;
    }

    private async Task<byte[]?> RuleEngineSchemaBytesAsync(DbConnection connection, DbTransaction transaction,
        Guid key, string revision, CancellationToken token)
    {
        await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
            SELECT definition FROM {Table("rule_engine_schema")} WHERE ruleset_key=@key AND schema_revision=@revision
            """, ("key", key), ("revision", revision));
        return (byte[]?)await query.ExecuteScalarAsync(token);
    }

    private static string RuleEngineEditETag(long revision)
        => "\"rule-engine.edit." + revision.ToString(CultureInfo.InvariantCulture) + "\"";
}
