// HC00 nonshipping facade. The eventual ASP.NET implementation belongs inside CIS.Core.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CIS.Healthcheck.AspNetCore;

/// <summary>Compile-only shape of the reserved HTTP branch installation.</summary>
public static class HealthcheckApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCisHealthcheck(
        this IApplicationBuilder app,
        IHealthSnapshotSource source,
        HealthcheckOptions options,
        Func<HttpContext, ValueTask<bool>>? authorizeDetails = null) =>
        throw ReferenceOnly.Unavailable();
}
