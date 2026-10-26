// HC00 compiler-only transport installation and explicit application lifetime.
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HealthcheckContractConsumer;

internal static class TransportConsumer
{
    internal static IApplicationBuilder Install(
        IApplicationBuilder app,
        IHealthSnapshotSource source,
        HealthcheckOptions options,
        Func<HttpContext, ValueTask<bool>> authorizeDetails) =>
        app.UseCisHealthcheck(source, options, authorizeDetails);

    internal static async Task PipeLifecycleAsync(
        IHealthSnapshotSource source,
        HealthcheckOptions options,
        CancellationToken waitCancellation)
    {
        await using var pipe = new HealthPipeServer(source, options, TimeProvider.System);
        await pipe.StartAsync(waitCancellation);
        await pipe.StopAsync(waitCancellation);
    }
}
