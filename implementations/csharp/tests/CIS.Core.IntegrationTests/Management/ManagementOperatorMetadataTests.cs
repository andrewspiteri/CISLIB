using CIS.Management.Service.Security;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
public sealed class ManagementOperatorMetadataTests
{
    [TestMethod]
    public async Task ShutdownCancelsMetadataButDisposalWaitsForTheOwnedNativeRequestToSettle()
    {
        using var lifetime = new Lifetime();
        var handler = new HeldMetadataHandler();
        var metadata = new ManagementEntraMetadata(lifetime, handler);
        Task<string> request = metadata.GetDocumentAsync("https://owned-issuer.example.test/metadata", CancellationToken.None);
        try
        {
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lifetime.StopApplication();
            await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disposal = metadata.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted, "Cancellation does not establish native request settlement.");
            Assert.IsFalse(handler.Disposed);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => metadata.GetDocumentAsync(
                "https://owned-issuer.example.test/late", CancellationToken.None));

            handler.Release.TrySetResult();
            await ObserveFailure(request);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(handler.Disposed);
        }
        finally
        {
            lifetime.StopApplication();
            handler.Release.TrySetResult();
            await ObserveFailure(request);
            await metadata.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationReachesTheResponseBodyAndDisposalWaitsForItsSettlement(bool callerCancellation)
    {
        using var lifetime = new Lifetime();
        using var caller = new CancellationTokenSource();
        var body = new HeldBody();
        var handler = new ReplyHandler(body);
        var metadata = new ManagementEntraMetadata(lifetime, handler);
        Task<string> request = metadata.GetDocumentAsync("https://owned-issuer.example.test/keys", caller.Token);
        try
        {
            await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (callerCancellation)
            {
                caller.Cancel();
            }
            else
            {
                lifetime.StopApplication();
            }

            await body.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disposal = metadata.DisposeAsync().AsTask();
            Assert.IsFalse(request.IsCompleted, "A signalled body is still owned until it actually settles.");
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsFalse(body.Disposed);
            Assert.IsFalse(handler.Disposed);

            body.Release.TrySetResult();
            await ObserveFailure(request);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(body.Disposed);
            Assert.IsTrue(handler.Disposed);
        }
        finally
        {
            lifetime.StopApplication();
            body.Release.TrySetResult();
            await ObserveFailure(request);
            await metadata.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task AChunkedDocumentCannotExceedTheOneMiBBufferAndItsResponseIsDisposed()
    {
        using var lifetime = new Lifetime();
        var body = new OversizedBody();
        var handler = new ReplyHandler(body);
        await using var metadata = new ManagementEntraMetadata(lifetime, handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => metadata.GetDocumentAsync(
            "https://owned-issuer.example.test/oversized-keys", CancellationToken.None));
        Assert.IsTrue(body.Disposed);
        Assert.AreEqual(1, handler.Calls, "The bounded metadata owner starts no automatic retry.");
    }

    [TestMethod]
    [DataRow("http://owned-issuer.example.test/keys")]
    [DataRow("/relative/keys")]
    [DataRow("https://user:example@owned-issuer.example.test/keys")]
    [DataRow("https://owned-issuer.example.test/keys#fragment")]
    public async Task InvalidMetadataAddressesFailBeforeNativeHttp(string address)
    {
        using var lifetime = new Lifetime();
        var handler = new ReplyHandler(new StringContent("{}"));
        await using var metadata = new ManagementEntraMetadata(lifetime, handler);
        await Assert.ThrowsAsync<ArgumentException>(() => metadata.GetDocumentAsync(address, CancellationToken.None));
        Assert.AreEqual(0, handler.Calls);
    }

    private static async Task ObserveFailure(Task request)
    {
        try
        {
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("The cancelled native metadata request must fail.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class ReplyHandler(HttpContent body) : HttpMessageHandler
    {
        internal int Calls
        {
            get; private set;
        }

        internal bool Disposed
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }

        protected override void Dispose(bool disposing)
        {
            body.Dispose();
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class HeldBody : HttpContent
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Disposed
        {
            get; private set;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class OversizedBody : HttpContent
    {
        internal bool Disposed
        {
            get; private set;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[16 * 1024];
            for (int index = 0; index < 65; index++)
            {
                await stream.WriteAsync(chunk).ConfigureAwait(false);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class HeldMetadataHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Disposed
        {
            get; private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class Lifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            stopping.Cancel();
        }

        public void Dispose()
        {
            stopping.Dispose();
        }
    }
}
