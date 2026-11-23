using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Management.Hosting;

/// <summary>Explicit finite managed admission for effect-producing native endpoints.</summary>
public static class ManagedWorkEndpointExtensions
{
    /// <summary>Checks the selected gate immediately before the endpoint/filter continuation; absence leaves unmanaged behavior unchanged.</summary>
    /// <remarks>Native authentication and authorization execute first. Select this on effect-producing routes;
    /// permitted read/status/recovery routes remain under their existing authorization and dependency policies.
    /// An admitted endpoint retains its existing cancellation, transaction and cleanup owners.</remarks>
    public static TBuilder RequireCisManagedWork<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
            IManagedWorkAdmission? admission = context.HttpContext.RequestServices.GetService<IManagedWorkAdmission>();
            if (admission is not null && !admission.TryAdmit())
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            return await next(context).ConfigureAwait(false);
        });
    }
}
