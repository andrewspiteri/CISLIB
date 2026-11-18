using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.TestSupport;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RuntimeBoundaryTests
{
    [TestMethod]
    public async Task CallbackMayReportTheActuallyCanceledCallerSourceWithoutBecomingAProgrammingFault()
    {
        using var caller = new CancellationTokenSource();
        await using var client = new HttpRestClient(new("orders") { BaseUrl = "http://127.0.0.1:1/" });
        Task<RestResult<int>> run = client.Get<int>("/", new()
        {
            ConfigureRequest = (_, _) => { caller.Cancel(); throw new OperationCanceledException(caller.Token); }
        }, caller.Token);
        OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
        Assert.AreEqual(caller.Token, error.CancellationToken);
        Assert.IsTrue(run.IsCanceled, "The exact signaled caller source is expected cancellation, not a faulted task.");
    }

    [TestMethod]
    public async Task AttemptTimeoutDuringOwnedOneShotAcquisitionCannotReplayTheNowDisposedInput()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var policy = new RestRetryPolicy("attempt", 1, TimeSpan.Zero, TimeSpan.Zero, useJitter: false,
            attemptTimeout: TimeSpan.FromSeconds(2), failureKinds: [RestFailureKind.Timeout]);
        await using WebApplication host = RuntimeTestSupport.Root(new("orders")
        { BaseUrl = "http://127.0.0.1:1/", RetryPolicy = RestRetrySelection.Named("attempt") }, clock, policy);
        var input = new CancelOnReadability(clock);
        RestResult<int> result = await host.Services.GetRequiredKeyedService<IRestClient>("orders").Get<int>("/", new()
        { Body = RestHttpBody.Stream(input, ownsStream: true) }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Timeout, result.Context.FailureKind);
        Assert.AreEqual("client.attempt_timeout", result.ErrorCode); Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.AreEqual(RestRetryStopReason.BodyNotReplayable, result.Context.RetryStopReason);
        Assert.IsTrue(input.Disposed);
    }

    [TestMethod]
    public async Task StopJoinsAnAdmittedCustomCodecCapabilityCallbackAndRejectsNewCallsBeforeInvokingItAgain()
    {
        using var codec = new HeldCapabilityCodec();
        await using var client = new HttpRestClient(new("orders") { BaseUrl = "http://127.0.0.1:1/", Codec = codec });
        Task<RestResult<int>> run = Task.Run(() => client.Get<int>("/"));
        Task? stop = null;
        try
        {
            await codec.Entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            stop = client.DisposeAsync().AsTask();
            Assert.IsFalse(stop.IsCompleted);
            RestResult<int> rejected = await client.Get<int>("/").WaitAsync(RestHttpTestSupport.Deadline);
            Assert.AreEqual(RestFailureKind.Stopping, rejected.Context.FailureKind);
            Assert.AreEqual(1, codec.Calls);
            codec.Release.Set();
            RestResult<int> result = await run.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.AreEqual(RestFailureKind.Stopping, result.Context.FailureKind);
            Assert.AreEqual(0, result.Context.AttemptCount);
            await stop.WaitAsync(RestHttpTestSupport.Deadline);
        }
        finally
        {
            codec.Release.Set();
            try { await RuntimeTestSupport.SettleAsync(run); }
            finally { if (stop is not null) { await stop.WaitAsync(RestHttpTestSupport.Deadline); } }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EncoderMayReportItsActuallyCanceledCallerSourceDuringRawSendOrExplicitSigning(bool signing)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            try { await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted); context.Response.StatusCode = 204; }
            catch (IOException) { }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        using var caller = new CancellationTokenSource();
        var codec = new CallerTokenCodec(caller, cancelBeforeThrow: true);
        var options = new RestClientOptions("orders") { BaseUrl = server.Address + "/", Codec = codec };
        if (signing) { options.Authenticate = async (authentication, token) => { await authentication.GetBodyBytesAsync(64, token); }; }
        await using var client = new HttpRestClient(options);
        Task<RestResult<RestNoValue>> run = client.Post<RestNoValue>("orders", new() { Body = RestBody.Text("value") }, caller.Token);
        try
        {
            OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreEqual(caller.Token, actual.CancellationToken); Assert.IsTrue(caller.IsCancellationRequested);
            Assert.IsTrue(run.IsCanceled); Assert.IsFalse(run.IsFaulted); Assert.AreEqual(1, codec.Calls);
        }
        finally { caller.Cancel(); await RuntimeTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    public async Task SigningFaultCaughtBeforeCallerCancellationCannotBecomeCallerCancellationWhenAuthenticationLaterRethrowsIt()
    {
        using var caller = new CancellationTokenSource();
        var codec = new CallerTokenCodec(caller, cancelBeforeThrow: false);
        var caught = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "http://127.0.0.1:1/", Codec = codec,
            Authenticate = async (authentication, token) =>
            {
                try { await authentication.GetBodyBytesAsync(64, token); }
                catch (OperationCanceledException error) when (ReferenceEquals(error, codec.Original))
                {
                    Assert.IsFalse(caller.IsCancellationRequested);
                    caught.TrySetResult(); await release.Task; throw;
                }
            }
        };
        await using var client = new HttpRestClient(options);
        Task<RestResult<RestNoValue>> run = client.Post<RestNoValue>("orders", new() { Body = RestBody.Text("value") }, caller.Token);
        try
        {
            await caught.Task.WaitAsync(RestHttpTestSupport.Deadline);
            caller.Cancel(); Assert.IsFalse(run.IsCompleted);
            release.TrySetResult();
            OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreSame(codec.Original, actual); Assert.AreEqual(caller.Token, actual.CancellationToken);
            Assert.IsTrue(run.IsFaulted, "An OCE recorded while its source was unsignaled remains a programming fault after the source later cancels.");
            Assert.IsFalse(run.IsCanceled); Assert.AreEqual(1, codec.Calls);
        }
        finally { release.TrySetResult(); caller.Cancel(); await RuntimeTestSupport.SettleAsync(run, codec.Original); }
    }

    private sealed class CallerTokenCodec(CancellationTokenSource caller, bool cancelBeforeThrow) : IRestContentCodec
    {
        internal int Calls { get; private set; }
        internal OperationCanceledException? Original { get; private set; }
        public bool CanDecode(Type type, RestResponseFormat format) => throw new AssertFailedException("RestNoValue must bypass response codec capability checks.");
        public ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        {
            Calls++; if (cancelBeforeThrow) { caller.Cancel(); }
            var error = new OperationCanceledException("Controlled encoder cancellation provenance.", null, caller.Token);
            Original = error; throw error;
        }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class CancelOnReadability(ManualTimeProvider clock) : MemoryStream([1, 2, 3])
    {
        private int inspected;
        internal bool Disposed { get; private set; }
        public override bool CanRead
        {
            get { if (Interlocked.Exchange(ref inspected, 1) == 0) { clock.Advance(TimeSpan.FromSeconds(2)); } return base.CanRead; }
        }
        public override ValueTask DisposeAsync() { Disposed = true; return base.DisposeAsync(); }
    }

    private sealed class HeldCapabilityCodec : IRestContentCodec, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = RestHttpTestSupport.Signal();
        internal ManualResetEventSlim Release { get; } = new();
        internal int Calls { get; private set; }
        public bool CanDecode(Type type, RestResponseFormat format)
        {
            Calls++; Entered.TrySetResult();
            if (!Release.Wait(RestHttpTestSupport.Deadline)) { throw new TimeoutException("The held capability callback was not released."); }
            return true;
        }
        public ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => Release.Dispose();
    }
}
