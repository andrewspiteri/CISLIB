using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CIS.RestClient;
using CIS.RestClient.Resilience;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestClientMetricsTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeHeadersFinishWhilePublicLogicalWorkAndConsumedBodyRemainOwned(bool metricsFirst)
    {
        TaskCompletionSource headers = RestHttpTestSupport.Signal(), release = RestHttpTestSupport.Signal();
        await using var server = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/plain"; context.Response.ContentLength = 6;
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.WriteAsync("ab"u8.ToArray(), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted); headers.TrySetResult();
            await release.Task;
            await context.Response.Body.WriteAsync("cdef"u8.ToArray(), context.RequestAborted);
        });
        var options = new RestClientOptions("catalog") { ResponseFormat = RestResponseFormat.Text };
        options.Operations.Add("read");
        var receivedHeaders = new HeadersObservedHandler(); options.Handlers.Add(receivedHeaders);
        await using var host = RestClientMetricsHost.Create(options, metricsFirst);
        Task<RestResult<string>>? operation = null;
        try
        {
            await host.StartAsync();
            operation = host.Rest().Get<string>(server.Address, new() { OperationName = "read" });
            await headers.Task.WaitAsync(RestHttpTestSupport.Deadline);
            await receivedHeaders.Observed.Task.WaitAsync(RestHttpTestSupport.Deadline);
            // The native response-headers boundary is observed through its real SDK sample.
            string[] held = await host.ScrapeAsync();
            Assert.IsFalse(operation.IsCompleted);
            Assert.AreEqual(1d, RestClientMetricSamples.Sum(held, "http_client_request_duration_seconds_count"));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(held, "http_client_active_requests"));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(held, "cis_restclient_operation_duration_seconds_count"));
            Assert.AreEqual(1d, RestClientMetricSamples.Sum(held, "cis_restclient_active_operations"));
            release.TrySetResult();
            RestResult<string> result = await operation.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsTrue(result.Success); Assert.AreEqual("abcdef", result.Value); Assert.AreEqual(1, result.Context.AttemptCount);
            string[] completed = await host.ScrapeAsync();
            Assert.AreEqual(1d, RestClientMetricSamples.Sum(completed, "cis_restclient_operation_duration_seconds_count", "outcome=\"success\""));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(completed, "cis_restclient_active_operations"));
            Assert.AreEqual(6d, RestClientMetricSamples.Sum(completed, "cis_restclient_payload_downloaded_bytes_total"));
        }
        finally
        {
            release.TrySetResult();
            if (operation is not null) { await operation.WaitAsync(RestHttpTestSupport.Deadline); }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealRetryCountsCompletedPayloadAgainButCompletesOneLogicalObservation(bool metricsFirst)
    {
        int calls = 0; var received = new ConcurrentQueue<byte[]>(); var keys = new ConcurrentQueue<string>();
        await using var server = await RestHttpLocalServer.StartAsync(async context =>
        {
            using var body = new MemoryStream(); await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            received.Enqueue(body.ToArray()); keys.Enqueue(context.Request.Headers["Idempotency-Key"].ToString());
            int attempt = Interlocked.Increment(ref calls);
            context.Response.StatusCode = attempt == 1 ? 503 : 200; context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(attempt == 1 ? "busy" : "ok", context.RequestAborted);
        });
        var policy = new RestRetryPolicy("replay-write", 1, TimeSpan.Zero, TimeSpan.Zero, [503], ["POST"], useJitter: false);
        var options = new RestClientOptions("catalog")
        { ResponseFormat = RestResponseFormat.Text, RetryPolicy = RestRetrySelection.Named("replay-write") };
        options.Operations.Add("publish");
        await using var host = RestClientMetricsHost.Create(options, metricsFirst,
            services => services.AddCisRestClientResilience(policy));
        await host.StartAsync();
        var call = new RestRequestOptions { Body = RestBody.Bytes(new byte[] { 1, 2, 3 }), OperationName = "publish", ReplaySafety = RestReplaySafety.Assured };
        call.Headers.Set("Idempotency-Key", "fixed-replay-key");
        RestResult<string> result = await host.Rest().Post<string>(server.Address, call).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(result.Success); Assert.AreEqual("ok", result.Value); Assert.AreEqual(2, result.Context.AttemptCount);
        Assert.AreEqual(2, calls); Assert.AreEqual(2, received.Count);
        foreach (byte[] body in received) { CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, body); }
        CollectionAssert.AreEqual(new[] { "fixed-replay-key", "fixed-replay-key" }, keys.ToArray());
        string[] samples = await host.ScrapeAsync();
        Assert.AreEqual(1d, RestClientMetricSamples.Sum(samples, "cis_restclient_operation_duration_seconds_count"));
        Assert.AreEqual(0d, RestClientMetricSamples.Sum(samples, "cis_restclient_active_operations"));
        Assert.AreEqual(6d, RestClientMetricSamples.Sum(samples, "cis_restclient_payload_uploaded_bytes_total"));
        Assert.AreEqual(6d, RestClientMetricSamples.Sum(samples, "cis_restclient_payload_downloaded_bytes_total"));
        Assert.AreEqual(2d, RestClientMetricSamples.Sum(samples, "http_client_request_duration_seconds_count"));
    }

    [TestMethod]
    public async Task AnOperationStartedBeforeAttachmentIsNotReplayedOrUnbalancedByLaterActivation()
    {
        TaskCompletionSource entered = RestHttpTestSupport.Signal(), release = RestHttpTestSupport.Signal();
        int calls = 0;
        await using var server = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.ContentType = "text/plain";
            if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); await release.Task; }
            await context.Response.WriteAsync("one", context.RequestAborted);
        });
        await using var host = RestClientMetricsHost.Create(new("catalog") { ResponseFormat = RestResponseFormat.Text }, metricsFirst: true);
        Task<RestResult<string>>? original = null;
        try
        {
            IRestClient client = host.Rest();
            original = client.Get<string>(server.Address);
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            await host.StartAsync();
            release.TrySetResult();
            Assert.IsTrue((await original.WaitAsync(RestHttpTestSupport.Deadline)).Success);
            string[] first = await host.ScrapeAsync();
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(first, "cis_restclient_operation_duration_seconds_count"));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(first, "cis_restclient_active_operations"));
            Assert.IsTrue((await client.Get<string>(server.Address).WaitAsync(RestHttpTestSupport.Deadline)).Success);
            string[] next = await host.ScrapeAsync();
            Assert.AreEqual(1d, RestClientMetricSamples.Sum(next, "cis_restclient_operation_duration_seconds_count"));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(next, "cis_restclient_active_operations"));
            Assert.AreEqual(3d, RestClientMetricSamples.Sum(next, "cis_restclient_payload_downloaded_bytes_total"));
        }
        finally { release.TrySetResult(); if (original is not null) { await original.WaitAsync(RestHttpTestSupport.Deadline); } }
    }

    [TestMethod]
    public async Task CompletedPartialReadsRemainCountedWhenTheActualDecoderThenFaults()
    {
        TaskCompletionSource release = RestHttpTestSupport.Signal();
        await using var server = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.ContentLength = 6;
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.WriteAsync("ab"u8.ToArray(), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await release.Task;
            try { await context.Response.Body.WriteAsync("cdef"u8.ToArray(), context.RequestAborted); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        var defect = new InvalidOperationException("Decoder failed after two consumed bytes.");
        var options = new RestClientOptions("catalog") { ResponseFormat = RestResponseFormat.Text, Codec = new PartialDecoder(defect) };
        await using var host = RestClientMetricsHost.Create(options, metricsFirst: true);
        Task<RestResult<string>>? operation = null;
        try
        {
            await host.StartAsync();
            operation = host.Rest().Get<string>(server.Address);
            Exception actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => operation.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreSame(defect, actual);
            string[] samples = await host.ScrapeAsync();
            Assert.AreEqual(2d, RestClientMetricSamples.Sum(samples, "cis_restclient_payload_downloaded_bytes_total"));
            Assert.AreEqual(1d, RestClientMetricSamples.Sum(samples, "cis_restclient_operation_duration_seconds_count", "outcome=\"unexpected_error\""));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(samples, "cis_restclient_active_operations"));
        }
        finally
        {
            release.TrySetResult();
            if (operation is not null)
            {
                try { await operation.WaitAsync(RestHttpTestSupport.Deadline); }
                catch (Exception) when (operation.IsCompleted) { /* The body asserts the actual original decoder defect. */ }
            }
        }
    }

    [TestMethod]
    public async Task PublicKeyedClientsUseTheirExactRootMetersAndUnknownOperationsUseTheBoundedFallback()
    {
        await using var server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("ok", context.RequestAborted));
        await using var first = RestClientMetricsHost.Create(new("catalog") { ResponseFormat = RestResponseFormat.Text }, true);
        await using var second = RestClientMetricsHost.Create(new("catalog") { ResponseFormat = RestResponseFormat.Text }, false);
        await first.StartAsync(); await second.StartAsync();
        Assert.AreNotSame(first.Context.OutgoingMeter, second.Context.OutgoingMeter);
        Assert.AreSame(first.Context.OutgoingMeter, first.Services.GetRequiredService<IMeterFactory>().Create("System.Net.Http"));
        Assert.IsTrue((await first.Rest().Get<string>(server.Address).WaitAsync(RestHttpTestSupport.Deadline)).Success);
        Assert.IsTrue((await second.Rest().Get<string>(server.Address).WaitAsync(RestHttpTestSupport.Deadline)).Success);
        Assert.IsTrue((await second.Rest().Get<string>(server.Address, new() { OperationName = "unregistered-sensitive-path" }).WaitAsync(RestHttpTestSupport.Deadline)).Success);
        using var foreign = new Meter("System.Net.Http");
        foreign.CreateHistogram<double>("http.client.request.duration", "s").Record(999, new KeyValuePair<string, object?>("http.request.method", "FOREIGN"));
        string[] firstSamples = await first.ScrapeAsync(), secondSamples = await second.ScrapeAsync();
        Assert.AreEqual(1d, RestClientMetricSamples.Sum(firstSamples, "http_client_request_duration_seconds_count"));
        Assert.AreEqual(2d, RestClientMetricSamples.Sum(secondSamples, "http_client_request_duration_seconds_count"));
        Assert.AreEqual(1d, RestClientMetricSamples.Sum(firstSamples, "cis_restclient_operation_duration_seconds_count"));
        Assert.AreEqual(2d, RestClientMetricSamples.Sum(secondSamples, "cis_restclient_operation_duration_seconds_count", "operation=\"request\""));
        Assert.IsFalse(firstSamples.Concat(secondSamples).Any(line => line.Contains("FOREIGN", StringComparison.Ordinal)
            || line.Contains("unregistered-sensitive-path", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("http", "http_error")]
    [DataRow("caller", "cancelled")]
    [DataRow("defect", "unexpected_error")]
    public async Task PublicFailureCancellationAndDefectEachSettleOneBalancedObservation(string mode, string outcome)
    {
        await using var server = await RestHttpLocalServer.StartAsync(context =>
        {
            context.Response.StatusCode = 503;
            return context.Response.WriteAsync("busy", context.RequestAborted);
        });
        TaskCompletionSource entered = RestHttpTestSupport.Signal();
        var defect = new InvalidOperationException("Application callback defect.");
        var options = new RestClientOptions("catalog") { BaseUrl = server.Address, ResponseFormat = RestResponseFormat.Text };
        if (mode == "caller")
        {
            options.ConfigureRequest = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        }
        if (mode == "defect") { options.ConfigureRequest = (_, _) => throw defect; }
        await using var host = RestClientMetricsHost.Create(options, metricsFirst: true);
        await host.StartAsync();
        using var caller = new CancellationTokenSource();
        Task<RestResult<string>> operation = host.Rest().Get<string>("/", cancellationToken: caller.Token);
        try
        {
            if (mode == "caller")
            {
                await entered.Task.WaitAsync(RestHttpTestSupport.Deadline); caller.Cancel();
                OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreEqual(caller.Token, actual.CancellationToken);
            }
            else if (mode == "defect")
            {
                Exception actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => operation.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreSame(defect, actual);
            }
            else
            {
                RestResult<string> result = await operation.WaitAsync(RestHttpTestSupport.Deadline);
                Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Http, result.Context.FailureKind);
            }
            string[] samples = await host.ScrapeAsync();
            Assert.AreEqual(1d, RestClientMetricSamples.Sum(samples, "cis_restclient_operation_duration_seconds_count", $"outcome=\"{outcome}\""));
            Assert.AreEqual(0d, RestClientMetricSamples.Sum(samples, "cis_restclient_active_operations"));
        }
        finally
        {
            caller.Cancel();
            try { await operation.WaitAsync(RestHttpTestSupport.Deadline); }
            catch (Exception) when (operation.IsCompleted) { /* The body asserts the expected original terminal behavior. */ }
        }
    }

    private sealed class HeadersObservedHandler : IRestExchangeHandler
    {
        internal TaskCompletionSource Observed { get; } = RestHttpTestSupport.Signal();
        public async ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token)
        {
            IRestResponse response = await next(request, token);
            Observed.TrySetResult();
            return response;
        }
    }

    private sealed class PartialDecoder(Exception defect) : IRestContentCodec
    {
        public bool CanDecode(Type resultType, RestResponseFormat format) => resultType == typeof(string) && format == RestResponseFormat.Text;
        public ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token) => throw new NotSupportedException();
        public async ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
        {
            byte[] bytes = new byte[2]; int received = 0;
            while (received < bytes.Length)
            {
                int count = await source.ReadAsync(bytes.AsMemory(received), token);
                if (count == 0) { throw new InvalidOperationException("The controlled endpoint ended before the expected prefix."); }
                received += count;
            }
            throw defect;
        }
    }
}
