using System.Globalization;
using System.Text;
using CIS.Metrics;
using Microsoft.Extensions.Logging;

namespace CIS.RestClient;

// Observational only: borrows the application's logger and retains no call
// history, raw URL, credential fields, response payload or exception details.
internal static class RestCallLogging
{
    private const int MaximumUriLength = 512;
    private const string Template = "[REST] {Method} {Uri} {Result} {Duration}{RetrySuffix}";
    private static readonly EventId Completed = new(0, "restclient.completed");

    internal static ILogger<RestClientRuntime>? CreateLogger(ILoggerFactory? factory)
    {
        try { return factory?.CreateLogger<RestClientRuntime>(); }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { return null; }
    }

    internal static string SafeUri(Uri destination)
    {
        try
        {
            if (!destination.IsAbsoluteUri || destination.Scheme is not ("http" or "https")) { return "unavailable"; }
            var text = new StringBuilder(MaximumUriLength);
            Append(destination.Scheme); Append("://");
            string host = destination.IdnHost;
            if (destination.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('[')) { Append("["); Append(host); Append("]"); }
            else { Append(host); }
            if (!destination.IsDefaultPort) { Append(":"); Append(destination.Port.ToString(CultureInfo.InvariantCulture)); }
            Append(destination.AbsolutePath);
            return text.ToString();

            void Append(string value)
            {
                int remaining = MaximumUriLength - text.Length;
                if (remaining <= 0) { return; }
                if (value.Length > remaining)
                { text.Append(value.AsSpan(0, remaining - 1)); text.Append('…'); }
                else { text.Append(value); }
            }
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { return "unavailable"; }
    }

    internal static void Write(ILogger<RestClientRuntime>? logger, string method, string safeUri,
        RestOperationOutcome outcome, int? status, TimeSpan elapsed, int dispatches, int maximumAttempts)
    {
        if (logger is null) { return; }
        try
        {
            LogLevel level = outcome == RestOperationOutcome.Success ? LogLevel.Information : LogLevel.Error;
            if (!logger.IsEnabled(level)) { return; }
            string label = outcome switch
            {
                RestOperationOutcome.Success => "Ok",
                RestOperationOutcome.HttpError or RestOperationOutcome.TransportError => "Technical Error",
                RestOperationOutcome.BusinessError => "Business Error",
                RestOperationOutcome.Timeout => "Timeout",
                RestOperationOutcome.Cancelled => "Cancelled",
                RestOperationOutcome.AuthenticationError => "Authentication Error",
                RestOperationOutcome.PreparationError => "Preparation Error",
                RestOperationOutcome.InvalidResponse => "Invalid Response",
                RestOperationOutcome.LimitExceeded => "Limit Exceeded",
                RestOperationOutcome.Stopping => "Stopping",
                _ => "Unexpected Error"
            };
            string result = status is { } code ? code.ToString(CultureInfo.InvariantCulture) + " " + label : label;
            string duration = elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + "ms";
            string suffix = maximumAttempts > 1 && dispatches > 0
                ? " (Attempt " + dispatches.ToString(CultureInfo.InvariantCulture) + "/" + maximumAttempts.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
            logger.Log(level, Completed, null, Template, method, safeUri, result, duration, suffix);
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
    }
}
