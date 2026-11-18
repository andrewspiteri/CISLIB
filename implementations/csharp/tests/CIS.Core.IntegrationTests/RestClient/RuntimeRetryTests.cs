using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.TestSupport;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RuntimeRetryTests
{
    [TestMethod]
    public async Task ReplayAssuredPostUsesFreshContentStableWirePayloadAndKeyAndRefreshesOnlyCredentials()
    {
        var received = new ConcurrentQueue<(string Body, string Key, string Authentication)>();
        int requests = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            using var body = new MemoryStream(); await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            received.Enqueue((Encoding.UTF8.GetString(body.ToArray()), context.Request.Headers["Idempotency-Key"].ToString(), context.Request.Headers.Authorization.ToString()));
            int request = Interlocked.Increment(ref requests);
            context.Response.StatusCode = request < 3 ? 503 : 200;
            await context.Response.WriteAsync(request < 3 ? "unavailable" : "7", context.RequestAborted);
        });
        var contexts = new List<RestRequestContext>(); var contents = new List<ReplayContent>(); var order = new List<string>();
        var policy = new RestRetryPolicy("post", 2, TimeSpan.Zero, TimeSpan.Zero, [503], ["POST"], useJitter: false);
        var options = new RestClientOptions("orders")
        {
            BaseUrl = server.Address + "/", RetryPolicy = RestRetrySelection.Named("post"),
            ConfigureRequest = (context, _) => { contexts.Add(context); order.Add("configure-" + context.Call.Iteration); return Task.CompletedTask; },
            Authenticate = (context, _) =>
            { order.Add("authenticate-" + context.Call.Iteration); context.Headers.Set("Authorization", "Bearer attempt-" + context.Call.Iteration); return Task.CompletedTask; }
        };
        options.Headers.Set("Idempotency-Key", "provider-operation-17");
        RestBody body = RestHttpBody.Content(_ =>
        {
            Assert.IsTrue(contents.All(content => content.Disposed), "Previous acquired content must be disposed before another attempt opens content.");
            order.Add("content-" + contents.Count);
            var content = new ReplayContent(Encoding.UTF8.GetBytes("same provider payload")); contents.Add(content);
            return ValueTask.FromResult<HttpContent>(content);
        });
        await using WebApplication host = RuntimeTestSupport.Root(options, TimeProvider.System, policy);
        IServiceProvider root = host.Services;
        IRestClient client = root.GetRequiredKeyedService<IRestClient>("orders");
        RestResult<int> result = await client.Post<int>("orders", new() { Body = body, ReplaySafety = RestReplaySafety.Assured }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(result.Success); Assert.AreEqual(7, result.Value); Assert.AreEqual(3, result.Context.AttemptCount); Assert.IsNull(result.Context.RetryStopReason);
        Assert.AreEqual(3, requests); Assert.AreEqual(3, contexts.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.AreEqual(3, contents.Distinct(ReferenceEqualityComparer.Instance).Count()); Assert.IsTrue(contents.All(content => content.Disposed));
        CollectionAssert.AreEqual(new[] { "configure-0", "content-0", "authenticate-0", "configure-1", "content-1", "authenticate-1", "configure-2", "content-2", "authenticate-2" }, order);
        CollectionAssert.AreEqual(new[] { "same provider payload", "same provider payload", "same provider payload" }, received.Select(item => item.Body).ToArray());
        CollectionAssert.AreEqual(new[] { "provider-operation-17", "provider-operation-17", "provider-operation-17" }, received.Select(item => item.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Bearer attempt-0", "Bearer attempt-1", "Bearer attempt-2" }, received.Select(item => item.Authentication).ToArray());
    }

    [TestMethod]
    public async Task ExplicitSigningReplayCopiesThePreparedWireBytesInsteadOfEncodingThemAgain()
    {
        var received = new ConcurrentQueue<string>(); var signed = new List<string>();
        int requests = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        {
            using var body = new MemoryStream(); await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            received.Enqueue(Encoding.UTF8.GetString(body.ToArray()));
            context.Response.StatusCode = Interlocked.Increment(ref requests) == 1 ? 503 : 204;
        });
        var codec = new ChangingRequestCodec();
        var policy = new RestRetryPolicy("signed", 1, TimeSpan.Zero, TimeSpan.Zero, [503], ["POST"], useJitter: false);
        var options = new RestClientOptions("orders")
        {
            BaseUrl = server.Address + "/", Codec = codec, RetryPolicy = RestRetrySelection.Named("signed"),
            Authenticate = async (context, token) =>
            {
                ReadOnlyMemory<byte> bytes = await context.GetBodyBytesAsync(64, token);
                signed.Add(Encoding.UTF8.GetString(bytes.Span));
                context.Headers.Set("X-Signature", Convert.ToBase64String(bytes.Span));
            }
        };
        options.CredentialHeaders.Add("X-Signature"); options.Headers.Set("Idempotency-Key", "provider-signed-operation");
        await using WebApplication host = RuntimeTestSupport.Root(options, TimeProvider.System, policy);
        RestResult<RestNoValue> result = await host.Services.GetRequiredKeyedService<IRestClient>("orders").Post<RestNoValue>("orders", new()
        { Body = RestBody.Text("original value"), ReplaySafety = RestReplaySafety.Assured }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(result.Success); Assert.AreEqual(2, result.Context.AttemptCount); Assert.AreEqual(2, requests);
        CollectionAssert.AreEqual(new[] { "encoded-1", "encoded-1" }, signed);
        CollectionAssert.AreEqual(new[] { "encoded-1", "encoded-1" }, received.ToArray());
        Assert.AreEqual(1, codec.Encodings);
    }

    [TestMethod]
    public async Task CallsCanInheritDisableOrReplaceTheRegisteredPolicyWithoutChangingClientDefaults()
    {
        var counts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        { counts.AddOrUpdate(context.Request.Path.Value!, 1, (_, count) => count + 1); context.Response.StatusCode = 503; await context.Response.WriteAsync("unavailable"); });
        var inherited = new RestRetryPolicy("inherited", 2, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false);
        var replacement = new RestRetryPolicy("replacement", 1, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false);
        var options = new RestClientOptions("orders") { BaseUrl = server.Address + "/", RetryPolicy = RestRetrySelection.Named("inherited") };
        await using WebApplication host = RuntimeTestSupport.Root(options, TimeProvider.System, inherited, replacement);
        IServiceProvider root = host.Services;
        IRestClient client = root.GetRequiredKeyedService<IRestClient>("orders");
        (string Path, RestRequestOptions? Options, int Count, RestRetryStopReason Reason)[] choices =
        [
            ("inherit", null, 3, RestRetryStopReason.AttemptLimit),
            ("none", new() { RetryPolicy = RestRetrySelection.None }, 1, RestRetryStopReason.Disabled),
            ("replace", new() { RetryPolicy = RestRetrySelection.Named("replacement") }, 2, RestRetryStopReason.AttemptLimit),
            ("inherit-again", null, 3, RestRetryStopReason.AttemptLimit)
        ];
        foreach (var choice in choices)
        {
            RestResult<int> result = await client.Get<int>(choice.Path, choice.Options).WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(result.Success); Assert.AreEqual("http.503", result.ErrorCode); Assert.AreEqual(503, result.Context.HttpStatusCode);
            Assert.AreEqual(choice.Count, result.Context.AttemptCount); Assert.AreEqual(choice.Count, counts["/" + choice.Path]);
            Assert.AreEqual(choice.Reason, result.Context.RetryStopReason);
        }
        Assert.AreEqual("inherited", options.RetryPolicy.PolicyName);
    }

    [TestMethod]
    public async Task ReplayAssuranceDoesNotMakeABorrowedOneShotBodyReplayable()
    {
        int received = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        { Interlocked.Increment(ref received); await context.Request.Body.CopyToAsync(Stream.Null); context.Response.StatusCode = 503; });
        var policy = new RestRetryPolicy("post", 2, TimeSpan.Zero, TimeSpan.Zero, [503], ["POST"], useJitter: false);
        await using WebApplication host = RuntimeTestSupport.Root(new("orders")
        { BaseUrl = server.Address + "/", RetryPolicy = RestRetrySelection.Named("post") }, TimeProvider.System, policy);
        IServiceProvider root = host.Services;
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("one shot"));
        RestResult<int> result = await root.GetRequiredKeyedService<IRestClient>("orders").Post<int>("orders", new()
        { Body = RestHttpBody.Stream(body), ReplaySafety = RestReplaySafety.Assured }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsFalse(result.Success); Assert.AreEqual("http.503", result.ErrorCode);
        Assert.AreEqual(1, result.Context.AttemptCount); Assert.AreEqual(1, received);
        Assert.AreEqual(RestRetryStopReason.BodyNotReplayable, result.Context.RetryStopReason); Assert.IsTrue(body.CanRead);
    }

    [TestMethod]
    [DataRow("key", RestRetryStopReason.NotEligible)]
    [DataRow("destination", RestRetryStopReason.NotEligible)]
    [DataRow("body", RestRetryStopReason.BodyNotReplayable)]
    public async Task ChangedOrdinaryReplayRepresentationPreservesLastFailureWithoutAnotherSend(string changed, RestRetryStopReason reason)
    {
        int received = 0, configurations = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        { Interlocked.Increment(ref received); await context.Request.Body.CopyToAsync(Stream.Null); context.Response.StatusCode = 503; });
        var policy = new RestRetryPolicy("post", 2, TimeSpan.Zero, TimeSpan.Zero, [503], ["POST"], useJitter: false);
        var options = new RestClientOptions("orders")
        {
            BaseUrl = server.Address + "/", RetryPolicy = RestRetrySelection.Named("post"),
            ConfigureRequest = (context, _) =>
            {
                configurations++;
                if (context.Call.Iteration != 0)
                {
                    if (changed == "key") { context.Headers.Set("Idempotency-Key", "different-operation"); }
                    else if (changed == "destination") { context.Url = server.Address + "/different"; }
                    else { context.Body = RestBody.Text("different payload"); }
                }
                return Task.CompletedTask;
            }
        };
        options.Headers.Set("Idempotency-Key", "provider-operation-17");
        await using WebApplication host = RuntimeTestSupport.Root(options, TimeProvider.System, policy);
        IServiceProvider root = host.Services;
        RestResult<int> result = await root.GetRequiredKeyedService<IRestClient>("orders").Post<int>("orders", new()
        { Body = RestBody.Text("same payload"), ReplaySafety = RestReplaySafety.Assured }).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsFalse(result.Success); Assert.AreEqual("http.503", result.ErrorCode); Assert.AreEqual(503, result.Context.HttpStatusCode);
        Assert.AreEqual(1, result.Context.AttemptCount); Assert.AreEqual(1, received); Assert.AreEqual(2, configurations);
        Assert.AreEqual(reason, result.Context.RetryStopReason);
    }

    [TestMethod]
    public async Task AttemptTimeoutJoinsItsActualCleanupBeforeFreshAttemptAndKeepsTheOriginalTotalBudget()
    {
        int received = 0, opened = 0, authentications = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(async context =>
        { Interlocked.Increment(ref received); await context.Request.Body.CopyToAsync(Stream.Null); await context.Response.WriteAsync("9"); });
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        using var caller = new CancellationTokenSource();
        var entered = RestHttpTestSupport.Signal(); var first = new RuntimeHeldInput();
        var policy = new RestRetryPolicy("attempt", 1, TimeSpan.Zero, TimeSpan.Zero, useJitter: false,
            attemptTimeout: TimeSpan.FromSeconds(2), failureKinds: [RestFailureKind.Timeout]);
        var options = new RestClientOptions("orders")
        {
            BaseUrl = server.Address + "/", Timeout = TimeSpan.FromSeconds(10), RetryPolicy = RestRetrySelection.Named("attempt"),
            Authenticate = async (context, token) =>
            { authentications++; if (context.Call.Iteration == 0) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } }
        };
        RestBody body = RestHttpBody.Reopen(_ => ValueTask.FromResult<Stream>(Interlocked.Increment(ref opened) == 1 ? first : new MemoryStream([1, 2, 3])));
        await using WebApplication host = RuntimeTestSupport.Root(options, clock, policy);
        IServiceProvider root = host.Services;
        Task<RestResult<int>> run = root.GetRequiredKeyedService<IRestClient>("orders").Get<int>("orders", new() { Body = body }, caller.Token);
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline); clock.Advance(TimeSpan.FromSeconds(2));
            await first.Cleaning.Task.WaitAsync(RestHttpTestSupport.Deadline);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.IsFalse(run.IsCompleted); Assert.AreEqual(1, opened); Assert.AreEqual(1, authentications); Assert.AreEqual(0, received);
            first.Release.TrySetResult();
            RestResult<int> result = await run.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsTrue(result.Success); Assert.AreEqual(9, result.Value); Assert.AreEqual(1, result.Context.AttemptCount);
            Assert.AreEqual(2, opened); Assert.AreEqual(2, authentications); Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(TimeSpan.FromSeconds(3), result.Context.Duration); Assert.AreEqual(1, received);
        }
        finally { caller.Cancel(); first.Release.TrySetResult(); await RuntimeTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    [DataRow("attempt")]
    [DataRow("caller-over-attempt")]
    [DataRow("total-over-attempt")]
    [DataRow("stopping-over-attempt")]
    public async Task ExhaustedAttemptTimeoutUsesCurrentSourcePrecedenceOnlyAfterAcquiredCleanupSettles(string source)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        using var caller = new CancellationTokenSource();
        var entered = RestHttpTestSupport.Signal(); var input = new RuntimeHeldInput();
        var policy = new RestRetryPolicy("attempt", 0, TimeSpan.Zero, TimeSpan.Zero, attemptTimeout: TimeSpan.FromSeconds(2));
        WebApplication host = RuntimeTestSupport.Root(new("orders")
        {
            BaseUrl = "http://127.0.0.1:1/", Timeout = TimeSpan.FromSeconds(5), RetryPolicy = RestRetrySelection.Named("attempt"),
            Authenticate = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
        }, clock, policy);
        IServiceProvider root = host.Services;
        Task<RestResult<int>> run = root.GetRequiredKeyedService<IRestClient>("orders").Post<int>("orders",
            new() { Body = RestHttpBody.Reopen(_ => ValueTask.FromResult<Stream>(input)) }, caller.Token);
        Task? stopping = null;
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline); clock.Advance(TimeSpan.FromSeconds(2));
            await input.Cleaning.Task.WaitAsync(RestHttpTestSupport.Deadline);
            if (source == "caller-over-attempt") { caller.Cancel(); }
            if (source == "total-over-attempt") { clock.Advance(TimeSpan.FromSeconds(3)); }
            if (source == "stopping-over-attempt") { stopping = host.DisposeAsync().AsTask(); }
            Assert.IsFalse(run.IsCompleted); if (stopping is not null) { Assert.IsFalse(stopping.IsCompleted); }
            input.Release.TrySetResult();
            if (source == "caller-over-attempt")
            {
                OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
                Assert.AreEqual(caller.Token, actual.CancellationToken);
            }
            else
            {
                RestResult<int> result = await run.WaitAsync(RestHttpTestSupport.Deadline);
                Assert.IsFalse(result.Success); Assert.AreEqual(0, result.Context.AttemptCount); Assert.IsNull(result.Context.HttpStatusCode);
                Assert.AreEqual(source == "stopping-over-attempt" ? RestFailureKind.Stopping : RestFailureKind.Timeout, result.Context.FailureKind);
                Assert.AreEqual(source switch { "attempt" => "client.attempt_timeout", "total-over-attempt" => "client.timeout", _ => "client.stopping" }, result.ErrorCode);
            }
        }
        finally
        {
            caller.Cancel(); input.Release.TrySetResult();
            try { await RuntimeTestSupport.SettleAsync(run); }
            finally { await (stopping ?? host.DisposeAsync().AsTask()).WaitAsync(RestHttpTestSupport.Deadline); }
        }
    }

    [TestMethod]
    public async Task CallerCancellationDuringActualUpstreamRetryWaitPreventsAnotherDispatchAndDisposesTheWait()
    {
        int received = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        { Interlocked.Increment(ref received); context.Response.StatusCode = 503; return Task.CompletedTask; });
        using var clock = new ResilienceClock(); using var caller = new CancellationTokenSource();
        var policy = new RestRetryPolicy("wait", 1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), [503], useJitter: false);
        await using WebApplication host = RuntimeTestSupport.Root(new("orders")
        { BaseUrl = server.Address + "/", RetryPolicy = RestRetrySelection.Named("wait"), Timeout = TimeSpan.FromSeconds(30) }, clock, policy);
        IServiceProvider root = host.Services;
        Task<RestResult<int>> run = root.GetRequiredKeyedService<IRestClient>("orders").Get<int>("orders", cancellationToken: caller.Token);
        try
        {
            ResilienceClock.Wait wait = await RuntimeTestSupport.RetryWaitAsync(clock, TimeSpan.FromSeconds(5));
            caller.Cancel();
            OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() => run.WaitAsync(RestHttpTestSupport.Deadline));
            Assert.AreEqual(caller.Token, actual.CancellationToken); Assert.AreEqual(1, received);
            await wait.Disposed.Task.WaitAsync(RestHttpTestSupport.Deadline);
        }
        finally { caller.Cancel(); await RuntimeTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    public async Task TotalDeadlineDuringAnIgnoringRetryDecisionStillWaitsForThatCallbackAndSendsNothingElse()
    {
        int received = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        { Interlocked.Increment(ref received); context.Response.StatusCode = 503; return Task.CompletedTask; });
        var entered = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var policy = new RestRetryPolicy("decision", 1, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false,
            shouldRetry: async (_, _) => { entered.TrySetResult(); await release.Task; return true; });
        await using WebApplication host = RuntimeTestSupport.Root(new("orders")
        { BaseUrl = server.Address + "/", Timeout = TimeSpan.FromSeconds(10), RetryPolicy = RestRetrySelection.Named("decision") }, clock, policy);
        IServiceProvider root = host.Services;
        Task<RestResult<int>> run = root.GetRequiredKeyedService<IRestClient>("orders").Get<int>("orders");
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline); clock.Advance(TimeSpan.FromSeconds(10));
            Assert.IsFalse(run.IsCompleted); Assert.AreEqual(1, received);
            release.TrySetResult();
            RestResult<int> result = await run.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Timeout, result.Context.FailureKind);
            Assert.AreEqual("client.timeout", result.ErrorCode); Assert.AreEqual(1, result.Context.AttemptCount); Assert.AreEqual(1, received);
        }
        finally { release.TrySetResult(); await RuntimeTestSupport.SettleAsync(run); }
    }

    [TestMethod]
    public async Task LaterExchangeWithoutResponseDoesNotReuseTheEarlier503Status()
    {
        int received = 0;
        await using RestHttpLocalServer server = await RestHttpLocalServer.StartAsync(context =>
        {
            if (Interlocked.Increment(ref received) == 1) { context.Response.StatusCode = 503; }
            else { context.Abort(); }
            return Task.CompletedTask;
        });
        var policy = new RestRetryPolicy("later", 1, TimeSpan.Zero, TimeSpan.Zero, [503], useJitter: false);
        await using WebApplication host = RuntimeTestSupport.Root(new("orders")
        { BaseUrl = server.Address + "/", Timeout = TimeSpan.FromSeconds(5), RetryPolicy = RestRetrySelection.Named("later") }, TimeProvider.System, policy);
        IServiceProvider root = host.Services;
        RestResult<int> result = await root.GetRequiredKeyedService<IRestClient>("orders").Get<int>("orders").WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsFalse(result.Success); Assert.AreEqual(RestFailureKind.Transport, result.Context.FailureKind);
        Assert.AreEqual("client.transport_error", result.ErrorCode); Assert.IsNull(result.Context.HttpStatusCode);
        Assert.AreEqual(2, result.Context.AttemptCount); Assert.IsTrue(received >= 2);
    }

    [TestMethod]
    public async Task StandaloneSelectedRetryWithoutAnExecutorFailsBeforeAnyOperationCanStart()
    {
        HttpRestClient? unexpected = null;
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => unexpected = new HttpRestClient(new("orders")
            { BaseUrl = "http://127.0.0.1:1/", RetryPolicy = RestRetrySelection.Named("missing") }));
        }
        finally { if (unexpected is not null) { await unexpected.DisposeAsync(); } }
    }

    private sealed class ReplayContent(byte[] payload) : HttpContent
    {
        private int disposed;
        internal bool Disposed => Volatile.Read(ref disposed) != 0;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(payload).AsTask();
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token) => stream.WriteAsync(payload, token).AsTask();
        protected override bool TryComputeLength(out long length) { length = payload.Length; return true; }
        protected override void Dispose(bool disposing) { if (disposing) { Interlocked.Exchange(ref disposed, 1); } base.Dispose(disposing); }
    }

    private sealed class ChangingRequestCodec : IRestContentCodec
    {
        internal int Encodings { get; private set; }
        public bool CanDecode(Type type, RestResponseFormat format) => false;
        public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken token)
        { Encodings++; await destination.WriteAsync(Encoding.UTF8.GetBytes("encoded-" + Encodings), token); }
        public ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format, CancellationToken token)
            => throw new NotSupportedException();
    }
}
