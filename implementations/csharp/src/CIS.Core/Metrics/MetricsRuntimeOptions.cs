using System.Diagnostics.Metrics;

namespace CIS.Metrics;

// This is a copied composition snapshot, not a public configuration/reload surface.
internal sealed class MetricsRuntimeOptions
{
    internal int MaxDefinitions { get; init; } = 96;
    internal int MaxDimensionKeys { get; init; } = 8;
    internal int MaxDimensionValueBytes { get; init; } = 256;
    internal int MaxNameLength { get; init; } = 128;
    internal int MaxKeyLength { get; init; } = 64;
    internal int MaxDescriptionBytes { get; init; } = 1_024;
    internal int MaxHistogramBounds { get; init; } = 32;
    internal int MaxObservableMeasurements { get; init; } = 128;
    internal int MaxReservedNames { get; init; } = 642;

    internal MetricsRuntimeOptions Copy()
    {
        if (MaxDefinitions <= 0 || MaxDimensionKeys is < 1 or > 8 || MaxDimensionValueBytes <= 0
            || MaxNameLength is < 1 or > 128 || MaxKeyLength is < 1 or > 64 || MaxDescriptionBytes <= 0
            || MaxHistogramBounds is < 1 or > 32 || MaxObservableMeasurements <= 0 || MaxReservedNames < 2)
        { throw new ArgumentOutOfRangeException(nameof(MetricsRuntimeOptions), "Supply positive finite Metrics limits within the v1 name/key/bucket profile."); }
        return (MetricsRuntimeOptions)MemberwiseClone();
    }
}

internal enum MetricKind { Counter, UpDownCounter, Gauge, Histogram, Timer, ObservableGauge }

// Immutable metadata only. The SDK owns all aggregate values and cardinality overflow.
internal sealed record MetricRegistration(MetricDefinition Definition, MetricKind Kind,
    string ExportedName, IReadOnlyList<double> HistogramBounds)
{
    // BCL publication is synchronous: the view must see metadata before creation returns its instrument.
    internal Instrument? Instrument { get; set; }
}
