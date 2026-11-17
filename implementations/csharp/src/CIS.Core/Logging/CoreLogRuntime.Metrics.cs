using CIS.Metrics;

namespace CIS.Logging;

public sealed partial class CoreLogRuntime : IMetricsSource
{
    private readonly MetricsAttachment metricsAttachment = new();
    private LoggingMetrics? metrics;

    void IMetricsSource.AttachMetrics(MetricsRuntime runtime)
    {
        if (!metricsAttachment.Attach(runtime)) { return; }
        var created = new LoggingMetrics(runtime);
        if (queuedOutputs is not null)
        { foreach (var output in queuedOutputs.Entries) { output.Queue.AttachMetrics(created); } }
        incoming?.AttachMetrics(created);
        Volatile.Write(ref metrics, created);
    }
}

internal sealed class LoggingMetrics
{
    private readonly ICounter records, processing, discarded, writerFailures;
    internal LoggingMetrics(IMetrics metrics)
    {
        records = metrics.CreateCounter(new("cis.logging.records", description: "Frontend log-call return dispositions, independent of backend delivery.",
            allowedDimensionKeys: ["outcome"]));
        processing = metrics.CreateCounter(new("cis.logging.processing.records", description: "Backend preparation/dispatch dispositions, including policy suppression.",
            allowedDimensionKeys: ["outcome"]));
        discarded = metrics.CreateCounter(new("cis.logging.discarded.records", description: "Frontend occurrences actually overwritten or removed during shutdown.",
            allowedDimensionKeys: ["reason"]));
        writerFailures = metrics.CreateCounter(new("cis.logging.writer.failures", description: "Actual output write or flush operations that throw."));
    }

    internal void Call(LogWriteOutcome outcome) => Record(records, new(("outcome", outcome switch
    {
        LogWriteOutcome.Accepted => "accepted", LogWriteOutcome.PartiallyAccepted => "partially_accepted",
        LogWriteOutcome.Filtered => "filtered", LogWriteOutcome.Rejected => "rejected", LogWriteOutcome.InvalidEvent => "invalid", _ => "stopped"
    })));
    internal void Processed(string outcome) => Record(processing, new(("outcome", outcome)));
    internal void Discarded(string reason) => Record(discarded, new(("reason", reason)));
    internal void WriterFailed() => Record(writerFailures, default);

    private static void Record(ICounter counter, MetricDimensions dimensions)
    {
        try { counter.Add(1, dimensions); }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
    }
}
