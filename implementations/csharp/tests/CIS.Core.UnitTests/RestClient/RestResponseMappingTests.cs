using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestResponseMappingTests
{
    [TestMethod]
    public async Task DefaultHttpFailureProvidesStatusGuidanceWithoutPromotingOrEchoingItsDiagnosticPayload()
    {
        const string payload = "{\"token\":\"private-provider-data\"}";
        await using var response = Response(503, payload);
        RestReadResult<Envelope> read = await Reader(response).ReadAsync<Envelope>(RestResponseFormat.Json, false, null, default);

        RestMappedResponse<Envelope> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, null, default);

        Assert.AreEqual(RestFailureKind.Http, mapped.FailureKind); Assert.AreEqual("http.503", mapped.ErrorCode);
        Assert.IsFalse(mapped.Decoded.HasValue); Assert.IsNull(mapped.Decoded.Value); Assert.IsNull(mapped.RemoteErrorCode);
        string explanation = mapped.Explanation ?? throw new AssertFailedException("An HTTP failure needs explanatory guidance.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(explanation));
        Assert.IsTrue(explanation.Contains("HTTP", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(explanation.Contains("503", StringComparison.Ordinal));
        Assert.IsFalse(explanation.Contains("private-provider-data", StringComparison.Ordinal));
        CollectionAssert.AreEqual(RestHttpTestSupport.Utf8(payload), read.ErrorBody!.Bytes.ToArray());
    }

    [TestMethod]
    [DataRow(199, false)]
    [DataRow(200, true)]
    [DataRow(299, true)]
    [DataRow(300, false)]
    public async Task DefaultMappingUsesTheWholeTwoHundredStatusFamilyAndNeitherAdjacentBoundary(int status, bool success)
    {
        await using var response = Response(status, "0");
        RestReadResult<int> read = await Reader(response).ReadAsync<int>(RestResponseFormat.Json, false, null, default);

        RestMappedResponse<int> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, null, default);

        Assert.AreEqual(success ? RestFailureKind.None : RestFailureKind.Http, mapped.FailureKind);
        Assert.AreEqual(success, mapped.Decoded.HasValue); Assert.AreEqual(0, mapped.Decoded.Value);
        Assert.AreEqual(success ? null : "http." + status, mapped.ErrorCode);
        if (success) { Assert.IsNull(mapped.Explanation); Assert.IsNull(read.ErrorBody); }
        else { Assert.IsNotNull(read.ErrorBody); }
    }

    [TestMethod]
    public async Task DefaultSuccessPreservesZeroAndFalseAsValues()
    {
        await CheckDefaultSuccess("0", 0, true);
        await CheckDefaultSuccess("false", false, true);
    }

    [TestMethod]
    [DataRow("null", true)]
    [DataRow("", false)]
    public async Task DefaultSuccessPreservesPresentNullSeparatelyFromAnAbsentBody(string body, bool present)
        => await CheckDefaultSuccess<string?>(body, null, present);

    [TestMethod]
    public async Task ADecodedHttp200BusinessRejectionClearsTheTypedSuccessValueAndPresence()
    {
        await using var response = Response(200, "{\"accepted\":false,\"count\":7}");
        RestReadResult<Envelope> read = await Reader(response).ReadAsync<Envelope>(RestResponseFormat.Json, true, null, default);
        Assert.IsTrue(read.Decoded.HasValue); Assert.AreEqual(7, read.Decoded.Value!.Count);
        var mapper = new CapturingMapper(RestResponseDecision.RejectBusiness("The envelope declined the operation.", "orders.declined", "DECLINED"));

        RestMappedResponse<Envelope> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, default);

        Assert.AreEqual(RestFailureKind.Business, mapped.FailureKind); Assert.AreEqual("orders.declined", mapped.ErrorCode);
        Assert.AreEqual("The envelope declined the operation.", mapped.Explanation);
        Assert.AreEqual("DECLINED", mapped.RemoteErrorCode); Assert.IsFalse(mapped.Decoded.HasValue); Assert.IsNull(mapped.Decoded.Value);
        var input = (RestResponseContext<Envelope>)mapper.Input!;
        Assert.AreEqual(200, input.HttpStatusCode); Assert.IsTrue(input.Decoded.HasValue);
        Assert.IsFalse(input.Decoded.Value!.Accepted); Assert.AreEqual(7, input.Decoded.Value.Count);
        Assert.IsNull(input.ErrorBody); Assert.AreEqual(1, mapper.Calls);
    }

    [TestMethod]
    public async Task NonSuccessErrorPrefixIsNeverDecodedAsTAndBusinessMappingCannotReplaceItsHttpClassification()
    {
        await using var response = Response(503, "not JSON and not an Envelope", "text/plain");
        var observed = new RestHttpObservation();
        RestReadResult<Envelope> read = await new RestHttpResponseReader(response, new(1024, 4, 1024), observed)
            .ReadAsync<Envelope>(RestResponseFormat.Json, true, null, default);
        var mapper = new CapturingMapper(RestResponseDecision.RejectBusiness("Provider is busy.", "orders.busy", "PROVIDER_BUSY"));

        RestMappedResponse<Envelope> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, default);

        Assert.AreEqual(RestFailureKind.Http, mapped.FailureKind); Assert.AreEqual("http.503", mapped.ErrorCode);
        Assert.AreEqual("Provider is busy.", mapped.Explanation);
        Assert.AreEqual("PROVIDER_BUSY", mapped.RemoteErrorCode); Assert.IsFalse(mapped.Decoded.HasValue); Assert.IsNull(mapped.Decoded.Value);
        Assert.IsTrue(read.ErrorBodyTruncated); CollectionAssert.AreEqual(RestHttpTestSupport.Utf8("not "), read.ErrorBody!.Bytes.ToArray());
        Assert.AreEqual(5L, observed.Downloaded);
        var input = (RestResponseContext<Envelope>)mapper.Input!;
        Assert.AreEqual(503, input.HttpStatusCode); Assert.IsFalse(input.Decoded.HasValue); Assert.IsNull(input.Decoded.Value);
        Assert.AreSame(read.ErrorBody, input.ErrorBody); Assert.AreEqual("text/plain", input.ErrorBody!.MediaType);
    }

    [TestMethod]
    [DataRow("null", true)]
    [DataRow("", false)]
    public async Task ContinueMappingPreservesTheDifferenceBetweenPresentJsonNullAndAbsence(string body, bool present)
    {
        await using var response = Response(200, body);
        RestReadResult<string?> read = await Reader(response).ReadAsync<string?>(RestResponseFormat.Json, present, null, default);
        var mapper = new CapturingMapper(RestResponseDecision.Continue);

        RestMappedResponse<string?> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, default);

        Assert.AreEqual(RestFailureKind.None, mapped.FailureKind); Assert.AreEqual(present, mapped.Decoded.HasValue);
        Assert.IsNull(mapped.Decoded.Value); Assert.IsNull(mapped.ErrorCode);
        var input = (RestResponseContext<string?>)mapper.Input!;
        Assert.AreEqual(present, input.Decoded.HasValue); Assert.IsNull(input.Decoded.Value); Assert.IsNull(input.ErrorBody);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnAsynchronouslyThrownMapperDefectOrForeignCancellationRemainsTheOriginalFault(bool foreignCancellation)
    {
        await using var response = Response(200, "{\"accepted\":true,\"count\":3}");
        RestReadResult<Envelope> read = await Reader(response).ReadAsync<Envelope>(RestResponseFormat.Json, true, null, default);
        var foreignToken = new CancellationToken(canceled: true);
        Exception original = foreignCancellation ? new OperationCanceledException(foreignToken) : new InvalidOperationException("Mapper defect.");
        var mapper = new CapturingMapper(RestResponseDecision.Continue, original);
        using var operation = new CancellationTokenSource();

        Exception actual = foreignCancellation
            ? await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, operation.Token).AsTask())
            : await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, operation.Token).AsTask());

        Assert.AreSame(original, actual); Assert.AreEqual(operation.Token, mapper.Token); Assert.IsFalse(operation.IsCancellationRequested);
        if (foreignCancellation) { Assert.AreEqual(foreignToken, ((OperationCanceledException)actual).CancellationToken); }
        Assert.AreEqual(1, mapper.Calls);
    }

    [TestMethod]
    public async Task MappingUsesTheOneDecodedValueAndASecondConsumptionCannotReadOrReplaceIt()
    {
        await using var response = Response(200, "false");
        var observed = new RestHttpObservation();
        var reader = new RestHttpResponseReader(response, new(1024, 128, 1024), observed);
        RestReadResult<bool> read = await reader.ReadAsync<bool>(RestResponseFormat.Json, true, null, default);
        var mapper = new CapturingMapper(RestResponseDecision.Continue);
        RestMappedResponse<bool> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, default);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync<bool>(RestResponseFormat.Json, true, null, default).AsTask());

        Assert.AreEqual(5L, observed.Downloaded); Assert.AreEqual(1, mapper.Calls);
        Assert.IsTrue(mapped.Decoded.HasValue); Assert.IsFalse(mapped.Decoded.Value); Assert.AreEqual(RestFailureKind.None, mapped.FailureKind);
        Assert.IsFalse(((RestResponseContext<bool>)mapper.Input!).Decoded.Value);
    }

    [TestMethod]
    public async Task MapperReceivesADetachedCaseInsensitiveSnapshotOfAllResponseHeaderValues()
    {
        await using var response = Response(200, "false");
        var sourceHeaders = (Dictionary<string, IReadOnlyList<string>>)response.Headers;
        string[] values = ["first", "second"];
        sourceHeaders["X-Provider-Detail"] = values;
        RestReadResult<bool> read = await Reader(response).ReadAsync<bool>(RestResponseFormat.Json, true, null, default);
        var mapper = new CapturingMapper(RestResponseDecision.Continue);

        await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, default);

        var input = (RestResponseContext<bool>)mapper.Input!;
        values[0] = "changed";
        sourceHeaders.Clear();
        CollectionAssert.AreEqual(new[] { "first", "second" }, input.Headers["x-provider-detail"].ToArray());
        CollectionAssert.AreEqual(new[] { "application/json" }, input.Headers["content-type"].ToArray());
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(503)]
    public async Task ANullMapperDecisionRemainsAnExtensionProgrammingFault(int status)
    {
        await using var response = Response(status, "false");
        RestReadResult<bool> read = await Reader(response).ReadAsync<bool>(RestResponseFormat.Json, false, null, default);
        var mapper = new CapturingMapper(null!);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, mapper, default).AsTask());

        Assert.AreEqual(1, mapper.Calls);
    }

    [TestMethod]
    public async Task CompletingAnAsynchronousMapperDoesNotRequirePumpingTheCallersContext()
    {
        await using var response = Response(200, "false");
        RestReadResult<bool> read = await Reader(response).ReadAsync<bool>(RestResponseFormat.Json, true, null, default);
        var decision = new TaskCompletionSource<RestResponseDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new HeldCallerContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task<RestMappedResponse<bool>> mapping;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            mapping = RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody,
                new SuspendedMapper(decision.Task), default).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        try
        {
            Assert.IsFalse(mapping.IsCompleted);
            decision.SetResult(RestResponseDecision.Continue);
            await Task.WhenAny(mapping, context.Posted.Task).WaitAsync(RestHttpTestSupport.Deadline);
            Assert.IsFalse(context.Posted.Task.IsCompleted,
                "Library-owned mapping completion must not depend on the caller servicing its continuation queue.");
            RestMappedResponse<bool> mapped = await mapping.WaitAsync(RestHttpTestSupport.Deadline);
            Assert.AreEqual(RestFailureKind.None, mapped.FailureKind);
            Assert.IsTrue(mapped.Decoded.HasValue); Assert.IsFalse(mapped.Decoded.Value);
        }
        finally
        {
            decision.TrySetResult(RestResponseDecision.Continue);
            context.Release();
            await mapping.WaitAsync(RestHttpTestSupport.Deadline);
        }
    }

    private static RestHttpTestResponse Response(int status, string body, string mediaType = "application/json")
        => new(status, new MemoryStream(RestHttpTestSupport.Utf8(body)), mediaType);
    private static RestHttpResponseReader Reader(IRestResponse response) => new(response, new(1024, 128, 1024));
    private static async Task CheckDefaultSuccess<T>(string body, T expected, bool present)
    {
        await using var response = Response(200, body);
        RestReadResult<T> read = await Reader(response).ReadAsync<T>(RestResponseFormat.Json, present, null, default);
        RestMappedResponse<T> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, null, default);
        Assert.AreEqual(RestFailureKind.None, mapped.FailureKind); Assert.AreEqual(present, mapped.Decoded.HasValue);
        Assert.AreEqual(expected, mapped.Decoded.Value); Assert.IsNull(mapped.ErrorCode); Assert.IsNull(mapped.Explanation); Assert.IsNull(mapped.RemoteErrorCode);
    }
    private sealed record Envelope(bool Accepted, int Count);
    private sealed class SuspendedMapper(Task<RestResponseDecision> decision) : IRestResponseMapper
    {
        public ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken token) => new(decision);
    }
    // The same held caller-queue pattern is used by the existing Healthcheck completion tests.
    private sealed class HeldCallerContext : SynchronizationContext
    {
        private readonly object gate = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();
        private bool released;
        internal TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (gate)
            {
                Posted.TrySetResult();
                if (!released) { pending.Enqueue((callback, state)); return; }
            }
            Queue((callback, state));
        }
        internal void Release()
        {
            lock (gate)
            {
                released = true;
                while (pending.TryDequeue(out var item)) { Queue(item); }
            }
        }
        private static void Queue((SendOrPostCallback Callback, object? State) item)
            => ThreadPool.UnsafeQueueUserWorkItem(static work => work.Callback(work.State), item, preferLocal: false);
    }
    private sealed class CapturingMapper(RestResponseDecision decision, Exception? failure = null) : IRestResponseMapper
    {
        internal object? Input;
        internal CancellationToken Token;
        internal int Calls;
        public async ValueTask<RestResponseDecision> MapAsync<T>(RestResponseContext<T> response, CancellationToken token)
        {
            Calls++; Input = response; Token = token;
            if (failure is not null) { await Task.Yield(); throw failure; }
            return decision;
        }
    }
}
