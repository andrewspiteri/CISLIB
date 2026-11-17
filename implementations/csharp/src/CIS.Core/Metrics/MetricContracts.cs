namespace CIS.Metrics;

/// <summary>Creates reusable, bounded metric instruments without exposing a platform SDK.</summary>
public interface IMetrics
{
    /// <summary>Creates or reuses a nonnegative accumulating counter.</summary>
    ICounter CreateCounter(MetricDefinition definition);
    /// <summary>Creates or reuses a signed additive counter.</summary>
    IUpDownCounter CreateUpDownCounter(MetricDefinition definition);
    /// <summary>Creates or reuses an absolute, finite gauge.</summary>
    IGauge CreateGauge(MetricDefinition definition);
    /// <summary>Creates or reuses a nonnegative distribution.</summary>
    IHistogram CreateHistogram(MetricDefinition definition);
    /// <summary>Creates or reuses a seconds distribution with monotonic timing scopes.</summary>
    ITimer CreateTimer(MetricDefinition definition);
    /// <summary>Registers one bounded in-memory snapshot callback for the host lifetime.</summary>
    /// <remarks>The callback must perform no I/O or application work. Equivalent registration requires the same callback.</remarks>
    void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe);
}

/// <summary>A nonnegative signed-64-bit accumulating quantity.</summary>
public interface ICounter
{
    /// <summary>Adds an occurrence or quantity; an invalid measurement is dropped.</summary>
    void Add(long amount = 1, MetricDimensions dimensions = default);
}

/// <summary>A signed-64-bit additive quantity such as active work.</summary>
public interface IUpDownCounter
{
    /// <summary>Adds the signed change; an invalid measurement is dropped.</summary>
    void Add(long delta, MetricDimensions dimensions = default);
}

/// <summary>An absolute floating-point state; concurrent accepted writes use backend last-value semantics.</summary>
public interface IGauge
{
    /// <summary>Sets a finite value; an invalid measurement is dropped.</summary>
    void Set(double value, MetricDimensions dimensions = default);
}

/// <summary>A cumulative distribution of finite nonnegative floating-point observations.</summary>
public interface IHistogram
{
    /// <summary>Records one observation; an invalid measurement is dropped.</summary>
    void Record(double value, MetricDimensions dimensions = default);
}

/// <summary>A duration distribution measured in seconds.</summary>
public interface ITimer
{
    /// <summary>Records an already measured finite nonnegative duration in seconds.</summary>
    void Record(double elapsedSeconds, MetricDimensions dimensions = default);
    /// <summary>Begins one operation-local monotonic timing scope.</summary>
    ITimingScope Start(MetricDimensions dimensions = default);
}

/// <summary>A timer whose first completion fixes its duration and makes at most one recording attempt.</summary>
public interface ITimingScope : IDisposable
{
    /// <summary>Returns the first elapsed seconds and attempts recording once with disjoint completion dimensions.</summary>
    /// <remarks>Invalid dimensions drop the attempt; later stop/dispose calls cannot retry or infer success.</remarks>
    double Stop(MetricDimensions completionDimensions = default);
}

/// <summary>The finite v1 vocabulary of metric units.</summary>
public enum MetricUnit
{
    /// <summary>A count without a physical-unit suffix.</summary>
    Count = 0,
    /// <summary>Seconds, represented by the standard unit s.</summary>
    Seconds = 1,
    /// <summary>Bytes, represented by the standard unit By.</summary>
    Bytes = 2,
    /// <summary>A dimensionless ratio, represented by the standard unit 1.</summary>
    Ratio = 3,
}

/// <summary>An immutable instrument declaration. Registration validates its name, schema and optional finite histogram profile.</summary>
public sealed class MetricDefinition
{
    /// <summary>Copies a stable definition; dimension keys are an allowed set, not required measurement values.</summary>
    public MetricDefinition(string name, MetricUnit unit = MetricUnit.Count, string? description = null,
        IReadOnlyList<string>? allowedDimensionKeys = null, IReadOnlyList<double>? histogramBounds = null)
    {
        Name = name;
        Unit = unit;
        Description = description ?? string.Empty;
        int keyCount = allowedDimensionKeys?.Count ?? 0;
        int boundCount = histogramBounds?.Count ?? 0;
        if (keyCount is < 0 or > 8 || boundCount is < 0 or > 32)
        { throw new ArgumentException("A v1 definition accepts at most eight keys and 32 finite bounds."); }
        var keys = new string[keyCount];
        for (int index = 0; index < keys.Length; index++) { keys[index] = allowedDimensionKeys![index]; }
        AllowedDimensionKeys = Array.AsReadOnly(keys);
        if (histogramBounds is not null)
        {
            var bounds = new double[boundCount];
            for (int index = 0; index < bounds.Length; index++) { bounds[index] = histogramBounds[index]; }
            HistogramBounds = Array.AsReadOnly(bounds);
        }
    }

    /// <summary>Gets the stable logical name before exporter translation.</summary>
    public string Name { get; }
    /// <summary>Gets the declared quantity's unit.</summary>
    public MetricUnit Unit { get; }
    /// <summary>Gets the bounded instrument description.</summary>
    public string Description { get; }
    /// <summary>Gets allowed ordinal dimension keys.</summary>
    public IReadOnlyList<string> AllowedDimensionKeys { get; }
    /// <summary>Gets explicit finite histogram bounds, or null for the standard seconds profile.</summary>
    public IReadOnlyList<double>? HistogramBounds { get; }
}

/// <summary>Immutable string-only measurement dimensions; default is empty.</summary>
/// <remarks>Construction copies values. Invalid keys/values are rejected at recording, without throwing into business work.</remarks>
public readonly struct MetricDimensions
{
    private readonly KeyValuePair<string, string>[]? values;
    private readonly bool invalid;

    /// <summary>Copies the supplied string pairs without converting arbitrary objects.</summary>
    public MetricDimensions(params (string Key, string Value)[] values)
    {
        // Invalid over-capacity input is marked for drop without retaining its pairs or their values.
        invalid = values is null || values.Length > 8;
        this.values = invalid ? null : values!.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)).ToArray();
    }

    /// <summary>Gets a read-only view of the copied pairs.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Values => Array.AsReadOnly(values ?? []);

    internal ReadOnlySpan<KeyValuePair<string, string>> Pairs => values;
    internal bool IsInvalid => invalid;

    internal static MetricDimensions Invalid => new(null!);
}

/// <summary>One finite gauge snapshot paired with its string dimensions.</summary>
public readonly record struct MetricObservation(double Value, MetricDimensions Dimensions = default);
