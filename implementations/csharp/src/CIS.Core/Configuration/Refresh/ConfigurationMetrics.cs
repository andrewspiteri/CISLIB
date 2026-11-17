using System.Diagnostics;
using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Configuration;

// Fixed aggregate labels only. Configuration values, types and store keys never
// enter a metric, and the actual refresh runner remains the operation owner.
internal sealed class ConfigurationMetrics
{
    private readonly ITimer duration;

    internal ConfigurationMetrics(MetricsRuntime runtime,
        Func<IReadOnlyList<MetricObservation>> prepared,
        Func<IReadOnlyList<MetricObservation>> entries,
        Func<IReadOnlyList<MetricObservation>> initialDuration,
        Func<IReadOnlyList<MetricObservation>> lastSuccess)
    {
        duration = runtime.CreateTimer(new("cis.configuration.operation.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["operation", "outcome"]));
        runtime.RegisterObservableGauge(new("cis.configuration.prepared"), prepared);
        runtime.RegisterObservableGauge(new("cis.configuration.entries", allowedDimensionKeys: ["state"]), entries);
        runtime.RegisterObservableGauge(new("cis.configuration.initialization.duration", MetricUnit.Seconds), initialDuration);
        runtime.RegisterObservableGauge(new("cis.configuration.last.success", MetricUnit.Seconds), lastSuccess);
    }

    internal void Record(string operation, string outcome, long started)
    {
        try
        {
            duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new MetricDimensions(("operation", operation), ("outcome", outcome)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        { /* Observation cannot change retrieval, commit or notification results. */ }
    }
}
