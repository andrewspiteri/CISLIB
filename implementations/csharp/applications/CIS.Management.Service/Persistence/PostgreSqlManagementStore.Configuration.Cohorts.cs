using System.Data.Common;
using CIS.Management.Service.Configuration;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationCohortChoice>>> ReadConfigurationCohortsAsync(
        ManagementConfigurationBindingResource binding, string? after, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Guid bindingId = ManagementSql.Uuid(binding.BindingId, nameof(binding));
        if (take is < 1 or > 501)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        if (after is not null && !SnapshotPortableId(after))
        {
            throw new ArgumentException("Supply an exact disclosed cohort position.", nameof(after));
        }
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await RequireConfigurationCatalogAsync(connection, transaction, sqlToken);
            ManagementConfigurationBindingResource? actual;
            await using (DbCommand target = ManagementSql.Command(connection, transaction,
                ConfigurationBindingSelect + "WHERE b.binding_id=@binding FOR SHARE OF b", ("binding", bindingId)))
            await using (DbDataReader reader = await target.ExecuteReaderAsync(sqlToken))
            {
                actual = await reader.ReadAsync(sqlToken) ? ReadConfigurationBinding(reader) : null;
            }
            if (actual is null || !actual.Enabled || actual.ApplicationId != binding.ApplicationId
                || actual.ConfigurationId != binding.ConfigurationId || actual.SchemaVersion != binding.SchemaVersion
                || actual.Environment != binding.Environment)
            {
                return Candidate<IReadOnlyList<ManagementConfigurationCohortChoice>>.Refused("management.configuration.not_found");
            }
            // Mirror assignment admission: configured mappings, frozen admitted sessions, and this binding's retained overrides.
            // Limit each ordered distinct source before the union; no identities or membership totals leave the store.
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                WITH configured AS (
                    SELECT DISTINCT cohort_id COLLATE "C" AS cohort_id FROM {Table("configuration_cohort")}
                    WHERE application_id=@application AND cohort_id IS NOT NULL
                        AND (@after IS NULL OR cohort_id COLLATE "C">@after)
                    ORDER BY cohort_id LIMIT @take
                ), captured AS (
                    SELECT DISTINCT cohort_id COLLATE "C" AS cohort_id FROM {Table("configuration_session")}
                    WHERE application_id=@application AND cohort_id IS NOT NULL
                        AND (@after IS NULL OR cohort_id COLLATE "C">@after)
                    ORDER BY cohort_id LIMIT @take
                ), retained AS (
                    SELECT DISTINCT cohort_id COLLATE "C" AS cohort_id FROM {Table("configuration_override")}
                    WHERE binding_id=@binding AND cohort_id IS NOT NULL
                        AND (@after IS NULL OR cohort_id COLLATE "C">@after)
                    ORDER BY cohort_id LIMIT @take
                ), choices AS (
                    SELECT cohort_id FROM configured UNION SELECT cohort_id FROM captured UNION SELECT cohort_id FROM retained
                )
                SELECT cohort_id FROM choices ORDER BY cohort_id COLLATE "C" LIMIT @take
                """, ("application", Guid.Parse(actual.ApplicationId)), ("binding", bindingId), ("after", after), ("take", take));
            await using DbDataReader choices = await command.ExecuteReaderAsync(sqlToken);
            var rows = new List<ManagementConfigurationCohortChoice>();
            while (await choices.ReadAsync(sqlToken))
            {
                string cohort = choices.GetString(0);
                if (!SnapshotPortableId(cohort))
                {
                    throw new ManagementSchemaException("A retained Configuration cohort identifier is invalid.");
                }
                // The trusted mapping currently has no separate display label. Preserve its exact operator-facing identifier.
                rows.Add(new(cohort, cohort));
            }
            return Candidate<IReadOnlyList<ManagementConfigurationCohortChoice>>.Existing(rows.AsReadOnly());
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }
}
