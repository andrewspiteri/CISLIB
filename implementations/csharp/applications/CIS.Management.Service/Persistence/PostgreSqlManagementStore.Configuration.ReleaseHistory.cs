using System.Data.Common;
using CIS.Management.Service.Configuration;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationRelease>>> ReadConfigurationReleasesAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (take is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        DateTime? created = null;
        Guid? release = null;
        if (after is not null)
        {
            if (!ManagementConfigurationReleasePosition.TryRead(after, out DateTime time, out Guid id))
            {
                throw new ArgumentException("Use the original retained-release position.", nameof(after));
            }
            created = time;
            release = id;
        }
        return RunCoreAsync(async (context, authoritySnapshot, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await LockConfigurationQuotaAsync(connection, transaction, sqlToken, write: false);
            ManagementConfigurationBindingResource? actual;
            await using (DbCommand target = ManagementSql.Command(connection, transaction,
                ConfigurationBindingSelect + "WHERE b.binding_id=@binding FOR SHARE OF b", ("binding", Guid.Parse(binding.BindingId))))
            await using (DbDataReader reader = await target.ExecuteReaderAsync(sqlToken))
            {
                actual = await reader.ReadAsync(sqlToken) ? ReadConfigurationBinding(reader) : null;
            }
            if (actual is null || !actual.Enabled || actual.ApplicationId != binding.ApplicationId || actual.Environment != binding.Environment
                || actual.ConfigurationId != binding.ConfigurationId || actual.SchemaVersion != binding.SchemaVersion
                || actual.HostApplicationName != binding.HostApplicationName)
            {
                return Candidate<IReadOnlyList<ManagementConfigurationRelease>>.Refused("management.configuration.not_found");
            }
            var rows = new List<ManagementConfigurationRelease>();
            await using DbCommand query = ManagementSql.Command(connection, transaction, $"""
                SELECT r.release_id,a.application_id,d.configuration_id,d.schema_version,a.environment,r.common_publication_id,
                    r.application_publication_id,r.actor,r.created_at,r.reason,r.encoded_bytes
                FROM {Table("configuration_release")} r JOIN {Table("configuration_binding")} b ON b.binding_id=r.binding_id
                JOIN {Table("management_application")} a ON a.application_id=b.application_id
                JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id
                WHERE r.binding_id=@binding AND (CAST(@created AS timestamptz) IS NULL
                    OR (r.created_at,r.release_id)<(CAST(@created AS timestamptz),CAST(@release AS uuid)))
                ORDER BY r.created_at DESC,r.release_id DESC LIMIT @take
                """, ("binding", Guid.Parse(actual.BindingId)), ("created", created is null ? null : ManagementSql.Timestamp(created.Value)),
                ("release", release), ("take", take));
            await using DbDataReader values = await query.ExecuteReaderAsync(sqlToken);
            while (await values.ReadAsync(sqlToken))
            {
                rows.Add(ReadConfigurationRelease(values));
            }
            return Candidate<IReadOnlyList<ManagementConfigurationRelease>>.Existing(rows.AsReadOnly());
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }
}

