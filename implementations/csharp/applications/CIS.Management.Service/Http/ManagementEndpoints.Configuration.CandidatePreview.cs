using System.Security.Cryptography;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] ConfigurationCandidateFeatures =
    [
        new("previewConfigurationCandidate", 1, "POST", "/configuration/candidate-preview",
            [ManagementConfigurationCapabilities.Preview, ManagementConfigurationCapabilities.Validate])
    ];

    private static void MapConfigurationCandidatePreview(RouteGroupBuilder group)
        => group.MapPost("/configuration/candidate-preview", ConfigurationCandidatePreview);

    private static async Task<IResult> ConfigurationCandidatePreview(HttpContext context, IManagementConfigurationBackend backend,
        ManagementCursor cursors)
    {
        if (!ManagementDiscoveryQuery.TryRead(context, filters: false, out _, out int size, out string? rawCursor))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        ManagementConfigurationCandidatePreviewCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationCandidatePreviewCommand>(context);
        if (command is null)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        byte[] commandBytes;
        try
        {
            commandBytes = ManagementConfigurationWire.EncodeRequest(command, backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, command.ApplicationId,
            command.ConfigurationId, command.SchemaVersion, ManagementConfigurationCapabilities.Preview);
        if (binding is null || !binding.Enabled)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        string? selected = command.SelectedSource == "common" ? binding.CommonSourceId : binding.ApplicationSourceId;
        if (binding.ApplicationId != command.ApplicationId || binding.ConfigurationId != command.ConfigurationId
            || binding.SchemaVersion != command.SchemaVersion || selected != command.SourceId)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        ManagementConfigurationDefinition? source = await VisibleSource(context, backend, command.SourceId,
            ManagementConfigurationCapabilities.Validate);
        if (source is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (source.SourceId != selected || source.Kind != command.SelectedSource || source.Environment != binding.Environment
            || source.ConfigurationId != binding.ConfigurationId || source.SchemaVersion != binding.SchemaVersion
            || command.SelectedSource == "application" && (source.ApplicationId != binding.ApplicationId
                || source.BindingId != binding.BindingId || source.HostApplicationName != binding.HostApplicationName)
            || command.SelectedSource == "common" && (source.ApplicationId is not null || source.BindingId is not null
                || source.HostApplicationName is not null))
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (backend is not IManagementConfigurationCandidatePreviewBackend candidate)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.candidate_preview.unsupported");
        }
        string endpoint = "configuration.candidate-preview." + Convert.ToHexStringLower(SHA256.HashData(commandBytes));
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, binding.BindingId, principal: context.User);
        if (!cursors.TryDecode(rawCursor, endpoint, actor, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        bool values = true;
        foreach (string sourceId in new[] { binding.CommonSourceId, binding.ApplicationSourceId }.OfType<string>())
        {
            values &= await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.ContentRead,
                ManagementConfigurationCapabilities.SourceResource, sourceId);
        }
        ManagementStoreResult<ManagementConfigurationCandidatePreview> result = await candidate.PreviewCandidateAsync(binding, command,
            after, size + 1, values, context.RequestAborted);
        if (result.HasMore || result.AttemptCompletion is not
            {
                Outcome: TransactionOutcome.NotCommitted, CommitAttempted: false, RollbackAttempted: true,
                RollbackConfirmed: true, Error: null, CleanupErrors.Count: 0
            })
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (!result.IsKnownCommitted || result.Value is not { } preview || result.Code is not null)
        {
            bool refusal = result.Value is null && !result.PreviouslyCommitted;
            int status = refusal && result.Code is "management.configuration.preview_stale" or "management.configuration.source_unavailable" ? 409
                : refusal && result.Code is "management.configuration.not_found" or "management.configuration.reference_not_found"
                    or "management.configuration.reference_scope" ? 404 : 503;
            return ManagementConfigurationHttp.Problem(context, status, status == 503 ? "management.configuration.unavailable" : result.Code!);
        }
        try
        {
            if (preview.Recipients?.Items is not { } rows || preview.Recipients.NextCursor is not null
                || rows.Count < 0 || rows.Count > size + 1)
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
            }
            var bounded = new List<ManagementConfigurationPreviewRecipient>();
            using (IEnumerator<ManagementConfigurationPreviewRecipient> iterator = rows.GetEnumerator())
            {
                while (bounded.Count <= size + 1 && iterator.MoveNext())
                {
                    bounded.Add(iterator.Current);
                }
            }
            if (bounded.Count != rows.Count || bounded.Count > size + 1)
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
            }
            string? previous = after;
            foreach (ManagementConfigurationPreviewRecipient row in bounded)
            {
                if (row is null || !ManagementWire.IsUuid(row.RegistrationId) || !ManagementWire.IsScalarText(row.Compatibility, 64)
                    || row.CohortId is not null && !ManagementWire.IsOperationKey(row.CohortId)
                    || previous is not null && StringComparer.Ordinal.Compare(row.RegistrationId, previous) <= 0)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                previous = row.RegistrationId;
            }
            ManagementConfigurationPreviewRecipient[] recipients = bounded.Take(size).ToArray();
            preview = preview with
            {
                Recipients = new(Array.AsReadOnly(recipients))
            };
            ManagementConfigurationCandidatePreview captured = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCandidatePreview>(
                ManagementConfigurationWire.EncodeResponse(preview, backend.ConfigurationWireLimits), backend.ConfigurationWireLimits)!;
            string digest = ManagementConfigurationWire.ComputePayloadSha256(command.PayloadJson, backend.ConfigurationWireLimits);
            if (captured.BindingId != binding.BindingId || captured.ApplicationId != binding.ApplicationId
                || captured.Environment != binding.Environment || captured.HostApplicationName != binding.HostApplicationName
                || captured.ConfigurationId != command.ConfigurationId || captured.SchemaVersion != command.SchemaVersion
                || captured.SelectedSource != command.SelectedSource || captured.SourceId != command.SourceId
                || captured.SourceETag != command.SourceETag || captured.AssignmentETag != command.AssignmentETag
                || captured.CommonSource != command.CommonSource || captured.ApplicationSource != command.ApplicationSource
                || captured.DraftSha256 != digest || captured.Redacted != !values)
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
            }
            captured = captured with
            {
                Recipients = new(captured.Recipients.Items, bounded.Count > size
                    ? cursors.Encode(endpoint, actor, size, recipients[^1].RegistrationId) : null)
            };
            context.RequestAborted.ThrowIfCancellationRequested();
            return ManagementConfigurationHttp.CandidatePreview(captured, backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
    }
}

