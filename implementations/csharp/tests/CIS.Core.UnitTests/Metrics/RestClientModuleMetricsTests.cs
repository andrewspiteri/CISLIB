using System.Diagnostics.Metrics;
using CIS.Metrics;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class RestClientModuleMetricsTests
{
    [TestMethod]
    [DataRow((int)RestOperationOutcome.Success, "success")]
    [DataRow((int)RestOperationOutcome.HttpError, "http_error")]
    [DataRow((int)RestOperationOutcome.BusinessError, "business_error")]
    [DataRow((int)RestOperationOutcome.TransportError, "transport_error")]
    [DataRow((int)RestOperationOutcome.Timeout, "timeout")]
    [DataRow((int)RestOperationOutcome.Cancelled, "cancelled")]
    [DataRow((int)RestOperationOutcome.AuthenticationError, "authentication_error")]
    [DataRow((int)RestOperationOutcome.PreparationError, "preparation_error")]
    [DataRow((int)RestOperationOutcome.InvalidResponse, "invalid_response")]
    [DataRow((int)RestOperationOutcome.LimitExceeded, "limit_exceeded")]
    [DataRow((int)RestOperationOutcome.Stopping, "stopping")]
    [DataRow((int)RestOperationOutcome.UnexpectedError, "unexpected_error")]
    public async Task OneEntryBindingRecordsRealElapsedAndBalancesEveryTerminalOutcome(int outcomeValue, string label)
    {
        using var meter = new Meter("RestModuleOutcome");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var metrics = new RestClientMetrics(runtime, Composition());
        IRestOperationObservation operation = metrics.Begin("catalog", "read")!;
        operation.UploadCompleted(4); operation.UploadCompleted(3);
        operation.DownloadCompleted(2); operation.DownloadCompleted(5);
        operation.Complete((RestOperationOutcome)outcomeValue, TimeSpan.FromSeconds(1.25));
        operation.Complete(RestOperationOutcome.Success, TimeSpan.FromSeconds(99));
        operation.UploadCompleted(999); operation.DownloadCompleted(999);

        Assert.AreEqual(4, listener.Instruments.Count);
        MetricsPoint duration = listener.Points("cis.restclient.operation.duration").Single();
        Assert.AreEqual(1.25, (double)duration.Value);
        MetricsTestSupport.AssertTags(duration, new("client", "catalog"), new("operation", "read"), new("outcome", label));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.restclient.active.operations").Select(point => (long)point.Value).ToArray());
        CollectionAssert.AreEqual(new long[] { 4, 3 }, listener.Points("cis.restclient.payload.uploaded").Select(point => (long)point.Value).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 5 }, listener.Points("cis.restclient.payload.downloaded").Select(point => (long)point.Value).ToArray());
    }

    [TestMethod]
    public async Task IndependentCallsUseTheFiniteFallbackAndNoExtraDefinitions()
    {
        using var meter = new Meter("RestModuleFallback");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var metrics = new RestClientMetrics(runtime, Composition());
        Assert.IsNull(metrics.Begin("unregistered", "read"));
        IRestOperationObservation first = metrics.Begin("catalog", "read")!;
        IRestOperationObservation fallback = metrics.Begin("catalog", "https://untrusted.example/123")!;
        first.DownloadCompleted(3); fallback.DownloadCompleted(5);
        fallback.Complete(RestOperationOutcome.HttpError, TimeSpan.FromSeconds(2));
        first.Complete(RestOperationOutcome.Success, TimeSpan.FromSeconds(3));

        MetricsPoint[] points = listener.Points("cis.restclient.operation.duration");
        Assert.AreEqual(2, points.Length);
        Assert.IsTrue(points.Any(point => point.Tags.Contains(new KeyValuePair<string, object?>("operation", "request"))));
        Assert.IsFalse(points.SelectMany(point => point.Tags).Any(pair => Equals(pair.Value, "https://untrusted.example/123")));
        Assert.AreEqual(4, runtime.Definitions.Count);
        Assert.AreEqual(0L, listener.Points("cis.restclient.active.operations").Sum(point => (long)point.Value));
    }

    [TestMethod]
    public async Task InvalidByteInputsDropWithoutChangingOtherAcceptedQuantities()
    {
        using var meter = new Meter("RestModuleBytes");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        IRestOperationObservation operation = new RestClientMetrics(runtime, Composition()).Begin("catalog", null)!;
        operation.UploadCompleted(-1); operation.UploadCompleted(0); operation.UploadCompleted(8);
        operation.DownloadCompleted(-1); operation.DownloadCompleted(0); operation.DownloadCompleted(9);
        operation.Complete(RestOperationOutcome.Success, TimeSpan.Zero);
        Assert.AreEqual(8L, (long)listener.Points("cis.restclient.payload.uploaded").Single().Value);
        Assert.AreEqual(9L, (long)listener.Points("cis.restclient.payload.downloaded").Single().Value);
    }

    private static RestClientComposition Composition() => new(new object(), [new("catalog", "request"), new("catalog", "read")]);
}
