using System.Data.Common;
using System.Globalization;
using System.Text;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;

namespace CIS.Management.Service.Persistence;

internal sealed record ConfigurationSourcePair(ManagementConfigurationSourceContent Common, ManagementConfigurationSourceContent Application);
internal sealed class ConfigurationReferenceException(string code) : Exception
{
    internal string Code { get; } = code;
}

public sealed partial class PostgreSqlManagementStore
{
    private async Task<(long Used, long Quota)> LockConfigurationQuotaAsync(DbConnection connection, DbTransaction transaction,
        CancellationToken token, bool write = true)
    {
        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            SELECT used_bytes,quota_bytes FROM {Table("configuration_catalog")} WHERE singleton=true AND manifest_digest IS NOT NULL
            """ + (write ? " FOR UPDATE" : " FOR SHARE"));
        await using DbDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            throw new ManagementBackendException("management.configuration.uninitialized");
        }
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private async Task<ConfigurationSourcePair> ReadConfigurationPairAsync(DbConnection connection, DbTransaction transaction,
        ManagementConfigurationBindingResource binding, ManagementConfigurationSourceReference common,
        ManagementConfigurationSourceReference application, CancellationToken token)
    {
        var publications = new Dictionary<string, ManagementConfigurationSourceContent>(StringComparer.Ordinal);
        foreach (string id in new[] { common.PublicationId, application.PublicationId }.OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT p.publication_id,p.sha256,p.payload,s.source_id,d.configuration_id,d.schema_version,s.environment,s.application_id,s.binding_id
                FROM {Table("configuration_publication")} p JOIN {Table("configuration_source")} s ON s.source_id=p.source_id
                JOIN {Table("configuration_definition")} d ON d.definition_id=s.definition_id
                WHERE p.publication_id=@id FOR KEY SHARE OF p,s
                """, ("id", Guid.Parse(id)));
            await using DbDataReader reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                throw new ConfigurationReferenceException("management.configuration.reference_not_found");
            }
            string? app = reader.IsDBNull(7) ? null : reader.GetGuid(7).ToString("D");
            string? sourceBinding = reader.IsDBNull(8) ? null : reader.GetGuid(8).ToString("D");
            string configuration = reader.GetString(4);
            string schema = reader.GetInt64(5).ToString(CultureInfo.InvariantCulture);
            string environment = reader.GetString(6);
            if (configuration != binding.ConfigurationId || schema != binding.SchemaVersion || environment != binding.Environment
                || app is not null && (app != binding.ApplicationId || sourceBinding != binding.BindingId))
            {
                throw new ConfigurationReferenceException("management.configuration.reference_scope");
            }
            string payload;
            try
            {
                payload = new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(2));
                if (ManagementConfigurationWire.ComputePayloadSha256(payload) != reader.GetString(1))
                {
                    throw new ManagementBackendException("management.configuration.content_unavailable");
                }
            }
            catch (ArgumentException)
            {
                throw new ManagementBackendException("management.configuration.content_unavailable");
            }
            publications.Add(id, new("present", id, reader.GetString(1), payload, reader.GetGuid(3).ToString("D"), configuration,
                schema, environment, app is null ? "common" : "application", app));
        }
        ManagementConfigurationSourceContent c = Resolve(common, "common");
        ManagementConfigurationSourceContent a = Resolve(application, "application");
        return new(c, a);

        ManagementConfigurationSourceContent Resolve(ManagementConfigurationSourceReference reference, string kind)
        {
            if (reference.State == "absent")
            {
                return new("absent");
            }
            ManagementConfigurationSourceContent source = publications[reference.PublicationId!];
            string? expected = kind == "common" ? binding.CommonSourceId : binding.ApplicationSourceId;
            if (source.Kind != kind || source.SourceId != expected)
            {
                throw new ConfigurationReferenceException("management.configuration.reference_scope");
            }
            if (!ManagementConfigurationStructure.Validate(binding.Shape, source.PayloadJson!).Valid)
            {
                throw new ConfigurationReferenceException("management.configuration.structural_rejection");
            }
            return source;
        }
    }

    private static int WorstConfigurationEnvelopeBytes(ConfigurationSourcePair pair)
    {
        const string id = "11111111-1111-1111-1111-111111111111";
        string environment = string.Concat(Enumerable.Repeat("\U0001f30d", 255));
        string configuration = new('x', 128);
        string version = long.MaxValue.ToString(CultureInfo.InvariantCulture);
        var desired = new ManagementConfigurationDesiredContext(id, id, environment, id, configuration, version,
            environment, id, version, new string('x', 128));
        var snapshot = new ManagementConfigurationSnapshot(desired, "\"" + new string('x', 254) + "\"", Copy(pair.Common), Copy(pair.Application));
        return ManagementConfigurationWire.EncodeResponse(snapshot).Length;

        ManagementConfigurationSourceContent Copy(ManagementConfigurationSourceContent source)
            => source.State == "absent" ? new("absent") : source with
            {
                ConfigurationId = configuration,
                SchemaVersion = version,
                Environment = environment,
                ApplicationId = source.Kind == "application" ? id : null
            };
    }
}
