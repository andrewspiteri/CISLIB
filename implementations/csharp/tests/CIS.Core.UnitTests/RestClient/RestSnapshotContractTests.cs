using CIS.RestClient;
using CIS.RestClient.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestSnapshotContractTests
{
    [TestMethod]
    public void RegistrationAndCallsDetachOptionsWhileHeaderSetAppendAndRemoveKeepTheirDeclaredMeaning()
    {
        var options = new RestClientOptions("orders") { BaseUrl = "https://example.test/api/" };
        options.Headers.Set("X-Repeat", "base"); options.Headers.Set("X-Remove", "gone");
        options.Operations.Add("create");
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        var input = new RestRequestOptions { OperationName = "create" };
        input.Headers.Append("x-repeat", "extra"); input.Headers.Remove("x-remove");
        input.Query.Set("q", "a b"); input.Query.Append("q", "a%20b");
        RestCallSnapshot first = RestCallSnapshot.Capture(client, input);
        input.Headers.Set("X-Repeat", "replaced"); input.Query.Set("q", "later");
        options.Headers.Set("X-Repeat", "mutated default"); options.Operations.Clear(); options.Timeout = TimeSpan.FromSeconds(1);
        RestCallSnapshot second = RestCallSnapshot.Capture(client, input);
        CollectionAssert.AreEqual(new[] { "base", "extra" }, first.Headers["X-REPEAT"].ToArray());
        CollectionAssert.AreEqual(new[] { "replaced" }, second.Headers["x-repeat"].ToArray());
        Assert.IsFalse(first.Headers.ContainsKey("X-Remove"));
        CollectionAssert.AreEqual(new[] { "a b", "a%20b" }, first.Query.Select(change => change.Value).ToArray());
        Assert.AreEqual(RestQueryChangeKind.Set, first.Query[0].Kind); Assert.AreEqual(RestQueryChangeKind.Append, first.Query[1].Kind);
        Assert.AreEqual(TimeSpan.FromSeconds(30), second.Timeout); Assert.AreEqual("create", second.OperationName);
    }

    [TestMethod]
    public void SharedInputsProduceIndependentSnapshotsAndExplicitDisableDoesNotRunBothCallbacks()
    {
        Func<RestRequestContext, CancellationToken, Task> configure = (_, _) => Task.CompletedTask;
        Func<RestAuthenticationContext, CancellationToken, Task> authenticate = (_, _) => Task.CompletedTask;
        var options = new RestClientOptions("orders") { BaseUrl = "https://example.test/", ConfigureRequest = configure, Authenticate = authenticate,
            RetryPolicy = RestRetrySelection.Named("transient-http") };
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        var input = new RestRequestOptions { DisableAuthentication = true, RetryPolicy = RestRetrySelection.None };
        RestCallSnapshot disabled = RestCallSnapshot.Capture(client, input);
        RestCallSnapshot inherited = RestCallSnapshot.Capture(client, null);
        Assert.IsNull(disabled.Authenticate); Assert.AreSame(configure, disabled.ConfigureRequest);
        Assert.IsNull(disabled.RetryPolicy.PolicyName); Assert.AreSame(authenticate, inherited.Authenticate);
        Assert.AreEqual("transient-http", inherited.RetryPolicy.PolicyName);
        input.Authenticate = authenticate;
        Assert.ThrowsExactly<ArgumentException>(() => RestCallSnapshot.Capture(client, input));
    }

    [TestMethod]
    public void ExplicitCustomReplacementAndTighterLimitsDoNotMutateRegisteredDefaults()
    {
        RestClientSnapshot client = RestClientSnapshot.Capture(new RestClientOptions("orders"));
        var input = new RestRequestOptions { Timeout = TimeSpan.FromSeconds(2), RequireValue = true,
            Limits = new() { MaxResponseBytes = 20, MaxErrorBodyBytes = 0, MaxErrorMessageScalars = 10 } };
        RestCallSnapshot captured = RestCallSnapshot.Capture(client, input);
        input.Limits.MaxResponseBytes = 100;
        Assert.AreEqual(20L, captured.Limits.MaxResponseBytes); Assert.AreEqual(0, captured.Limits.MaxErrorBodyBytes);
        Assert.IsTrue(captured.RequireValue); Assert.AreEqual(16L * 1024 * 1024, client.Limits.MaxResponseBytes);
        input.Limits.MaxResponseBytes = client.Limits.MaxResponseBytes + 1;
        Assert.ThrowsExactly<ArgumentException>(() => RestCallSnapshot.Capture(client, input));
    }

    [TestMethod]
    [DataRow("origin")]
    [DataRow("header")]
    [DataRow("timeout")]
    [DataRow("duplicate-operation")]
    public void InvalidRegistrationFailsBeforeAnyTransportOrCallbackIsAcquired(string invalid)
    {
        var options = new RestClientOptions("orders");
        switch (invalid)
        {
            case "origin": options.Authenticate = (_, _) => throw new AssertFailedException("Validation cannot invoke authentication."); break;
            case "header": options.Headers.Set("Bad Header", "value"); break;
            case "timeout": options.Timeout = TimeSpan.Zero; break;
            case "duplicate-operation": options.Operations.Add("same"); options.Operations.Add("same"); break;
        }
        Assert.Throws<ArgumentException>(() => RestClientSnapshot.Capture(options));
    }

    [TestMethod]
    public void PreCanceledSnapshotPreservesItsTokenAndUnknownOperationsUseOnlyTheRegisteredFallback()
    {
        RestClientSnapshot client = RestClientSnapshot.Capture(new RestClientOptions("orders"));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        OperationCanceledException error = Assert.ThrowsExactly<OperationCanceledException>(() => RestCallSnapshot.Capture(client, new(), canceled.Token));
        Assert.AreEqual(canceled.Token, error.CancellationToken);
        for (int index = 0; index < 100; index++)
        { Assert.AreEqual("request", RestCallSnapshot.Capture(client, new() { OperationName = "unregistered-" + index }).OperationName); }
        CollectionAssert.AreEqual(new[] { "request" }, client.Operations.ToArray());
    }

    [TestMethod]
    public void BodyDescriptorsAndSnapshotsDoNotAcquireDisposeOrDeepCloneCallerContent()
    {
        var dto = new MutableBody { Count = 3 };
        RestBody json = RestBody.Json(dto);
        byte[] bytes = [1, 2]; RestBody binary = RestBody.Bytes(bytes);
        using var stream = new MemoryStream([4, 5]);
        int opens = 0;
        RestBody reopened = RestHttpBody.Reopen(_ => { opens++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        RestBody borrowed = RestHttpBody.Stream(stream);
        RestClientSnapshot client = RestClientSnapshot.Capture(new RestClientOptions("orders"));
        Assert.AreSame(json, RestCallSnapshot.Capture(client, new() { Body = json }).Body);
        Assert.AreSame(dto, json.Value); Assert.AreEqual(typeof(MutableBody), json.ValueType);
        Assert.AreEqual(0, opens); Assert.IsTrue(reopened.IsReplayable); Assert.IsFalse(borrowed.IsReplayable);
        Assert.AreEqual(2L, stream.Length);
        bytes[0] = 9;
        Assert.AreEqual((byte)9, ((ReadOnlyMemory<byte>)binary.Value!).Span[0], "Caller buffers are borrowed and must remain unchanged during real use.");
    }
    private sealed class MutableBody { internal int Count { get; set; } }
}
