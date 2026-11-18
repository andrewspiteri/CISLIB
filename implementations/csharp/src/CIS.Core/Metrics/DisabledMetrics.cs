namespace CIS.Metrics;

/// <summary>Reusable inert Metrics handles without a meter, provider, event history or retained callbacks.</summary>
public sealed class DisabledMetrics : IMetrics
{
    /// <summary>Gets the shared facade used when collection is absent or explicitly disabled.</summary>
    public static DisabledMetrics Instance { get; } = new();
    private static readonly NoopTimer Timer = new(TimeProvider.System);

    private DisabledMetrics()
    {
    }

    /// <inheritdoc />
    public ICounter CreateCounter(MetricDefinition definition) => NoopMetrics.Instance;
    /// <inheritdoc />
    public IUpDownCounter CreateUpDownCounter(MetricDefinition definition) => NoopMetrics.Instance;
    /// <inheritdoc />
    public IGauge CreateGauge(MetricDefinition definition) => NoopMetrics.Instance;
    /// <inheritdoc />
    public IHistogram CreateHistogram(MetricDefinition definition) => NoopMetrics.Instance;
    /// <inheritdoc />
    public ITimer CreateTimer(MetricDefinition definition) => Timer;
    /// <inheritdoc />
    public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe)
    {
    }
}
