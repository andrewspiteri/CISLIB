using System.Diagnostics;

namespace CIS.RestClient;

// The module forwards existing entry context, never creates a tracing owner or
// relies on ambient context that callbacks may replace after public entry.
internal static class RestTraceForwarding
{
    internal static string? Capture(Activity? activity)
    {
        string? traceParent = activity?.Id;
        if (activity is not { IdFormat: ActivityIdFormat.W3C } || activity.TraceId == default || activity.SpanId == default
            || !ActivityContext.TryParse(traceParent, null, out ActivityContext context)
            || context.TraceId != activity.TraceId || context.SpanId != activity.SpanId) { return null; }
        return traceParent;
    }

    internal static void Apply(RestRequestContext request, bool? enabled, string? capturedTraceParent)
    {
        if (capturedTraceParent is null || request.Headers.Snapshot().ContainsKey("traceparent")) { return; }
        if (enabled ?? IsLocal(new Uri(request.Url, UriKind.Absolute)))
        { request.Headers.Set("traceparent", capturedTraceParent); }
    }

    private static bool IsLocal(Uri destination)
    {
        if (destination.HostNameType != UriHostNameType.Dns) { return false; }
        string host = destination.IdnHost;
        if (host.EndsWith('.')) { host = host[..^1]; }
        return host.Length > ".local".Length && host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }
}
