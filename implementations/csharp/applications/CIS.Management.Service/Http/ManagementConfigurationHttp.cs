using System.Buffers;
using System.Net.Mime;
using CIS.Management.Service.Configuration;
using CIS.Management.Wire;
using CIS.Security;
using Microsoft.AspNetCore.Http.Features;

namespace CIS.Management.Service.Http;

internal static class ManagementConfigurationHttp
{
    internal static bool HasRequestBody(HttpContext context)
        => context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody is true
            || context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count != 0;

    internal static async ValueTask<bool> Allowed(HttpContext context, string capability, string type, string id)
        => await ManagementConfigurationAuthorization.AllowedAsync(context.RequestServices.GetRequiredService<ICapabilityAuthorizer>(),
            context.RequestServices.GetRequiredService<Persistence.PostgreSqlManagementStore>(), capability, type, id, context.RequestAborted);

    internal static async ValueTask<bool> AllowedBundle(HttpContext context, IReadOnlyList<string> capabilities, string type, string id)
        => await ManagementConfigurationAuthorization.AllowedBundleAsync(context.RequestServices.GetRequiredService<ICapabilityAuthorizer>(),
            context.RequestServices.GetRequiredService<Persistence.PostgreSqlManagementStore>(), capabilities, type, id, context.RequestAborted);

    internal static IResult Json<T>(T value, int status = 200) where T : class
        => new Encoded(ManagementConfigurationWire.EncodeResponse(value), status,
            status >= 400 ? "application/problem+json" : MediaTypeNames.Application.Json);

    internal static IResult Json<T>(T value, ManagementConfigurationWireLimits limits) where T : class
        => new Encoded(ManagementConfigurationWire.EncodeResponse(value, limits), 200, MediaTypeNames.Application.Json);

    internal static IResult EditorContext(ManagementConfigurationEditorContext value, ManagementConfigurationWireLimits limits)
    {
        byte[] bytes = ManagementConfigurationWire.EncodeResponse(value, limits);
        ManagementConfigurationEditorContext captured = ManagementConfigurationWire.DecodeResponse<ManagementConfigurationEditorContext>(bytes, limits)!;
        if (captured.Defaults.State == "available"
            && !ManagementConfigurationStructure.Validate(captured.Shape, captured.Defaults.DefaultsJson!, complete: true).Valid)
        {
            throw new ArgumentException("The declared defaults do not match the immutable structural declaration.", nameof(value));
        }
        return new Encoded(bytes, 200, MediaTypeNames.Application.Json);
    }

    internal static IResult CandidatePreview(ManagementConfigurationCandidatePreview value, ManagementConfigurationWireLimits limits)
        => new Encoded(ManagementConfigurationWire.EncodeResponse(value, limits), 200, MediaTypeNames.Application.Json);

    internal static IResult Actions(ManagementConfigurationActions value)
        => new Encoded(ManagementSchedulerWire.EncodeResponse(value), 200, MediaTypeNames.Application.Json);

    internal static IResult Cohorts(ManagementPage<ManagementConfigurationCohortChoice> value)
        => new Encoded(ManagementSchedulerWire.EncodeResponse(value), 200, MediaTypeNames.Application.Json);

    internal static IResult Problem(HttpContext context, int status, string code,
        ManagementOperationReference? operation = null, ManagementConfigurationOperationResult? result = null)
        => Json(new ManagementConfigurationProblem("urn:cis:management:problem:" + code,
            "Configuration request could not be completed", status, code,
            CorrelationId: ManagementWire.IsScalarText(context.TraceIdentifier, 128) ? context.TraceIdentifier : null,
            Operation: operation, Result: result), status);

    internal static IResult Operation(HttpContext context, ManagementConfigurationResponse response)
    {
        if (response.Status == 200 && response.Result is { } result)
        {
            if (result.Result?.ETag is { } etag)
            {
                context.Response.Headers.ETag = etag;
            }
            return Json(result);
        }
        return Problem(context, response.Status, response.Code ?? "management.configuration.unavailable", response.Operation, response.Result);
    }

    internal static bool MutationHeaders(HttpContext context, out string key, out string etag, out IResult? refusal, bool schemaQuery = false)
    {
        key = context.Request.Headers["Idempotency-Key"].ToString();
        etag = context.Request.Headers.IfMatch.ToString();
        refusal = null;
        bool invalidQuery = schemaQuery ? context.Request.Query.Count != 1 || !context.Request.Query.ContainsKey("schemaVersion")
            : context.Request.Query.Count != 0;
        if (invalidQuery || context.Request.Headers["Idempotency-Key"].Count != 1 || !ManagementWire.IsOperationKey(key))
        {
            refusal = Problem(context, 400, "management.configuration.invalid_request");
        }
        else if (!context.Request.Headers.ContainsKey("If-Match"))
        {
            refusal = Problem(context, 428, "management.configuration.precondition_required");
        }
        else if (context.Request.Headers.IfMatch.Count != 1 || !ManagementWire.IsStrongETag(etag))
        {
            refusal = Problem(context, 400, "management.configuration.invalid_request");
        }
        return refusal is null;
    }

    internal static async Task<T?> Read<T>(HttpContext context) where T : class
    {
        const int maximum = 1024 * 1024;
        if (context.Request.ContentType is not { } contentType || contentType.Length > 128
            || !string.Equals(contentType.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadHttpRequestException("A JSON request is required.", StatusCodes.Status415UnsupportedMediaType);
        }
        if (context.Request.ContentLength > maximum)
        {
            throw new BadHttpRequestException("The configuration request exceeds its finite byte limit.", StatusCodes.Status413PayloadTooLarge);
        }
        byte[] buffer = ArrayPool<byte>.Shared.Rent(maximum + 1);
        try
        {
            int count = 0;
            while (count <= maximum)
            {
                int read = await context.Request.Body.ReadAsync(buffer.AsMemory(count, maximum + 1 - count), context.RequestAborted);
                if (read == 0)
                {
                    return ManagementConfigurationWire.DecodeRequest<T>(buffer.AsMemory(0, count));
                }
                count += read;
            }
            throw new BadHttpRequestException("The configuration request exceeds its finite byte limit.", StatusCodes.Status413PayloadTooLarge);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private sealed record Encoded(byte[] Bytes, int Status, string ContentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.StatusCode = Status;
            context.Response.ContentType = ContentType;
            context.Response.ContentLength = Bytes.Length;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.Body.WriteAsync(Bytes, context.RequestAborted);
        }
    }
}
