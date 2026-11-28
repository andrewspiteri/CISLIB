using System.Data.Common;
using System.Text.Json.Nodes;
using System.Text.Json;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Persistence;

public sealed partial class PostgreSqlManagementStore
{
    internal Task<ManagementStoreResult<ManagementConfigurationCandidatePreview>> PreviewConfigurationCandidateAsync(
        ManagementConfigurationBindingResource binding, ManagementConfigurationCandidatePreviewCommand command,
        string? after, int take, bool includeValues, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(command);
        _ = ManagementConfigurationWire.EncodeRequest(command);
        Guid cursor = after is null ? Guid.Empty : ManagementSql.Uuid(after, nameof(after));
        if (take is < 1 or > 501)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }
        return RunCoreAsync(async (context, _, token) => await context.UseAdoNetAsync(async (connection, transaction, sqlToken) =>
        {
            await LockConfigurationQuotaAsync(connection, transaction, sqlToken, write: false);
            ManagementConfigurationBindingResource? actual;
            await using (DbCommand select = ManagementSql.Command(connection, transaction,
                ConfigurationBindingSelect + "WHERE b.binding_id=@binding FOR SHARE OF b", ("binding", Guid.Parse(binding.BindingId))))
            await using (DbDataReader reader = await select.ExecuteReaderAsync(sqlToken))
            {
                actual = await reader.ReadAsync(sqlToken) ? ReadConfigurationBinding(reader) : null;
            }
            if (actual is null || !actual.Enabled || actual.ApplicationId != command.ApplicationId
                || actual.ApplicationId != binding.ApplicationId || actual.ConfigurationId != command.ConfigurationId
                || actual.SchemaVersion != command.SchemaVersion || actual.Environment != binding.Environment
                || actual.HostApplicationName != binding.HostApplicationName || actual.CommonSourceId != binding.CommonSourceId
                || actual.ApplicationSourceId != binding.ApplicationSourceId)
            {
                return Candidate<ManagementConfigurationCandidatePreview>.Refused("management.configuration.not_found");
            }
            if (await IsRuleEngineConfigurationAsync(connection, transaction, actual.ApplicationId, actual.Environment, actual.ConfigurationId, sqlToken))
            {
                return Candidate<ManagementConfigurationCandidatePreview>.Refused("management.configuration.not_found");
            }
            string? sourceId = command.SelectedSource == "common" ? actual.CommonSourceId : actual.ApplicationSourceId;
            if (sourceId != command.SourceId)
            {
                return Candidate<ManagementConfigurationCandidatePreview>.Refused("management.configuration.reference_scope");
            }
            long revision;
            await using (DbCommand select = ManagementSql.Command(connection, transaction,
                $"SELECT revision FROM {Table("configuration_source")} WHERE source_id=@source AND enabled FOR SHARE",
                ("source", Guid.Parse(command.SourceId))))
            await using (DbDataReader reader = await select.ExecuteReaderAsync(sqlToken))
            {
                if (!await reader.ReadAsync(sqlToken))
                {
                    return Candidate<ManagementConfigurationCandidatePreview>.Refused("management.configuration.source_unavailable");
                }
                revision = reader.GetInt64(0);
            }
            ManagementConfigurationAssignmentMap current = await ReadConfigurationMapAsync(connection, transaction, actual, sqlToken);
            string sourceETag = ConfigurationETag("source", revision);
            if (sourceETag != command.SourceETag || current.ETag != command.AssignmentETag)
            {
                return Candidate<ManagementConfigurationCandidatePreview>.Refused("management.configuration.preview_stale");
            }
            var changes = new List<ManagementConfigurationChange>();
            var issues = new List<ManagementConfigurationIssue>();
            try
            {
                ConfigurationSourcePair baseline = await ReadConfigurationPairAsync(connection, transaction, actual,
                    command.CommonSource, command.ApplicationSource, sqlToken);
                if (CandidateEnvelopeBytes(baseline, command.SelectedSource, command.PayloadJson) > 1024 * 1024)
                {
                    throw new ArgumentException("The candidate source pair exceeds the native snapshot envelope bound.");
                }
                issues.AddRange(ManagementConfigurationStructure.Validate(actual.Shape, command.PayloadJson).Issues);
                if (issues.Count == 0)
                {
                    JsonNode proposed = ManagementConfigurationComposition.ComposeCandidate(actual, baseline, command.SelectedSource, command.PayloadJson);
                    if (includeValues)
                    {
                        JsonNode before;
                        if (current.DefaultReleaseId is null)
                        {
                            before = ManagementConfigurationComposition.Compose(actual, new(new("absent"), new("absent")));
                        }
                        else
                        {
                            ManagementConfigurationRelease previous = await ReadConfigurationReleaseRowAsync(connection, transaction,
                                Guid.Parse(current.DefaultReleaseId), sqlToken, protect: true)
                                ?? throw new ConfigurationReferenceException("management.configuration.reference_not_found");
                            if (previous.ApplicationId != actual.ApplicationId || previous.ConfigurationId != actual.ConfigurationId
                                || previous.SchemaVersion != actual.SchemaVersion || previous.Environment != actual.Environment)
                            {
                                throw new ConfigurationReferenceException("management.configuration.reference_scope");
                            }
                            ConfigurationSourcePair previousPair = await ReadConfigurationPairAsync(connection, transaction, actual,
                                previous.CommonSource, previous.ApplicationSource, sqlToken);
                            before = ManagementConfigurationComposition.Compose(actual, previousPair);
                        }
                        ManagementConfigurationComposition.Changes(actual.Shape, before, proposed, "", changes);
                    }
                }
            }
            catch (ConfigurationReferenceException error) when (error.Code == "management.configuration.structural_rejection")
            {
                issues.Add(new("configuration.structure"));
            }
            catch (ConfigurationReferenceException error)
            {
                return Candidate<ManagementConfigurationCandidatePreview>.Refused(error.Code);
            }
            catch (ArgumentException)
            {
                return Candidate<ManagementConfigurationCandidatePreview>.Refused("management.configuration.response_too_large");
            }
            var recipients = new List<ManagementConfigurationPreviewRecipient>();
            await using (DbCommand select = ManagementSql.Command(connection, transaction, $"""
                SELECT r.registration_id,s.cohort_id FROM {Table("management_registration")} r
                JOIN {Table("configuration_session")} s ON s.registration_id=r.registration_id
                WHERE r.application_id=@application AND s.application_id=@application AND r.closed_at IS NULL AND r.registration_id>@after
                    AND EXISTS(SELECT 1 FROM jsonb_array_elements(convert_from(s.supported_body,'UTF8')::jsonb) binding
                        WHERE binding->>'bindingId'=@binding)
                ORDER BY r.registration_id LIMIT @take
                """, ("application", Guid.Parse(actual.ApplicationId)), ("binding", actual.BindingId), ("after", cursor), ("take", take)))
            await using (DbDataReader reader = await select.ExecuteReaderAsync(sqlToken))
            {
                while (await reader.ReadAsync(sqlToken))
                {
                    recipients.Add(new(reader.GetGuid(0).ToString("D"), "unknown", reader.IsDBNull(1) ? null : reader.GetString(1)));
                }
            }
            IReadOnlyList<ManagementConfigurationIssue> disclosedIssues = Array.AsReadOnly(issues.Take(64)
                .Select(issue => includeValues ? issue : issue with { Path = null }).ToArray());
            return Candidate<ManagementConfigurationCandidatePreview>.Existing(new(actual.BindingId, actual.ApplicationId, actual.Environment,
                actual.ConfigurationId, actual.SchemaVersion, actual.HostApplicationName, command.SelectedSource, command.SourceId,
                sourceETag, current.ETag, command.CommonSource, command.ApplicationSource,
                ManagementConfigurationWire.ComputePayloadSha256(command.PayloadJson), issues.Count == 0, !includeValues,
                includeValues ? changes.AsReadOnly() : Array.Empty<ManagementConfigurationChange>(), disclosedIssues, current,
                new(recipients.AsReadOnly())));
        }, token), cancellationToken, requireReady: true, requireCleanRead: true);
    }

    private int CandidateEnvelopeBytes(ConfigurationSourcePair baseline, string selectedSource, string payloadJson)
    {
        const int absentBytes = 18; // Exact {"state":"absent"} representation, without a manufactured reference.
        int bytes = WorstConfigurationEnvelopeBytes(new(new("absent"), new("absent"))) - 2 * absentBytes;
        bytes += selectedSource == "common" ? Present("common", payloadJson)
            : baseline.Common.State == "present" ? Present("common", baseline.Common.PayloadJson!) : absentBytes;
        bytes += selectedSource == "application" ? Present("application", payloadJson)
            : baseline.Application.State == "present" ? Present("application", baseline.Application.PayloadJson!) : absentBytes;
        return bytes;

        static int Present(string kind, string payload)
        {
            int length = 2;
            int fields = 0;
            Add("state", 7);
            Add("publicationId", 36);
            Add("sha256", 64);
            Add("sourceId", 36);
            Add("configurationId", 128);
            Add("schemaVersion", 19);
            string worstEnvironment = string.Concat(Enumerable.Repeat("\U0001f30d", 255));
            Add("environment", JsonSerializer.SerializeToUtf8Bytes(worstEnvironment).Length - 2);
            Add("kind", kind.Length);
            if (kind == "application")
            {
                Add("applicationId", 36);
            }
            length += 1 + JsonSerializer.SerializeToUtf8Bytes("payloadJson").Length + 1
                + JsonSerializer.SerializeToUtf8Bytes(payload).Length;
            return length;

            void Add(string name, int scalarWidth)
            {
                length += (fields++ == 0 ? 0 : 1) + JsonSerializer.SerializeToUtf8Bytes(name).Length + 1 + 2 + scalarWidth;
            }
        }
    }
}

