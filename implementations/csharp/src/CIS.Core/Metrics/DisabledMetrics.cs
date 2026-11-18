namespace CIS.Metrics;

// Core-only consumers need the same inert handles as an explicitly disabled
// optional collector, without constructing a meter or retaining user callbacks.
internal sealed class DisabledMetrics : IMetrics
{
    internal static DisabledMetrics Instance { get; } = new();
    private static readonly NoopTimer Timer = new(TimeProvider.System);
    public ICounter CreateCounter(MetricDefinition definition) => NoopMetrics.Instance;
    public IUpDownCounter CreateUpDownCounter(MetricDefinition definition) => NoopMetrics.Instance;
    public IGauge CreateGauge(MetricDefinition definition) => NoopMetrics.Instance;
    public IHistogram CreateHistogram(MetricDefinition definition) => NoopMetrics.Instance;
    public ITimer CreateTimer(MetricDefinition definition) => Timer;
    public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe) { }
}
