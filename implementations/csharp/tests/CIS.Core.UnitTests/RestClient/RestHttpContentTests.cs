using System.Text;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestHttpContentTests
{
    [TestMethod]
    public async Task ExplicitSigningMaterializesOnceAndTheIdenticalBytesAreSentAndCountedOnlyAtWireWrites()
    {
        var codec = new ChangingCodec(); var observed = new RestHttpObservation();
        await using RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(RestBody.Text("data"), codec, observed, default);
        ReadOnlyMemory<byte> signed = await lease.GetSigningBytesAsync(32, default);
        Assert.AreEqual(0L, observed.Uploaded);
        CollectionAssert.AreEqual(signed.ToArray(), (await lease.GetSigningBytesAsync(32, default)).ToArray());
        using var wire = new MemoryStream(); await lease.Content!.CopyToAsync(wire);
        Assert.AreEqual(1, codec.Calls); CollectionAssert.AreEqual(signed.ToArray(), wire.ToArray());
        Assert.AreEqual((long)signed.Length, observed.Uploaded);
        await Assert.ThrowsExactlyAsync<RestContentPreparationException>(() => lease.GetSigningBytesAsync(1, default).AsTask());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StreamOwnershipTransfersAtAcquisitionAndOwnedCleanupIsActuallyAwaited(bool owns)
    {
        var stream = new HeldDisposeStream();
        RestBody descriptor = RestHttpBody.Stream(stream, owns);
        RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(descriptor, null, null, default);
        Task dispose = lease.DisposeAsync().AsTask();
        try
        {
            Assert.AreEqual(owns, stream.Entered.Task.IsCompleted);
            Assert.AreEqual(!owns, dispose.IsCompleted);
            stream.Release.TrySetResult(); await dispose.WaitAsync(RestHttpTestSupport.Deadline);
            await lease.DisposeAsync(); Assert.AreEqual(owns ? 1 : 0, stream.Disposals);
        }
        finally { stream.Release.TrySetResult(); await dispose.WaitAsync(RestHttpTestSupport.Deadline); if (!owns) { await stream.DisposeAsync(); } }
    }

    [TestMethod]
    public async Task FreshFactoriesAreAcquiredOncePerLeaseAndCanceledAcquisitionStillDisposesTheReturnedOwner()
    {
        int opens = 0; var sources = new List<MemoryStream>();
        RestBody body = RestHttpBody.Reopen(_ => { opens++; var stream = new MemoryStream([1, 2]); sources.Add(stream); return ValueTask.FromResult<Stream>(stream); });
        await using (RestHttpContentLease first = await RestHttpContentLease.AcquireAsync(body, null, null, default))
        { using var target = new MemoryStream(); await first.Content!.CopyToAsync(target); }
        await using (RestHttpContentLease second = await RestHttpContentLease.AcquireAsync(body, null, null, default)) { Assert.IsTrue(second.CanReplay); }
        Assert.AreEqual(2, opens); Assert.AreNotSame(sources[0], sources[1]); Assert.IsTrue(sources.All(stream => !stream.CanRead));
        using var canceled = new CancellationTokenSource(); var returned = new MemoryStream([3]);
        RestBody canceledBody = RestHttpBody.Reopen(_ => { canceled.Cancel(); return ValueTask.FromResult<Stream>(returned); });
        await Assert.ThrowsAsync<OperationCanceledException>(() => RestHttpContentLease.AcquireAsync(canceledBody, null, null, canceled.Token).AsTask());
        Assert.IsFalse(returned.CanRead);
    }

    [TestMethod]
    public async Task FailedBoundedSigningCannotLaterSendTheConsumedOneShotRemainder()
    {
        using var input = new MemoryStream([1, 2, 3, 4, 5]);
        await using RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(RestHttpBody.Stream(input), null, null, default);
        await Assert.ThrowsExactlyAsync<RestContentPreparationException>(() => lease.GetSigningBytesAsync(4, default).AsTask());
        using var target = new MemoryStream();
        await Assert.ThrowsExactlyAsync<RestContentPreparationException>(() => lease.Content!.CopyToAsync(target));
        Assert.AreEqual(0L, target.Length); Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task NativeMultipartFactoryRemainsAnOwnedExtensionWithoutAPortableMultipartModel()
    {
        MultipartFormDataContent? native = null;
        RestBody body = RestHttpBody.Content(_ =>
        {
            native = new MultipartFormDataContent("fixed-boundary"); native.Add(new StringContent("value"), "name");
            return ValueTask.FromResult<HttpContent>(native);
        });
        await using RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(body, null, null, default);
        Assert.IsTrue(lease.Headers["Content-Type"].Single().Contains("fixed-boundary", StringComparison.Ordinal));
        using var sent = new MemoryStream(); await lease.Content!.CopyToAsync(sent); Assert.IsTrue(Encoding.UTF8.GetString(sent.ToArray()).Contains("value", StringComparison.Ordinal));
        await lease.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => native!.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task UnreadableSuppliedInputDoesNotTransferButUnreadableFactoryOutputIsAlreadyOwned()
    {
        var supplied = new UnreadableStream();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => RestHttpContentLease.AcquireAsync(RestHttpBody.Stream(supplied, true), null, null, default).AsTask());
        Assert.AreEqual(0, supplied.Disposals);
        var acquired = new UnreadableStream();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => RestHttpContentLease.AcquireAsync(RestHttpBody.Reopen(_ => ValueTask.FromResult<Stream>(acquired)), null, null, default).AsTask());
        Assert.AreEqual(1, acquired.Disposals);
        await supplied.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SigningCancellationReportsTheCanceledSourceRatherThanItsPrivateHelperLink(bool cancelOperation)
    {
        using var operation = new CancellationTokenSource(); using var signingSource = new CancellationTokenSource();
        var codec = new AwaitCancellationCodec();
        await using RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(RestBody.Text("data"), codec, null, operation.Token);
        Task<ReadOnlyMemory<byte>> signing = lease.GetSigningBytesAsync(32, signingSource.Token).AsTask();
        try
        {
            await codec.Entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            (cancelOperation ? operation : signingSource).Cancel();
            OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => signing.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreEqual(cancelOperation ? operation.Token : signingSource.Token, actual.CancellationToken);
        }
        finally
        {
            operation.Cancel(); signingSource.Cancel();
            try { await signing.WaitAsync(RestHttpTestSupport.Deadline); } catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    public async Task AnAcquisitionCancellationAndCleanupDefectRetainBothOriginalCauses()
    {
        using var canceled = new CancellationTokenSource();
        var cleanup = new InvalidOperationException("Owned source cleanup defect.");
        var stream = new FailingDisposeStream(cleanup);
        try
        {
            RestBody body = RestHttpBody.Reopen(_ => { canceled.Cancel(); return ValueTask.FromResult<Stream>(stream); });
            AggregateException actual = await Assert.ThrowsExactlyAsync<AggregateException>(() => RestHttpContentLease.AcquireAsync(body, null, null, canceled.Token).AsTask());
            Assert.HasCount(2, actual.InnerExceptions); Assert.IsInstanceOfType<OperationCanceledException>(actual.InnerExceptions[0]);
            Assert.AreSame(cleanup, actual.InnerExceptions[1]); Assert.AreEqual(1, stream.Disposals);
        }
        finally { stream.Dispose(); }
    }

    private sealed class ChangingCodec : IRestContentCodec
    {
        internal int Calls;
        public bool CanDecode(Type type, RestResponseFormat format) => false;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { Calls++; await destination.WriteAsync(Encoding.UTF8.GetBytes("body-" + Calls), token); }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class HeldDisposeStream : MemoryStream
    {
        internal TaskCompletionSource Entered { get; } = RestHttpTestSupport.Signal();
        internal TaskCompletionSource Release { get; } = RestHttpTestSupport.Signal();
        internal int Disposals;
        public override async ValueTask DisposeAsync()
        { Disposals++; Entered.TrySetResult(); await Release.Task; await base.DisposeAsync(); }
    }
    private sealed class UnreadableStream : MemoryStream
    {
        internal int Disposals;
        public override bool CanRead => false;
        public override ValueTask DisposeAsync() { Disposals++; return base.DisposeAsync(); }
    }
    private sealed class AwaitCancellationCodec : IRestContentCodec
    {
        internal TaskCompletionSource Entered { get; } = RestHttpTestSupport.Signal();
        public bool CanDecode(Type type, RestResponseFormat format) => false;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class FailingDisposeStream(Exception failure) : MemoryStream
    {
        internal int Disposals;
        public override ValueTask DisposeAsync() { Disposals++; return ValueTask.FromException(failure); }
    }
}
