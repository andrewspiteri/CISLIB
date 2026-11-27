using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] ConfigurationReleaseHistoryFeatures =
    [
        new("listConfigurationReleases", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/releases",
            [ManagementConfigurationCapabilities.DefinitionsRead])
    ];
    private static readonly ManagementFeature[] ConfigurationBindingStatusFeatures =
    [
        new("getConfigurationBindingStatus", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/status",
            [ManagementConfigurationCapabilities.AdoptionRead])
    ];

    private static void MapConfigurationBindingReads(RouteGroupBuilder group)
    {
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/releases", ConfigurationReleases);
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/status", ConfigurationBindingStatus);
    }

    private static async Task<IResult> ConfigurationReleases(HttpContext context, string applicationId, string configurationId,
        IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        if (!BindingReadRequest(context, applicationId, configurationId, out string schema, out int size, out string? cursor))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        (ManagementConfigurationBindingResource? binding, IResult? failure) = await InspectionBinding(context, backend,
            applicationId, configurationId, schema, ManagementConfigurationCapabilities.DefinitionsRead);
        if (failure is not null)
        {
            return failure;
        }
        if (backend is not IManagementConfigurationReleaseHistoryBackend releases)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.releases.unsupported");
        }
        string endpoint = "configuration.releases." + binding!.BindingId;
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, binding.BindingId, principal: context.User);
        if (!cursors.TryDecodeConfigurationRelease(cursor, endpoint, actor, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        var items = new List<ManagementConfigurationRelease>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        int remaining = backend.ConfigurationWireLimits.MaximumBodyBytes - 8192;
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
                ManagementStoreResult<IReadOnlyList<ManagementConfigurationRelease>> result = await releases.ReadReleasesAsync(
                    binding, after, take, context.RequestAborted);
                if (!CleanConfigurationRead(result) || result.HasMore)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                if (!result.IsKnownCommitted || result.Value is not { } values)
                {
                    return InspectionRefusal(context, result.Code, result.Value is null && !result.PreviouslyCommitted);
                }
                if (values.Count < 0 || values.Count > take)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                var rows = new List<ManagementConfigurationRelease>();
                using (IEnumerator<ManagementConfigurationRelease> iterator = values.GetEnumerator())
                {
                    while (rows.Count <= take && iterator.MoveNext())
                    {
                        rows.Add(iterator.Current);
                    }
                }
                if (rows.Count != values.Count || rows.Count > take)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                ended = rows.Count < take;
                foreach (ManagementConfigurationRelease row in rows)
                {
                    scanned++;
                    if (row is null)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    byte[] bytes = ManagementConfigurationWire.EncodeResponse(row, backend.ConfigurationWireLimits);
                    ManagementConfigurationRelease captured = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationRelease>(
                        bytes, backend.ConfigurationWireLimits)!;
                    string position = ManagementConfigurationReleasePosition.Create(captured);
                    if (captured.ApplicationId != binding.ApplicationId || captured.ConfigurationId != binding.ConfigurationId
                        || captured.SchemaVersion != binding.SchemaVersion || captured.Environment != binding.Environment
                        || !identities.Add(captured.ReleaseId)
                        || after is not null && ManagementConfigurationReleasePosition.Compare(position, after) >= 0)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                    }
                    after = position;
                    if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead,
                        ManagementConfigurationCapabilities.ReleaseResource, captured.ReleaseId))
                    {
                        continue;
                    }
                    if (items.Count == size || bytes.Length + 1 > remaining)
                    {
                        if (items.Count == 0)
                        {
                            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.response_too_large");
                        }
                        more = true;
                        break;
                    }
                    remaining -= bytes.Length + 1;
                    items.Add(captured);
                }
            }
            context.RequestAborted.ThrowIfCancellationRequested();
            return ManagementConfigurationHttp.Json(new ManagementPage<ManagementConfigurationRelease>(items.AsReadOnly(),
                more ? cursors.EncodeConfigurationRelease(endpoint, actor, size, ManagementConfigurationReleasePosition.Create(items[^1])) : null),
                backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
    }

    private static async Task<IResult> ConfigurationBindingStatus(HttpContext context, string applicationId, string configurationId,
        IManagementBackend applications, IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        if (!BindingReadRequest(context, applicationId, configurationId, out string schema, out int size, out string? cursor))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications, ManagementConfigurationCapabilities.AdoptionRead);
        if (application is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        (ManagementConfigurationBindingResource? binding, IResult? failure) = await InspectionBinding(context, backend,
            applicationId, configurationId, schema, null);
        if (failure is not null)
        {
            return failure;
        }
        if (application.ApplicationId != binding!.ApplicationId || application.Application != binding.Application
            || application.Environment != binding.Environment)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (backend is not IManagementConfigurationBindingAdoptionBackend adoption)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.binding_status.unsupported");
        }
        string endpoint = "configuration.binding-status." + binding.BindingId;
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, binding.BindingId, principal: context.User);
        if (!cursors.TryDecodeConfigurationAdoption(cursor, endpoint, actor, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>> result = await adoption.ReadBindingAdoptionAsync(
            binding, after, size, context.RequestAborted);
        if (!CleanConfigurationRead(result))
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (!result.IsKnownCommitted || result.Value is not { } values)
        {
            return InspectionRefusal(context, result.Code, result.Value is null && !result.PreviouslyCommitted);
        }
        if (values.Count < 0 || values.Count > size)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        var rows = new List<ManagementConfigurationAdoptionRow>();
        using (IEnumerator<ManagementConfigurationAdoptionRow> iterator = values.GetEnumerator())
        {
            while (rows.Count <= size && iterator.MoveNext())
            {
                rows.Add(iterator.Current);
            }
        }
        if (rows.Count != values.Count || rows.Count > size || result.HasMore && rows.Count == 0)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        try
        {
            string? previous = after;
            var entries = new List<ManagementConfigurationAdoptionEntry>();
            foreach (ManagementConfigurationAdoptionRow row in rows)
            {
                if (row?.Entry is not { } entry || row.Position is null)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                string expected = entry.ConfigurationId + "/" + entry.SchemaVersion + "/" + (entry.CohortId ?? "") + "/" + (entry.RegistrationId ?? "");
                if (row.Position != expected || previous is not null && StringComparer.Ordinal.Compare(row.Position, previous) <= 0
                    || entry.ApplicationId != binding.ApplicationId || entry.ConfigurationId != binding.ConfigurationId
                    || entry.SchemaVersion != binding.SchemaVersion || entry.Environment != binding.Environment)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                previous = row.Position;
                entries.Add(entry);
            }
            ManagementPage<ManagementConfigurationAdoptionEntry> captured = ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationAdoptionEntry>>(
                ManagementConfigurationWire.EncodeResponse(new ManagementPage<ManagementConfigurationAdoptionEntry>(entries.AsReadOnly()),
                    backend.ConfigurationWireLimits), backend.ConfigurationWireLimits)!;
            context.RequestAborted.ThrowIfCancellationRequested();
            return ManagementConfigurationHttp.Json(captured with
            {
                NextCursor = result.HasMore ? cursors.EncodeConfigurationAdoption(endpoint, actor, size, rows[^1].Position) : null
            }, backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
    }

    private static bool BindingReadRequest(HttpContext context, string application, string configuration,
        out string schema, out int size, out string? cursor)
    {
        schema = "";
        size = 100;
        cursor = null;
        return ManagementWire.IsUuid(application) && ManagementWire.IsOperationKey(configuration)
            && ManagementDiscoveryQuery.TryReadConfigurationPage(context, out schema, out size, out cursor)
            && !ManagementConfigurationHttp.HasRequestBody(context);
    }

    private static async Task<(ManagementConfigurationBindingResource? Binding, IResult? Failure)> InspectionBinding(
        HttpContext context, IManagementConfigurationBackend backend, string application, string configuration, string schema, string? capability)
    {
        ManagementStoreResult<ManagementConfigurationBindingResource> result = await backend.ReadBindingAsync(application, configuration, schema,
            context.RequestAborted);
        if (!CleanConfigurationRead(result) || result.HasMore)
        {
            return (null, ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable"));
        }
        if (!result.IsKnownCommitted || result.Value is not { } binding)
        {
            return (null, InspectionRefusal(context, result.Code, result.Value is null && !result.PreviouslyCommitted));
        }
        if (!binding.Enabled)
        {
            return (null, ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found"));
        }
        if (binding.ApplicationId != application || binding.ConfigurationId != configuration || binding.SchemaVersion != schema
            || !ManagementWire.IsUuid(binding.BindingId) || !ManagementWire.IsScalarText(binding.Application, 255)
            || !ManagementWire.IsScalarText(binding.Environment, 255) || !ManagementWire.IsScalarText(binding.HostApplicationName, 255))
        {
            return (null, ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable"));
        }
        if (capability is not null && !await ManagementConfigurationHttp.Allowed(context, capability,
            ManagementConfigurationCapabilities.BindingResource, binding.BindingId))
        {
            return (null, ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found"));
        }
        return (binding, null);
    }

    private static bool CleanConfigurationRead<T>(ManagementStoreResult<T>? result)
        => result?.AttemptCompletion is
        {
            Outcome: TransactionOutcome.NotCommitted, CommitAttempted: false, RollbackAttempted: true,
            RollbackConfirmed: true, Error: null, CleanupErrors.Count: 0
        };

    private static IResult InspectionRefusal(HttpContext context, string? code, bool noKnownValue)
        => code == "management.configuration.not_found" && noKnownValue
            ? ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found")
            : ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
}

