using System.Data.Common;
using System.Text;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    /// <summary>Reads an exact deployed rule version for the native owner's frozen Configuration binding.</summary>
    public Task<ManagementStoreResult<ManagementRuleWorkloadVersion>> ReadRuleEngineWorkloadVersionAsync(ManagementRegistrationOwner owner,
        string registrationId, string configurationId, string schemaVersion, string versionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Guid registration = ManagementSql.Uuid(registrationId, nameof(registrationId));
        Guid version = ManagementSql.Uuid(versionId, nameof(versionId));
        if (!SnapshotPortableId(configurationId) || !SnapshotSchema(schemaVersion, out long schema))
        {
            throw new ArgumentException("A canonical Configuration binding identity and schema version are required.");
        }
        return RunCoreAsync(async (context, state, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            ManagementAuthoritySnapshot confirmed = state ?? throw new ManagementBackendException("management.authority.unavailable");
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction, $"""
                SELECT manifest_digest IS NOT NULL FROM {Table("configuration_catalog")} WHERE singleton=true FOR SHARE
                """))
            {
                if (await catalog.ExecuteScalarAsync(sqlToken) is not true)
                {
                    return Candidate<ManagementRuleWorkloadVersion>.Refused("management.configuration.uninitialized");
                }
            }
            RegistrationRow? native = await ReadRegistrationAsync(connection, transaction, registration, "FOR SHARE OF r", sqlToken);
            if (native is null || !Owned(native, owner))
            {
                return Candidate<ManagementRuleWorkloadVersion>.Refused("management.configuration.not_found");
            }
            if (native.ClosedAtUtc is not null)
            {
                return Candidate<ManagementRuleWorkloadVersion>.Refused("management.registration.closed");
            }

            // A frozen native binding and same-scope assignment witness outlive their carrier release.
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT e.ruleset_id,b.host_scope,b.binding_id,se.cohort_id,se.supported_body,v.version_id,v.definition
                FROM {Table("configuration_session")} se
                JOIN {Table("configuration_binding")} b ON b.application_id=se.application_id AND b.enabled
                JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id AND d.enabled
                JOIN {Table("rule_engine_ruleset")} e ON e.application_id=se.application_id
                    AND e.configuration_binding_id=b.binding_id AND e.configuration_id=d.configuration_id AND e.enabled
                JOIN {Table("rule_engine_deployed_version")} w ON w.ruleset_key=e.ruleset_key AND w.version_id=@version
                JOIN {Table("rule_engine_version")} v ON v.ruleset_key=w.ruleset_key AND v.version_id=w.version_id
                WHERE se.registration_id=@registration AND se.application_id=@application
                    AND d.configuration_id=@configuration AND d.schema_version=@schema
                """, ("registration", registration), ("application", native.ApplicationId), ("configuration", configurationId),
                ("schema", schema), ("version", version));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            if (!await reader.ReadAsync(sqlToken))
            {
                return Candidate<ManagementRuleWorkloadVersion>.Refused("management.configuration.not_found");
            }
            string host = reader.GetString(1);
            string binding = reader.GetGuid(2).ToString("D");
            SessionBinding[] supported = JsonSerializer.Deserialize<SessionBinding[]>(reader.GetFieldValue<byte[]>(4), Json)
                ?? throw new ManagementSchemaException("A retained configuration session is unreadable.");
            if (supported.Length > 256 || !supported.Any(item => item.BindingId == binding && item.ConfigurationId == configurationId
                && item.SchemaVersion == schemaVersion && item.HostApplicationName == host))
            {
                return Candidate<ManagementRuleWorkloadVersion>.Refused("management.configuration.not_found");
            }
            var target = new ManagementConfigurationSnapshotTarget(confirmed.AuthorityId, native.ApplicationId.ToString("D"),
                native.Environment, registrationId, configurationId, schemaVersion, host, reader.IsDBNull(3) ? null : reader.GetString(3));
            var result = new ManagementRuleWorkloadVersion(target, reader.GetString(0), reader.GetGuid(5).ToString("D"),
                new UTF8Encoding(false, true).GetString(reader.GetFieldValue<byte[]>(6)));
            return Candidate<ManagementRuleWorkloadVersion>.Existing(result);
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }
}
