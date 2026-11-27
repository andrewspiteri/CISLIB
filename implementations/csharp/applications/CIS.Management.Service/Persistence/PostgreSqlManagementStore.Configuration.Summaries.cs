using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<IReadOnlyList<ManagementConfigurationSummaryCandidate>>> ReadConfigurationSummaryCandidatesAsync(
        string applicationId, string? after, int take, CancellationToken token)
    {
        if (take is < 1 or > 501)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        Guid application = ManagementSql.Uuid(applicationId, nameof(applicationId));
        Guid? position = after is null ? null : ManagementSql.Uuid(after, nameof(after));
        return RunCoreAsync(async (context, _, cancellation) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await LockConfigurationQuotaAsync(connection, transaction, sqlToken, write: false);
            var rows = new List<ManagementConfigurationSummaryCandidate>();
            await using DbCommand command = ManagementSql.Command(connection, transaction, $"""
                SELECT s.source_id,b.binding_id,d.configuration_id,d.schema_version,b.host_scope,a.environment,
                    d.definition_body,m.generation,m.default_release_id,
                    (SELECT count(*) FROM (SELECT 1 FROM {Table("configuration_override")} o WHERE o.binding_id=b.binding_id LIMIT 17) bounded),
                    COALESCE((SELECT jsonb_agg(jsonb_build_object('id',x.release_id,'common',r.common_publication_id,
                        'application',r.application_publication_id,'valid',r.binding_id=b.binding_id
                        AND (r.common_publication_id IS NULL OR cs.definition_id=b.definition_id AND cs.application_id IS NULL AND cs.environment=a.environment)
                        AND (r.application_publication_id IS NULL OR aps.binding_id=b.binding_id AND aps.application_id=b.application_id AND aps.environment=a.environment)))::text
                        FROM (SELECT m.default_release_id AS release_id WHERE m.default_release_id IS NOT NULL
                            UNION SELECT release_id FROM (SELECT o.release_id FROM {Table("configuration_override")} o
                                WHERE o.binding_id=b.binding_id ORDER BY o.cohort_id COLLATE "C" LIMIT 17) pins) x
                        LEFT JOIN {Table("configuration_release")} r ON r.release_id=x.release_id
                        LEFT JOIN {Table("configuration_publication")} cp ON cp.publication_id=r.common_publication_id
                        LEFT JOIN {Table("configuration_source")} cs ON cs.source_id=cp.source_id
                        LEFT JOIN {Table("configuration_publication")} ap ON ap.publication_id=r.application_publication_id
                        LEFT JOIN {Table("configuration_source")} aps ON aps.source_id=ap.source_id),'[]'),
                    selected_publication.created_at,selected_publication.source_id
                FROM {Table("configuration_binding")} b
                JOIN {Table("management_application")} a ON a.application_id=b.application_id
                JOIN {Table("configuration_definition")} d ON d.definition_id=b.definition_id
                JOIN {Table("configuration_source")} s ON s.binding_id=b.binding_id AND s.application_id=b.application_id
                JOIN {Table("configuration_assignment")} m ON m.binding_id=b.binding_id
                LEFT JOIN {Table("configuration_release")} selected_release ON selected_release.release_id=m.default_release_id AND selected_release.binding_id=b.binding_id
                LEFT JOIN {Table("configuration_publication")} selected_publication
                    ON selected_publication.publication_id=COALESCE(selected_release.application_publication_id,selected_release.common_publication_id)
                WHERE b.application_id=@application AND b.enabled AND d.enabled AND s.enabled
                    AND (CAST(@after AS uuid) IS NULL OR s.source_id>CAST(@after AS uuid))
                ORDER BY s.source_id LIMIT @take
                """, ("application", application), ("after", position), ("take", take));
            await using DbDataReader reader = await command.ExecuteReaderAsync(sqlToken);
            while (await reader.ReadAsync(sqlToken))
            {
                string sourceId = reader.GetGuid(0).ToString("D");
                string bindingId = reader.GetGuid(1).ToString("D");
                var definition = JsonSerializer.Deserialize<ManagementConfigurationSchema>(reader.GetFieldValue<byte[]>(6), ManagementConfigurationOptions.Json)
                    ?? throw new ManagementSchemaException("The retained Configuration definition is invalid.");
                int overrides = checked((int)reader.GetInt64(9));
                string? current = reader.IsDBNull(8) ? null : reader.GetGuid(8).ToString("D");
                using JsonDocument references = JsonDocument.Parse(reader.GetString(10));
                var releases = new List<string>();
                string origin = current is null ? "unassigned" : "unavailable";
                string? common = null;
                string? app = null;
                bool valid = overrides <= 16 && (current is null) == reader.IsDBNull(7) && (current is not null || overrides == 0);
                foreach (JsonElement item in references.RootElement.EnumerateArray())
                {
                    string id = item.GetProperty("id").GetString()!;
                    releases.Add(id);
                    valid &= item.GetProperty("valid").ValueKind == JsonValueKind.True;
                    if (id == current)
                    {
                        common = item.GetProperty("common").GetString();
                        app = item.GetProperty("application").GetString();
                        origin = common is null ? app is null ? "defaultsOnly" : "applicationOnly" : app is null ? "shared" : "applicationOverride";
                    }
                }
                if (current is not null)
                {
                    valid &= releases.Contains(current, StringComparer.Ordinal);
                    foreach (JsonElement item in references.RootElement.EnumerateArray())
                    {
                        if (item.GetProperty("common").GetString() != common || item.GetProperty("application").GetString() != app)
                        {
                            origin = "mixed";
                        }
                    }
                }
                if (!valid)
                {
                    origin = "unavailable";
                }
                string? modifiedAt = valid && !reader.IsDBNull(11) ? ManagementSql.Timestamp(reader.GetDateTime(11)) : null;
                string? modifiedSource = modifiedAt is not null && !reader.IsDBNull(12) ? reader.GetGuid(12).ToString("D") : null;
                rows.Add(new(new(applicationId, reader.GetString(5), reader.GetString(2), reader.GetInt64(3).ToString(CultureInfo.InvariantCulture),
                    sourceId, bindingId, reader.GetString(4), origin, overrides != 0, definition.Description)
                {
                    ModifiedAtUtc = modifiedAt
                }, releases.AsReadOnly())
                {
                    ModifiedSourceId = modifiedSource
                });
            }
            return Candidate<IReadOnlyList<ManagementConfigurationSummaryCandidate>>.Existing(rows.AsReadOnly());
        }, cancellation), token, requireReady: true, requireCleanRead: true);
    }
}
