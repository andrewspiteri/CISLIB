using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CIS.Healthcheck.AspNetCore;

/// <summary>Installs the reserved, read-only Healthcheck branch into an application-owned pipeline.</summary>
public static class HealthcheckApplicationBuilderExtensions
{
    /// <summary>Handles /_health and its descendants before ordinary application middleware.</summary>
    /// <remarks>
    /// Install this before business middleware. Each enabled read captures and completely formats one snapshot
    /// before committing response headers. Details require an explicit access predicate when enabled.
    /// Request cancellation never cancels shared state, collectors or other reports.
    /// </remarks>
    public static IApplicationBuilder UseCisHealthcheck(
        this IApplicationBuilder app,
        IHealthSnapshotSource source,
        HealthcheckOptions options,
        Func<HttpContext, ValueTask<bool>>? authorizeDetails = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        HealthcheckOptions settings = options.ValidatedCopy();
        if (settings.DetailsEnabled && authorizeDetails is null)
        {
            throw new ArgumentException("Supply an explicit details authorization predicate when details are enabled.", nameof(authorizeDetails));
        }

        return app.Use(next => context =>
        {
            string path = context.Request.Path.Value ?? string.Empty;
            bool reserved = path.Equals("/_health", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/_health/", StringComparison.OrdinalIgnoreCase);
            return reserved ? HandleAsync(context, path, source, settings, authorizeDetails) : next(context);
        });
    }

    private static async Task HandleAsync(HttpContext context, string path, IHealthSnapshotSource source,
        HealthcheckOptions options, Func<HttpContext, ValueTask<bool>>? authorizeDetails)
    {
        try
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            // Remove exactly one optional slash; an additional slash remains an unknown reserved path.
            string selected = path.EndsWith('/') ? path[..^1] : path;
            HealthReportView? view = selected.Equals("/_health", StringComparison.OrdinalIgnoreCase) ? HealthReportView.Health
                : selected.Equals("/_health/ready", StringComparison.OrdinalIgnoreCase) ? HealthReportView.Readiness
                : selected.Equals("/_health/details", StringComparison.OrdinalIgnoreCase) ? HealthReportView.Details
                : null;
            if (view is null || view == HealthReportView.Details && !options.DetailsEnabled)
            {
                await WriteAsync(context, HealthReportFormatter.FormatError("NotFound", pipe: false), StatusCodes.Status404NotFound).ConfigureAwait(false);
                return;
            }
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Headers.Allow = "GET, HEAD";
                await WriteAsync(context, HealthReportFormatter.FormatError("MethodNotAllowed", pipe: false), StatusCodes.Status405MethodNotAllowed).ConfigureAwait(false);
                return;
            }
            if (view == HealthReportView.Details)
            {
                bool authorized;
                try { authorized = await authorizeDetails!(context).ConfigureAwait(false); }
                catch (OperationCanceledException) { context.Abort(); return; }
                catch (Exception)
                {
                    await WriteAsync(context, HealthReportFormatter.FormatError("ReportingUnavailable", pipe: false), StatusCodes.Status500InternalServerError).ConfigureAwait(false);
                    return;
                }
                context.RequestAborted.ThrowIfCancellationRequested();
                if (!authorized)
                {
                    await WriteAsync(context, HealthReportFormatter.FormatError("Forbidden", pipe: false), StatusCodes.Status403Forbidden).ConfigureAwait(false);
                    return;
                }
            }

            HealthReportBuffer report;
            try
            {
                HealthSnapshot snapshot = source.CaptureSnapshot();
                report = HealthReportFormatter.FormatSnapshot(snapshot, view.Value, options);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); return; }
            catch (Exception)
            {
                report = HealthReportFormatter.FormatError("ReportingUnavailable", pipe: false);
            }
            int status = report.SelectedHealthy ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
            await WriteAsync(context, report, status).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { context.Abort(); }
    }

    private static async Task WriteAsync(HttpContext context, HealthReportBuffer report, int status)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = report.Utf8Bytes.Length;
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(report.Utf8Bytes, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
