using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestCallbackCancellationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CallbackCancellationAwaitsItsActualFinallyBeforeFinalizingTheAttempt(bool authentication)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = RestHttpTestSupport.Signal(); var cleaning = RestHttpTestSupport.Signal(); var release = RestHttpTestSupport.Signal();
        async Task Callback(CancellationToken token)
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cleaning.TrySetResult(); await release.Task; }
        }
        var options = new RestClientOptions("orders") { BaseUrl = "https://example.test/" };
        if (authentication) { options.Authenticate = (_, token) => Callback(token); }
        else { options.ConfigureRequest = (_, token) => Callback(token); }
        RestClientSnapshot client = RestClientSnapshot.Capture(options);
        RestCallSnapshot call = RestCallSnapshot.Capture(client, null);
        RestRequestContext request = RestRequestFinalizer.Create(client, call, "GET", "/orders", 0);
        int nativePrepared = 0;
        Task<RestDeclaredFailure?> run = RestRequestFinalizer.PrepareAsync(request, client, call, _ => ValueTask.CompletedTask,
            (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty), cancellation.Token,
            () => nativePrepared++).AsTask();
        try
        {
            await entered.Task.WaitAsync(RestHttpTestSupport.Deadline); cancellation.Cancel();
            await cleaning.Task.WaitAsync(RestHttpTestSupport.Deadline); Assert.IsFalse(run.IsCompleted);
            release.TrySetResult();
            OperationCanceledException error = await Assert.ThrowsAsync<OperationCanceledException>(() => run);
            Assert.AreEqual(cancellation.Token, error.CancellationToken); Assert.IsTrue(request.IsSealed);
            Assert.AreEqual(authentication ? 1 : 0, nativePrepared);
        }
        finally
        {
            cancellation.Cancel(); release.TrySetResult();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
