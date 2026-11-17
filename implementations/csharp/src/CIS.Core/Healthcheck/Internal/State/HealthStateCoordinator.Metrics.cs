using CIS.Metrics;

namespace CIS.Healthcheck.Internal;

internal sealed partial class HealthStateCoordinator
{
    private readonly MetricsAttachment metricsAttachment = new();
    private HealthcheckMetrics? metrics;
    internal HealthcheckMetrics? CurrentMetrics => Volatile.Read(ref metrics);

    internal void AttachMetrics(MetricsRuntime runtime)
    {
        if (!metricsAttachment.Attach(runtime)) { return; }
        var created = new HealthcheckMetrics(runtime);
        runtime.RegisterObservableGauge(new("cis.healthcheck.ready", description: "Existing application readiness, without executing collectors."), CaptureMetricReadiness);
        runtime.RegisterObservableGauge(new("cis.healthcheck.monitors", description: "Active monitors grouped by existing policy state.",
            allowedDimensionKeys: ["state"]), CaptureMetricStates);
        Volatile.Write(ref metrics, created);
    }

    private IReadOnlyList<MetricObservation> CaptureMetricReadiness()
    {
        lock (gate)
        {
            if (disposed) { return []; }
            bool ready = startupCompleted && !stopping && !active.Values.Any(entry =>
                entry.Handle.TryGetTarget(out _) && entry.IsCritical && entry.State == MonitorState.Unhealthy);
            return [new(ready ? 1 : 0)];
        }
    }

    private IReadOnlyList<MetricObservation> CaptureMetricStates()
    {
        lock (gate)
        {
            if (disposed) { return []; }
            return [new(active.Values.Count(entry => entry.Handle.TryGetTarget(out _) && entry.State == MonitorState.Healthy), new(("state", "healthy"))),
                new(active.Values.Count(entry => entry.Handle.TryGetTarget(out _) && entry.State == MonitorState.Unhealthy), new(("state", "unhealthy"))),
                new(active.Values.Count(entry => entry.Handle.TryGetTarget(out _) && entry.State == MonitorState.Pending), new(("state", "pending")))];
        }
    }
}
