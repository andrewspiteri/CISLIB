using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class MetricsConformanceTests
{
    [TestMethod]
    [DataRow("COUNTER-INT64")]
    [DataRow("UPDOWN-SIGNED")]
    [DataRow("GAUGE-FINITE")]
    [DataRow("HISTOGRAM-BYTES")]
    [DataRow("TIMER-DIRECT-SECONDS")]
    [DataRow("DIMENSIONS-ATOMIC-DROP")]
    public async Task PortableMeasurementFixturesMatchActualBclEmissions(string id)
    {
        using JsonDocument document = Fixture();
        JsonElement item = Case(document, "measurementCases", id);
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        MetricDefinition definition = Definition(item);
        Action<string, MetricDimensions> record;
        switch (item.GetProperty("kind").GetString())
        {
            case "counter":
                ICounter counter = runtime.CreateCounter(definition);
                record = (value, tags) => counter.Add(long.Parse(value, CultureInfo.InvariantCulture), tags); break;
            case "upDownCounter":
                IUpDownCounter active = runtime.CreateUpDownCounter(definition);
                record = (value, tags) => active.Add(long.Parse(value, CultureInfo.InvariantCulture), tags); break;
            case "gauge":
                IGauge gauge = runtime.CreateGauge(definition);
                record = (value, tags) => gauge.Set(double.Parse(value, CultureInfo.InvariantCulture), tags); break;
            case "histogram":
                IHistogram histogram = runtime.CreateHistogram(definition);
                record = (value, tags) => histogram.Record(double.Parse(value, CultureInfo.InvariantCulture), tags); break;
            case "timer":
                ITimer timer = runtime.CreateTimer(definition);
                record = (value, tags) => timer.Record(double.Parse(value, CultureInfo.InvariantCulture), tags); break;
            default: throw new AssertFailedException("Unknown portable fixture kind.");
        }
        JsonElement operations = item.GetProperty("operations");
        foreach (JsonElement operation in operations.EnumerateArray())
        { record(operation.GetProperty("value").GetString()!, Dimensions(operation.GetProperty("dimensions"))); }
        MetricsPoint[] observed = listener.Points(definition.Name);
        AssertEmissions(item.GetProperty("expectedEmissions"), observed);
        Assert.AreEqual(item.GetProperty("expectedDropped").GetInt32(), operations.GetArrayLength() - observed.Length);
    }

    [TestMethod]
    [DataRow("TIMING-FIRST-STOP")]
    [DataRow("TIMING-INVALID-COMPLETION-CANNOT-RETRY")]
    [DataRow("TIMING-DISPOSE-INFERS-NO-OUTCOME")]
    public async Task PortableTimingFixturesPreserveFirstCompletionAndDoNotInferAnOutcome(string id)
    {
        using JsonDocument document = Fixture();
        JsonElement item = Case(document, "timingScopeCases", id);
        var clock = new MetricsTestClock(long.Parse(item.GetProperty("timestampFrequency").GetString()!, CultureInfo.InvariantCulture),
            long.Parse(item.GetProperty("startedAt").GetString()!, CultureInfo.InvariantCulture));
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter, clock: clock);
        string name = item.GetProperty("name").GetString()!;
        ITimer timer = runtime.CreateTimer(new(name, MetricUnit.Seconds, allowedDimensionKeys: Keys(item)));
        using ITimingScope timing = timer.Start(Dimensions(item.GetProperty("initialDimensions")));
        var stopped = new List<double>();
        foreach (JsonElement completion in item.GetProperty("completions").EnumerateArray())
        {
            clock.SetTimestamp(long.Parse(completion.GetProperty("timestamp").GetString()!, CultureInfo.InvariantCulture));
            if (completion.GetProperty("operation").GetString() == "dispose") { timing.Dispose(); }
            else { stopped.Add(timing.Stop(Dimensions(completion.GetProperty("dimensions")))); }
        }
        CollectionAssert.AreEqual(item.GetProperty("expectedStopSeconds").EnumerateArray().Select(value => value.GetDouble()).ToArray(), stopped);
        AssertEmissions(item.GetProperty("expectedEmissions"), listener.Points(name));
    }

    [TestMethod]
    [DataRow("OBSERVABLE-UNAVAILABLE")]
    [DataRow("OBSERVABLE-FAILURE")]
    [DataRow("OBSERVABLE-KNOWN-ZERO")]
    public async Task PortableSnapshotFixturesDistinguishUnavailableFailureAndKnownZero(string id)
    {
        using JsonDocument document = Fixture();
        JsonElement item = Case(document, "observableCases", id);
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        MetricDefinition definition = Definition(item);
        runtime.RegisterObservableGauge(definition, () => item.GetProperty("throws").GetBoolean()
            ? throw new InvalidOperationException("Controlled fixture failure.")
            : item.GetProperty("observations").EnumerateArray().Select(observation =>
                new MetricObservation(double.Parse(observation.GetProperty("value").GetString()!, CultureInfo.InvariantCulture),
                    Dimensions(observation.GetProperty("dimensions")))).ToArray());
        listener.Collect();
        AssertEmissions(item.GetProperty("expectedEmissions"), listener.Points(definition.Name));
    }

    private static MetricDefinition Definition(JsonElement item)
        => new(item.GetProperty("name").GetString()!, item.GetProperty("unit").GetString() switch
        {
            "count" => MetricUnit.Count, "seconds" => MetricUnit.Seconds, "bytes" => MetricUnit.Bytes, "ratio" => MetricUnit.Ratio,
            _ => throw new AssertFailedException("Unknown portable unit.")
        }, allowedDimensionKeys: Keys(item), histogramBounds: item.TryGetProperty("histogramBounds", out JsonElement bounds)
            ? bounds.EnumerateArray().Select(value => value.GetDouble()).ToArray() : null);

    private static string[] Keys(JsonElement item)
        => item.GetProperty("allowedDimensionKeys").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static MetricDimensions Dimensions(JsonElement dimensions)
        => new(dimensions.EnumerateArray().Select(pair => (pair[0].GetString()!, pair[1].GetString()!)).ToArray());

    private static void AssertEmissions(JsonElement expected, MetricsPoint[] observed)
    {
        Assert.AreEqual(expected.GetArrayLength(), observed.Length);
        int index = 0;
        foreach (JsonElement emission in expected.EnumerateArray())
        {
            MetricsPoint point = observed[index++];
            string value = emission.GetProperty("value").GetString()!;
            if (point.Value is long integer) { Assert.AreEqual(long.Parse(value, CultureInfo.InvariantCulture), integer); }
            else { Assert.AreEqual(double.Parse(value, CultureInfo.InvariantCulture), (double)point.Value); }
            MetricsTestSupport.AssertTags(point, emission.GetProperty("dimensions").EnumerateArray().Select(pair =>
                new KeyValuePair<string, object?>(pair[0].GetString()!, pair[1].GetString())).ToArray());
        }
    }

    private static JsonElement Case(JsonDocument document, string group, string id)
    {
        Assert.AreEqual(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        return document.RootElement.GetProperty(group).EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
    }

    private static JsonDocument Fixture()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "conformance", "metrics", "v1", "primitive-cases.json");
            if (File.Exists(path)) { return JsonDocument.Parse(File.ReadAllBytes(path)); }
        }
        throw new AssertFailedException("The preserved Metrics portable fixture was not found under the repository.");
    }
}
