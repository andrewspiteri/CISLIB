using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Management.Service.Configuration;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationCatalog>> ReadConfigurationCatalogAsync(
        ManagementConfigurationOptions selected, CancellationToken cancellationToken)
        => RunCoreAsync(async (context, _, token) => Candidate<ManagementConfigurationCatalog>.Existing(
            await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
            {
                await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                    SELECT catalog_id,revision,manifest_digest,quota_bytes,used_bytes,
                        (SELECT count(*)::integer FROM {Table("configuration_definition")} WHERE enabled),
                        (SELECT count(*)::integer FROM {Table("configuration_source")} WHERE enabled),
                        (SELECT count(*)::integer FROM {Table("configuration_binding")} WHERE enabled)
                    FROM {Table("configuration_catalog")} WHERE singleton=true
                    """);
                await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
                if (!await reader.ReadAsync(sqlToken))
                {
                    throw new ManagementSchemaException("The stable configuration catalog is absent.");
                }
                string? applied = reader.IsDBNull(2) ? null : reader.GetString(2);
                return new ManagementConfigurationCatalog(reader.GetGuid(0).ToString("D"), ConfigurationETag("catalog", reader.GetInt64(1)),
                    selected.ManifestDigest, applied is not null,
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                    reader.IsDBNull(3) ? selected.QuotaBytes : reader.GetInt64(3), reader.GetInt64(4), applied);
            }, token)), cancellationToken, requireReady: true, requireCleanRead: true);

    internal Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>>> ReadConfigurationDefinitionsAsync(
        string? after, int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        Guid cursor = after is null ? Guid.Empty : ManagementSql.Uuid(after, nameof(after));
        return RunCoreAsync(async (context, _, token) => Candidate<IReadOnlyList<ManagementConfigurationDefinition>>.Existing(
            await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
            {
                await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
                await using DbCommand command = ManagementSql.Command(connection, transaction, ConfigurationSourceSelect + $"""
                    WHERE s.enabled AND (b.binding_id IS NULL OR b.enabled) AND s.source_id>@after
                    ORDER BY s.source_id LIMIT @take
                    """, ("after", cursor), ("take", take));
                await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
                var rows = new List<ManagementConfigurationDefinition>();
                while (await reader.ReadAsync(sqlToken))
                {
                    rows.Add(ReadConfigurationDefinition(reader));
                }
                return (IReadOnlyList<ManagementConfigurationDefinition>)rows.AsReadOnly();
            }, token)), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    internal Task<ManagementStoreResult<ManagementConfigurationDefinition>> ReadConfigurationDefinitionAsync(string sourceId,
        CancellationToken cancellationToken)
    {
        Guid id = ManagementSql.Uuid(sourceId, nameof(sourceId));
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            await using DbCommand command = ManagementSql.Command(connection, transaction,
                ConfigurationSourceSelect + "WHERE s.source_id=@id", ("id", id));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            return await reader.ReadAsync(sqlToken)
                ? Candidate<ManagementConfigurationDefinition>.Existing(ReadConfigurationDefinition(reader))
                : Candidate<ManagementConfigurationDefinition>.Refused("management.configuration.not_found");
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    internal Task<ManagementStoreResult<ManagementConfigurationSource>> ReadConfigurationSourceAsync(string sourceId,
        string? after, int take, CancellationToken cancellationToken)
    {
        Guid id = ManagementSql.Uuid(sourceId, nameof(sourceId));
        Guid cursor = after is null ? Guid.Empty : ManagementSql.Uuid(after, nameof(after));
        if (take is < 1 or > 501)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            ManagementConfigurationDefinition definition;
            long revision;
            await using (DbCommand command = ManagementSql.Command(connection, transaction,
                ConfigurationSourceSelect + "WHERE s.source_id=@id FOR SHARE OF s", ("id", id)))
            await using (DbDataReader reader = await command.ExecuteReaderAsync(sqlToken))
            {
                if (!await reader.ReadAsync(sqlToken))
                {
                    return Candidate<ManagementConfigurationSource>.Refused("management.configuration.not_found");
                }
                definition = ReadConfigurationDefinition(reader);
                revision = reader.GetInt64(5);
            }
            await using DbCommand publications = ManagementSql.Command(connection, transaction, $"""
                SELECT publication_id,sha256,actor,created_at,reason FROM {Table("configuration_publication")}
                WHERE source_id=@id AND publication_id>@after ORDER BY publication_id LIMIT @take
                """, ("id", id), ("after", cursor), ("take", take));
            await using DbDataReader history = await publications.ExecuteReaderAsync(sqlToken);
            var items = new List<ManagementConfigurationPublicationMetadata>();
            while (await history.ReadAsync(sqlToken))
            {
                items.Add(new(history.GetGuid(0).ToString("D"), sourceId, definition.ConfigurationId, definition.SchemaVersion,
                    definition.Environment, definition.Kind, history.GetString(1), history.GetString(2),
                    ManagementSql.Timestamp(history, 3), history.GetString(4), definition.ApplicationId));
            }
            return Candidate<ManagementConfigurationSource>.Existing(new(sourceId, definition.ConfigurationId, definition.SchemaVersion,
                definition.Environment, definition.Kind, ConfigurationETag("source", revision), new(items.AsReadOnly()), definition.ApplicationId));
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    internal Task<ManagementStoreResult<ManagementConfigurationPublication>> ReadConfigurationPublicationAsync(string publicationId,
        CancellationToken cancellationToken)
    {
        Guid id = ManagementSql.Uuid(publicationId, nameof(publicationId));
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT p.publication_id,s.source_id,d.configuration_id,d.schema_version,s.environment,s.application_id,
                    p.sha256,p.actor,p.created_at,p.reason,p.payload
                FROM {Table("configuration_publication")} p JOIN {Table("configuration_source")} s ON s.source_id=p.source_id
                JOIN {Table("configuration_definition")} d ON d.definition_id=s.definition_id WHERE p.publication_id=@id
                    AND NOT EXISTS(SELECT 1 FROM {Table("rule_engine_ruleset")} r
                        JOIN {Table("management_application")} a ON a.application_id=r.application_id
                        WHERE r.configuration_id=d.configuration_id AND a.environment=s.environment
                          AND (s.application_id IS NULL OR a.application_id=s.application_id))
                """, ("id", id));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            if (!await reader.ReadAsync(sqlToken))
            {
                return Candidate<ManagementConfigurationPublication>.Refused("management.configuration.not_found");
            }
            string? application = reader.IsDBNull(5) ? null : reader.GetGuid(5).ToString("D");
            return Candidate<ManagementConfigurationPublication>.Existing(new(reader.GetGuid(0).ToString("D"), reader.GetGuid(1).ToString("D"),
                reader.GetString(2), reader.GetInt64(3).ToString(CultureInfo.InvariantCulture), reader.GetString(4),
                application is null ? "common" : "application", reader.GetString(6), reader.GetString(7),
                ManagementSql.Timestamp(reader, 8), reader.GetString(9), Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(10)), application));
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    internal Task<ManagementStoreResult<string>> ResolveConfigurationPublicationSourceAsync(string publicationId, CancellationToken cancellationToken)
    {
        Guid id = ManagementSql.Uuid(publicationId, nameof(publicationId));
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            await using DbCommand command = ManagementSql.Command(connection, transaction,
                $"SELECT source_id FROM {Table("configuration_publication")} WHERE publication_id=@id", ("id", id));
            object? source = await command.ExecuteScalarAsync(sqlToken);
            return source is Guid known ? Candidate<string>.Existing(known.ToString("D"))
                : Candidate<string>.Refused("management.configuration.not_found");
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    private string ConfigurationSourceSelect => $"""
        SELECT s.source_id,s.environment,s.application_id,s.binding_id,b.host_scope,s.revision,d.definition_body
        FROM {Table("configuration_source")} s JOIN {Table("configuration_definition")} d ON d.definition_id=s.definition_id
        LEFT JOIN {Table("configuration_binding")} b ON b.binding_id=s.binding_id
        """ + " ";

    private static ManagementConfigurationDefinition ReadConfigurationDefinition(DbDataReader reader)
    {
        var definition = JsonSerializer.Deserialize<ManagementConfigurationSchema>(reader.GetFieldValue<byte[]>(6), ManagementConfigurationOptions.Json)
            ?? throw new ManagementSchemaException("A retained configuration definition is unreadable.");
        bool application = !reader.IsDBNull(2);
        return new(definition.ConfigurationId, definition.SchemaVersion, reader.GetGuid(0).ToString("D"), reader.GetString(1),
            application ? "application" : "common", definition.Shape, definition.DefaultsJson, definition.Description,
            application ? reader.GetGuid(2).ToString("D") : null, application ? reader.GetGuid(3).ToString("D") : null,
            application ? reader.GetString(4) : null);
    }

    private async Task RequireConfigurationCatalogAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
    {
        await using DbCommand command = ManagementSql.Command(connection, transaction,
            $"SELECT manifest_digest IS NOT NULL FROM {Table("configuration_catalog")} WHERE singleton=true");
        if (await command.ExecuteScalarAsync(token) is not true)
        {
            throw new Backends.ManagementBackendException("management.configuration.uninitialized");
        }
    }

    internal static string ConfigurationETag(string kind, long revision)
        => "\"configuration." + kind + "." + revision.ToString(CultureInfo.InvariantCulture) + "\"";
}
