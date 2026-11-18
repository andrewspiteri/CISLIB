using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.TestSupport;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RuntimeCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeclaredPreparationAndAuthenticationFailuresHaveNoPrimaryDispatch(bool authentication)
    {
        int received = 0, opens = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        { Interlocked.Increment(ref received); return context.Response.WriteAsync("1"); });
        var input = new MemoryStream([1, 2]);
        var options = new RestClientOptions("orders") { BaseUrl = server.Address + "/" };
        if (authentication) { options.Authenticate = async (context, _) => { await Task.Yield(); context.Fail("Controlled authentication rejection."); }; }
        else { options.ConfigureRequest = async (context, _) => { await Task.Yield(); context.Fail("Controlled preparation rejection."); }; }
        await using var client = new HttpRestClient(options);
        try
        {
            RestResult<int> result = await client.Post<int>("orders", new()
            {
                Body = RestHttpBody.Reopen(_ => { opens++; return ValueTask.FromResult<Stream>(input); })
            }).WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(result.Success); Assert.IsFalse(result.Context.HasValue); Assert.AreEqual(0, result.Value);
            Assert.AreEqual(authentication ? RestFailureKind.Authentication : RestFailureKind.Preparation, result.Context.FailureKind);
            Assert.AreEqual(authentication ? "client.authentication_failed" : "client.preparation_failed", result.ErrorCode);
            Assert.AreEqual(0, result.Context.AttemptCount); Assert.IsNull(result.Context.HttpStatusCode);
            Assert.AreEqual(0, received); Assert.AreEqual(authentication ? 1 : 0, opens);
            Assert.AreEqual(!authentication, input.CanRead);
        }
        finally { await input.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow("caller")]
    [DataRow("total")]
    [DataRow("stopping")]
    [DataRow("caller-over-total")]
    [DataRow("total-over-stopping")]
    public async Task PublicCompletionWaitsActualCleanupAndUsesTheHighestSignaledCancellationSource(string source)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        using var caller = new CancellationTokenSource();
        var entered = RestHttpTestSupport.Signal();
        var input = new RuntimeHeldInput();
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "http://127.0.0.1:1/", Timeout = TimeSpan.FromSeconds(5),
            Authenticate = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
        };
        await using var client = new HttpRestClient(options, clock: clock);
        Task<RestResult<int>> run = client.Post<int>("orders", new() { Body = RestHttpBody.Reopen(_ => ValueTask.FromResult<Stream>(input)) }, caller.Token);
        Task? stopping = null;
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            switch (source)
            {
                case "total": case "caller-over-total": clock.Advance(TimeSpan.FromSeconds(5)); break;
                case "stopping": case "total-over-stopping": stopping = client.DisposeAsync().AsTask(); break;
                default: caller.Cancel(); break;
            }
            await input.Cleaning.Task.WaitAsync(RestHttpTestSupport.Deadline);
            if (source == "caller-over-total") { caller.Cancel(); }
            if (source == "total-over-stopping") { clock.Advance(TimeSpan.FromSeconds(5)); }
            Assert.IsFalse(run.IsCompleted); if (stopping is not null) { Assert.IsFalse(stopping.IsCompleted); }
            input.Release.TrySetResult();
            if (source.StartsWith("caller", StringComparison.Ordinal))
            {
                OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreEqual(caller.Token, actual.CancellationToken);
            }
            else
            {
                RestResult<int> result = await run.WaitAsync(RestHttpTestSupport.Deadline);
                Assert.IsFalse(result.Success); Assert.AreEqual(0, result.Context.AttemptCount); Assert.IsNull(result.Context.HttpStatusCode);
                Assert.AreEqual(source.StartsWith("total", StringComparison.Ordinal) ? RestFailureKind.Timeout : RestFailureKind.Stopping, result.Context.FailureKind);
                Assert.AreEqual(source.StartsWith("total", StringComparison.Ordinal) ? "client.timeout" : "client.stopping", result.ErrorCode);
                if (source.StartsWith("total", StringComparison.Ordinal)) { Assert.AreEqual(TimeSpan.FromSeconds(5), result.Context.Duration); }
            }
            Assert.AreEqual(1, input.Disposals);
        }
        finally
        {
            caller.Cancel(); input.Release.TrySetResult();
            try { await RuntimeTestSupport.SettleAsync(run); }
            finally { if (stopping is not null) { await stopping.WaitAsync(RestHttpTestSupport.Deadline); } }
        }
    }

    [TestMethod]
    public async Task NativeResponseBodyReadObservesTotalDeadlineAndRetainsItsActualReceivedStatus()
    {
        var release = RestHttpTestSupport.Signal(); var finished = RestHttpTestSupport.Signal();
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            try
            {
                context.Response.ContentType = "application/octet-stream"; context.Response.ContentLength = 2;
                await context.Response.Body.WriteAsync(new byte[] { 1 }, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await release.Task;
                await context.Response.Body.WriteAsync(new byte[] { 2 }, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (IOException) { }
            finally { finished.TrySetResult(); }
        });
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var codec = new HeldResponseCodec();
        await using var client = new HttpRestClient(new("orders")
        { BaseUrl = server.Address + "/", Timeout = TimeSpan.FromSeconds(5), ResponseFormat = RestResponseFormat.Bytes, Codec = codec }, clock: clock);
        Task<RestResult<byte[]>> run = client.Get<byte[]>("orders");
        try
        {
            await codec.Reading.Task.WaitAsync(RestHttpTestSupport.Deadline);
            clock.Advance(TimeSpan.FromSeconds(5));
            RestResult<byte[]> result = await run.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Timeout, result.Context.FailureKind);
            Assert.AreEqual("client.timeout", result.ErrorCode); Assert.AreEqual(200, result.Context.HttpStatusCode);
            Assert.AreEqual(1, result.Context.AttemptCount); Assert.AreEqual(TimeSpan.FromSeconds(5), result.Context.Duration);
            Assert.IsTrue(codec.CancellationObserved);
        }
        finally
        {
            release.TrySetResult();
            try { await RuntimeTestSupport.SettleAsync(run); }
            finally { await finished.Task.WaitAsync(RestHttpTestSupport.Deadline); }
        }
    }

    [TestMethod]
    [DataRow("authentication-fault")]
    [DataRow("foreign-cancellation")]
    [DataRow("codec-http")]
    [DataRow("codec-io")]
    public async Task UnexpectedFaultWinsEvenWhenCallerAndTotalCancellationBecomeSignaled(string kind)
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            try { await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted); }
            catch (IOException) { }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        });
        var entered = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        using var caller = new CancellationTokenSource();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        Exception defect = kind switch
        {
            "foreign-cancellation" => new OperationCanceledException(new CancellationToken(canceled: true)),
            "codec-http" => new HttpRequestException("Controlled custom codec fault."),
            "codec-io" => new IOException("Controlled custom codec fault."),
            _ => new InvalidOperationException("Controlled authentication defect.")
        };
        var options = new RestClientOptions("orders") { BaseUrl = server.Address + "/", Timeout = TimeSpan.FromSeconds(5) };
        bool encoding = kind.StartsWith("codec", StringComparison.Ordinal);
        if (encoding) { options.Codec = new FaultingCodec(entered, release, defect); }
        else { options.Authenticate = async (_, _) => { entered.TrySetResult(); await release.Task; throw defect; }; }
        await using var client = new HttpRestClient(options, clock: clock);
        Task<RestResult<int>> run = client.Post<int>("orders", new() { Body = RestBody.Bytes(new byte[] { 1 }) }, caller.Token);
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            caller.Cancel(); clock.Advance(TimeSpan.FromSeconds(5)); release.TrySetResult();
            Exception? actual = null;
            try { await run.WaitAsync(RestHttpTestSupport.Deadline); }
            catch (Exception error) { actual = error; }
            Assert.AreSame(defect, actual);
        }
        finally { release.TrySetResult(); caller.Cancel(); await RuntimeTestSupport.SettleAsync(run, defect); }
    }

    [TestMethod]
    public async Task CallbackCancellationThrownBeforeItsTokenIsCanceledRemainsTheOriginalFaultAfterOwnedSigningSettles()
    {
        var entered = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        using var caller = new CancellationTokenSource();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        OperationCanceledException? original = null;
        Task<ReadOnlyMemory<byte>>? signing = null;
        var options = new RestClientOptions("orders")
        {
            BaseUrl = "http://127.0.0.1:1/", Timeout = TimeSpan.FromSeconds(5), Codec = new HeldSigningCodec(entered, release),
            Authenticate = (authentication, token) =>
            {
                Assert.IsFalse(token.IsCancellationRequested);
                signing = authentication.GetBodyBytesAsync(32, token).AsTask();
                original = new OperationCanceledException("Controlled callback fault before cancellation.", null, token);
                throw original;
            }
        };
        await using var client = new HttpRestClient(options, clock: clock);
        Task<RestResult<int>> run = client.Post<int>("orders", new() { Body = RestBody.Text("value") }, caller.Token);
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsNotNull(original); Assert.IsFalse(run.IsCompleted);
            caller.Cancel(); clock.Advance(TimeSpan.FromSeconds(5));
            Assert.IsFalse(run.IsCompleted, "The callback fault does not permit abandoning its started signing work.");
            release.TrySetResult();
            Exception? actual = null;
            try { await run.WaitAsync(RestHttpTestSupport.Deadline); }
            catch (Exception error) { actual = error; }
            Assert.AreSame(original, actual);
        }
        finally
        {
            release.TrySetResult(); caller.Cancel();
            try { await RuntimeTestSupport.SettleAsync(run, original); }
            finally { if (signing is not null) { await signing.WaitAsync(RestHttpTestSupport.Deadline); } }
        }
    }

    [TestMethod]
    public async Task CallerCancellationIsIsolatedAndCannotRewriteAnAlreadyCommittedOtherCall()
    {
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context => context.Response.WriteAsync("17", context.RequestAborted));
        await using var client = new HttpRestClient(new("orders") { BaseUrl = server.Address + "/" });
        using var canceled = new CancellationTokenSource(); using var completed = new CancellationTokenSource();
        var entered = RestHttpTestSupport.Signal();
        Task<RestResult<int>> held = client.Get<int>("held", new()
        { Authenticate = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } }, canceled.Token);
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline);
            RestResult<int> done = await client.Get<int>("fast", cancellationToken: completed.Token).WaitAsync(RestHttpTestSupport.Deadline);
            completed.Cancel(); canceled.Cancel();
            OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => held.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreEqual(canceled.Token, actual.CancellationToken);
            Assert.IsTrue(done.Success); Assert.AreEqual(17, done.Value); Assert.AreEqual(1, done.Context.AttemptCount);
            RestResult<int> later = await client.Get<int>("later").WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsTrue(later.Success); Assert.AreEqual(17, later.Value);
        }
        finally { canceled.Cancel(); await RuntimeTestSupport.SettleAsync(held); }
    }

    private sealed class HeldResponseCodec : IRestContentCodec
    {
        internal TaskCompletionSource Reading { get; } = RestHttpTestSupport.Signal();
        internal bool CancellationObserved { get; private set; }
        public bool CanDecode(Type type, RestResponseFormat format) => type == typeof(byte[]) && format == RestResponseFormat.Bytes;
        public ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token) => throw new NotSupportedException();
        public async ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
        {
            byte[] first = new byte[1]; Assert.AreEqual(1, await source.ReadAsync(first, token)); Reading.TrySetResult();
            try { using var remainder = new MemoryStream(); await source.CopyToAsync(remainder, token); return RestDecodedValue<T>.Absent; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { CancellationObserved = true; throw; }
        }
    }

    private sealed class FaultingCodec(TaskCompletionSource entered, TaskCompletionSource release, Exception defect) : IRestContentCodec
    {
        public bool CanDecode(Type type, RestResponseFormat format) => true;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { entered.TrySetResult(); await release.Task; throw defect; }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class HeldSigningCodec(TaskCompletionSource entered, TaskCompletionSource release) : IRestContentCodec
    {
        public bool CanDecode(Type type, RestResponseFormat format) => true;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { entered.TrySetResult(); await release.Task; await destination.WriteAsync(new byte[] { 1, 2, 3 }, CancellationToken.None); }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token) => throw new NotSupportedException();
    }
}
