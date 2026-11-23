using CIS.Management.Service.Metadata;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    static partial void AddMetadataFeatures(List<ManagementFeature> features)
    {
        features.Add(new("replaceApplicationMetadata", 1, "PUT", "/applications/{applicationId}/metadata",
            ["management.applications.metadata.write"]));
        features.Add(new("getMetadataOperation", 1, "GET", "/applications/{applicationId}/metadata-operations/{operationKey}",
            ["management.operations.read"]));
    }

    static partial void MapMetadata(RouteGroupBuilder group)
    {
        group.MapPut("/applications/{applicationId}/metadata", ReplaceMetadata);
        group.MapGet("/applications/{applicationId}/metadata-operations/{operationKey}", MetadataOperation);
    }

    private static async Task<IResult> ReplaceMetadata(HttpContext context, string applicationId,
        PostgreSqlManagementStore store, ManagementMetadataService metadata)
    {
        ManagementApplication? application = await VisibleApplication(context, applicationId, store,
            "management.applications.metadata.write");
        if (application is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        if (context.Request.Query.Count != 0 || context.Request.Headers["Idempotency-Key"].Count != 1
            || !ManagementWire.IsOperationKey(context.Request.Headers["Idempotency-Key"].ToString()))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        if (!context.Request.Headers.ContainsKey("If-Match"))
        {
            return ManagementHttp.Problem(context, 428, "management.precondition_required");
        }
        if (context.Request.Headers.IfMatch.Count != 1 || !ManagementWire.IsStrongETag(context.Request.Headers.IfMatch.ToString()))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        ManagementMetadataCommand? command = await ManagementHttp.Read<ManagementMetadataCommand>(context, store.Options.WireLimits);
        if (command is null)
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        ManagementMetadataResponse response = await metadata.ReplaceAsync(application,
            context.Request.Headers["Idempotency-Key"].ToString(), context.Request.Headers.IfMatch.ToString(),
            command, context.RequestAborted);
        return MetadataResponse(context, response, store.Options.WireLimits);
    }

    private static async Task<IResult> MetadataOperation(HttpContext context, string applicationId, string operationKey,
        PostgreSqlManagementStore store, ManagementMetadataService metadata)
    {
        ManagementApplication? application = await VisibleApplication(context, applicationId, store, "management.operations.read");
        if (application is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        if (context.Request.Query.Count != 0 || !ManagementWire.IsOperationKey(operationKey))
        {
            return ManagementHttp.Problem(context, 400, "management.invalid_request");
        }
        return MetadataResponse(context, await metadata.LookupAsync(application, operationKey, context.RequestAborted), store.Options.WireLimits);
    }

    private static IResult MetadataResponse(HttpContext context, ManagementMetadataResponse response, ManagementWireLimits limits)
    {
        if (response.Status == 200 && response.Result is { } result)
        {
            if (result.Result is { } value)
            {
                context.Response.Headers.ETag = value.MetadataETag;
            }
            return ManagementHttp.Json(result, limits: limits);
        }
        return ManagementHttp.Problem(context, response.Status, response.Code ?? "management.operation.unavailable",
            response.Operation, response.Result);
    }
}
