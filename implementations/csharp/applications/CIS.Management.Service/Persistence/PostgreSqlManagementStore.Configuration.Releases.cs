using System.Data.Common;
using System.Text;
using CIS.Management.Service.Configuration;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ConfigurationEffect> CreateConfigurationReleaseAsync(ILocalTransactionContext context,
        ManagementReleaseInput input, string actor, CancellationToken cancellationToken)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            (long used, long quota) = await LockConfigurationQuotaAsync(connection, transaction, token);
            ManagementConfigurationBindingResource? binding = await LockConfigurationBindingAsync(connection, transaction, input.Binding.BindingId, token);
            if (binding is null || !binding.Enabled)
            {
                return ConfigurationEffect.Reject("management.configuration.binding_unavailable");
            }
            if (await IsRuleEngineConfigurationAsync(connection, transaction, binding.ApplicationId, binding.Environment, binding.ConfigurationId, token))
            {
                return ConfigurationEffect.Reject("management.configuration.rule_engine_managed");
            }
            ManagementConfigurationAssignmentMap map = await ReadConfigurationMapAsync(connection, transaction, binding, token, write: true);
            if (map.ETag != input.IfMatch)
            {
                return ConfigurationEffect.Reject("management.configuration.precondition_failed");
            }
            ConfigurationSourcePair pair;
            int encodedBytes;
            try
            {
                pair = await ReadConfigurationPairAsync(connection, transaction, binding, input.Command.CommonSource, input.Command.ApplicationSource, token);
                _ = ManagementConfigurationComposition.Compose(binding, pair);
                encodedBytes = WorstConfigurationEnvelopeBytes(pair);
            }
            catch (ConfigurationReferenceException error)
            {
                return ConfigurationEffect.Reject(error.Code);
            }
            catch (ArgumentException)
            {
                return ConfigurationEffect.Reject("management.configuration.response_too_large");
            }
            long bytes = 192 + Encoding.UTF8.GetByteCount(actor) + Encoding.UTF8.GetByteCount(input.Command.Reason);
            if (bytes > quota - used)
            {
                return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
            }
            Guid id = Guid.NewGuid();
            string timestamp;
            await using (DbCommand insert = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("configuration_release")}(release_id,binding_id,common_publication_id,application_publication_id,
                    actor,created_at,reason,encoded_bytes,logical_bytes)
                VALUES(@id,@binding,CAST(@common AS uuid),CAST(@application AS uuid),@actor,clock_timestamp(),@reason,@encoded,@bytes)
                RETURNING created_at
                """, ("id", id), ("binding", Guid.Parse(binding.BindingId)),
                ("common", input.Command.CommonSource.PublicationId), ("application", input.Command.ApplicationSource.PublicationId),
                ("actor", actor), ("reason", input.Command.Reason), ("encoded", encodedBytes), ("bytes", bytes)))
            {
                timestamp = ManagementSql.Timestamp((DateTime)(await insert.ExecuteScalarAsync(token))!);
            }
            await using DbCommand charge = ManagementSql.Command(connection, transaction,
                $"UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@bytes WHERE singleton=true", ("bytes", bytes));
            await charge.ExecuteNonQueryAsync(token);
            return new ConfigurationEffect(new(1, "releaseCreated", "applied", "management.configuration.release_created", timestamp,
                ApplicationId: binding.ApplicationId, ConfigurationId: binding.ConfigurationId, SchemaVersion: binding.SchemaVersion,
                ReleaseId: id.ToString("D"), ETag: ConfigurationReleaseETag(id.ToString("D"))), PreviousETag: map.ETag);
        }, cancellationToken);
    }

    internal Task<ManagementStoreResult<ManagementConfigurationRelease>> ReadConfigurationReleaseAsync(string releaseId,
        CancellationToken cancellationToken)
    {
        Guid id = ManagementSql.Uuid(releaseId, nameof(releaseId));
        return RunCoreAsync(async (context, authoritySnapshot, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            _ = await LockConfigurationQuotaAsync(connection, transaction, sqlToken, write: false);
            ManagementConfigurationRelease? release = await ReadConfigurationReleaseRowAsync(connection, transaction, id, sqlToken);
            return release is null ? Candidate<ManagementConfigurationRelease>.Refused("management.configuration.not_found")
                : Candidate<ManagementConfigurationRelease>.Existing(release);
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    private async Task<ManagementConfigurationRelease?> ReadConfigurationReleaseRowAsync(DbConnection connection, DbTransaction transaction,
        Guid releaseId, CancellationToken token, bool protect = false)
    {
        await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
            SELECT r.release_id,a.application_id,d.configuration_id,d.schema_version,a.environment,r.common_publication_id,
                r.application_publication_id,r.actor,r.created_at,r.reason,r.encoded_bytes
            FROM {Table("configuration_release")} r JOIN {Table("configuration_binding")} b ON b.binding_id=r.binding_id
            JOIN {Table("management_application")} a ON a.application_id=b.application_id
            JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id WHERE r.release_id=@id
            """ + (protect ? " FOR KEY SHARE OF r" : ""), ("id", releaseId));
        await using DbDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
        {
            return null;
        }
        return ReadConfigurationRelease(reader);
    }

    private static ManagementConfigurationRelease ReadConfigurationRelease(DbDataReader reader)
    {
        if (reader.GetInt64(10) is < 1 or > 1024 * 1024)
        {
            throw new Backends.ManagementBackendException("management.configuration.content_unavailable");
        }
        return new(reader.GetGuid(0).ToString("D"), reader.GetGuid(1).ToString("D"), reader.GetString(2),
            reader.GetInt64(3).ToString(System.Globalization.CultureInfo.InvariantCulture), reader.GetString(4),
            reader.IsDBNull(5) ? new("absent") : new("present", reader.GetGuid(5).ToString("D")),
            reader.IsDBNull(6) ? new("absent") : new("present", reader.GetGuid(6).ToString("D")), reader.GetString(7),
            ManagementSql.Timestamp(reader, 8), reader.GetString(9), ConfigurationReleaseETag(reader.GetGuid(0).ToString("D")));
    }

    private static string ConfigurationReleaseETag(string id) => "\"configuration.release." + id + "\"";
}
