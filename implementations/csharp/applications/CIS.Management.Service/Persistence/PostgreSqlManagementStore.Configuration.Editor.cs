using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationEditorBindingResource>> ReadConfigurationEditorBindingAsync(
        string applicationId, string configurationId, string schemaVersion, CancellationToken cancellationToken)
    {
        Guid application = ManagementSql.Uuid(applicationId, nameof(applicationId));
        if (!ManagementWire.IsOperationKey(configurationId) || !ManagementWire.IsOrderedInteger(schemaVersion))
        {
            throw new ArgumentException("Select one exact portable definition/schema binding.");
        }
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT b.binding_id,a.application_id,a.application,a.environment,b.host_scope,d.definition_body,b.enabled AND d.enabled,
                    cs.source_id,aps.source_id,cs.enabled,aps.enabled,
                    cs.binding_id IS NULL,
                    aps.definition_id=b.definition_id AND aps.environment=a.environment AND aps.application_id=a.application_id
                FROM {Table("configuration_binding")} b JOIN {Table("management_application")} a ON a.application_id=b.application_id
                JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id
                LEFT JOIN {Table("configuration_source")} cs ON cs.definition_id=b.definition_id
                    AND cs.environment=a.environment AND cs.application_id IS NULL
                LEFT JOIN {Table("configuration_source")} aps ON aps.binding_id=b.binding_id
                WHERE a.application_id=@application AND d.configuration_id=@configuration AND d.schema_version=@version
                """, ("application", application), ("configuration", configurationId),
                ("version", long.Parse(schemaVersion, CultureInfo.InvariantCulture)));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            if (!await reader.ReadAsync(sqlToken))
            {
                return Candidate<ManagementConfigurationEditorBindingResource>.Refused("management.configuration.not_found");
            }
            ManagementConfigurationBindingResource binding = ReadConfigurationBinding(reader);
            ManagementConfigurationSchema schema = JsonSerializer.Deserialize<ManagementConfigurationSchema>(reader.GetFieldValue<byte[]>(5),
                ManagementConfigurationOptions.Json) ?? throw new ManagementSchemaException("The immutable configuration schema is unreadable.");
            if (!binding.Enabled)
            {
                return Candidate<ManagementConfigurationEditorBindingResource>.Refused("management.configuration.not_found");
            }
            ManagementConfigurationEditorSourceResource? common = Source(7, 9, 11, "common");
            ManagementConfigurationEditorSourceResource? local = Source(8, 10, 12, "application");
            if (await reader.ReadAsync(sqlToken))
            {
                throw new ManagementSchemaException("The exact binding source snapshot is ambiguous.");
            }
            return Candidate<ManagementConfigurationEditorBindingResource>.Existing(new(binding, common, local, schema.Description));

            ManagementConfigurationEditorSourceResource? Source(int id, int enabled, int coherent, string kind)
            {
                if (reader.IsDBNull(id))
                {
                    return null;
                }
                if (reader.IsDBNull(enabled) || reader.IsDBNull(coherent) || !reader.GetBoolean(coherent))
                {
                    throw new ManagementSchemaException("The exact binding source snapshot is inconsistent.");
                }
                return new(new(binding.ConfigurationId, binding.SchemaVersion, reader.GetGuid(id).ToString("D"),
                    binding.Environment, kind, binding.Shape, binding.DefaultsJson,
                    ApplicationId: kind == "application" ? binding.ApplicationId : null,
                    BindingId: kind == "application" ? binding.BindingId : null,
                    HostApplicationName: kind == "application" ? binding.HostApplicationName : null), reader.GetBoolean(enabled));
            }
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }
}

