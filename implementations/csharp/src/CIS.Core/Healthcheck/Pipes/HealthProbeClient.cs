using System.Globalization;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using CIS.Healthcheck.Reporting;

namespace CIS.Healthcheck.Pipes;

/// <summary>The matching executable's internal transport; response validation stays in the shared Core codec.</summary>
internal static class HealthProbeClient
{
    internal static async Task<int> RunAsync(string[] arguments, Stream standardOutput, TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        if (!TryParseArguments(arguments, out var settings))
        {
            await WriteErrorAsync(standardError, "InvalidArguments").ConfigureAwait(false);
            return 2;
        }
        try
        {
            using var budget = new HealthPipeBudget(TimeProvider.System, settings.TimeoutMilliseconds, cancellationToken);
            using var client = new NamedPipeClientStream(".", settings.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(budget.Token).ConfigureAwait(false);
            budget.ThrowIfExpired();
            byte[] request = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"" + HealthReportProtocol.ViewName(settings.View) + "\"}");
            await HealthReportProtocol.WriteFrameAsync(client, request, 512, budget.Token).ConfigureAwait(false);
            budget.ThrowIfExpired();
            byte[] response = await HealthReportProtocol.ReadFrameAsync(client, settings.MaxReportBytes, budget.Token).ConfigureAwait(false);
            budget.ThrowIfExpired();
            var parsed = HealthReportProtocol.ParseResponse(response, settings.View, settings.MaxReportBytes);
            budget.ThrowIfExpired();
            if (parsed.IsError)
            {
                await WriteErrorAsync(standardError, parsed.ErrorCode!).ConfigureAwait(false);
                return 2;
            }
            await standardOutput.WriteAsync(parsed.SnapshotUtf8, cancellationToken).ConfigureAwait(false);
            await standardOutput.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await standardOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
            return parsed.SelectedHealthy ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            await WriteErrorAsync(standardError, "TimeoutOrCanceled").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            await WriteErrorAsync(standardError, "TransportOrProtocolFailure").ConfigureAwait(false);
        }
        return 2;
    }

    private static bool TryParseArguments(string[] arguments, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProbeSettings? settings)
    {
        settings = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? pipeName = null;
        HealthReportView view = HealthReportView.Health;
        int timeout = 1000, maximum = 4194304;
        for (int index = 0; index < arguments.Length; index += 2)
        {
            string option = arguments[index];
            if (index + 1 >= arguments.Length || !seen.Add(option)) { return false; }
            string value = arguments[index + 1];
            switch (option)
            {
                case "--pipe":
                    if (!HealthcheckOptions.IsValidPipeName(value)) { return false; }
                    pipeName = value;
                    break;
                case "--view":
                    switch (value)
                    {
                        case "health": view = HealthReportView.Health; break;
                        case "readiness": view = HealthReportView.Readiness; break;
                        case "details": view = HealthReportView.Details; break;
                        default: return false;
                    }
                    break;
                case "--timeout-ms":
                    if (!Integer(value, 50, 60000, out timeout)) { return false; }
                    break;
                case "--max-report-bytes":
                    if (!Integer(value, 1024, 16777216, out maximum)) { return false; }
                    break;
                default: return false;
            }
        }
        if (pipeName is null) { return false; }
        settings = new ProbeSettings(pipeName, view, timeout, maximum);
        return true;

        static bool Integer(string value, int minimum, int maximum, out int number)
            => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number) && number >= minimum && number <= maximum;
    }

    private static async Task WriteErrorAsync(TextWriter output, string code)
    {
        // Only known bounded codes reach stderr, never server text, paths or exception messages.
        // An unavailable stderr must not replace the documented error exit with an unhandled exception.
        try { await output.WriteLineAsync("cis-health-probe: " + code).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }

    private sealed record ProbeSettings(string PipeName, HealthReportView View, int TimeoutMilliseconds, int MaxReportBytes);
}
