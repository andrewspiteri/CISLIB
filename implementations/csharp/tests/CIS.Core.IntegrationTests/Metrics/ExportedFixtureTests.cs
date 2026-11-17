using System.Text.Json;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class ExportedFixtureTests
{
    [TestMethod]
    public async Task PortableExportedFamiliesMatchTheActualPinnedExporterAndNativeHostingInstrument()
    {
        string path = Path.Combine(CandidatePackageFixture.Repository(), "conformance", "metrics", "v1", "primitive-cases.json");
        using JsonDocument fixture = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        await using var host = CollectionTestHost.Create();
        var record = new List<Action>();
        foreach (JsonElement family in fixture.RootElement.GetProperty("exportedFamilies").EnumerateArray())
        {
            string kind = family.GetProperty("kind").GetString()!;
            if (kind == "nativeHistogram") { continue; }
            MetricUnit unit = family.GetProperty("unit").GetString() switch
            {
                "seconds" => MetricUnit.Seconds, "bytes" => MetricUnit.Bytes,
                "count" => MetricUnit.Count, _ => throw new AssertFailedException("Unknown portable unit.")
            };
            double[]? bounds = family.TryGetProperty("histogramBounds", out JsonElement values)
                ? values.EnumerateArray().Select(value => value.GetDouble()).ToArray() : null;
            var definition = new MetricDefinition(family.GetProperty("logicalName").GetString()!, unit, histogramBounds: bounds);
            switch (kind)
            {
                case "counter":
                    ICounter counter = host.Metrics.CreateCounter(definition);
                    record.Add(() => counter.Add(1)); break;
                case "timer":
                    CIS.Metrics.ITimer timer = host.Metrics.CreateTimer(definition);
                    record.Add(() => timer.Record(0.125)); break;
                case "histogram":
                    IHistogram histogram = host.Metrics.CreateHistogram(definition);
                    record.Add(() => histogram.Record(1)); break;
                case "upDownCounter":
                    IUpDownCounter active = host.Metrics.CreateUpDownCounter(definition);
                    record.Add(() => active.Add(1)); break;
                case "gauge":
                    IGauge gauge = host.Metrics.CreateGauge(definition);
                    record.Add(() => gauge.Set(1)); break;
                default: throw new AssertFailedException("Unknown portable instrument kind.");
            }
        }
        await host.StartAsync();
        foreach (Action operation in record) { operation(); }
        using HttpResponseMessage work = await host.Client.GetAsync("/work/fixture").WaitAsync(CollectionTestHost.Deadline);
        work.EnsureSuccessStatusCode();
        string text = await host.ScrapeAsync();
        CollectionText.AssertIdentity(text);
        string[] samples = CollectionText.Samples(text);
        foreach (JsonElement family in fixture.RootElement.GetProperty("exportedFamilies").EnumerateArray())
        {
            string[] names = family.GetProperty("expectedSampleNames").EnumerateArray().Select(value => value.GetString()!).ToArray();
            foreach (string name in names)
            { Assert.IsTrue(samples.Any(sample => sample.StartsWith(name + "{", StringComparison.Ordinal)), family.GetProperty("id").GetString() + ": " + text); }
            string type = family.GetProperty("expectedPrometheusType").GetString()!;
            string baseName = type == "histogram" ? names[0][..^"_bucket".Length] : names[0];
            // Prometheus/OpenMetrics describe counter families with their protocol's total-suffix convention.
            string unsuffixed = type == "counter" ? baseName[..^"_total".Length] : baseName;
            Assert.IsTrue(text.Contains("# TYPE " + baseName + " " + type, StringComparison.Ordinal)
                || text.Contains("# TYPE " + unsuffixed + " " + type, StringComparison.Ordinal), text);
        }
    }
}
