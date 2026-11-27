using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Management.Service.Configuration;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

internal sealed record ConfigurationEffect(ManagementConfigurationDecision? Decision, string? RejectionCode = null,
    string? PreviousETag = null, ManagementConfigurationAssignmentMap? BeforeAssignment = null)
{
    internal static ConfigurationEffect Reject(string code, string? previousETag = null, ManagementConfigurationAssignmentMap? before = null)
        => new(null, code, previousETag, before);
}

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ConfigurationEffect> ApplyConfigurationCatalogAsync(ILocalTransactionContext context, string catalogId,
        string ifMatch, string requestedDigest, ManagementConfigurationOptions selected, CancellationToken cancellationToken)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            // Registration also takes capacity first. All configuration mutations then take the catalog row.
            Capacity capacity = await LockCapacityAsync(connection, transaction, token);
            long revision;
            long used;
            long previousMetadata;
            string? appliedDigest;
            await using (DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT catalog_id,revision,used_bytes,metadata_bytes,manifest_digest FROM {Table("configuration_catalog")} WHERE singleton=true FOR UPDATE
                """))
            await using (DbDataReader reader = await command.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token) || reader.GetGuid(0).ToString("D") != catalogId)
                {
                    throw new ManagementSchemaException("Resolve the actual stable configuration catalog.");
                }
                revision = reader.GetInt64(1);
                used = reader.GetInt64(2);
                previousMetadata = reader.GetInt64(3);
                appliedDigest = reader.IsDBNull(4) ? null : reader.GetString(4);
            }
            // This check is deliberately inside the effect, after RM-03 replay arbitration.
            if (requestedDigest != selected.ManifestDigest)
            {
                return ConfigurationEffect.Reject("management.configuration.manifest_unavailable");
            }
            if (ifMatch != ConfigurationETag("catalog", revision))
            {
                return ConfigurationEffect.Reject("management.configuration.precondition_failed");
            }
            if (revision == long.MaxValue)
            {
                return ConfigurationEffect.Reject("management.configuration.revision_exhausted");
            }
            if (appliedDigest == requestedDigest)
            {
                await using DbCommand replayTimestamp = ManagementSql.Command(connection, transaction, "SELECT clock_timestamp()");
                return new ConfigurationEffect(new(1, "catalogApplied", "applied", "management.configuration.catalog_applied",
                    ManagementSql.Timestamp((DateTime)(await replayTimestamp.ExecuteScalarAsync(token))!), CatalogId: catalogId,
                    ManifestDigest: requestedDigest, ETag: ConfigurationETag("catalog", revision)));
            }
            foreach (string pin in selected.PinnedReleaseIds)
            {
                await using DbCommand retained = ManagementSql.Command(connection, transaction,
                    $"SELECT EXISTS(SELECT 1 FROM {Table("configuration_release")} WHERE release_id=@id)", ("id", Guid.Parse(pin)));
                if (await retained.ExecuteScalarAsync(token) is not true)
                {
                    return ConfigurationEffect.Reject("management.configuration.pin_not_found");
                }
            }

            var definitions = new Dictionary<(string, string), Guid>();
            var addedDefinitions = new List<(Guid Id, byte[] Body, byte[] Digest, long Bytes)>();
            long delta = 0;
            foreach (ManagementConfigurationSchema definition in selected.Manifest.Definitions)
            {
                byte[] body = JsonSerializer.SerializeToUtf8Bytes(definition, ManagementConfigurationOptions.Json);
                byte[] digest = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition with
                {
                    Description = null
                }, ManagementConfigurationOptions.Json));
                await using DbCommand find = ManagementSql.Command(connection, transaction, $"""
                    SELECT definition_id,definition_digest,logical_bytes,definition_body FROM {Table("configuration_definition")}
                    WHERE configuration_id=@configuration AND schema_version=@version
                    """, ("configuration", definition.ConfigurationId), ("version", long.Parse(definition.SchemaVersion, CultureInfo.InvariantCulture)));
                await using DbDataReader reader = await find.ExecuteReaderAsync(token);
                Guid id;
                if (await reader.ReadAsync(token))
                {
                    id = reader.GetGuid(0);
                    if (!CryptographicOperations.FixedTimeEquals(reader.GetFieldValue<byte[]>(1), digest))
                    {
                        return ConfigurationEffect.Reject("management.configuration.schema_immutable");
                    }
                    if (!body.AsSpan().SequenceEqual(reader.GetFieldValue<byte[]>(3)))
                    {
                        long bytes = body.LongLength + 64;
                        addedDefinitions.Add((id, body, digest, bytes));
                        delta = checked(delta + bytes - reader.GetInt64(2));
                    }
                }
                else
                {
                    id = Guid.NewGuid();
                    long bytes = body.LongLength + 64;
                    addedDefinitions.Add((id, body, digest, bytes));
                    delta = checked(delta + bytes);
                }
                definitions.Add((definition.ConfigurationId, definition.SchemaVersion), id);
            }

            var applications = new Dictionary<(string, string), Guid>();
            var addedApplications = new List<(Guid Id, string Application, string Environment)>();
            foreach (var binding in selected.Manifest.Bindings.Select(item => (item.Application, item.Environment)).Distinct())
            {
                await using DbCommand find = ManagementSql.Command(connection, transaction,
                    $"SELECT application_id FROM {Table("management_application")} WHERE application=@application AND environment=@environment",
                    ("application", binding.Application), ("environment", binding.Environment));
                object? found = await find.ExecuteScalarAsync(token);
                Guid id = found is Guid existing ? existing : Guid.NewGuid();
                applications.Add(binding, id);
                if (found is not Guid)
                {
                    addedApplications.Add((id, binding.Application, binding.Environment));
                }
            }
            if (addedApplications.Count > capacity.MaximumApplications - capacity.Applications)
            {
                return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
            }

            var bindings = new List<(Guid Id, Guid Application, Guid Definition, string Host, long Bytes, bool New)>();
            foreach (ManagementConfigurationBinding binding in selected.Manifest.Bindings)
            {
                Guid app = applications[(binding.Application, binding.Environment)];
                Guid definition = definitions[(binding.ConfigurationId, binding.SchemaVersion)];
                await using DbCommand find = ManagementSql.Command(connection, transaction,
                    $"SELECT binding_id,logical_bytes FROM {Table("configuration_binding")} WHERE application_id=@application AND definition_id=@definition",
                    ("application", app), ("definition", definition));
                await using DbDataReader reader = await find.ExecuteReaderAsync(token);
                bool exists = await reader.ReadAsync(token);
                Guid id = exists ? reader.GetGuid(0) : Guid.NewGuid();
                long bytes = 128 + Encoding.UTF8.GetByteCount(binding.HostApplicationName);
                delta = checked(delta + bytes - (exists ? reader.GetInt64(1) : 0) + (exists ? 0 : 128));
                bindings.Add((id, app, definition, binding.HostApplicationName, bytes, !exists));
            }
            await using (DbCommand preserved = ManagementSql.Command(connection, transaction, $"""
                SELECT count(*) FROM {Table("configuration_definition")} d WHERE d.enabled AND NOT(d.definition_id=ANY(string_to_array(@selected,',')::uuid[]))
                    AND (NOT d.manifest_managed OR EXISTS(SELECT 1 FROM {Table("configuration_binding")} b
                        WHERE b.definition_id=d.definition_id AND NOT b.manifest_managed))
                """, ("selected", string.Join(',', definitions.Values.Select(value => value.ToString("D"))))))
            {
                if ((long)(await preserved.ExecuteScalarAsync(token))! + definitions.Count > 4096)
                {
                    return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
                }
            }
            foreach (var application in bindings.GroupBy(item => item.Application))
            {
                await using DbCommand preserved = ManagementSql.Command(connection, transaction, $"""
                    SELECT count(*) FROM {Table("configuration_binding")} WHERE application_id=@app AND NOT manifest_managed
                        AND NOT(definition_id=ANY(string_to_array(@selected,',')::uuid[]))
                    """, ("app", application.Key), ("selected", string.Join(',', application.Select(item => item.Definition.ToString("D")))));
                if ((long)(await preserved.ExecuteScalarAsync(token))! + application.Count() > 256)
                {
                    return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
                }
            }
            var sources = new List<(Guid Id, Guid Definition, string Environment, Guid? Application, Guid? Binding, long Bytes, bool New)>();
            foreach (ManagementConfigurationCommonSource source in selected.Manifest.CommonSources)
            {
                await AddSource(definitions[(source.ConfigurationId, source.SchemaVersion)], source.Environment, null, null);
            }
            foreach (ManagementConfigurationBinding binding in selected.Manifest.Bindings)
            {
                Guid app = applications[(binding.Application, binding.Environment)];
                Guid definition = definitions[(binding.ConfigurationId, binding.SchemaVersion)];
                await AddSource(definition, binding.Environment, app, bindings.Single(item => item.Application == app && item.Definition == definition).Id);
            }

            long previousCohorts;
            await using (DbCommand count = ManagementSql.Command(connection, transaction,
                $"SELECT COALESCE(sum(logical_bytes),0)::bigint FROM {Table("configuration_cohort")}"))
            {
                previousCohorts = (long)(await count.ExecuteScalarAsync(token))!;
            }
            var cohorts = selected.Manifest.Cohorts.Select(item => (Value: item, Application: applications[(item.Application, item.Environment)],
                Bytes: 64L + Encoding.UTF8.GetByteCount(item.Issuer) + Encoding.UTF8.GetByteCount(item.Subject) + Encoding.UTF8.GetByteCount(item.CohortId))).ToArray();
            byte[] pins = JsonSerializer.SerializeToUtf8Bytes(selected.PinnedReleaseIds, ManagementConfigurationOptions.Json);
            long metadata = 160 + pins.LongLength;
            delta = checked(delta + cohorts.Sum(item => item.Bytes) - previousCohorts + metadata - previousMetadata);
            if (delta > selected.QuotaBytes - used)
            {
                return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
            }

            // All immutable/precondition/capacity checks precede any catalog effect.
            await Execute($"""
                UPDATE {Table("configuration_definition")} d SET enabled=false WHERE d.manifest_managed
                    AND NOT EXISTS(SELECT 1 FROM {Table("configuration_binding")} b WHERE b.definition_id=d.definition_id AND NOT b.manifest_managed)
                """);
            foreach (var definition in addedDefinitions)
            {
                ManagementConfigurationSchema value = JsonSerializer.Deserialize<ManagementConfigurationSchema>(definition.Body, ManagementConfigurationOptions.Json)!;
                await Execute($"""
                    INSERT INTO {Table("configuration_definition")}(definition_id,configuration_id,schema_version,definition_body,definition_digest,logical_bytes)
                    VALUES(@id,@configuration,@version,@body,@digest,@bytes) ON CONFLICT(definition_id)
                    DO UPDATE SET definition_body=EXCLUDED.definition_body,logical_bytes=EXCLUDED.logical_bytes
                    """, ("id", definition.Id), ("configuration", value.ConfigurationId),
                    ("version", long.Parse(value.SchemaVersion, CultureInfo.InvariantCulture)), ("body", definition.Body), ("digest", definition.Digest), ("bytes", definition.Bytes));
            }
            foreach (var app in addedApplications)
            {
                await Execute($"INSERT INTO {Table("management_application")}(application_id,application,environment,display_name) VALUES(@id,@application,@environment,@application)",
                    ("id", app.Id), ("application", app.Application), ("environment", app.Environment));
            }
            foreach (Guid definition in definitions.Values)
            {
                await Execute($"UPDATE {Table("configuration_definition")} SET enabled=true,manifest_managed=true WHERE definition_id=@id", ("id", definition));
            }
            await Execute($"UPDATE {Table("management_capacity")} SET application_count=application_count+@count WHERE singleton=true", ("count", addedApplications.Count));
            await Execute($"""
                UPDATE {Table("configuration_binding")} SET enabled=false WHERE manifest_managed;
                UPDATE {Table("configuration_source")} s SET enabled=false WHERE s.binding_id IS NULL
                    OR EXISTS(SELECT 1 FROM {Table("configuration_binding")} b WHERE b.binding_id=s.binding_id AND b.manifest_managed)
                """);
            foreach (var binding in bindings)
            {
                await Execute($"""
                    INSERT INTO {Table("configuration_binding")}(binding_id,application_id,definition_id,host_scope,logical_bytes)
                    VALUES(@id,@application,@definition,@host,@bytes) ON CONFLICT(binding_id)
                    DO UPDATE SET host_scope=EXCLUDED.host_scope,logical_bytes=EXCLUDED.logical_bytes,enabled=true,manifest_managed=true
                    """, ("id", binding.Id), ("application", binding.Application), ("definition", binding.Definition), ("host", binding.Host), ("bytes", binding.Bytes));
                if (binding.New)
                {
                    await Execute($"INSERT INTO {Table("configuration_assignment")}(binding_id,logical_bytes) VALUES(@id,128)", ("id", binding.Id));
                }
            }
            foreach (var source in sources)
            {
                await Execute($"""
                    INSERT INTO {Table("configuration_source")}(source_id,definition_id,environment,application_id,binding_id,logical_bytes)
                    VALUES(@id,@definition,@environment,CAST(@application AS uuid),CAST(@binding AS uuid),@bytes) ON CONFLICT(source_id) DO UPDATE SET enabled=true
                    """, ("id", source.Id), ("definition", source.Definition), ("environment", source.Environment),
                    ("application", source.Application), ("binding", source.Binding), ("bytes", source.Bytes));
            }
            await Execute($"DELETE FROM {Table("configuration_cohort")}");
            await Execute($"UPDATE {Table("configuration_release")} SET pinned=false");
            foreach (string pin in selected.PinnedReleaseIds)
            {
                await Execute($"UPDATE {Table("configuration_release")} SET pinned=true WHERE release_id=@id", ("id", Guid.Parse(pin)));
            }
            foreach (var cohort in cohorts)
            {
                await Execute($"""
                    INSERT INTO {Table("configuration_cohort")}(application_id,issuer,subject,cohort_id,logical_bytes)
                    VALUES(@application,@issuer,@subject,@cohort,@bytes)
                    """, ("application", cohort.Application), ("issuer", cohort.Value.Issuer), ("subject", cohort.Value.Subject),
                    ("cohort", cohort.Value.CohortId), ("bytes", cohort.Bytes));
            }
            string timestamp;
            await using (DbCommand update = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("configuration_catalog")} SET revision=revision+1,manifest_digest=@digest,quota_bytes=@quota,
                    retained_releases=@retained,pins_body=@pins,metadata_bytes=@metadata,used_bytes=used_bytes+@delta
                WHERE singleton=true RETURNING clock_timestamp()
                """, ("digest", selected.ManifestDigest), ("quota", selected.QuotaBytes), ("retained", selected.RetainedUnreferencedReleases),
                ("pins", pins), ("metadata", metadata), ("delta", delta)))
            {
                timestamp = ManagementSql.Timestamp((DateTime)(await update.ExecuteScalarAsync(token))!);
            }
            return new ConfigurationEffect(new(1, "catalogApplied", "applied", "management.configuration.catalog_applied", timestamp,
                CatalogId: catalogId, ManifestDigest: selected.ManifestDigest, ETag: ConfigurationETag("catalog", revision + 1)));

            async Task AddSource(Guid definition, string environment, Guid? application, Guid? binding)
            {
                await using DbCommand find = ManagementSql.Command(connection, transaction, $"""
                    SELECT source_id FROM {Table("configuration_source")} WHERE definition_id=@definition AND environment=@environment
                        AND application_id IS NOT DISTINCT FROM CAST(@application AS uuid)
                    """, ("definition", definition), ("environment", environment), ("application", application));
                object? found = await find.ExecuteScalarAsync(token);
                bool exists = found is Guid;
                long bytes = 128 + Encoding.UTF8.GetByteCount(environment);
                sources.Add((found is Guid known ? known : Guid.NewGuid(), definition, environment, application, binding, bytes, !exists));
                delta = checked(delta + (exists ? 0 : bytes));
            }

            async Task Execute(string sql, params (string Name, object? Value)[] parameters)
            {
                await using DbCommand command = ManagementSql.Command(connection, transaction, sql, parameters);
                await command.ExecuteNonQueryAsync(token);
            }
        }, cancellationToken);
    }
}
