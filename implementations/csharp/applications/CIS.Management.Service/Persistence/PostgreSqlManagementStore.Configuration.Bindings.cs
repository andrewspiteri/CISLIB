using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationBindingResource>> ReadConfigurationBindingAsync(string applicationId,
        string configurationId, string schemaVersion, CancellationToken cancellationToken)
    {
        Guid application = ManagementSql.Uuid(applicationId, nameof(applicationId));
        if (!ManagementWire.IsOperationKey(configurationId) || !ManagementWire.IsOrderedInteger(schemaVersion))
        {
            throw new ArgumentException("Select one exact portable definition/schema binding.");
        }
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            await using DbCommand command = ManagementSql.Command(connection, transaction, ConfigurationBindingSelect +
                "WHERE a.application_id=@application AND d.configuration_id=@configuration AND d.schema_version=@version",
                ("application", application), ("configuration", configurationId), ("version", long.Parse(schemaVersion, CultureInfo.InvariantCulture)));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            return await reader.ReadAsync(sqlToken)
                ? Candidate<ManagementConfigurationBindingResource>.Existing(ReadConfigurationBinding(reader))
                : Candidate<ManagementConfigurationBindingResource>.Refused("management.configuration.not_found");
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    private string ConfigurationBindingSelect => $"""
        SELECT b.binding_id,a.application_id,a.application,a.environment,b.host_scope,d.definition_body,b.enabled AND d.enabled,
            (SELECT s.source_id FROM {Table("configuration_source")} s WHERE s.definition_id=b.definition_id
                AND s.environment=a.environment AND s.application_id IS NULL),
            (SELECT s.source_id FROM {Table("configuration_source")} s WHERE s.binding_id=b.binding_id)
        FROM {Table("configuration_binding")} b JOIN {Table("management_application")} a ON a.application_id=b.application_id
        JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id
        """ + " ";

    private static ManagementConfigurationBindingResource ReadConfigurationBinding(DbDataReader reader)
    {
        ManagementConfigurationSchema schema = JsonSerializer.Deserialize<ManagementConfigurationSchema>(reader.GetFieldValue<byte[]>(5),
            ManagementConfigurationOptions.Json) ?? throw new ManagementSchemaException("The immutable configuration schema is unreadable.");
        return new(reader.GetGuid(0).ToString("D"), reader.GetGuid(1).ToString("D"), reader.GetString(2), reader.GetString(3),
            schema.ConfigurationId, schema.SchemaVersion, reader.GetString(4), schema.Shape, schema.DefaultsJson, reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7).ToString("D"), reader.IsDBNull(8) ? null : reader.GetGuid(8).ToString("D"))
        {
            ObjectDefaults = schema.ObjectDefaults
        };
    }

    private async Task<ManagementConfigurationBindingResource?> LockConfigurationBindingAsync(DbConnection connection,
        DbTransaction transaction, string bindingId, CancellationToken token)
    {
        await using DbCommand command = ManagementSql.Command(connection, transaction,
            ConfigurationBindingSelect + "WHERE b.binding_id=@id FOR UPDATE OF b", ("id", Guid.Parse(bindingId)));
        await using DbDataReader reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadConfigurationBinding(reader) : null;
    }
}
