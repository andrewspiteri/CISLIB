using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Metrics.OpenTelemetry;

/// <summary>Connects the reserved Prometheus branch and thin native request policy to the application pipeline.</summary>
public static class MetricsApplicationBuilderExtensions
{
    private const string Installed = "CIS.Metrics.ReservedBranch";

    /// <summary>Installs GET /_metrics once, after routing and before authorization, health branches and business readiness.</summary>
    /// <remarks>Apply PathBase and any separate management-access policy first. Routes are a fixed startup snapshot.
    /// Completed management requests are excluded; native active requests remain host-wide.</remarks>
    public static IApplicationBuilder UseCisMetrics(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        MetricsSettings settings = app.ApplicationServices.GetService<MetricsSettings>()
            ?? throw new InvalidOperationException("Install AddCisMetrics before constructing its application branch.");
        if (!settings.Enabled) { return app; }
        MetricsCollectionContext collection = app.ApplicationServices.GetRequiredService<MetricsCollectionContext>();
        if (app.Properties.TryGetValue(Installed, out object? existing))
        {
            if (!ReferenceEquals(existing, collection)) { throw new InvalidOperationException("The reserved Metrics branch is already owned by another composition."); }
            return app;
        }
        app.Properties.Add(Installed, collection);
        app.Use(next =>
        {
            // Run after all Map* registrations while the real web pipeline is built, before its listener starts.
            MetricsHttpPolicy.ValidateRoutes(app.ApplicationServices);
            return async context =>
            {
                IHttpMetricsTagsFeature? original = null;
                if (settings.CollectIncomingHttp)
                {
                    try { original = MetricsHttpPolicy.ApplyDurationPolicy(context); }
                    catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { collection.Runtime.Diagnose(MetricsDiagnostic.Backend); }
                }
                try { await next(context).ConfigureAwait(false); }
                finally
                {
                    if (settings.CollectIncomingHttp)
                    {
                        try { MetricsHttpPolicy.RemoveNativeOverrides(context, original); }
                        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { collection.Runtime.Diagnose(MetricsDiagnostic.Backend); }
                    }
                }
            };
        });
        app.MapWhen(context => MetricsHttpPolicy.IsMetricsPath(context.Request.Path), branch =>
        {
            branch.Use(async (HttpContext context, RequestDelegate next) =>
            {
                if (!HttpMethods.IsGet(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    context.Response.Headers.Allow = "GET";
                    return;
                }
                if (collection.IsStopping) { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                using RateLimitLease? admission = collection.HttpPolicy.TryAcquire();
                if (admission is null) { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
                if (!admission.IsAcquired) { context.Response.StatusCode = StatusCodes.Status429TooManyRequests; return; }
                await next(context).ConfigureAwait(false);
            });
            // Exact outer matching handles route ownership; upstream retains negotiation, caching, deadline and encoding.
            branch.UseOpenTelemetryPrometheusScrapingEndpoint(meterProvider: null, predicate: static _ => true,
                path: null, configureBranchedPipeline: null, optionsName: null);
        });
        return app;
    }
}
