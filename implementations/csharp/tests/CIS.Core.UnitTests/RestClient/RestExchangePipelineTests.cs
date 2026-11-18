using System.Text;
using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestExchangePipelineTests
{
    [TestMethod]
    public async Task OrderedHandlersUnwindInReverseAndOneResponseIsDisposedOnceWithoutUsingItsEquality()
    {
        var events = new List<string>();
        var response = new OwnedResponse();
        IRestExchangeHandler Handler(string name) => new Handler(async (request, next, token) =>
        {
            events.Add(name + ".out"); await Task.Yield();
            IRestResponse result = await next(request, token);
            events.Add(name + ".in"); return result;
        });
        var pipeline = new RestExchangePipeline([Handler("A"), Handler("B"), Handler("C")], (_, _) =>
        { events.Add("primary"); return ValueTask.FromResult<IRestResponse>(response); }, default);
        try
        {
            Assert.AreSame(response, await pipeline.RunAsync(Request()));
            CollectionAssert.AreEqual(new[] { "A.out", "B.out", "C.out", "primary", "C.in", "B.in", "A.in" }, events);
            Assert.AreEqual(0, response.Disposals);
        }
        finally { await pipeline.DisposeAsync(); }
        await pipeline.DisposeAsync();
        Assert.AreEqual(1, response.Disposals);
    }

    [TestMethod]
    public async Task ShortCircuitStillUsesTheActualReaderAndOwnedDisposalWithoutPrimaryDispatch()
    {
        var response = new OwnedResponse("7"); int primary = 0;
        var pipeline = new RestExchangePipeline([new Handler((_, _, _) => ValueTask.FromResult<IRestResponse>(response))],
            (_, _) => { primary++; throw new AssertFailedException("A short circuit cannot dispatch."); }, default);
        try
        {
            IRestResponse result = await pipeline.RunAsync(Request());
            RestReadResult<int> read = await new RestHttpResponseReader(result, RestLimitsSnapshot.Capture(new())).ReadAsync<int>(RestResponseFormat.Json, true, null, default);
            Assert.IsTrue(read.Decoded.HasValue); Assert.AreEqual(7, read.Decoded.Value); Assert.AreEqual(0, primary);
        }
        finally { await pipeline.DisposeAsync(); }
        Assert.AreEqual(1, response.Disposals);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ThrowAfterNextAndSecondNextBothJoinTheAlreadyStartedExchange(bool secondNext)
    {
        var started = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        var response = new OwnedResponse(); var original = new InvalidOperationException("Application handler defect.");
        int dispatched = 0;
        var pipeline = new RestExchangePipeline([new Handler((request, next, token) =>
        {
            _ = next(request, token);
            if (secondNext) { return next(request, token); }
            throw original;
        })], async (_, _) => { dispatched++; started.TrySetResult(); await release.Task; return response; }, default);
        Task<IRestResponse> run = pipeline.RunAsync(Request()).AsTask();
        try
        {
            await started.Task.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(run.IsCompleted); Assert.AreEqual(1, dispatched);
            release.TrySetResult();
            InvalidOperationException caught = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run);
            if (!secondNext) { Assert.AreSame(original, caught); }
        }
        finally
        {
            release.TrySetResult();
            try { await run; } catch (InvalidOperationException) { }
            await pipeline.DisposeAsync();
        }
        Assert.AreEqual(1, dispatched); Assert.AreEqual(1, response.Disposals);
    }

    [TestMethod]
    public async Task ReturningAnotherResponseStillJoinsNextAndDisposesBothLeases()
    {
        var started = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        var downstream = new OwnedResponse(); var selected = new OwnedResponse("9");
        var pipeline = new RestExchangePipeline([new Handler((request, next, token) =>
        { _ = next(request, token); return ValueTask.FromResult<IRestResponse>(selected); })],
            async (_, _) => { started.TrySetResult(); await release.Task; return downstream; }, default);
        Task<IRestResponse> run = pipeline.RunAsync(Request()).AsTask();
        try
        {
            await started.Task.WaitAsync(RestHttpTestSupport.Deadline); Assert.IsFalse(run.IsCompleted);
            release.TrySetResult(); Assert.AreSame(selected, await run.WaitAsync(RestHttpTestSupport.Deadline));
        }
        finally { release.TrySetResult(); await run; await pipeline.DisposeAsync(); }
        Assert.AreEqual(1, downstream.Disposals); Assert.AreEqual(1, selected.Disposals);
    }

    [TestMethod]
    public async Task LateNextCannotStartAnExchangeAndForeignContextsOrTokensCannotCrossOperations()
    {
        RestExchange? saved = null; RestRequestContext request = Request();
        var response = new OwnedResponse(); int primary = 0;
        var pipeline = new RestExchangePipeline([new Handler((_, next, _) => { saved = next; return ValueTask.FromResult<IRestResponse>(response); })],
            (_, _) => { primary++; return ValueTask.FromResult<IRestResponse>(response); }, default);
        try
        {
            await pipeline.RunAsync(request);
            Assert.ThrowsExactly<InvalidOperationException>(() => saved!(request, default));
            Assert.AreEqual(0, primary);
        }
        finally { await pipeline.DisposeAsync(); }
        using var owner = new CancellationTokenSource();
        foreach (bool foreignContext in new[] { true, false })
        {
            var invalid = new RestExchangePipeline([new Handler((current, next, token) => next(foreignContext ? Request() : current,
                foreignContext ? token : CancellationToken.None))], (_, _) => { primary++; return ValueTask.FromResult<IRestResponse>(new OwnedResponse()); }, owner.Token);
            try { await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => invalid.RunAsync(Request()).AsTask()); }
            finally { await invalid.DisposeAsync(); }
        }
        Assert.AreEqual(0, primary);
    }

    [TestMethod]
    public async Task ARealOwnedDefectSurvivesConcurrentKnownCancellationAfterNext()
    {
        using var cancellation = new CancellationTokenSource(); var release = RestHttpTestSupport.Signal();
        var original = new InvalidOperationException("Owned exchange defect.");
        var pipeline = new RestExchangePipeline([new Handler((request, next, token) =>
        { _ = next(request, token); cancellation.Cancel(); throw new OperationCanceledException(token); })],
            async (_, _) => { await release.Task; throw original; }, cancellation.Token);
        Task<IRestResponse> run = pipeline.RunAsync(Request()).AsTask();
        try
        {
            Assert.IsFalse(run.IsCompleted); release.TrySetResult();
            Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run));
        }
        finally
        {
            release.TrySetResult(); try { await run; } catch (InvalidOperationException) { }
            await pipeline.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task DisposalWaitsForTheActualLeaseAndStillAttemptsOtherLeasesAfterACleanupFault()
    {
        var held = new OwnedResponse(holdDisposal: true); var original = new InvalidOperationException("Cleanup fault.");
        var faulty = new OwnedResponse(disposalFault: original);
        var pipeline = new RestExchangePipeline([new Handler(async (request, next, token) => { await next(request, token); return faulty; })],
            (_, _) => ValueTask.FromResult<IRestResponse>(held), default);
        Task? disposal = null;
        try
        {
            await pipeline.RunAsync(Request()); disposal = pipeline.DisposeAsync().AsTask();
            await held.DisposalStarted.Task.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(disposal.IsCompleted); held.ReleaseDisposal.TrySetResult();
            Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => disposal));
        }
        finally
        {
            held.ReleaseDisposal.TrySetResult();
            if (disposal is not null) { try { await disposal; } catch (InvalidOperationException) { } }
            else { await pipeline.DisposeAsync(); }
        }
        Assert.AreEqual(1, faulty.Disposals); Assert.AreEqual(1, held.Disposals);
    }

    private static RestRequestContext Request() => new("GET", "https://example.test/", new("orders", "request", 0), new(), new(), null);
    private sealed class Handler(Func<RestRequestContext, RestExchange, CancellationToken, ValueTask<IRestResponse>> handle) : IRestExchangeHandler
    { public ValueTask<IRestResponse> HandleAsync(RestRequestContext request, RestExchange next, CancellationToken token) => handle(request, next, token); }
    private sealed class OwnedResponse(string body = "0", bool holdDisposal = false, Exception? disposalFault = null) : IRestResponse
    {
        internal int Disposals;
        internal TaskCompletionSource DisposalStarted { get; } = RestHttpTestSupport.Signal();
        internal TaskCompletionSource ReleaseDisposal { get; } = RestHttpTestSupport.Signal();
        public int HttpStatusCode => 200;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; } = new Dictionary<string, IReadOnlyList<string>>();
        public Stream Body { get; } = new MemoryStream(Encoding.UTF8.GetBytes(body));
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposals); DisposalStarted.TrySetResult();
            if (holdDisposal) { await ReleaseDisposal.Task; }
            await Body.DisposeAsync();
            if (disposalFault is not null) { throw disposalFault; }
        }
        public override bool Equals(object? other) => throw new AssertFailedException("Response ownership must use reference identity.");
        public override int GetHashCode() => throw new AssertFailedException("Response ownership must use reference identity.");
    }
}
