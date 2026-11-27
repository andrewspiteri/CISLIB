using CIS.Management.Service.Persistence;
using CIS.Management.Service.Backends;
using CIS.Management.Wire;
using CIS.Security;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    internal static IReadOnlyList<ManagementFeature> Features
    {
        get
        {
            var features = new List<ManagementFeature>
            {
                new("getServiceInfo", 1, "GET", "/service-info", []),
                new("registerInstance", 1, "PUT", "/registrations/{registrationId}", ["management.registrations.write"]),
                new("getRegistrationControl", 1, "GET", "/registrations/{registrationId}/control", ["management.registrations.write"]),
                new("closeRegistration", 1, "DELETE", "/registrations/{registrationId}", ["management.registrations.write"]),
                new("reportObservation", 1, "PUT", "/registrations/{registrationId}/observation", ["management.registrations.write"]),
                new("replaceInventory", 1, "PUT", "/registrations/{registrationId}/inventory", ["management.registrations.write"]),
                new("listApplications", 1, "GET", "/applications", ["management.applications.read"]),
                new("getApplication", 1, "GET", "/applications/{applicationId}", ["management.applications.read"]),
                new("listInstances", 1, "GET", "/applications/{applicationId}/instances", ["management.applications.read"]),
                new("listInventory", 1, "GET", "/applications/{applicationId}/inventory", ["management.applications.read"]),
                new("getApplicationCapabilities", 1, "GET", "/applications/{applicationId}/capabilities", ["management.applications.read"])
            };
            AddMetadataFeatures(features);
            return features.AsReadOnly();
        }
    }

    internal static void Map(WebApplication web, ManagementServiceOptions options)
    {
        var admission = new Admission(options.MaximumConcurrentRequests);
        PathString prefix = new(options.BasePath + "/management/v1");
        web.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(prefix))
            {
                await next(context);
                return;
            }
            if (ManagementHttp.Caller(context) is null)
            {
                await ManagementHttp.Problem(context, 401, "management.unauthenticated").ExecuteAsync(context);
                return;
            }
            if (!admission.Enter())
            {
                await ManagementHttp.Problem(context, 503, "management.capacity.exceeded").ExecuteAsync(context);
                return;
            }
            try
            {
                await next(context);
            }
            catch (ManagementRequestException error) when (!context.Response.HasStarted)
            {
                await ManagementHttp.Problem(context, error.Status, error.Code).ExecuteAsync(context);
            }
            catch (BadHttpRequestException error) when (!context.Response.HasStarted)
            {
                await ManagementHttp.Problem(context, error.StatusCode, error.StatusCode == 413
                    ? "management.request_too_large" : "management.invalid_request").ExecuteAsync(context);
            }
            catch (ManagementBackendException error) when (!context.Response.HasStarted)
            {
                await ManagementHttp.Problem(context, 503, error.Code).ExecuteAsync(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (!context.Response.HasStarted)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CIS.Management.Service")
                    .LogError("Management request failed; correlation {CorrelationId}.", context.TraceIdentifier);
                await ManagementHttp.Problem(context, 500, "management.internal_error").ExecuteAsync(context);
            }
            finally
            {
                admission.Exit();
            }
        });

        RouteGroupBuilder group = web.MapGroup(prefix.Value!);
        group.MapGet("/service-info", async (HttpContext context, IManagementBackend backend, IManagementConfigurationBackend configuration) => ManagementHttp.Json(new ManagementServiceInfo(
            EnvironmentOptions.ServiceVersion, [1], Array.AsReadOnly(Features.Concat(DiscoveryFeatures(backend)).Concat(await ConfigurationFeatures(configuration, context.RequestAborted)).ToArray()))
        {
            MaximumInventoryRequestBytes = backend.MaximumInventoryRequestBytes
        }, limits: backend.WireLimits));
        group.MapPut("/registrations/{registrationId}", Register);
        group.MapGet("/registrations/{registrationId}/control", Control);
        group.MapPut("/registrations/{registrationId}/observation", Observation);
        group.MapPut("/registrations/{registrationId}/inventory", Inventory);
        group.MapDelete("/registrations/{registrationId}", Close);
        group.MapGet("/applications", Applications);
        group.MapGet("/environments", Environments);
        group.MapGet("/applications/{applicationId}", Application);
        group.MapGet("/applications/{applicationId}/instances", Instances);
        group.MapGet("/applications/{applicationId}/inventory", Inventories);
        group.MapGet("/applications/{applicationId}/capabilities", Capabilities);
        MapMetadata(group);
        MapConfiguration(group, options);
    }

    private static async Task<IResult> Control(HttpContext context, string registrationId, IManagementBackend store)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementHttp.Problem(context, 403, "management.registration.forbidden");
        }
        if (!ManagementWire.IsUuid(registrationId) || context.Request.Query.Count != 1
            || context.Request.Query.Keys.Any(key => key != "attemptId") || context.Request.Query["attemptId"].Count != 1
            || !ManagementWire.IsUuid(context.Request.Query["attemptId"].ToString())
            || context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count != 0)
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        return ManagementHttp.Store(context, await store.ConfirmControlAsync(owner, registrationId,
            context.Request.Query["attemptId"].ToString(), context.RequestAborted), store.WireLimits);
    }

    private static async Task<IResult> Register(HttpContext context, string registrationId, IManagementBackend store)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementHttp.Problem(context, 403, "management.registration.forbidden");
        }
        if (!ManagementWire.IsUuid(registrationId))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        ManagementRegistrationRequest? request = await ManagementHttp.Read<ManagementRegistrationRequest>(context, store.WireLimits);
        return request is null ? ManagementHttp.Problem(context, 400, "management.invalid_request")
            : ManagementHttp.Store(context, await store.RegisterAsync(owner, registrationId, request, context.RequestAborted), store.WireLimits);
    }

    private static async Task<IResult> Observation(HttpContext context, string registrationId, IManagementBackend store)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementHttp.Problem(context, 403, "management.registration.forbidden");
        }
        if (!ManagementWire.IsUuid(registrationId))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        ManagementObservation? request = await ManagementHttp.Read<ManagementObservation>(context, store.WireLimits);
        return request is null ? ManagementHttp.Problem(context, 400, "management.invalid_request")
            : ManagementHttp.Store(context, await store.ReportObservationAsync(owner, registrationId, request, context.RequestAborted), store.WireLimits);
    }

    private static async Task<IResult> Inventory(HttpContext context, string registrationId, IManagementBackend store)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementHttp.Problem(context, 403, "management.registration.forbidden");
        }
        if (!ManagementWire.IsUuid(registrationId))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        ManagementInventorySnapshot? request = await ManagementHttp.Read<ManagementInventorySnapshot>(context, store.WireLimits);
        return request is null ? ManagementHttp.Problem(context, 400, "management.invalid_request")
            : ManagementHttp.Store(context, await store.ReportInventoryAsync(owner, registrationId, request, context.RequestAborted), store.WireLimits);
    }

    private static async Task<IResult> Close(HttpContext context, string registrationId, IManagementBackend store)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementHttp.Problem(context, 403, "management.registration.forbidden");
        }
        return !ManagementWire.IsUuid(registrationId) ? ManagementHttp.Problem(context, 400, "management.invalid_request")
            : ManagementHttp.Store(context, await store.CloseAsync(owner, registrationId, context.RequestAborted), store.WireLimits);
    }

    private static async Task<ManagementRegistrationOwner?> RegistrationOwner(HttpContext context)
    {
        CallerContext? caller = ManagementHttp.Caller(context);
        if (caller is null || caller.ActorKind != SecurityActorKind.Application || caller.Application is null || caller.Environment is null
            || !await ManagementHttp.Allowed(context, "management.registrations.write"))
        {
            return null;
        }
        string evidence = caller.Instance.Kind switch
        {
            InstanceEvidenceKind.Verified => "verified",
            InstanceEvidenceKind.Reported => "reported",
            _ => "notProvided"
        };
        return new(caller.Application, caller.Environment, caller.Subject.Issuer, caller.Subject.Identifier,
            new(evidence, caller.Instance.ReportedIdentifier, caller.Instance.VerifiedIdentifier));
    }

    internal static async Task<ManagementApplication?> VisibleApplication(HttpContext context, string applicationId,
        IManagementBackend store, string capability = "management.applications.read")
    {
        if (!ManagementWire.IsUuid(applicationId))
        {
            return null;
        }
        ManagementStoreResult<ManagementApplication> result = await store.ReadApplicationAsync(applicationId, context.RequestAborted);
        if (result.Code is not null && result.Code is not ("management.not_found" or "management.application.not_found"))
        {
            throw new ManagementRequestException(503, result.Code);
        }
        return result.Value is { } application && result.IsKnownCommitted
            && await ManagementHttp.Allowed(context, capability, application.ApplicationId) ? application : null;
    }

    private static async Task<IResult> Application(HttpContext context, string applicationId, IManagementBackend store)
    {
        ManagementApplication? application = await VisibleApplication(context, applicationId, store);
        if (application is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        context.Response.Headers.ETag = application.MetadataETag;
        return ManagementHttp.Json(application, limits: store.WireLimits);
    }

    private static async Task<IResult> Applications(HttpContext context, IManagementBackend store, ManagementCursor cursors)
    {
        const string endpoint = "applications";
        if (!ManagementDiscoveryQuery.TryRead(context, filters: true, out ManagementApplicationQuery query, out int size, out string? rawCursor))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_query");
        }
        string scope = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, query: query, principal: context.User);
        if (!cursors.TryDecode(rawCursor, endpoint, scope, size, out string? after))
        {
            return ManagementHttp.Problem(context, 400, "management.cursor.invalid");
        }
        bool filtered = query.Query is not null || query.Environment is not null;
        if (filtered && store is not IManagementDiscoveryBackend)
        {
            return ManagementHttp.Problem(context, 501, "management.discovery.unsupported");
        }
        var items = new List<ManagementApplication>();
        int remaining = store.WireLimits.MaximumRegularResponseBytes - 4096;
        bool more = false;
        int scanned = 0;
        while (true)
        {
            if (scanned >= DiscoveryScanLimit)
            {
                return ManagementHttp.Problem(context, 503, "management.discovery.scan_limit");
            }
            ManagementStoreResult<IReadOnlyList<ManagementApplication>> batch = filtered
                ? await ((IManagementDiscoveryBackend)store).QueryApplicationsAsync(query, after, 16, context.RequestAborted)
                : await store.ReadApplicationsAsync(after, 16, context.RequestAborted);
            if (batch.Code is not null || batch.Value is null || !batch.IsKnownCommitted)
            {
                return ManagementHttp.Problem(context, 503, batch.Code ?? "management.transaction_unknown");
            }
            foreach (ManagementApplication candidate in batch.Value)
            {
                scanned++;
                after = candidate.ApplicationId;
                if (!await ManagementHttp.Allowed(context, "management.applications.read", candidate.ApplicationId))
                {
                    continue;
                }
                int bytes = ManagementWire.EncodeResponse(candidate, store.WireLimits).Length + 1;
                if (items.Count == size || bytes > remaining)
                {
                    if (items.Count == 0)
                    {
                        return ManagementHttp.Problem(context, 503, "management.response_too_large");
                    }
                    more = true;
                    break;
                }
                items.Add(candidate);
                remaining -= bytes;
            }
            if (more || batch.Value.Count < 16)
            {
                break;
            }
        }
        return ManagementHttp.Json(new ManagementPage<ManagementApplication>(items.AsReadOnly(), more
            ? cursors.Encode(endpoint, scope, size, items[^1].ApplicationId) : null), limits: store.WireLimits);
    }

    private static async Task<IResult> Instances(HttpContext context, string applicationId, IManagementBackend store, ManagementCursor cursors)
    {
        if (await VisibleApplication(context, applicationId, store) is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        const string endpoint = "instances";
        if (!ManagementHttp.TryPage(context, cursors, endpoint, applicationId, out int size, out string? after))
        {
            return ManagementHttp.Problem(context, 400, "management.cursor.invalid");
        }
        var items = new List<ManagementInstance>();
        int remaining = store.WireLimits.MaximumRegularResponseBytes - 4096;
        bool more = false;
        while (true)
        {
            ManagementStoreResult<IReadOnlyList<ManagementInstance>> batch = await store.ReadInstancesAsync(applicationId, after, 16, context.RequestAborted);
            if (batch.Code is not null || batch.Value is null || !batch.IsKnownCommitted)
            {
                return ManagementHttp.Problem(context, 503, batch.Code ?? "management.transaction_unknown");
            }
            foreach (ManagementInstance candidate in batch.Value)
            {
                int bytes = ManagementWire.EncodeResponse(candidate, store.WireLimits).Length + 1;
                if (items.Count == size || bytes > remaining)
                {
                    if (items.Count == 0)
                    {
                        return ManagementHttp.Problem(context, 503, "management.response_too_large");
                    }
                    more = true;
                    break;
                }
                items.Add(candidate);
                after = candidate.RegistrationId;
                remaining -= bytes;
            }
            if (more || batch.Value.Count < 16)
            {
                break;
            }
        }
        return ManagementHttp.Json(new ManagementPage<ManagementInstance>(items.AsReadOnly(), more
            ? ManagementHttp.Cursor(context, cursors, endpoint, applicationId, size, items[^1].RegistrationId) : null), limits: store.WireLimits);
    }

    private static async Task<IResult> Inventories(HttpContext context, string applicationId, IManagementBackend store, ManagementCursor cursors)
    {
        if (await VisibleApplication(context, applicationId, store) is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        const string endpoint = "inventory";
        if (!ManagementHttp.TryPage(context, cursors, endpoint, applicationId, out int size, out string? after))
        {
            return ManagementHttp.Problem(context, 400, "management.cursor.invalid");
        }
        ManagementStoreResult<IReadOnlyList<ManagementInventoryEntry>> result = await store.ReadInventoryAsync(applicationId,
            after, size + 1, store.WireLimits.MaximumInventoryResponseBytes - 4096, context.RequestAborted);
        if (result.Code is not null || result.Value is null || !result.IsKnownCommitted)
        {
            return ManagementHttp.Problem(context, 503, result.Code ?? "management.transaction_unknown");
        }
        ManagementInventoryEntry[] items = result.Value.Take(size).ToArray();
        bool more = result.HasMore || result.Value.Count > size;
        return ManagementHttp.Json(new ManagementPage<ManagementInventoryEntry>(Array.AsReadOnly(items), more && items.Length != 0
            ? ManagementHttp.Cursor(context, cursors, endpoint, applicationId, size, items[^1].RegistrationId) : null), limits: store.WireLimits);
    }

    private static async Task<IResult> Capabilities(HttpContext context, string applicationId, IManagementBackend store)
    {
        if (await VisibleApplication(context, applicationId, store) is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        var hints = new List<ManagementOperationHint>();
        IReadOnlyList<ManagementFeature> implemented = Array.AsReadOnly(Features.Concat(DiscoveryFeatures(store)).ToArray());
        foreach (ManagementFeature feature in implemented)
        {
            bool allowed = true;
            foreach (string capability in feature.RequiredCapabilities)
            {
                allowed &= await ManagementHttp.Allowed(context, capability,
                    capability == "management.registrations.write" ? null : applicationId);
            }
            hints.Add(new(feature.OperationId, allowed));
        }
        IReadOnlyList<ManagementFeature> configuration = await ConfigurationFeatures(
            context.RequestServices.GetRequiredService<IManagementConfigurationBackend>(), context.RequestAborted);
        // Source-scoped hints require a discovered actual source; application grants cannot synthesize them.
        return ManagementHttp.Json(new ManagementCapabilities(applicationId, Array.AsReadOnly(implemented.Concat(configuration).ToArray()),
            hints.AsReadOnly()), limits: store.WireLimits);
    }

    static partial void MapMetadata(RouteGroupBuilder group);
    static partial void AddMetadataFeatures(List<ManagementFeature> features);

    private sealed class Admission(int maximum)
    {
        private int active;

        internal bool Enter()
        {
            if (Interlocked.Increment(ref active) <= maximum)
            {
                return true;
            }
            Interlocked.Decrement(ref active);
            return false;
        }

        internal void Exit() => Interlocked.Decrement(ref active);
    }
}
