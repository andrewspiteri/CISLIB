using System.Data.Common;
using System.Text;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ConfigurationEffect> PublishConfigurationAsync(ILocalTransactionContext context,
        ManagementPublicationInput input, string actor, CancellationToken cancellationToken)
    {
        ManagementSql.RequireContext(context);
        return context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            long used;
            long quota;
            await using (DbCommand catalog = ManagementSql.Command(connection, transaction, $"""
                SELECT used_bytes,quota_bytes FROM {Table("configuration_catalog")}
                WHERE singleton=true AND manifest_digest IS NOT NULL FOR UPDATE
                """))
            await using (DbDataReader reader = await catalog.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token))
                {
                    return ConfigurationEffect.Reject("management.configuration.uninitialized");
                }
                used = reader.GetInt64(0);
                quota = reader.GetInt64(1);
            }
            ManagementConfigurationDefinition source;
            long revision;
            await using (DbCommand find = ManagementSql.Command(connection, transaction,
                ConfigurationSourceSelect + "WHERE s.source_id=@id AND s.enabled AND (b.binding_id IS NULL OR b.enabled) FOR UPDATE OF s",
                ("id", Guid.Parse(input.Source.SourceId))))
            await using (DbDataReader reader = await find.ExecuteReaderAsync(token))
            {
                if (!await reader.ReadAsync(token))
                {
                    return ConfigurationEffect.Reject("management.configuration.source_unavailable");
                }
                source = ReadConfigurationDefinition(reader);
                revision = reader.GetInt64(5);
            }
            if (source.ConfigurationId != input.Source.ConfigurationId || source.SchemaVersion != input.Source.SchemaVersion
                || source.Environment != input.Source.Environment || source.ApplicationId != input.Source.ApplicationId)
            {
                return ConfigurationEffect.Reject("management.configuration.scope_changed");
            }
            if (input.IfMatch != ConfigurationETag("source", revision))
            {
                return ConfigurationEffect.Reject("management.configuration.precondition_failed");
            }
            if (revision == long.MaxValue)
            {
                return ConfigurationEffect.Reject("management.configuration.revision_exhausted");
            }
            if (!ManagementConfigurationStructure.Validate(source.Shape, input.Command.PayloadJson).Valid)
            {
                return ConfigurationEffect.Reject("management.configuration.structural_rejection");
            }
            byte[] payload = Encoding.UTF8.GetBytes(input.Command.PayloadJson);
            string sha256 = ManagementConfigurationWire.ComputePayloadSha256(input.Command.PayloadJson);
            long bytes = payload.LongLength + 128 + Encoding.UTF8.GetByteCount(actor) + Encoding.UTF8.GetByteCount(input.Command.Reason);
            if (bytes > quota - used)
            {
                return ConfigurationEffect.Reject("management.configuration.capacity_exceeded");
            }
            Guid publication = Guid.NewGuid();
            try
            {
                // A literal inbound JSON body can fit even when our response encoder's escaping does not.
                // Refuse before installing content or advancing its source/occupancy, within original-key arbitration.
                _ = ManagementConfigurationWire.EncodeResponse(new ManagementConfigurationPublication(publication.ToString("D"),
                    source.SourceId, source.ConfigurationId, source.SchemaVersion, source.Environment, source.Kind, sha256, actor,
                    "2000-01-01T00:00:00.0000000Z", input.Command.Reason, input.Command.PayloadJson, source.ApplicationId));
            }
            catch (ArgumentException)
            {
                return ConfigurationEffect.Reject("management.configuration.response_too_large");
            }
            string timestamp;
            await using (DbCommand insert = ManagementSql.Command(connection, transaction, $"""
                INSERT INTO {Table("configuration_publication")}(publication_id,source_id,payload,sha256,actor,created_at,reason,logical_bytes)
                VALUES(@id,@source,@payload,@sha256,@actor,clock_timestamp(),@reason,@bytes) RETURNING created_at
                """, ("id", publication), ("source", Guid.Parse(source.SourceId)), ("payload", payload), ("sha256", sha256),
                ("actor", actor), ("reason", input.Command.Reason), ("bytes", bytes)))
            {
                timestamp = ManagementSql.Timestamp((DateTime)(await insert.ExecuteScalarAsync(token))!);
            }
            await using DbCommand update = ManagementSql.Command(connection, transaction, $"""
                UPDATE {Table("configuration_source")} SET revision=revision+1 WHERE source_id=@source;
                UPDATE {Table("configuration_catalog")} SET used_bytes=used_bytes+@bytes WHERE singleton=true
                """, ("source", Guid.Parse(source.SourceId)), ("bytes", bytes));
            await update.ExecuteNonQueryAsync(token);
            return new ConfigurationEffect(new(1, "sourcePublished", "applied", "management.configuration.source_published", timestamp,
                SourceId: source.SourceId, PublicationId: publication.ToString("D"), Sha256: sha256, ETag: ConfigurationETag("source", revision + 1)));
        }, cancellationToken);
    }
}
