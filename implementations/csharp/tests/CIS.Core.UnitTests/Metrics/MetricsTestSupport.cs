using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

internal sealed record MetricsPoint(Instrument Instrument, object Value, KeyValuePair<string, object?>[] Tags);

// Observe the actual BCL instruments. This records test evidence, not a replacement
// aggregator or an assertion about OpenTelemetry bucket/exporter implementation.
internal sealed class MetricsTestListener : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<MetricsPoint> points = new();
    private readonly ConcurrentQueue<Instrument> instruments = new();

    internal MetricsTestListener(Meter meter)
    {
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (!ReferenceEquals(meter, instrument.Meter)) { return; }
            instruments.Enqueue(instrument);
            current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            points.Enqueue(new(instrument, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            points.Enqueue(new(instrument, value, tags.ToArray())));
        listener.Start();
    }

    internal IReadOnlyList<Instrument> Instruments => instruments.ToArray();
    internal MetricsPoint[] Points(string name) => points.Where(point => point.Instrument.Name == name).ToArray();
    internal void Collect() => listener.RecordObservableInstruments();
    public void Dispose() => listener.Dispose();
}

internal sealed class MetricsTestClock(long frequency = 1_000, long started = 0) : TimeProvider
{
    private long timestamp = started;
    public override long TimestampFrequency => frequency;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    internal void AdvanceMilliseconds(long elapsed) => Interlocked.Add(ref timestamp, elapsed);
    internal void SetTimestamp(long value) => Interlocked.Exchange(ref timestamp, value);
}

internal static class MetricsTestSupport
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static void AssertTags(MetricsPoint point, params KeyValuePair<string, object?>[] expected)
        => CollectionAssert.AreEquivalent(expected, point.Tags);
}
