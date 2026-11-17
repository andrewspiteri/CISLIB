using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class MetricsDefinitionTests
{
    [TestMethod]
    public void DefinitionsAndDimensionsKeepDetachedImmutableCopiesOfCallerOwnedCollections()
    {
        var keys = new List<string> { "operation" };
        var bounds = new List<double> { 0, 64, 1_024 };
        var definition = new MetricDefinition("app.payload", MetricUnit.Bytes, "Payload bytes", keys, bounds);
        keys[0] = "changed";
        keys.Add("extra");
        bounds[0] = 32;
        bounds.Clear();
        CollectionAssert.AreEqual(new[] { "operation" }, definition.AllowedDimensionKeys.ToArray());
        CollectionAssert.AreEqual(new[] { 0d, 64d, 1_024d }, definition.HistogramBounds!.ToArray());
        var pairs = new[] { (Key: "operation", Value: "read") };
        var dimensions = new MetricDimensions(pairs);
        pairs[0] = ("operation", "write");
        CollectionAssert.AreEqual(new[] { new KeyValuePair<string, string>("operation", "read") }, dimensions.Values.ToArray());
        Assert.AreEqual(0, default(MetricDimensions).Values.Count);
    }

    [TestMethod]
    public async Task EquivalentDefinitionsReuseOneInstrumentAndTreatAllowedKeysAsAnOrdinalSet()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        ICounter first = runtime.CreateCounter(new("app.reused", description: "Completed work", allowedDimensionKeys: ["operation", "outcome"]));
        ICounter second = runtime.CreateCounter(new("app.reused", description: "Completed work", allowedDimensionKeys: ["outcome", "operation"]));
        Assert.AreSame(first, second);
        Assert.AreEqual(1, listener.Instruments.Count);
        first.Add(1, new(("operation", "read")));
        second.Add(2, new(("outcome", "success")));
        CollectionAssert.AreEqual(new[] { 1L, 2L }, listener.Points("app.reused").Select(point => (long)point.Value).ToArray());
    }

    [TestMethod]
    [DataRow("kind")]
    [DataRow("unit")]
    [DataRow("description")]
    [DataRow("schema")]
    [DataRow("histogram-profile")]
    public async Task ReusingANameWithDifferentMeaningFailsInsteadOfPublishingASecondInstrument(string conflict)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        if (conflict == "histogram-profile")
        {
            runtime.CreateHistogram(new("app.conflict", histogramBounds: [1, 2]));
            Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CreateHistogram(new("app.conflict", histogramBounds: [1, 3])));
        }
        else
        {
            runtime.CreateGauge(new("app.conflict", description: "Current work", allowedDimensionKeys: ["operation"]));
            Action conflicting = conflict switch
            {
                "kind" => () => runtime.CreateCounter(new("app.conflict", description: "Current work", allowedDimensionKeys: ["operation"])),
                "unit" => () => runtime.CreateGauge(new("app.conflict", MetricUnit.Bytes, "Current work", ["operation"])),
                "description" => () => runtime.CreateGauge(new("app.conflict", description: "Different meaning", allowedDimensionKeys: ["operation"])),
                _ => () => runtime.CreateGauge(new("app.conflict", description: "Current work", allowedDimensionKeys: ["outcome"]))
            };
            Assert.ThrowsExactly<InvalidOperationException>(conflicting);
        }
        Assert.AreEqual(1, runtime.Definitions.Count);
        Assert.AreEqual(1, listener.Instruments.Count);
    }

    [TestMethod]
    [DataRow("translation")]
    [DataRow("histogram-child")]
    [DataRow("target-info")]
    [DataRow("scope-info")]
    public async Task ExporterTranslationGeneratedChildrenAndReservedFamiliesCannotCollide(string conflict)
    {
        using var meter = new Meter("CIS.Metrics");
        await using var runtime = new MetricsRuntime(meter);
        string name;
        if (conflict == "translation") { runtime.CreateGauge(new("app.work")); name = "app_work"; }
        else if (conflict == "histogram-child")
        { runtime.CreateTimer(new("app.work.duration", MetricUnit.Seconds)); name = "app.work.duration.seconds.count"; }
        else { name = conflict == "target-info" ? "target_info" : "otel_scope_info"; }
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CreateGauge(new(name)));
    }

    [TestMethod]
    [DataRow("empty-name")]
    [DataRow("uppercase-name")]
    [DataRow("long-name")]
    [DataRow("unknown-unit")]
    [DataRow("description-bytes")]
    [DataRow("duplicate-key")]
    [DataRow("translated-key")]
    [DataRow("reserved-key")]
    [DataRow("key-count")]
    [DataRow("generated-suffix")]
    public async Task InvalidDefinitionsFailBeforeAnyInstrumentIsPublished(string invalid)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        string name = invalid switch
        { "empty-name" => "", "uppercase-name" => "App.Work", "long-name" => new string('a', 129), "generated-suffix" => "app.work_total", _ => "app.valid" };
        string[] keys = invalid switch
        {
            "duplicate-key" => ["operation", "operation"],
            "translated-key" => ["client.id", "client_id"],
            "reserved-key" => ["application"],
            "key-count" => Enumerable.Range(0, 9).Select(index => "key" + index).ToArray(),
            _ => []
        };
        Assert.ThrowsExactly<ArgumentException>(() => runtime.CreateCounter(new MetricDefinition(name,
            invalid == "unknown-unit" ? (MetricUnit)99 : MetricUnit.Count,
            invalid == "description-bytes" ? new string('é', 513) : "", keys)));
        Assert.AreEqual(0, runtime.Definitions.Count);
        Assert.AreEqual(0, listener.Instruments.Count);
    }

    [TestMethod]
    [DataRow("negative")]
    [DataRow("duplicate")]
    [DataRow("descending")]
    [DataRow("nan")]
    [DataRow("infinite")]
    [DataRow("empty")]
    [DataRow("too-many")]
    [DataRow("missing-nonduration")]
    public async Task HistogramProfilesMustHaveExplicitFiniteOrderedNonnegativeBoundaries(string invalid)
    {
        using var meter = new Meter("CIS.Metrics");
        await using var runtime = new MetricsRuntime(meter);
        double[]? bounds = invalid switch
        {
            "negative" => [-1, 1], "duplicate" => [1, 1], "descending" => [2, 1], "nan" => [double.NaN],
            "infinite" => [double.PositiveInfinity], "too-many" => Enumerable.Range(0, 33).Select(value => (double)value).ToArray(),
            "missing-nonduration" => null, _ => []
        };
        Assert.ThrowsExactly<ArgumentException>(() => runtime.CreateHistogram(new("app.profile", MetricUnit.Bytes, histogramBounds: bounds)));
    }

    [TestMethod]
    public async Task TimersRequireSecondsAndTheDefaultDurationProfileMatchesThePortableLiteralCatalog()
    {
        using var meter = new Meter("CIS.Metrics");
        await using var runtime = new MetricsRuntime(meter);
        Assert.ThrowsExactly<ArgumentException>(() => runtime.CreateTimer(new("app.wrong", MetricUnit.Bytes)));
        runtime.CreateTimer(new("app.duration", MetricUnit.Seconds));
        CollectionAssert.AreEqual(new[] { 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10 },
            runtime.Definitions.Single().HistogramBounds.ToArray());
    }

    [TestMethod]
    public async Task KnownCapacityFailsStartupButLateRejectionsDoNotGrowTheCatalogOrInterruptAdmittedSeries()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var diagnostics = new ConcurrentQueue<string>();
        await using var runtime = new MetricsRuntime(meter, new() { MaxDefinitions = 1 }, diagnostics.Enqueue);
        ICounter admitted = runtime.CreateCounter(new("app.admitted"));
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CreateGauge(new("app.startup.rejected")));
        Assert.AreEqual(1, runtime.Definitions.Count);
        runtime.SealStartup();
        for (int index = 0; index < 100; index++)
        { runtime.CreateCounter(new("app.late" + index)).Add(1); }
        // Rejected metadata must not reserve its name or manufacture a conflict.
        runtime.CreateGauge(new("app.late0")).Set(12);
        admitted.Add(5);
        Assert.AreEqual(1, runtime.Definitions.Count);
        Assert.AreEqual(1, listener.Instruments.Count);
        Assert.AreEqual(5L, (long)listener.Points("app.admitted").Single().Value);
        Assert.AreEqual(1, diagnostics.Count);
    }

    [TestMethod]
    public async Task CompositionAdmissionRunsBeforeSynchronousInstrumentPublicationAndRejectionLeavesNoPartialDefinition()
    {
        using var meter = new Meter("CIS.Metrics");
        bool admit = false;
        await using var runtime = new MetricsRuntime(meter, admitDefinition: _ => admit);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CreateCounter(new("app.refused")));
        Assert.AreEqual(0, runtime.Definitions.Count);
        MetricRegistration? publishedMetadata = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (ReferenceEquals(instrument.Meter, meter) && runtime.TryGetRegistration(instrument, out MetricRegistration? registration))
            { publishedMetadata = registration; }
        };
        listener.Start();
        admit = true;
        runtime.CreateCounter(new("app.accepted", description: "Accepted work"));
        Assert.IsNotNull(publishedMetadata, "The provider must see admitted metadata while the BCL publishes its instrument.");
        Assert.AreEqual("app.accepted", publishedMetadata.Definition.Name);
        Assert.AreEqual(MetricKind.Counter, publishedMetadata.Kind);
        Assert.AreEqual("Accepted work", publishedMetadata.Definition.Description);
        Assert.AreEqual(1, runtime.Definitions.Count);
    }

    [TestMethod]
    public async Task NativeNameReservationsAreIdempotentAtomicAndProtectTheSameExportedNamespace()
    {
        using var meter = new Meter("CIS.Metrics");
        await using var runtime = new MetricsRuntime(meter);
        string[] names = ["http_server_request_duration_seconds", "http_server_request_duration_seconds_count"];
        runtime.ReserveExportedNames(names);
        string[] before = runtime.ReservedExportedNames.ToArray();
        runtime.ReserveExportedNames(names);
        CollectionAssert.AreEqual(before, runtime.ReservedExportedNames.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => runtime.ReserveExportedNames(["otherwise_valid", "invalid-name"]));
        CollectionAssert.AreEqual(before, runtime.ReservedExportedNames.ToArray(), "An invalid set must not partly reserve its valid prefix.");
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CreateGauge(new("http.server.request.duration.seconds.count")));
        runtime.CreateCounter(new("app.accepted"));
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.ReserveExportedNames(["late_native_name"]));
    }
}
