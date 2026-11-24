using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] SnapshotFeatures =
    [
        new("getRegistrationConfiguration", 1, "GET", "/registrations/{registrationId}/configuration/{configurationId}", ["management.registrations.write"])
    ];

    static partial void MapConfigurationSnapshots(RouteGroupBuilder group)
        => group.MapGet("/registrations/{registrationId}/configuration/{configurationId}", RegistrationConfiguration);

    private static async Task<IResult> RegistrationConfiguration(HttpContext context, string registrationId, string configurationId,
        IManagementConfigurationBackend backend)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementConfigurationHttp.Problem(context, 403, "management.registration.forbidden");
        }
        string schema = context.Request.Query["schemaVersion"].ToString();
        bool conditional = context.Request.Headers.ContainsKey("If-None-Match");
        string? etag = conditional ? context.Request.Headers.IfNoneMatch.ToString() : null;
        if (!ManagementWire.IsUuid(registrationId) || !PostgreSqlManagementStore.SnapshotPortableId(configurationId)
            || context.Request.Query.Count != 1 || context.Request.Query.Keys.Any(key => key != "schemaVersion")
            || context.Request.Query["schemaVersion"].Count != 1 || !PostgreSqlManagementStore.SnapshotSchema(schema, out _)
            || conditional && (context.Request.Headers.IfNoneMatch.Count != 1 || !ManagementWire.IsStrongETag(etag!))
            || context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count != 0)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementStoreResult<ManagementResolvedConfiguration> result = await backend.ReadRegistrationConfigurationAsync(owner,
            registrationId, configurationId, schema, etag, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } resolved)
        {
            return ManagementConfigurationHttp.Problem(context, result.Code == "management.configuration.not_found" ? 404
                : result.Code == "management.registration.closed" ? 409 : 503, result.Code ?? "management.configuration.unavailable");
        }
        context.Response.Headers.ETag = resolved.ETag;
        context.Response.Headers.CacheControl = "no-store";
        return resolved.NotModified ? Results.StatusCode(StatusCodes.Status304NotModified)
            : ManagementConfigurationHttp.Json(resolved.Snapshot!);
    }
}
