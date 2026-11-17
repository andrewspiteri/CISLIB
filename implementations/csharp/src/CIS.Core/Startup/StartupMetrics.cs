using System.Diagnostics;
using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Startup;

// One complete host binding, captured once by the retained startup operation.
internal sealed class StartupMetrics
{
    private readonly ITimer duration;

    internal StartupMetrics(MetricsRuntime runtime, Func<bool> completed)
    {
        duration = runtime.CreateTimer(new("cis.startup.operation.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["stage", "outcome"]));
        runtime.RegisterObservableGauge(new("cis.startup.completed"),
            () => [new(completed() ? 1 : 0)]);
    }

    internal void Record(string stage, string outcome, long started)
    {
        try
        {
            duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new MetricDimensions(("stage", stage), ("outcome", outcome)));
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        { /* Observation cannot change the actual startup result. */ }
    }
}
