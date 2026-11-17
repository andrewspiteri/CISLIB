using System.Net;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class HttpAdmissionTests
{
    [TestMethod]
    public async Task TwoRealScrapesHoldPermitsThirdRejectsWithoutQueueAndCompletionReleasesBoth()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var host = HttpTestHost.Create(options => options.ScrapeTimeout = TimeSpan.FromSeconds(10));
        host.Release.Add(release.Set);
        host.Metrics.RegisterObservableGauge(new("app.http.held"), () =>
        {
            entered.Set();
            if (!release.Wait(HttpTestHost.Deadline)) { throw new TimeoutException("Controlled callback release was not supplied."); }
            return [new(1)];
        });
        await host.StartAsync();
        Task<HttpResponseMessage> first = host.Client.GetAsync("/_metrics");
        Assert.IsTrue(await Task.Run(() => entered.Wait(HttpTestHost.Deadline)));
        Task<HttpResponseMessage> second = host.Client.GetAsync("/_metrics");
        await HttpTestHost.UntilAsync(() => host.Context.HttpPolicy.AvailablePermits == 0);
        using HttpResponseMessage rejected = await host.Client.GetAsync("/_metrics").WaitAsync(HttpTestHost.Deadline);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        release.Set();
        using HttpResponseMessage firstResult = await first.WaitAsync(HttpTestHost.Deadline);
        using HttpResponseMessage secondResult = await second.WaitAsync(HttpTestHost.Deadline);
        Assert.AreEqual(HttpStatusCode.OK, firstResult.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResult.StatusCode);
        await HttpTestHost.UntilAsync(() => host.Context.HttpPolicy.AvailablePermits == 2);
        await host.ScrapeAsync();
    }

    [TestMethod]
    public async Task UpstreamTimeoutReleasesHttpPermitButActualCallbackStillBlocksHostStop()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await using var host = HttpTestHost.Create(options => options.ScrapeTimeout = TimeSpan.FromMilliseconds(200));
        host.Release.Add(release.Set);
        host.Metrics.RegisterObservableGauge(new("app.http.timeout"), () =>
        {
            entered.Set();
            if (!release.Wait(HttpTestHost.Deadline)) { throw new TimeoutException("Controlled callback release was not supplied."); }
            return [new(1)];
        });
        await host.StartAsync();
        Task<HttpResponseMessage> request = host.Client.GetAsync("/_metrics");
        Assert.IsTrue(await Task.Run(() => entered.Wait(HttpTestHost.Deadline)));
        using HttpResponseMessage expired = await request.WaitAsync(HttpTestHost.Deadline);
        Assert.AreEqual(HttpStatusCode.RequestTimeout, expired.StatusCode);
        await HttpTestHost.UntilAsync(() => host.Context.HttpPolicy.AvailablePermits == 2);
        Task stop = host.App.StopAsync(CancellationToken.None);
        Assert.IsTrue(host.Context.IsStopping);
        Assert.IsFalse(stop.IsCompleted, "An expired HTTP wait does not settle the held observable callback.");
        release.Set();
        await stop.WaitAsync(HttpTestHost.Deadline);
    }

    [TestMethod]
    public async Task ReservedBranchRetainsActualExporterCacheInsteadOfReinvokingSnapshots()
    {
        int calls = 0;
        await using var host = HttpTestHost.Create(options => options.ScrapeCacheDuration = TimeSpan.FromMinutes(1));
        host.Metrics.RegisterObservableGauge(new("app.http.cached"), () => [new(Interlocked.Increment(ref calls))]);
        await host.StartAsync();
        string first = await host.ScrapeAsync();
        string second = await host.ScrapeAsync();
        Assert.AreEqual(1, calls);
        string sample = CollectionText.Samples(first).Single(line => line.StartsWith("app_http_cached{", StringComparison.Ordinal));
        Assert.AreEqual(1d, CollectionText.Value(sample));
        Assert.IsTrue(second.Contains(sample, StringComparison.Ordinal), second);
        Assert.AreEqual(2L, host.Context.HttpPolicy.AvailablePermits);
    }

    [TestMethod]
    public async Task ProductBranchRetainsExporterResponseCapAndReturnsItsPermitAfterFailure()
    {
        await using var host = HttpTestHost.Create(options =>
        { options.CollectIncomingHttp = false; options.MaxScrapeResponseSizeBytes = 85_000; });
        IHistogram histogram = host.Metrics.CreateHistogram(new("app.http.capped", MetricUnit.Seconds, allowedDimensionKeys: ["category"]));
        await host.StartAsync();
        for (int index = 0; index < 64; index++)
        { histogram.Record(0.25, new(("category", new string('x', 250) + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)))); }
        using HttpResponseMessage response = await host.Client.GetAsync("/_metrics").WaitAsync(HttpTestHost.Deadline);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        await HttpTestHost.UntilAsync(() => host.Context.HttpPolicy.AvailablePermits == 2);
        Assert.IsTrue(host.Context.IsActive);
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted, "Exporter failure must not change readiness.");
    }
}
