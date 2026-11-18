using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.RestClient;

// One complete four-handle bundle belongs to the registry, not to individual clients.
// The runtime publishes it only after its one-host MetricsAttachment claim succeeds.
internal sealed class RestClientMetrics
{
    private readonly ITimer duration;
    private readonly IUpDownCounter active;
    private readonly ICounter uploaded, downloaded;
    private readonly HashSet<RestOperationIdentity> operations;
    private readonly HashSet<string> clients;

    internal RestClientMetrics(IMetrics metrics, RestClientComposition composition)
    {
        ArgumentNullException.ThrowIfNull(metrics); ArgumentNullException.ThrowIfNull(composition);
        operations = new(composition.Operations);
        clients = new(composition.ClientAliases, StringComparer.Ordinal);
        duration = metrics.CreateTimer(new("cis.restclient.operation.duration", MetricUnit.Seconds,
            "Logical REST work from public entry through retries, body processing and actual cleanup.", ["client", "operation", "outcome"]));
        active = metrics.CreateUpDownCounter(new("cis.restclient.active.operations", description: "Admitted REST operations not yet settled.",
            allowedDimensionKeys: ["client"]));
        uploaded = metrics.CreateCounter(new("cis.restclient.payload.uploaded", MetricUnit.Bytes,
            "Application request bytes accepted by completed writes, including retransmission.", ["client", "operation"]));
        downloaded = metrics.CreateCounter(new("cis.restclient.payload.downloaded", MetricUnit.Bytes,
            "Application or decompressed response bytes returned by completed reads, including retries.", ["client", "operation"]));
    }

    internal IRestOperationObservation? Begin(string clientAlias, string? operationAlias)
    {
        if (!clients.Contains(clientAlias)) { return null; }
        string selected = operationAlias is not null && operations.Contains(new(clientAlias, operationAlias)) ? operationAlias : "request";
        try
        {
            var operation = new Operation(this, clientAlias, selected);
            active.Add(1, operation.ClientDimensions);
            return operation;
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { return null; }
    }

    private sealed class Operation(RestClientMetrics owner, string clientAlias, string operationAlias) : IRestOperationObservation
    {
        private readonly MetricDimensions dimensions = new(("client", clientAlias), ("operation", operationAlias));
        private readonly MetricDimensions clientDimensions = new(("client", clientAlias));
        private int completed;
        internal MetricDimensions ClientDimensions => clientDimensions;

        public void UploadCompleted(long bytes) => Add(owner.uploaded, bytes);
        public void DownloadCompleted(long bytes) => Add(owner.downloaded, bytes);

        private void Add(ICounter counter, long bytes)
        {
            if (bytes <= 0 || Volatile.Read(ref completed) != 0) { return; }
            try { counter.Add(bytes, dimensions); }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
        }

        public void Complete(RestOperationOutcome outcome, TimeSpan settledElapsed)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) { return; }
            try
            {
                string? label = outcome switch
                {
                    RestOperationOutcome.Success => "success", RestOperationOutcome.HttpError => "http_error",
                    RestOperationOutcome.BusinessError => "business_error", RestOperationOutcome.TransportError => "transport_error",
                    RestOperationOutcome.Timeout => "timeout", RestOperationOutcome.Cancelled => "cancelled",
                    RestOperationOutcome.AuthenticationError => "authentication_error", RestOperationOutcome.PreparationError => "preparation_error",
                    RestOperationOutcome.InvalidResponse => "invalid_response", RestOperationOutcome.LimitExceeded => "limit_exceeded",
                    RestOperationOutcome.Stopping => "stopping", RestOperationOutcome.UnexpectedError => "unexpected_error", _ => null
                };
                if (label is not null)
                {
                    owner.duration.Record(settledElapsed.TotalSeconds,
                        new(("client", clientAlias), ("operation", operationAlias), ("outcome", label)));
                }
            }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
            finally
            {
                try { owner.active.Add(-1, clientDimensions); }
                catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
            }
        }
    }
}
