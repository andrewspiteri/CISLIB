using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class CollectionIdentityTests
{
    [TestMethod]
    [DataRow("text/plain; version=0.0.4")]
    [DataRow("application/openmetrics-text; version=0.0.1")]
    [DataRow("application/openmetrics-text; version=1.0.0")]
    [DataRow("text/plain; version=1.0.0; escaping=underscores")]
    [DataRow("text/plain; version=1.0.0; escaping=allow-utf-8")]
    [DataRow("text/plain; version=1.0.0; escaping=dots")]
    [DataRow("text/plain; version=1.0.0; escaping=values")]
    [DataRow("application/openmetrics-text; version=1.0.0; escaping=underscores")]
    [DataRow("application/openmetrics-text; version=1.0.0; escaping=allow-utf-8")]
    [DataRow("application/openmetrics-text; version=1.0.0; escaping=dots")]
    [DataRow("application/openmetrics-text; version=1.0.0; escaping=values")]
    public async Task EveryRealExportedSampleKeepsAuthoritativeIdentityAcrossNegotiatedFormats(string accept)
    {
        await using var host = CollectionTestHost.Create(options =>
        { options.CardinalityLimit = 1; options.HistogramCardinalityLimit = 1; });
        ICounter counter = host.Metrics.CreateCounter(new("app.collection.events", allowedDimensionKeys: ["category"]));
        IHistogram duration = host.Metrics.CreateHistogram(new("app.collection.duration", MetricUnit.Seconds, allowedDimensionKeys: ["category"]));
        IGauge gauge = host.Metrics.CreateGauge(new("app.collection.level", allowedDimensionKeys: ["category"]));
        await host.StartAsync();
        counter.Add(2, new(("category", "first")));
        counter.Add(3, new(("category", "second")));
        counter.Add(99, new(("application", "facade-override")));
        duration.Record(0.25, new(("category", "first")));
        duration.Record(0.5, new(("category", "second")));
        gauge.Set(7, new(("category", "quote\"slash\\newline\n")));
        using HttpResponseMessage work = await host.Client.GetAsync("/work/123").WaitAsync(CollectionTestHost.Deadline);
        work.EnsureSuccessStatusCode();

        string text = await host.ScrapeAsync(accept);
        CollectionText.AssertIdentity(text);
        Assert.IsFalse(text.Contains("facade-override", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("otel_metric_overflow", StringComparison.Ordinal), text);
        string[] samples = CollectionText.Samples(text);
        Assert.IsTrue(samples.Any(line => line.Contains("target_info", StringComparison.Ordinal)), text);
        Assert.IsTrue(samples.Any(line => line.Contains("http", StringComparison.Ordinal) && line.Contains("duration", StringComparison.Ordinal)), text);
        Assert.IsTrue(samples.Any(line => line.Contains("bucket", StringComparison.Ordinal)), text);
        Assert.IsTrue(samples.Any(line => line.Contains("events", StringComparison.Ordinal) && CollectionText.Value(line) == 2), text);
        Assert.IsTrue(samples.Any(line => line.Contains("events", StringComparison.Ordinal) && CollectionText.Value(line) == 3), text);
        Assert.IsTrue(samples.Any(line => line.Contains("quote\\\"slash\\\\newline\\n", StringComparison.Ordinal) && CollectionText.Value(line) == 7), text);
        Assert.IsFalse(samples.Any(line => line.Contains("# {", StringComparison.Ordinal)), "Exemplars are disabled.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TwoActualHostsIsolateCisAndNativeStreamsEvenWhenActivationOrderIsReversed(bool reversed)
    {
        await using var first = CollectionTestHost.Create();
        await using var second = CollectionTestHost.Create();
        Assert.AreNotSame(first.Context.CisMeter, second.Context.CisMeter);
        Assert.AreNotSame(first.Context.HostingMeter, second.Context.HostingMeter);
        ICounter firstCounter = first.Metrics.CreateCounter(new("app.isolation.events"));
        ICounter secondCounter = second.Metrics.CreateCounter(new("app.isolation.events"));
        if (reversed) { await second.StartAsync(); await first.StartAsync(); }
        else { await first.StartAsync(); await second.StartAsync(); }
        firstCounter.Add(3);
        secondCounter.Add(7);
        using HttpResponseMessage firstWork = await first.Client.GetAsync("/work/1").WaitAsync(CollectionTestHost.Deadline);
        using HttpResponseMessage secondWork = await second.Client.GetAsync("/work/2").WaitAsync(CollectionTestHost.Deadline);
        using HttpResponseMessage thirdWork = await second.Client.GetAsync("/work/3").WaitAsync(CollectionTestHost.Deadline);
        firstWork.EnsureSuccessStatusCode(); secondWork.EnsureSuccessStatusCode(); thirdWork.EnsureSuccessStatusCode();
        string[] firstSamples = CollectionText.Samples(await first.ScrapeAsync());
        string[] secondSamples = CollectionText.Samples(await second.ScrapeAsync());
        Assert.AreEqual(3d, CollectionText.Value(firstSamples.Single(line => line.StartsWith("app_isolation_events_total{", StringComparison.Ordinal))));
        Assert.AreEqual(7d, CollectionText.Value(secondSamples.Single(line => line.StartsWith("app_isolation_events_total{", StringComparison.Ordinal))));
        Assert.AreEqual(1d, CollectionText.Value(firstSamples.Single(line => line.StartsWith("http_server_request_duration_seconds_count{", StringComparison.Ordinal))));
        Assert.AreEqual(2d, CollectionText.Value(secondSamples.Single(line => line.StartsWith("http_server_request_duration_seconds_count{", StringComparison.Ordinal))));
    }
}
