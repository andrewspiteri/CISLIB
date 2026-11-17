using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ITimer = CIS.Metrics.ITimer;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class MetricsPrimitiveTests
{
    [TestMethod]
    public async Task PrimitiveHandlesEmitTheActualBclKindsUnitsAndLiteralValues()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        ICounter counter = runtime.CreateCounter(new("app.requests"));
        IUpDownCounter active = runtime.CreateUpDownCounter(new("app.active"));
        IGauge gauge = runtime.CreateGauge(new("app.utilization", MetricUnit.Ratio));
        IHistogram sizes = runtime.CreateHistogram(new("app.payload", MetricUnit.Bytes, histogramBounds: [0, 64, 1_024]));
        counter.Add(7); counter.Add(2);
        active.Add(3); active.Add(-1);
        gauge.Set(10); gauge.Set(-5);
        sizes.Record(0); sizes.Record(32); sizes.Record(64);
        CollectionAssert.AreEqual(new long[] { 7, 2 }, listener.Points("app.requests").Select(point => (long)point.Value).ToArray());
        CollectionAssert.AreEqual(new long[] { 3, -1 }, listener.Points("app.active").Select(point => (long)point.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 10d, -5d }, listener.Points("app.utilization").Select(point => (double)point.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 0d, 32d, 64d }, listener.Points("app.payload").Select(point => (double)point.Value).ToArray());
        Assert.IsInstanceOfType<Counter<long>>(listener.Points("app.requests")[0].Instrument);
        Assert.IsInstanceOfType<UpDownCounter<long>>(listener.Points("app.active")[0].Instrument);
        Assert.IsInstanceOfType<Gauge<double>>(listener.Points("app.utilization")[0].Instrument);
        Assert.IsInstanceOfType<Histogram<double>>(listener.Points("app.payload")[0].Instrument);
        Assert.IsTrue(string.IsNullOrEmpty(listener.Points("app.requests")[0].Instrument.Unit));
        Assert.AreEqual("1", listener.Points("app.utilization")[0].Instrument.Unit);
        Assert.AreEqual("By", listener.Points("app.payload")[0].Instrument.Unit);
        // SDK accumulation/buckets and gauge replacement are independently checked
        // at the later real provider boundary; this listener sees raw BCL emissions.
    }

    [TestMethod]
    public async Task DefaultCounterIncrementAndSignedDeltasRetainTheFullInt64Input()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        ICounter counter = runtime.CreateCounter(new("app.quantity"));
        IUpDownCounter delta = runtime.CreateUpDownCounter(new("app.balance"));
        counter.Add(); counter.Add(long.MaxValue);
        delta.Add(long.MinValue);
        CollectionAssert.AreEqual(new[] { 1L, long.MaxValue }, listener.Points("app.quantity").Select(point => (long)point.Value).ToArray());
        Assert.AreEqual(long.MinValue, (long)listener.Points("app.balance").Single().Value);
    }

    [TestMethod]
    public async Task TimerUsesMonotonicSecondsMergesCompletionDimensionsAndRecordsOnlyItsFirstCompletion()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var clock = new MetricsTestClock();
        await using var runtime = new MetricsRuntime(meter, clock: clock);
        ITimer timer = runtime.CreateTimer(new("app.operation.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["operation", "outcome"]));
        ITimingScope timing = timer.Start(new(("operation", "read")));
        clock.AdvanceMilliseconds(125);
        Assert.AreEqual(0.125, timing.Stop(new(("outcome", "success"))));
        clock.AdvanceMilliseconds(1_000);
        Assert.AreEqual(0.125, timing.Stop(new(("outcome", "error"))));
        timing.Dispose();
        MetricsPoint point = listener.Points("app.operation.duration").Single();
        Assert.AreEqual(0.125, (double)point.Value);
        Assert.AreEqual("s", point.Instrument.Unit);
        MetricsTestSupport.AssertTags(point, new("operation", "read"), new("outcome", "success"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentStopAndStopOrDisposeHaveOnePublishedDurationAndOneRecordingAttempt(bool dispose)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var clock = new MetricsTestClock();
        await using var runtime = new MetricsRuntime(meter, clock: clock);
        ITimer timer = runtime.CreateTimer(new("app.raced.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["operation", "outcome"]));
        ITimingScope timing = timer.Start(new(("operation", "read")));
        var firstEntered = MetricsTestSupport.Signal();
        var secondEntered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        double? secondStoppedSeconds = null;
        Task<double> first = Task.Run(async () =>
        { firstEntered.TrySetResult(); await release.Task; return timing.Stop(new(("outcome", "success"))); });
        Task second = Task.Run(async () =>
        {
            secondEntered.TrySetResult(); await release.Task;
            if (dispose) { timing.Dispose(); }
            else { secondStoppedSeconds = timing.Stop(new(("outcome", "error"))); }
        });
        try
        {
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(MetricsTestSupport.Deadline);
            clock.AdvanceMilliseconds(25);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(MetricsTestSupport.Deadline);
            Assert.AreEqual(0.025, await first);
            if (!dispose) { Assert.AreEqual(0.025, secondStoppedSeconds.GetValueOrDefault()); }
            Assert.AreEqual(0.025, timing.Stop());
            MetricsPoint point = listener.Points("app.raced.duration").Single();
            Assert.AreEqual(0.025, (double)point.Value);
            Assert.IsTrue(point.Tags.Any(tag => tag.Key == "operation" && Equals(tag.Value, "read")));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(MetricsTestSupport.Deadline);
            timing.Dispose();
        }
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("unknown")]
    [DataRow("identity")]
    [DataRow("oversize")]
    [DataRow("null-array")]
    [DataRow("capacity")]
    public async Task InvalidCompletionDropsTheWholeTimerAttemptAndDisposeCannotRetry(string invalid)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var clock = new MetricsTestClock();
        await using var runtime = new MetricsRuntime(meter, clock: clock);
        ITimer timer = runtime.CreateTimer(new("app.invalid.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["operation", "outcome"]));
        ITimingScope timing = timer.Start(new(("operation", "read")));
        MetricDimensions completion = invalid switch
        {
            "duplicate" => new(("operation", "write")),
            "unknown" => new(("missing", "value")),
            "identity" => new(("application", "override")),
            "null-array" => new MetricDimensions(null!),
            "capacity" => new MetricDimensions(Enumerable.Repeat(("outcome", "success"), 9).ToArray()),
            _ => new(("outcome", new string('x', 257)))
        };
        clock.AdvanceMilliseconds(10);
        Assert.AreEqual(0.01, timing.Stop(completion));
        timing.Dispose();
        clock.AdvanceMilliseconds(500);
        Assert.AreEqual(0.01, timing.Stop(new(("outcome", "success"))));
        Assert.AreEqual(0, listener.Points("app.invalid.duration").Length);
    }

    [TestMethod]
    [DataRow("counter-negative")]
    [DataRow("gauge-nan")]
    [DataRow("gauge-infinity")]
    [DataRow("histogram-negative")]
    [DataRow("histogram-nan")]
    [DataRow("timer-negative")]
    [DataRow("unknown-key")]
    [DataRow("duplicate-key")]
    [DataRow("null-key")]
    [DataRow("null-value")]
    [DataRow("application")]
    [DataRow("environment")]
    [DataRow("utf8-limit")]
    [DataRow("too-many-keys")]
    public async Task InvalidRuntimeValuesOrDimensionsDropWithoutPoisoningSubsequentValidRecording(string invalid)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        const string name = "app.validation";
        var definition = new MetricDefinition(name, allowedDimensionKeys: ["operation"]);
        Action reject, accept;
        if (invalid.StartsWith("gauge", StringComparison.Ordinal))
        {
            IGauge gauge = runtime.CreateGauge(definition);
            reject = () => gauge.Set(invalid == "gauge-nan" ? double.NaN : double.PositiveInfinity);
            accept = () => gauge.Set(2);
        }
        else if (invalid.StartsWith("histogram", StringComparison.Ordinal))
        {
            IHistogram histogram = runtime.CreateHistogram(new(name, histogramBounds: [0, 1]));
            reject = () => histogram.Record(invalid == "histogram-negative" ? -1 : double.NaN);
            accept = () => histogram.Record(2);
        }
        else if (invalid == "timer-negative")
        {
            ITimer timer = runtime.CreateTimer(new(name, MetricUnit.Seconds));
            reject = () => timer.Record(-1);
            accept = () => timer.Record(2);
        }
        else
        {
            ICounter counter = runtime.CreateCounter(definition);
            MetricDimensions dimensions = invalid switch
            {
                "unknown-key" => new(("unknown", "read")),
                "duplicate-key" => new(("operation", "read"), ("operation", "write")),
                "null-key" => new((null!, "read")),
                "null-value" => new(("operation", null!)),
                "application" => new(("application", "override")),
                "environment" => new(("environment", "override")),
                "utf8-limit" => new(("operation", new string('é', 129))),
                "too-many-keys" => new(Enumerable.Range(0, 9).Select(index => ("key" + index, "value")).ToArray()),
                _ => default
            };
            reject = () => counter.Add(invalid == "counter-negative" ? -1 : 1, dimensions);
            accept = () => counter.Add(2);
        }
        reject();
        Assert.AreEqual(0, listener.Points(name).Length);
        accept();
        MetricsPoint accepted = listener.Points(name).Single();
        Assert.AreEqual(2d, Convert.ToDouble(accepted.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task RepeatedRejectedMeasurementsUseBoundedValueFreeDiagnosticsAndASinkFailureCannotEscape()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var diagnostics = new ConcurrentQueue<string>();
        await using var runtime = new MetricsRuntime(meter, diagnostic: diagnostics.Enqueue);
        ICounter counter = runtime.CreateCounter(new("app.bounded", allowedDimensionKeys: ["operation"]));
        for (int index = 0; index < 1_000; index++)
        { counter.Add(-1, new(("operation", "PRIVATE-" + index))); }
        Assert.AreEqual(1, diagnostics.Count, "One fixed diagnostic category must not become one log per rejected measurement.");
        Assert.IsTrue(diagnostics.All(message => !message.Contains("PRIVATE", StringComparison.Ordinal)));
        counter.Add(3);
        Assert.AreEqual(3L, (long)listener.Points("app.bounded").Single().Value);
        using var otherMeter = new Meter("CIS.Metrics");
        using var otherListener = new MetricsTestListener(otherMeter);
        await using var other = new MetricsRuntime(otherMeter, diagnostic: _ => throw new InvalidOperationException("Broken diagnostic sink."));
        ICounter isolated = other.CreateCounter(new("app.isolated"));
        isolated.Add(-1);
        isolated.Add(1);
        Assert.AreEqual(1L, (long)otherListener.Points("app.isolated").Single().Value);
    }

    [TestMethod]
    public async Task DisabledMeasurementsAndATimerStartedWithoutCollectionDoNotReplayAfterAListenerAppears()
    {
        using var meter = new Meter("CIS.Metrics");
        var clock = new MetricsTestClock();
        await using var runtime = new MetricsRuntime(meter, clock: clock);
        ICounter counter = runtime.CreateCounter(new("app.disabled.count"));
        ITimer timer = runtime.CreateTimer(new("app.disabled.duration", MetricUnit.Seconds,
            allowedDimensionKeys: ["operation", "outcome"]));
        counter.Add(9);
        using ITimingScope timing = timer.Start(new(("operation", "early")));
        clock.AdvanceMilliseconds(10);
        using var listener = new MetricsTestListener(meter);
        Assert.AreEqual(0.01, timing.Stop(new(("outcome", "success"))));
        Assert.AreEqual(0, listener.Points("app.disabled.duration").Length);
        counter.Add(3);
        timer.Record(0.5);
        Assert.AreEqual(3L, (long)listener.Points("app.disabled.count").Single().Value);
        Assert.AreEqual(0.5, (double)listener.Points("app.disabled.duration").Single().Value);
    }

    [TestMethod]
    public async Task AnOrdinaryBackendListenerFailureCannotFailTheBusinessRecordingCall()
    {
        using var meter = new Meter("CIS.Metrics");
        using var observed = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using (var faulty = new MeterListener())
        {
            faulty.InstrumentPublished = (instrument, listener) =>
            { if (ReferenceEquals(instrument.Meter, meter)) { listener.EnableMeasurementEvents(instrument); } };
            faulty.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("Controlled listener failure."));
            faulty.Start();
            runtime.CreateCounter(new("app.backend")).Add(1);
        }
        runtime.CreateCounter(new("app.backend")).Add(2);
        Assert.AreEqual(2L, (long)observed.Points("app.backend").Last().Value);
    }
}
