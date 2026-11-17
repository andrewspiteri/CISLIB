using System.Text;
using System.Threading.RateLimiting;
using CIS.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Metrics.OpenTelemetry;

// Standard bounded HTTP admission plus native-duration policy, not another recorder or collector.
internal sealed class MetricsHttpPolicy : IDisposable
{
    private readonly object gate = new();
    private readonly ConcurrencyLimiter limiter;
    private bool stopping, disposed;

    internal MetricsHttpPolicy(MetricsSettings settings)
    {
        limiter = new(new ConcurrencyLimiterOptions
        {
            PermitLimit = settings.MaxConcurrentScrapes,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    // Null is terminal cutoff; a non-acquired lease is immediate capacity rejection.
    internal RateLimitLease? TryAcquire()
    {
        lock (gate) { return stopping ? null : limiter.AttemptAcquire(); }
    }
    internal long AvailablePermits => limiter.GetStatistics()?.CurrentAvailablePermits ?? 0;

    internal void BeginStopping() { lock (gate) { stopping = true; } }

    public void Dispose()
    {
        lock (gate)
        {
            stopping = true;
            if (disposed) { return; }
            disposed = true;
            limiter.Dispose();
        }
    }

    internal static IHttpMetricsTagsFeature? ApplyDurationPolicy(HttpContext context)
    {
        FirstPass? first = context.Features.Get<FirstPass>();
        if (first is null)
        {
            // A policy first entered during reexecution must not capture the handler as the original request.
            PathString path = context.Features.Get<IExceptionHandlerPathFeature>()?.Path is { } originalPath
                ? new(originalPath) : context.Request.Path;
            first = new(IsMetricsPath(path) || path.StartsWithSegments("/_health", StringComparison.OrdinalIgnoreCase));
            context.Features.Set(first);
        }
        // Preserve explicit application/native disables. Never reinterpret an error-handler reexecution as a new request.
        IHttpMetricsTagsFeature? metrics = context.Features.Get<IHttpMetricsTagsFeature>();
        if (first.Management && metrics is not null)
        { metrics.MetricsDisabled = true; }
        return metrics;
    }

    internal static void RemoveNativeOverrides(HttpContext context, IHttpMetricsTagsFeature? original)
    {
        // The pinned framework owns this List. Replacement features/collections are outside the supported producer.
        if (original?.Tags is List<KeyValuePair<string, object?>> tags)
        {
            tags.RemoveAll(static pair => pair.Key is "http.route" or "http.request.method"
                or "http.response.status_code" or "url.scheme");
        }
        // The mapped error endpoint has finished executing. Restore the public original endpoint
        // for native completion instead of publishing a duplicate selected tag that the SDK can reject.
        if (context.Features.Get<IExceptionHandlerFeature>() is { } handled) { context.SetEndpoint(handled.Endpoint); }
    }

    internal static bool IsMetricsPath(PathString path) => path.Equals("/_metrics", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateRoutes(IServiceProvider root)
    {
        bool collectHttp = root.GetRequiredService<MetricsSettings>().CollectIncomingHttp;
        EndpointDataSource? source = root.GetService<EndpointDataSource>();
        if (source is null) { return; } // An ordinary non-web host has no route producer.
        foreach (Endpoint endpoint in source.Endpoints)
        {
            if (collectHttp)
            {
                string? route = endpoint.Metadata.GetMetadata<IRouteDiagnosticsMetadata>()?.Route;
                if (route is not null && !IsBoundedRoute(route))
                { throw new InvalidOperationException("CIS Metrics requires fixed route diagnostics within 256 UTF-8 bytes; use bounded declared route metadata."); }
            }
            if (endpoint is RouteEndpoint declared
                && string.Equals(declared.RoutePattern.RawText?.Trim('/'), "_metrics", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("The /_metrics route belongs exclusively to the CIS Metrics branch."); }
        }
    }

    private static bool IsBoundedRoute(string route) => route.Length <= 256
        && UnicodeScalarText.IsValid(route, 256, allowControls: false) && Encoding.UTF8.GetByteCount(route) <= 256;

    private sealed record FirstPass(bool Management);
}
