using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    // Called only for a newly admitted native registration. Replays never consult today's mappings.
    private async Task<bool> CaptureConfigurationSessionAsync(DbConnection connection, DbTransaction transaction,
        Guid registration, Guid application, ManagementRegistrationOwner owner, CancellationToken token)
    {
        long revision;
        long available;
        await using (DbCommand catalog = ManagementSql.Command(connection, transaction, $"""
            SELECT revision,quota_bytes-used_bytes FROM {Table("configuration_catalog")}
            WHERE singleton=true AND manifest_digest IS NOT NULL FOR UPDATE
            """))
        await using (DbDataReader reader = await catalog.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
            {
                return true;
            }
            revision = reader.GetInt64(0);
            available = reader.GetInt64(1);
        }
        var bindings = new List<SessionBinding>();
        await using (DbCommand select = ManagementSql.Command(connection, transaction, $"""
            SELECT b.binding_id,d.configuration_id,d.schema_version,b.host_scope
            FROM {Table("configuration_binding")} b JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id
            WHERE b.application_id=@id AND b.enabled ORDER BY b.binding_id
            """, ("id", application)))
        await using (DbDataReader reader = await select.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                bindings.Add(new(reader.GetGuid(0).ToString("D"), reader.GetString(1),
                    reader.GetInt64(2).ToString(System.Globalization.CultureInfo.InvariantCulture), reader.GetString(3)));
            }
        }
        if (bindings.Count == 0)
        {
            return true;
        }
        string? cohort;
        await using (DbCommand select = ManagementSql.Command(connection, transaction, $"""
            SELECT cohort_id FROM {Table("configuration_cohort")} WHERE application_id=@id AND issuer=@issuer AND subject=@subject
            """, ("id", application), ("issuer", owner.Issuer), ("subject", owner.Subject)))
        {
            cohort = await select.ExecuteScalarAsync(token) as string;
        }
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(bindings, Json);
        long bytes = 128 + body.LongLength + (cohort is null ? 0 : Encoding.UTF8.GetByteCount(cohort));
        if (bytes > available)
        {
            return false;
        }
        await using DbCommand insert = ManagementSql.Command(connection, transaction, $"""
            INSERT INTO {Table("configuration_session")}(registration_id,application_id,cohort_id,catalog_revision,supported_body,logical_bytes)
            VALUES(@registration,@application,@cohort,@revision,@body,@bytes);
            UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@bytes WHERE singleton=true
            """, ("registration", registration), ("application", application), ("cohort", cohort), ("revision", revision), ("body", body), ("bytes", bytes));
        await insert.ExecuteNonQueryAsync(token);
        return true;
    }

    private sealed record SessionBinding(string BindingId, string ConfigurationId, string SchemaVersion, string HostApplicationName);
}
