using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestEncodingOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisposalJoinsAnIgnoringEncoderAndRetainsItsOriginalFaultWithoutDuplicatingIt(bool io)
    {
        Exception original = io ? new IOException("Controlled encoder fault.") : new HttpRequestException("Controlled encoder fault.");
        var codec = new HeldCodec(original);
        using var cancellation = new CancellationTokenSource();
        RestHttpContentLease lease = await RestHttpContentLease.AcquireAsync(RestBody.Text("value"), codec, null, cancellation.Token);
        Task copy = lease.Content!.CopyToAsync(Stream.Null, cancellation.Token);
        Task? disposal = null;
        try
        {
            await codec.Entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            disposal = lease.DisposeAsync().AsTask();
            cancellation.Cancel();
            Assert.IsFalse(copy.IsCompleted); Assert.IsFalse(disposal.IsCompleted);
            Assert.IsNull(lease.EncodingFault);
            codec.Release.TrySetResult();
            RestEncodingFaultException fault = await Assert.ThrowsExactlyAsync<RestEncodingFaultException>(() => copy.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreSame(original, fault.Original.SourceException);
            await disposal.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
            var retained = lease.EncodingFault;
            Assert.IsNotNull(retained); Assert.AreSame(original, retained.SourceException);
            Assert.AreEqual(1, codec.Calls);
            Exception? lateFailure = null;
            try { await lease.Content!.CopyToAsync(Stream.Null).WaitAsync(RestHttpTestSupport.Deadline); }
            catch (Exception error) { lateFailure = error; }
            Assert.IsNotNull(lateFailure); Assert.IsFalse(lateFailure is TimeoutException);
            Assert.AreEqual(1, codec.Calls, "A closed lease cannot invoke the encoder again.");
        }
        finally
        {
            codec.Release.TrySetResult();
            try
            {
                try { await copy.WaitAsync(RestHttpTestSupport.Deadline); }
                catch (RestEncodingFaultException error) when (ReferenceEquals(error.Original.SourceException, original)) { }
            }
            finally { await (disposal ?? lease.DisposeAsync().AsTask()).WaitAsync(RestHttpTestSupport.Deadline); }
        }
    }

    private sealed class HeldCodec(Exception original) : IRestContentCodec
    {
        internal TaskCompletionSource Entered { get; } = RestHttpTestSupport.Signal();
        internal TaskCompletionSource Release { get; } = RestHttpTestSupport.Signal();
        internal int Calls { get; private set; }
        public bool CanDecode(Type resultType, RestResponseFormat format) => false;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { Calls++; Entered.TrySetResult(); await Release.Task; throw original; }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
            => throw new NotSupportedException();
    }
}
