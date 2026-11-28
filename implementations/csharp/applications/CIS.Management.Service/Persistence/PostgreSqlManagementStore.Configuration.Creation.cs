using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationCreationContext>> ReadConfigurationCreationContextAsync(string applicationId, CancellationToken token)
        => RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT a.application,a.environment,c.revision FROM {Table("management_application")} a
                CROSS JOIN {Table("configuration_catalog")} c WHERE a.application_id=@id AND c.singleton=true
                """, ("id", ManagementSql.Uuid(applicationId, nameof(applicationId))));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            if (!await reader.ReadAsync(sqlToken))
            {
                return Candidate<ManagementConfigurationCreationContext>.Refused("management.configuration.not_found");
            }
            return reader.GetString(0) == "CommonConfiguration"
                ? Candidate<ManagementConfigurationCreationContext>.Refused("management.configuration.host_reserved")
                : Candidate<ManagementConfigurationCreationContext>.Existing(new(applicationId, reader.GetString(0), reader.GetString(1),
                    CreationETag(applicationId, reader.GetInt64(2)), "1", reader.GetString(0)));
        }, cancellation), token, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<string>> ReadNativeGenericApplicationAsync(string resourceType, string resourceId, CancellationToken token)
    {
        string from = resourceType switch
        {
            ManagementConfigurationCapabilities.SourceResource => $"FROM {Table("configuration_source")} s JOIN {Table("configuration_binding")} b ON b.binding_id=s.binding_id AND b.application_id=s.application_id WHERE s.source_id=@id",
            ManagementConfigurationCapabilities.BindingResource => $"FROM {Table("configuration_binding")} b WHERE b.binding_id=@id",
            ManagementConfigurationCapabilities.ReleaseResource => $"FROM {Table("configuration_release")} r JOIN {Table("configuration_binding")} b ON b.binding_id=r.binding_id WHERE r.release_id=@id",
            _ => throw new ArgumentException("Select a concrete Configuration source, binding or release.", nameof(resourceType))
        };
        return RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT b.application_id,d.definition_body,d.definition_digest FROM (
                    SELECT b.application_id,b.definition_id,b.predeployment_created {from}
                ) b JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id
                WHERE b.predeployment_created
                """, ("id", ManagementSql.Uuid(resourceId, nameof(resourceId))));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            if (await reader.ReadAsync(sqlToken))
            {
                var definition = JsonSerializer.Deserialize<ManagementConfigurationSchema>(reader.GetFieldValue<byte[]>(1), ManagementConfigurationOptions.Json)
                    ?? throw new ManagementSchemaException("The retained generic definition is invalid.");
                if (ManagementPredeploymentConfiguration.Matches(definition, reader.GetFieldValue<byte[]>(2)))
                {
                    return Candidate<string>.Existing(reader.GetGuid(0).ToString("D"));
                }
            }
            return Candidate<string>.Refused("management.configuration.not_found");
        }, cancellation), token, requireReady: true, requireCleanRead: true);
    }

    internal Task<ConfigurationEffect> CreateConfigurationAsync(ILocalTransactionContext context, ManagementCreationInput input, CancellationToken token)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            _ = await LockCapacityAsync(connection, transaction, sqlToken);
            long revision;
            long used;
            long quota;
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction,
                $"SELECT revision,used_bytes,quota_bytes FROM {Table("configuration_catalog")} WHERE singleton=true AND manifest_digest IS NOT NULL FOR UPDATE"))
            await using (DbDataReader reader = await catalog.ExecuteReaderAsync(sqlToken))
            {
                if (!await reader.ReadAsync(sqlToken))
                {
                    throw new ManagementSchemaException("Apply the selected trusted catalog before preparing Configuration.");
                }
                revision = reader.GetInt64(0);
                used = reader.GetInt64(1);
                quota = reader.GetInt64(2);
            }
            if (input.IfMatch != CreationETag(input.Application.ApplicationId, revision))
            {
                return ConfigurationEffect.Reject("management.configuration.precondition_failed");
            }
            if (revision == long.MaxValue)
            {
                return ConfigurationEffect.Reject("management.configuration.revision_exhausted");
            }
            await using (DbCommand actual = ManagementSql.Command(connection, transaction,
                $"SELECT application,environment FROM {Table("management_application")} WHERE application_id=@id FOR SHARE",
                ("id", Guid.Parse(input.Application.ApplicationId))))
            await using (DbDataReader reader = await actual.ExecuteReaderAsync(sqlToken))
            {
                if (!await reader.ReadAsync(sqlToken) || reader.GetString(0) != input.Application.Application || reader.GetString(1) != input.Application.Environment)
                {
                    return ConfigurationEffect.Reject("management.configuration.not_found");
                }
            }
            if (await IsRuleEngineConfigurationAsync(connection, transaction, input.Application.ApplicationId,
                input.Application.Environment, input.Command.ConfigurationId, sqlToken))
            {
                return ConfigurationEffect.Reject("management.configuration.rule_engine_managed");
            }
            string host = input.Application.Application;
            if (host == "CommonConfiguration")
            {
                return ConfigurationEffect.Reject("management.configuration.host_reserved");
            }
            ManagementConfigurationSchema definition = ManagementPredeploymentConfiguration.Definition(input.Command.ConfigurationId);
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(definition, ManagementConfigurationOptions.Json);
            byte[] digest = ManagementPredeploymentConfiguration.Digest(definition);
            Guid definitionId = Guid.NewGuid();
            bool existing = false;
            await using (DbCommand find = ManagementSql.Command(connection, transaction,
                $"SELECT definition_id,definition_digest,enabled,definition_body FROM {Table("configuration_definition")} WHERE configuration_id=@configuration AND schema_version=1",
                ("configuration", definition.ConfigurationId)))
            await using (DbDataReader reader = await find.ExecuteReaderAsync(sqlToken))
            {
                if (await reader.ReadAsync(sqlToken))
                {
                    existing = true;
                    definitionId = reader.GetGuid(0);
                    if (input.Command.Intent != "reuseGeneric")
                    {
                        return ConfigurationEffect.Reject("management.configuration.definition_exists");
                    }
                    ManagementConfigurationSchema? retained = JsonSerializer.Deserialize<ManagementConfigurationSchema>(reader.GetFieldValue<byte[]>(3), ManagementConfigurationOptions.Json);
                    if (!reader.GetBoolean(2) || retained is null || !ManagementPredeploymentConfiguration.Matches(retained, reader.GetFieldValue<byte[]>(1))
                        || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(digest, reader.GetFieldValue<byte[]>(1)))
                    {
                        return ConfigurationEffect.Reject("management.configuration.schema_immutable");
                    }
                }
            }
            if (!existing && input.Command.Intent == "reuseGeneric")
            {
                return ConfigurationEffect.Reject("management.configuration.definition_not_found");
            }
            await using (DbCommand counts = ManagementSql.Command(connection, transaction, $"""
                SELECT (SELECT count(*) FROM {Table("configuration_definition")}),
                    (SELECT count(*) FROM {Table("configuration_binding")} WHERE application_id=@app),
                    EXISTS(SELECT 1 FROM {Table("configuration_binding")} WHERE application_id=@app AND definition_id=@definition)
                """, ("app", Guid.Parse(input.Application.ApplicationId)), ("definition", definitionId)))
            await using (DbDataReader reader = await counts.ExecuteReaderAsync(sqlToken))
            {
                _ = await reader.ReadAsync(sqlToken);
                if (reader.GetBoolean(2))
                {
                    return ConfigurationEffect.Reject("management.configuration.binding_exists");
                }
                if (!existing && reader.GetInt64(0) >= 4096 || reader.GetInt64(1) >= 256)
                {
                    return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
                }
            }
            long bindingBytes = 128 + Encoding.UTF8.GetByteCount(host);
            long sourceBytes = 128 + Encoding.UTF8.GetByteCount(input.Application.Environment);
            long delta = bindingBytes + sourceBytes + 128 + (existing ? 0 : body.LongLength + 64);
            if (delta > quota - used)
            {
                return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
            }
            Guid bindingId = Guid.NewGuid();
            Guid sourceId = Guid.NewGuid();
            if (!existing)
            {
                await Execute($"INSERT INTO {Table("configuration_definition")}(definition_id,configuration_id,schema_version,definition_body,definition_digest,logical_bytes,manifest_managed) VALUES(@id,@name,1,@body,@digest,@bytes,false)",
                    ("id", definitionId), ("name", definition.ConfigurationId), ("body", body), ("digest", digest), ("bytes", body.LongLength + 64));
            }
            await Execute($"INSERT INTO {Table("configuration_binding")}(binding_id,application_id,definition_id,host_scope,logical_bytes,manifest_managed,predeployment_created) VALUES(@id,@app,@definition,@host,@bytes,false,true)",
                ("id", bindingId), ("app", Guid.Parse(input.Application.ApplicationId)), ("definition", definitionId), ("host", host), ("bytes", bindingBytes));
            await Execute($"INSERT INTO {Table("configuration_source")}(source_id,definition_id,environment,application_id,binding_id,logical_bytes) VALUES(@id,@definition,@environment,@app,@binding,@bytes)",
                ("id", sourceId), ("definition", definitionId), ("environment", input.Application.Environment), ("app", Guid.Parse(input.Application.ApplicationId)), ("binding", bindingId), ("bytes", sourceBytes));
            await Execute($"INSERT INTO {Table("configuration_assignment")}(binding_id,logical_bytes) VALUES(@binding,128)", ("binding", bindingId));
            string decided;
            await using (DbCommand update = ManagementSql.Command(connection, transaction,
                $"UPDATE {Table("configuration_catalog")} SET revision=revision+1,used_bytes=used_bytes+@delta WHERE singleton=true RETURNING clock_timestamp()", ("delta", delta)))
            {
                decided = ManagementSql.Timestamp((DateTime)(await update.ExecuteScalarAsync(sqlToken))!);
            }
            return new ConfigurationEffect(new(1, "configurationCreated", "applied", "management.configuration.created", decided,
                SourceId: sourceId.ToString("D"), ApplicationId: input.Application.ApplicationId, ConfigurationId: definition.ConfigurationId, SchemaVersion: "1",
                ETag: CreationETag(input.Application.ApplicationId, revision + 1))
            {
                BindingId = bindingId.ToString("D"),
                HostApplicationName = host,
                SourceETag = ConfigurationETag("source", 1)
            });

            async Task Execute(string sql, params (string Name, object? Value)[] parameters)
            {
                await using DbCommand command = ManagementSql.Command(connection, transaction, sql, parameters);
                await command.ExecuteNonQueryAsync(sqlToken);
            }
        }, token);
    }

    internal static string CreationETag(string applicationId, long revision)
        => "\"configuration.creation." + applicationId + "." + revision.ToString(CultureInfo.InvariantCulture) + "\"";
}
