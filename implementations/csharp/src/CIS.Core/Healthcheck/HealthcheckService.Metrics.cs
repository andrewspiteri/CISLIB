using CIS.Metrics;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Healthcheck;

public sealed partial class HealthcheckService : IMetricsSource
{
    void IMetricsSource.AttachMetrics(MetricsRuntime runtime) => state.AttachMetrics(runtime);
}

internal sealed class HealthcheckMetrics
{
    private readonly ITimer evaluation;
    internal HealthcheckMetrics(IMetrics metrics)
        => evaluation = metrics.CreateTimer(new("cis.healthcheck.evaluation.duration", MetricUnit.Seconds,
            "Actual collector evaluation and result acceptance; success does not imply healthy application state.", ["outcome"]));

    internal ITimingScope? Start()
    {
        try { return evaluation.Start(); }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { return null; }
    }

    internal static void Complete(ITimingScope? timing, string outcome)
    {
        try { timing?.Stop(new(("outcome", outcome))); }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
    }
}
