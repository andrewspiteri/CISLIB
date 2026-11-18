using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Text;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestHttpTransportTests
{
    [TestMethod]
    [DataRow("json", "{\"count\":0}")]
    [DataRow("text", "héllo")]
    [DataRow("bytes", "bytes")]
    [DataRow("form", "x=a+b&x=%2B")]
    [DataRow("custom-bytes", "encoded")]
    public async Task ActualNativePostUsesFreshContentAndCountsOnlyTheBytesWrittenAndRead(string mode, string expected)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            using var received = new MemoryStream(); await context.Request.Body.CopyToAsync(received, context.RequestAborted);
            context.Response.Headers.Append("X-Repeated", "first"); context.Response.Headers.Append("X-Repeated", "second");
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(received.ToArray(), context.RequestAborted);
        });
        RestBody body = mode switch
        {
            "json" => RestBody.Json(new { Count = 0 }),
            "text" => RestBody.Text(expected),
            "form" => RestBody.Form([new("x", "a b"), new("x", "+")]),
            "custom-bytes" => RestBody.Bytes(new byte[] { 1 }),
            _ => RestBody.Bytes(Encoding.UTF8.GetBytes(expected))
        };
        var observed = new RestHttpObservation();
        using var timeout = new CancellationTokenSource(RestHttpTestSupport.Deadline);
        await using RestHttpTransport transport = RestHttpTransport.Create();
        await using RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(body,
            mode == "custom-bytes" ? new LiteralCodec(expected) : null, observed, timeout.Token);
        if (mode == "custom-bytes") { Assert.IsNull(lease.Content!.Headers.ContentLength); }
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Address) { Content = lease.Content };
        int dispatches = 0;
        await using IRestResponse response = await transport.SendAsync(request, () => Interlocked.Increment(ref dispatches), timeout.Token);
        RestReadResult<byte[]> result = await new RestHttpResponseReader(response, new(1024, 128, 1024), observed)
            .ReadAsync<byte[]>(RestResponseFormat.Bytes, true, null, timeout.Token);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), result.Decoded.Value!);
        Assert.AreEqual(200, response.HttpStatusCode); Assert.AreEqual(1, dispatches);
        Assert.HasCount(2, response.Headers["x-repeated"]);
        Assert.AreEqual((long)Encoding.UTF8.GetByteCount(expected), observed.Uploaded);
        Assert.AreEqual(observed.Uploaded, observed.Downloaded);
    }

    [TestMethod]
    public async Task NativeDefaultsDoNotFollowRedirectsOrReplayServerCookies()
    {
        int targetCalls = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            if (context.Request.Path == "/redirect")
            { context.Response.StatusCode = 302; context.Response.Headers.Location = "/target"; context.Response.Headers.Append("Set-Cookie", "sid=secret; Path=/"); return; }
            if (context.Request.Path == "/target") { Interlocked.Increment(ref targetCalls); }
            await context.Response.WriteAsync(context.Request.Headers.Cookie.ToString() + "|" + context.Request.Headers.Authorization.ToString(), context.RequestAborted);
        });
        await using RestHttpTransport transport = RestHttpTransport.Create();
        using var timeout = new CancellationTokenSource(RestHttpTestSupport.Deadline);
        int dispatches = 0;
        using (var request = new HttpRequestMessage(HttpMethod.Get, server.Address + "/redirect"))
        {
            await using IRestResponse response = await transport.SendAsync(request, () => Interlocked.Increment(ref dispatches), timeout.Token);
            Assert.AreEqual(302, response.HttpStatusCode); Assert.AreEqual("/target", response.Headers["Location"].Single());
        }
        using (var request = new HttpRequestMessage(HttpMethod.Get, server.Address + "/check"))
        {
            await using IRestResponse response = await transport.SendAsync(request, () => Interlocked.Increment(ref dispatches), timeout.Token);
            Assert.AreEqual("|", (await new RestHttpResponseReader(response, new(32, 16, 1024))
                .ReadAsync<string>(RestResponseFormat.Text, true, null, timeout.Token)).Decoded.Value);
        }
        Assert.AreEqual(0, targetCalls); Assert.AreEqual(2, dispatches);
    }

    [TestMethod]
    public async Task HeadersCanCompleteWhileTheActualBodyReadRemainsOwnedAndCancellationSettlesIt()
    {
        TaskCompletionSource headers = RestHttpTestSupport.Signal(), release = RestHttpTestSupport.Signal(), finished = RestHttpTestSupport.Signal();
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            try
            {
                context.Response.ContentType = "application/octet-stream";
                await context.Response.Body.WriteAsync(new byte[] { 1 }, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted); headers.TrySetResult();
                await release.Task.WaitAsync(RestHttpTestSupport.Deadline);
                await context.Response.Body.WriteAsync(new byte[] { 2 }, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (IOException) { }
            finally { finished.TrySetResult(); }
        });
        await using RestHttpTransport transport = RestHttpTransport.Create();
        using var cancellation = new CancellationTokenSource(RestHttpTestSupport.Deadline);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        IRestResponse? response = null;
        Task<RestReadResult<byte[]>>? read = null;
        try
        {
            response = await transport.SendAsync(request, static () => { }, cancellation.Token);
            await headers.Task.WaitAsync(RestHttpTestSupport.Deadline);
            read = new RestHttpResponseReader(response, new(32, 16, 1024))
                .ReadAsync<byte[]>(RestResponseFormat.Bytes, false, null, cancellation.Token).AsTask();
            Assert.AreEqual(200, response.HttpStatusCode); Assert.IsFalse(read.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => read.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.IsTrue(read.IsCompleted); Assert.AreEqual(200, response.HttpStatusCode);
        }
        finally
        {
            cancellation.Cancel(); release.TrySetResult();
            try
            {
                if (read is not null) { try { await read.WaitAsync(RestHttpTestSupport.Deadline); } catch (OperationCanceledException) { } }
            }
            finally
            {
                if (response is not null) { await response.DisposeAsync(); }
                if (headers.Task.IsCompleted) { await finished.Task.WaitAsync(RestHttpTestSupport.Deadline); }
            }
        }
    }

    [TestMethod]
    public async Task ConsumedLimitAppliesAfterNativeDecompressionRatherThanToCompressedContentLength()
    {
        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true)) { gzip.Write(Encoding.UTF8.GetBytes(new string('a', 33))); }
            compressed = buffer.ToArray();
        }
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            context.Response.Headers.ContentEncoding = "gzip"; context.Response.ContentLength = compressed.Length;
            await context.Response.Body.WriteAsync(compressed, context.RequestAborted);
        });
        await using RestHttpTransport transport = RestHttpTransport.Create();
        using var timeout = new CancellationTokenSource(RestHttpTestSupport.Deadline);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        await using IRestResponse response = await transport.SendAsync(request, static () => { }, timeout.Token);
        var observed = new RestHttpObservation();
        await Assert.ThrowsExactlyAsync<RestResponseLimitException>(() => new RestHttpResponseReader(response, new(16, 8, 1024), observed)
            .ReadAsync<byte[]>(RestResponseFormat.Bytes, false, null, timeout.Token).AsTask());
        Assert.AreEqual(17L, observed.Downloaded); Assert.AreEqual(200, response.HttpStatusCode);
    }

    [TestMethod]
    public async Task PoolDisposalClosesAdmissionWithoutCancelingAnExistingBorrowedResponseLease()
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("alive", context.RequestAborted));
        await using RestHttpTransport transport = RestHttpTransport.Create();
        using var timeout = new CancellationTokenSource(RestHttpTestSupport.Deadline);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Address);
        IRestResponse response = await transport.SendAsync(request, static () => { }, timeout.Token);
        Task disposal = transport.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(disposal.IsCompleted);
            using var late = new HttpRequestMessage(HttpMethod.Get, server.Address);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.SendAsync(late, static () => { }, default).AsTask());
            Assert.AreEqual("alive", (await new RestHttpResponseReader(response, new(32, 16, 1024))
                .ReadAsync<string>(RestResponseFormat.Text, true, null, timeout.Token)).Decoded.Value);
        }
        finally { await response.DisposeAsync(); await disposal.WaitAsync(RestHttpTestSupport.Deadline); }
        await response.DisposeAsync(); await transport.DisposeAsync();
    }

    [TestMethod]
    public async Task KnownTransportRequiresTheExactRootMeterFactoryAndPreCanceledWorkNeverDispatches()
    {
        var firstServices = new ServiceCollection(); firstServices.AddMetrics();
        var secondServices = new ServiceCollection(); secondServices.AddMetrics();
        using ServiceProvider firstRoot = firstServices.BuildServiceProvider();
        using ServiceProvider secondRoot = secondServices.BuildServiceProvider();
        IMeterFactory first = firstRoot.GetRequiredService<IMeterFactory>(), second = secondRoot.GetRequiredService<IMeterFactory>();
        await using RestHttpTransport transport = RestHttpTransport.Create(first);
        transport.ValidateForHost(first);
        Assert.ThrowsExactly<InvalidOperationException>(() => transport.ValidateForHost(second));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1");
        int dispatches = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => transport.SendAsync(request, () => dispatches++, new CancellationToken(canceled: true)).AsTask());
        Assert.AreEqual(0, dispatches);
    }

    [TestMethod]
    [DataRow("http")]
    [DataRow("io")]
    [DataRow("foreign-cancel")]
    public async Task NativeContentWrappingKeepsUnexpectedCodecFaultsIdentifiedUntilThePublicOwner(string kind)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            try { await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted); }
            catch (IOException) { }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        Exception original = kind switch
        {
            "http" => new HttpRequestException("Custom codec fault."),
            "io" => new IOException("Custom codec fault."),
            _ => new OperationCanceledException(new CancellationToken(canceled: true))
        };
        using var timeout = new CancellationTokenSource(RestHttpTestSupport.Deadline);
        await using RestHttpTransport transport = RestHttpTransport.Create();
        await using RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(RestBody.Bytes(new byte[] { 1 }), new LiteralCodec("", original), null, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Address) { Content = lease.Content };
        int dispatches = 0;
        RestEncodingFaultException fault = await Assert.ThrowsExactlyAsync<RestEncodingFaultException>(() => transport.SendAsync(request, () => dispatches++, timeout.Token).AsTask());
        Assert.AreSame(original, fault.Original.SourceException); Assert.AreEqual(1, dispatches);
    }

    private sealed class LiteralCodec(string text, Exception? failure = null) : IRestContentCodec
    {
        public bool CanDecode(Type type, RestResponseFormat format) => false;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { if (failure is not null) { throw failure; } await destination.WriteAsync(Encoding.UTF8.GetBytes(text), token); }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
    }
}
