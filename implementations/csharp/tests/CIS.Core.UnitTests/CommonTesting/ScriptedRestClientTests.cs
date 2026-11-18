using CIS.CommonTesting;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class ScriptedRestClientTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task JsonValuesSupportAnApplicationDtoAndCallsAreBoundedWithoutHttpOrRetry()
    {
        var client = new ScriptedRestClient(capacity: 2);
        client.EnqueueJsonValue("{\"count\":17}");
        Assert.AreEqual(17, (await client.Get<Payload>("test://first")).Value!.Count);
        client.EnqueueJsonValue("{\"count\":18}");
        client.EnqueueJsonValue("{\"count\":19}");
        await client.Get<Payload>("test://second");
        await client.Get<Payload>("test://third");
        Assert.AreEqual(3L, client.TotalCalls);
        Assert.AreEqual(1L, client.DroppedCalls);
        Assert.HasCount(2, client.Calls);
        Assert.AreEqual("test://second", client.Calls[0].Url);
        Assert.AreEqual(typeof(Payload), client.Calls[0].ResponseType);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Get<Payload>("test://unconfigured"));
        client.Clear();
        Assert.AreEqual(0L, client.TotalCalls);
    }

    [TestMethod]
    public async Task ExpectedFailuresAbsenceAndTypedValuesUseThePublicGuardedContract()
    {
        var client = new ScriptedRestClient();
        client.EnqueueFailure(RestFailureKind.Http, "provider.busy", "busy", 503);
        RestResult<int> failed = await client.Get<int>("test://failure");
        Assert.IsFalse(failed.Success);
        Assert.AreEqual(RestFailureKind.Http, failed.Context.FailureKind);
        client.EnqueueEmpty();
        Assert.IsFalse((await client.Head("test://empty")).Context.HasValue);
        var value = RestResult<int>.FromValue(42, new RestResultContext(200, hasValue: true));
        client.EnqueueResult(value);
        Assert.AreSame(value, await client.Get<int>("test://typed"));
        client.EnqueueResult(value, method: "POST");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Get<int>("test://wrong-method"));
        client.EnqueueResult(value);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.Get<string>("test://wrong-type"));
    }

    [TestMethod]
    public async Task PendingWorkObservesCallerCancellationButAnUnexpectedOceRemainsFaulted()
    {
        var client = new ScriptedRestClient();
        PendingRestCall pending = client.EnqueuePendingJsonValue("42");
        using var cancellation = new CancellationTokenSource();
        Task<RestResult<int>> operation = client.Get<int>("test://pending", cancellationToken: cancellation.Token);
        await pending.Entered.WaitAsync(Deadline);
        Assert.IsFalse(operation.IsCompleted);
        cancellation.Cancel();
        OperationCanceledException expected = await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline));
        Assert.AreEqual(cancellation.Token, expected.CancellationToken);
        Assert.IsTrue(operation.IsCanceled);
        var original = new OperationCanceledException("unrelated cancellation");
        client.EnqueueException(original);
        Task<RestResult<int>> fault = client.Get<int>("test://fault");
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fault));
        Assert.IsTrue(fault.IsFaulted);
    }

    [TestMethod]
    public async Task DeliberatelyIgnoringWorkRemainsPendingUntilItsActualRelease()
    {
        var client = new ScriptedRestClient();
        PendingRestCall pending = client.EnqueuePendingJsonValue("42", observeCancellation: false);
        using var cancellation = new CancellationTokenSource();
        Task<RestResult<int>> operation = client.Get<int>("test://held", cancellationToken: cancellation.Token);
        try
        {
            await pending.Entered.WaitAsync(Deadline);
            cancellation.Cancel();
            Assert.IsFalse(operation.IsCompleted);
            pending.Release();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline));
        }
        finally
        {
            pending.Release();
            try
            {
                await operation.WaitAsync(Deadline);
            }
            catch (OperationCanceledException) when (operation.IsCanceled)
            {
            }
        }
    }

    private sealed class Payload
    {
        public Payload()
        {
        }
        public int Count
        {
            get; set;
        }
    }
}
