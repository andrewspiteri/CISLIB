namespace CIS.Metrics;

// A single operation-local value. No running timer registry and no inferred outcome.
internal sealed class TimingScope : ITimingScope
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly Action<double, MetricDimensions> record;
    private readonly MetricDimensions initial;
    private readonly long started;
    private bool completed;
    private double elapsed;

    internal TimingScope(TimeProvider clock, Action<double, MetricDimensions> record, MetricDimensions initial)
    {
        this.clock = clock;
        this.record = record;
        this.initial = initial;
        started = clock.GetTimestamp();
    }

    public double Stop(MetricDimensions completionDimensions = default)
    {
        lock (gate)
        {
            if (completed) { return elapsed; }
            completed = true;
            elapsed = clock.GetElapsedTime(started, clock.GetTimestamp()).TotalSeconds;
            // Completion and recording stay under the one local gate: another caller cannot publish or retry first.
            MetricDimensions combined;
            if (initial.IsInvalid || completionDimensions.IsInvalid) { combined = MetricDimensions.Invalid; }
            else
            {
                var pairs = initial.Pairs.ToArray().Concat(completionDimensions.Pairs.ToArray())
                    .Select(pair => (pair.Key, pair.Value)).ToArray();
                combined = new MetricDimensions(pairs);
            }
            record(elapsed, combined);
            return elapsed;
        }
    }

    public void Dispose() => _ = Stop();
}

internal sealed class NoopMetrics : ICounter, IUpDownCounter, IGauge, IHistogram
{
    internal static NoopMetrics Instance { get; } = new();
    public void Add(long amount = 1, MetricDimensions dimensions = default) { }
    public void Set(double value, MetricDimensions dimensions = default) { }
    public void Record(double value, MetricDimensions dimensions = default) { }
}

internal sealed class NoopTimer(TimeProvider clock) : ITimer
{
    public void Record(double elapsedSeconds, MetricDimensions dimensions = default) { }
    public ITimingScope Start(MetricDimensions dimensions = default) => new TimingScope(clock, static (_, _) => { }, default);
}
