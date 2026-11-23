using System.Buffers;
using System.Globalization;
using System.Net.Mime;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Security;

namespace CIS.Management.Service.Http;

internal static class ManagementHttp
{
    internal static CallerContext? Caller(HttpContext context)
        => context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current;

    internal static CapabilityTarget Target(string? applicationId = null)
        => new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
            applicationId is null ? null : "management.application", applicationId);

    internal static async ValueTask<bool> Allowed(HttpContext context, string capability, string? applicationId = null)
        => (await context.RequestServices.GetRequiredService<ICapabilityAuthorizer>().AuthorizeAsync(
            capability, Target(applicationId), context.RequestAborted)).IsAllowed;

    internal static IResult Problem(HttpContext context, int status, string code,
        ManagementOperationReference? operation = null, ManagementMetadataResult? result = null)
        => Json(new ManagementProblem("urn:cis:management:problem:" + code, "Management request could not be completed",
            status, code, CorrelationId: ManagementWire.IsScalarText(context.TraceIdentifier, 128) ? context.TraceIdentifier : null,
            Operation: operation, Result: result), status);

    internal static IResult Json<T>(T value, int status = 200, ManagementWireLimits? limits = null) where T : class
        => new EncodedResult(ManagementWire.EncodeResponse(value, limits), status,
            status >= 400 ? "application/problem+json" : MediaTypeNames.Application.Json);

    internal static IResult Store<T>(HttpContext context, ManagementStoreResult<T> result, ManagementWireLimits? limits = null)
        where T : class
    {
        if (result.Value is not null && result.Code is null && result.IsKnownCommitted)
        {
            return Json(result.Value, limits: limits);
        }
        string code = result.Code ?? "management.transaction_unknown";
        int status = code switch
        {
            "management.not_found" or "management.registration.not_found" or "management.application.not_found" => 404,
            "management.sequence.obsolete" or "management.sequence.conflict" or "management.registration.conflict"
                or "management.registration.closed" => 409,
            "management.capacity.exceeded" or "management.response_too_large" => 503,
            _ => 503
        };
        return Problem(context, status, code);
    }

    internal static bool TryPage(HttpContext context, ManagementCursor cursors, string endpoint,
        string? applicationId, out int size, out string? after)
    {
        size = 100;
        after = null;
        if (context.Request.Query.Any(pair => pair.Key is not ("pageSize" or "cursor"))
            || context.Request.Query["pageSize"].Count > 1 || context.Request.Query["cursor"].Count > 1)
        {
            return false;
        }
        if (context.Request.Query.TryGetValue("pageSize", out var raw)
            && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 500))
        {
            return false;
        }
        CallerContext caller = Caller(context) ?? throw new InvalidOperationException("Native caller scope is required.");
        return cursors.TryDecode(context.Request.Query["cursor"].FirstOrDefault(), endpoint,
            ManagementActorScope.Create(caller, applicationId), size, out after);
    }

    internal static string Cursor(HttpContext context, ManagementCursor cursors, string endpoint,
        string? applicationId, int size, string last)
        => cursors.Encode(endpoint, ManagementActorScope.Create(Caller(context)
            ?? throw new InvalidOperationException("Native caller scope is required."), applicationId), size, last);

    internal static async Task<T?> Read<T>(HttpContext context, ManagementWireLimits limits) where T : class
    {
        if (context.Request.ContentType is not { } contentType
            || !string.Equals(contentType.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Length > 128)
        {
            throw new BadHttpRequestException("A JSON request is required.", StatusCodes.Status415UnsupportedMediaType);
        }
        int maximum = typeof(T) == typeof(ManagementInventorySnapshot)
            ? limits.MaximumInventoryRequestBytes : limits.MaximumRegularRequestBytes;
        if (context.Request.ContentLength > maximum)
        {
            throw new BadHttpRequestException("The request exceeds its finite byte limit.", StatusCodes.Status413PayloadTooLarge);
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
                    T? value = ManagementWire.DecodeRequest<T>(buffer.AsMemory(0, count), out ManagementWireFailure failure, limits);
                    if (failure == ManagementWireFailure.UnsupportedVersion)
                    {
                        throw new ManagementRequestException(400, "management.unsupported_version");
                    }
                    return value;
                }
                count += read;
            }
            throw new BadHttpRequestException("The request exceeds its finite byte limit.", StatusCodes.Status413PayloadTooLarge);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private sealed record EncodedResult(byte[] Bytes, int Status, string ContentType) : IResult
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

internal sealed class ManagementRequestException(int status, string code) : Exception
{
    internal int Status { get; } = status;
    internal string Code { get; } = code;
}
