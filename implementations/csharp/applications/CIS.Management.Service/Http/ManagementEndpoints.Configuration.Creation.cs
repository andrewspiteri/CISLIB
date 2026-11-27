using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] ConfigurationCreationFeatures =
    [
        new("getConfigurationCreationContext", 1, "GET", "/applications/{applicationId}/configuration-creations/context",
            [ManagementConfigurationCapabilities.Create, ManagementConfigurationCapabilities.OperationsRead]),
        new("createConfiguration", 1, "POST", "/applications/{applicationId}/configuration-creations",
            [ManagementConfigurationCapabilities.Create, ManagementConfigurationCapabilities.OperationsRead]),
        new("getConfigurationCreationOperation", 1, "GET", "/applications/{applicationId}/configuration-creation-operations/{operationKey}",
            [ManagementConfigurationCapabilities.OperationsRead]),
        new("getConfigurationSummaries", 1, "GET", "/applications/{applicationId}/configuration-summaries",
            ["management.applications.read", ManagementConfigurationCapabilities.DefinitionsRead])
    ];

    private static void MapConfigurationCreation(RouteGroupBuilder group)
    {
        group.MapGet("/applications/{applicationId}/configuration-creations/context", ConfigurationCreationContext);
        group.MapPost("/applications/{applicationId}/configuration-creations", CreateConfiguration);
        group.MapGet("/applications/{applicationId}/configuration-creation-operations/{operationKey}", ConfigurationCreationOperation);
        group.MapGet("/applications/{applicationId}/configuration-summaries", ConfigurationSummaries);
    }

    private static async Task<IResult> ConfigurationCreationContext(HttpContext context, string applicationId,
        IManagementBackend applications, IManagementConfigurationBackend backend)
    {
        if (context.Request.Query.Count != 0 || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications, ManagementConfigurationCapabilities.Create);
        if (application is null || !await ManagementHttp.Allowed(context, ManagementConfigurationCapabilities.OperationsRead, applicationId))
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (backend is not IManagementConfigurationCreationBackend creation)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.creation.unsupported");
        }
        ManagementStoreResult<ManagementConfigurationCreationContext> result = await creation.ReadCreationContextAsync(applicationId, context.RequestAborted);
        if (!CleanConfigurationRead(result) || result.HasMore)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (!result.IsKnownCommitted || result.Value is not { } value)
        {
            return result.Code == "management.configuration.not_found" && result.Value is null && !result.PreviouslyCommitted
                ? ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found")
                : ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        try
        {
            ManagementConfigurationCreationContext captured = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCreationContext>(
                ManagementConfigurationWire.EncodeResponse(value, backend.ConfigurationWireLimits), backend.ConfigurationWireLimits)!;
            if (captured.ApplicationId != applicationId || captured.Application != application.Application || captured.Environment != application.Environment)
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
            }
            context.Response.Headers.ETag = captured.ETag;
            return ManagementConfigurationHttp.Json(captured, backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
    }

    private static async Task<IResult> CreateConfiguration(HttpContext context, string applicationId,
        IManagementBackend applications, IManagementConfigurationBackend backend)
    {
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications, ManagementConfigurationCapabilities.Create);
        if (application is null || !await ManagementHttp.Allowed(context, ManagementConfigurationCapabilities.OperationsRead, applicationId))
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (backend is not IManagementConfigurationCreationBackend creation)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.creation.unsupported");
        }
        if (!ManagementConfigurationHttp.MutationHeaders(context, out string key, out string etag, out IResult? refusal))
        {
            return refusal!;
        }
        ManagementConfigurationCreationCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationCreationCommand>(context);
        return command is null ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await creation.CreateConfigurationAsync(application, key, etag, command, context.RequestAborted));
    }

    private static async Task<IResult> ConfigurationCreationOperation(HttpContext context, string applicationId, string operationKey,
        IManagementBackend applications, IManagementConfigurationBackend backend)
    {
        if (context.Request.Query.Count != 0 || ManagementConfigurationHttp.HasRequestBody(context) || !ManagementWire.IsOperationKey(operationKey))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications, ManagementConfigurationCapabilities.OperationsRead);
        if (application is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        return backend is not IManagementConfigurationCreationBackend creation
            ? ManagementConfigurationHttp.Problem(context, 501, "management.configuration.creation.unsupported")
            : ManagementConfigurationHttp.Operation(context, await creation.LookupCreationAsync(application, operationKey, context.RequestAborted));
    }

    private static async Task<IResult> ConfigurationSummaries(HttpContext context, string applicationId,
        IManagementBackend applications, IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications);
        if (application is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (backend is not IManagementConfigurationCreationBackend creation)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.creation.unsupported");
        }
        const string endpoint = "configuration.summaries";
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, applicationId, new(Environment: application.Environment), context.User);
        if (!ManagementDiscoveryQuery.TryRead(context, filters: false, out _, out int size, out string? cursor)
            || ManagementConfigurationHttp.HasRequestBody(context) || !cursors.TryDecode(cursor, endpoint, actor, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        var items = new List<ManagementConfigurationSummary>();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        int remaining = backend.ConfigurationWireLimits.MaximumBodyBytes - 4096;
        int scanned = 0;
        bool more = false;
        bool ended = false;
        try
        {
            while (!more && !ended)
            {
                if (scanned >= DiscoveryScanLimit)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.discovery.scan_limit");
                }
                int take = Math.Min(16, DiscoveryScanLimit - scanned);
                ManagementStoreResult<IReadOnlyList<ManagementConfigurationSummaryCandidate>> result = await creation.ReadSummaryCandidatesAsync(applicationId, after, take, context.RequestAborted);
                if (!CleanConfigurationRead(result) || !result.IsKnownCommitted || result.HasMore || result.Value is not { } values)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                int declaredRows = values.Count;
                if (declaredRows < 0 || declaredRows > take)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                var rows = new List<ManagementConfigurationSummaryCandidate>();
                using (IEnumerator<ManagementConfigurationSummaryCandidate> iterator = values.GetEnumerator())
                {
                    while (rows.Count <= declaredRows && iterator.MoveNext())
                    {
                        rows.Add(iterator.Current);
                    }
                }
                if (rows.Count != declaredRows)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                ended = rows.Count < take;
                foreach (ManagementConfigurationSummaryCandidate row in rows)
                {
                    scanned++;
                    if (row?.Summary is not { } summary || row.ReleaseIds is not { } releaseIds)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    int declaredReferences = releaseIds.Count;
                    if (declaredReferences is < 0 or > 18)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    ManagementConfigurationSummary captured = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSummary>(
                        ManagementConfigurationWire.EncodeResponse(summary, backend.ConfigurationWireLimits), backend.ConfigurationWireLimits)!;
                    if ((captured.ModifiedAtUtc is null) != (row.ModifiedSourceId is null)
                        || row.ModifiedSourceId is not null && !ManagementWire.IsUuid(row.ModifiedSourceId))
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    if (captured.ApplicationId != applicationId || captured.Environment != application.Environment || !sources.Add(captured.SourceId)
                        || !bindings.Add(captured.BindingId) || !definitions.Add(captured.ConfigurationId + "/" + captured.SchemaVersion)
                        || after is not null && StringComparer.Ordinal.Compare(captured.SourceId, after) <= 0)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    after = captured.SourceId;
                    var refs = new HashSet<string>(StringComparer.Ordinal);
                    using (IEnumerator<string> iterator = releaseIds.GetEnumerator())
                    {
                        while (refs.Count <= declaredReferences && iterator.MoveNext())
                        {
                            if (!ManagementWire.IsUuid(iterator.Current) || !refs.Add(iterator.Current))
                            {
                                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                            }
                        }
                    }
                    if (refs.Count != declaredReferences
                        || (captured.Origin is "shared" or "applicationOnly" or "applicationOverride" or "defaultsOnly") && refs.Count == 0
                        || captured.Origin == "mixed" && (!captured.HasCohortOverrides || refs.Count < 2)
                        || captured.Origin == "unassigned" && (captured.HasCohortOverrides || refs.Count != 0))
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead, ManagementConfigurationCapabilities.SourceResource, captured.SourceId)
                        || !await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead, ManagementConfigurationCapabilities.BindingResource, captured.BindingId))
                    {
                        continue;
                    }
                    foreach (string release in refs)
                    {
                        if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead, ManagementConfigurationCapabilities.ReleaseResource, release))
                        {
                            captured = captured with
                            {
                                Origin = "protected",
                                ModifiedAtUtc = null
                            };
                            break;
                        }
                    }
                    if (captured.ModifiedAtUtc is not null && (captured.Origin is not ("shared" or "applicationOnly" or "applicationOverride" or "mixed")
                        || !await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead,
                            ManagementConfigurationCapabilities.SourceResource, row.ModifiedSourceId!)))
                    {
                        captured = captured with
                        {
                            ModifiedAtUtc = null
                        };
                    }
                    int bytes = ManagementConfigurationWire.EncodeResponse(captured, backend.ConfigurationWireLimits).Length + 1;
                    if (items.Count == size || bytes > remaining)
                    {
                        if (items.Count == 0)
                        {
                            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.response_too_large");
                        }
                        more = true;
                        break;
                    }
                    items.Add(captured);
                    remaining -= bytes;
                }
            }
            return ManagementConfigurationHttp.Json(new ManagementPage<ManagementConfigurationSummary>(items.AsReadOnly(),
                more ? cursors.Encode(endpoint, actor, size, items[^1].SourceId) : null), backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
    }
}
