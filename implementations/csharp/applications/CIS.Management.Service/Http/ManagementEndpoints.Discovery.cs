using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private const int DiscoveryScanLimit = 4096;

    private static IReadOnlyList<ManagementFeature> DiscoveryFeatures(IManagementBackend backend)
        => backend is IManagementDiscoveryBackend
            ? [new("queryApplications", 1, "GET", "/applications", ["management.applications.read"]),
                new("listEnvironments", 1, "GET", "/environments", ["management.applications.read"])] : [];

    private static async Task<IResult> Environments(HttpContext context, IManagementBackend backend, ManagementCursor cursors)
    {
        if (backend is not IManagementDiscoveryBackend discovery)
        {
            return ManagementHttp.Problem(context, 501, "management.discovery.unsupported");
        }
        const string endpoint = "environments";
        string scope = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, principal: context.User);
        if (!ManagementDiscoveryQuery.TryRead(context, filters: false, out _, out int size, out string? rawCursor)
            || !cursors.TryDecodeEnvironment(rawCursor, endpoint, scope, size, out string? afterEnvironment))
        {
            return ManagementHttp.Problem(context, 400, "management.cursor.invalid");
        }
        var items = new List<ManagementEnvironment>();
        string? afterApplication = null;
        string? disclosed = null;
        int remaining = backend.WireLimits.MaximumRegularResponseBytes - 4096;
        int scanned = 0;
        while (true)
        {
            if (scanned >= DiscoveryScanLimit)
            {
                return ManagementHttp.Problem(context, 503, "management.discovery.scan_limit");
            }
            ManagementStoreResult<IReadOnlyList<ManagementApplication>> batch = await discovery.ReadEnvironmentCandidatesAsync(
                afterEnvironment, afterApplication, 16, context.RequestAborted);
            if (!batch.IsKnownCommitted || batch.Value is null || batch.Code is not null)
            {
                return ManagementHttp.Problem(context, 503, batch.Code ?? "management.transaction_unknown");
            }
            foreach (ManagementApplication candidate in batch.Value)
            {
                scanned++;
                if (candidate.Environment == disclosed)
                {
                    continue;
                }
                afterEnvironment = candidate.Environment;
                afterApplication = candidate.ApplicationId;
                if (!await ManagementHttp.Allowed(context, "management.applications.read", candidate.ApplicationId))
                {
                    continue;
                }
                var visible = new ManagementEnvironment(candidate.Environment);
                int bytes = ManagementWire.EncodeResponse(visible, backend.WireLimits).Length + 1;
                if (items.Count == size || bytes > remaining)
                {
                    return items.Count == 0 ? ManagementHttp.Problem(context, 503, "management.response_too_large")
                        : ManagementHttp.Json(new ManagementPage<ManagementEnvironment>(items.AsReadOnly(),
                            cursors.EncodeEnvironment(endpoint, scope, size, items[^1].Environment)), limits: backend.WireLimits);
                }
                items.Add(visible);
                remaining -= bytes;
                disclosed = candidate.Environment;
                // The next query skips this completed group, not hidden application IDs.
                afterApplication = null;
            }
            if (batch.Value.Count < 16)
            {
                return ManagementHttp.Json(new ManagementPage<ManagementEnvironment>(items.AsReadOnly()), limits: backend.WireLimits);
            }
        }
    }

    private static async Task<IResult> ApplicationConfigurationDefinitions(HttpContext context, string applicationId,
        IManagementBackend applications, IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications);
        if (application is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (backend is not IManagementApplicationConfigurationBackend discovery)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.discovery.unsupported");
        }
        const string endpoint = "configuration.application-definitions";
        string scope = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, applicationId,
            new(Environment: application.Environment), context.User);
        if (!ManagementDiscoveryQuery.TryRead(context, filters: false, out _, out int size, out string? rawCursor)
            || !cursors.TryDecode(rawCursor, endpoint, scope, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        var items = new List<ManagementConfigurationDefinition>();
        int remaining = backend.ConfigurationWireLimits.MaximumBodyBytes - 4096;
        int scanned = 0;
        bool more = false;
        while (true)
        {
            if (scanned >= DiscoveryScanLimit)
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.discovery.scan_limit");
            }
            ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>> batch = await discovery.ReadApplicationDefinitionsAsync(
                applicationId, application.Environment, after, 16, context.RequestAborted);
            if (!batch.IsKnownCommitted || batch.Value is null || batch.Code is not null)
            {
                return ManagementConfigurationHttp.Problem(context, 503, batch.Code ?? "management.configuration.unavailable");
            }
            foreach (ManagementConfigurationDefinition row in batch.Value)
            {
                scanned++;
                after = row.SourceId;
                if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead,
                    ManagementConfigurationCapabilities.SourceResource, row.SourceId))
                {
                    continue;
                }
                ManagementConfigurationDefinition visible = row with
                {
                    DefaultsJson = null
                };
                int bytes = ManagementConfigurationWire.EncodeResponse(visible).Length + 1;
                if (items.Count == size || bytes > remaining)
                {
                    if (items.Count == 0)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.response_too_large");
                    }
                    more = true;
                    break;
                }
                items.Add(visible);
                remaining -= bytes;
            }
            if (more || batch.Value.Count < 16)
            {
                break;
            }
        }
        return ManagementConfigurationHttp.Json(new ManagementPage<ManagementConfigurationDefinition>(items.AsReadOnly(), more
            ? cursors.Encode(endpoint, scope, size, items[^1].SourceId) : null));
    }
}
