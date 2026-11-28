using System.Data.Common;
using System.Text.Json.Nodes;
using CIS.Management.Service.Configuration;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationPreview>> PreviewConfigurationAsync(
        ManagementConfigurationBindingResource binding, ManagementConfigurationPreviewCommand command,
        string? after, int take, bool includeValues, string? expectedMapETag, CancellationToken cancellationToken)
    {
        Guid cursor = after is null ? Guid.Empty : ManagementSql.Uuid(after, nameof(after));
        if (take is < 1 or > 501)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        return RunCoreAsync(async (context, authoritySnapshot, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            _ = await LockConfigurationQuotaAsync(connection, transaction, sqlToken, write: false);
            if (await IsRuleEngineConfigurationAsync(connection, transaction, binding.ApplicationId, binding.Environment, binding.ConfigurationId, sqlToken))
            {
                return Candidate<ManagementConfigurationPreview>.Refused("management.configuration.not_found");
            }
            ManagementConfigurationAssignmentMap current = await ReadConfigurationMapAsync(connection, transaction, binding, sqlToken);
            if (expectedMapETag is not null && current.ETag != expectedMapETag)
            {
                return Candidate<ManagementConfigurationPreview>.Refused("management.configuration.preview_stale");
            }
            var changes = new List<ManagementConfigurationChange>();
            var issues = new List<ManagementConfigurationIssue>();
            try
            {
                if (command.Assignment is { } proposal)
                {
                    await ValidateConfigurationAssignmentAsync(connection, transaction, binding, proposal, sqlToken);
                    var beforeMap = current.Overrides.ToDictionary(item => item.CohortId, item => item.ReleaseId, StringComparer.Ordinal);
                    var afterMap = proposal.Overrides.ToDictionary(item => item.CohortId, item => item.ReleaseId, StringComparer.Ordinal);
                    if (current.DefaultReleaseId != proposal.DefaultReleaseId)
                    {
                        ManagementConfigurationComposition.Add(changes, "/defaultReleaseId", current.DefaultReleaseId is null ? "added" : "overridden");
                    }
                    foreach (string cohort in beforeMap.Keys.Union(afterMap.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    {
                        beforeMap.TryGetValue(cohort, out string? before);
                        afterMap.TryGetValue(cohort, out string? afterRelease);
                        ManagementConfigurationComposition.Add(changes, "/overrides/" + cohort,
                            before is null ? "added" : afterRelease is null ? "removed" : before == afterRelease ? "retained" : "overridden");
                    }
                    if (includeValues)
                    {
                        await ValueChanges(current.DefaultReleaseId, proposal.DefaultReleaseId, "/default");
                        foreach (string cohort in beforeMap.Keys.Union(afterMap.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                        {
                            string? before = beforeMap.GetValueOrDefault(cohort) ?? current.DefaultReleaseId;
                            string afterRelease = afterMap.GetValueOrDefault(cohort) ?? proposal.DefaultReleaseId;
                            await ValueChanges(before, afterRelease, "/cohorts/" + cohort);
                        }
                    }
                }
                else
                {
                    ConfigurationSourcePair selected = await ReadConfigurationPairAsync(connection, transaction, binding,
                        command.CommonSource!, command.ApplicationSource!, sqlToken);
                    JsonNode proposed = ManagementConfigurationComposition.Compose(binding, selected);
                    _ = WorstConfigurationEnvelopeBytes(selected);
                    if (includeValues)
                    {
                        JsonNode before = await Effective(current.DefaultReleaseId);
                        ManagementConfigurationComposition.Changes(binding.Shape, before, proposed, "", changes);
                    }
                }
            }
            catch (ConfigurationReferenceException error) when (error.Code == "management.configuration.structural_rejection")
            {
                issues.Add(new(error.Code));
            }
            catch (ConfigurationReferenceException error)
            {
                return Candidate<ManagementConfigurationPreview>.Refused(error.Code);
            }
            catch (ArgumentException)
            {
                return Candidate<ManagementConfigurationPreview>.Refused("management.configuration.response_too_large");
            }
            var recipients = new List<ManagementConfigurationPreviewRecipient>();
            await using (DbCommand select = ManagementSql.Command(connection, transaction, $"""
                SELECT r.registration_id,s.cohort_id FROM {Table("management_registration")} r
                JOIN {Table("configuration_session")} s ON s.registration_id=r.registration_id
                WHERE r.application_id=@application AND s.application_id=@application AND r.closed_at IS NULL AND r.registration_id>@after
                    AND EXISTS(SELECT 1 FROM jsonb_array_elements(convert_from(s.supported_body,'UTF8')::jsonb) binding
                        WHERE binding->>'bindingId'=@binding)
                ORDER BY r.registration_id LIMIT @take
                """, ("application", Guid.Parse(binding.ApplicationId)), ("binding", binding.BindingId), ("after", cursor), ("take", take)))
            await using (DbDataReader reader = await select.ExecuteReaderAsync(sqlToken))
            {
                while (await reader.ReadAsync(sqlToken))
                {
                    // Deployment binding capture is not a report of local typed compatibility.
                    recipients.Add(new(reader.GetGuid(0).ToString("D"), "unknown", reader.IsDBNull(1) ? null : reader.GetString(1)));
                }
            }
            return Candidate<ManagementConfigurationPreview>.Existing(new(binding.ApplicationId, binding.ConfigurationId, binding.SchemaVersion,
                issues.Count == 0, !includeValues, changes.AsReadOnly(), issues.AsReadOnly(), current, new(recipients.AsReadOnly()),
                command.CommonSource, command.ApplicationSource, command.Assignment));

            async Task<JsonNode> Effective(string? releaseId)
            {
                if (releaseId is null)
                {
                    return ManagementConfigurationComposition.Compose(binding, new(new("absent"), new("absent")));
                }
                ManagementConfigurationRelease release = await ReadConfigurationReleaseRowAsync(connection, transaction, Guid.Parse(releaseId), sqlToken, protect: true)
                    ?? throw new ConfigurationReferenceException("management.configuration.reference_not_found");
                ConfigurationSourcePair pair = await ReadConfigurationPairAsync(connection, transaction, binding, release.CommonSource, release.ApplicationSource, sqlToken);
                return ManagementConfigurationComposition.Compose(binding, pair);
            }

            async Task ValueChanges(string? previous, string next, string path)
            {
                if (previous == next)
                {
                    return;
                }
                JsonNode before = await Effective(previous);
                JsonNode proposed = await Effective(next);
                ManagementConfigurationComposition.Changes(binding.Shape, before, proposed, path, changes);
            }
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }
}
