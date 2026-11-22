using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageDispatchPressureTests
{
    [TestMethod]
    public async Task MinimumRpcBudgetAdmitsInputAndResponseTogetherAndContinuesAfterPressure()
    {
        await using var fixture = new DispatchUnitFixture(requests: true);
        fixture.Transactions.Enqueue();
        fixture.Transactions.Enqueue();
        fixture.Transactions.Enqueue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OnHandle = async (_, _, _) =>
        {
            if (fixture.HandlerCalls == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
        };
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        EncodedMessage firstMessage = fixture.Message();
        var firstDelivery = fixture.Delivery(firstMessage.CopyBytes());
        Task<MessageDispatchResult> first = fixture.Dispatcher.DispatchAsync(firstDelivery, guard.Token);
        using var secondMemory = new ObservedMessageMemory(fixture.Message().CopyBytes());
        var secondDelivery = fixture.Delivery(secondMemory.Memory);
        Task<MessageDispatchResult>? second = null;
        try
        {
            await FirstHandlerEnteredAsync(first, entered.Task, guard.Token);
            second = fixture.Dispatcher.DispatchAsync(secondDelivery, guard.Token);
            Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual(0, secondMemory.SpanReads, "The waiting request must own neither decoded input nor a partial byte reservation.");
            Assert.AreEqual(0, secondDelivery.Stops);
            int normalCapacity = NormalCapacity(fixture);
            int unoccupied = normalCapacity - checked(4 * firstMessage.Length + 4 * fixture.Snapshot.Settings.Limits.MaximumEnvelopeBytes);
            Assert.IsTrue(fixture.Resources.TryAcquireBytes(unoccupied, out IDisposable? remainder));
            remainder!.Dispose();
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(MessageDispatchOutcome.Handled, (await first.WaitAsync(guard.Token)).Outcome);
        Assert.AreEqual(MessageDispatchOutcome.Handled, (await second!.WaitAsync(guard.Token)).Outcome);
        Assert.AreEqual(1, firstDelivery.Acknowledgements);
        Assert.AreEqual(1, secondDelivery.Acknowledgements);
        Assert.AreEqual(0, firstDelivery.Stops + secondDelivery.Stops);
        var third = fixture.Delivery(fixture.Message().CopyBytes());
        Assert.AreEqual(MessageDispatchOutcome.Handled,
            (await fixture.Dispatcher.DispatchAsync(third, guard.Token)).Outcome);
        Assert.AreEqual(1, third.Acknowledgements);
        Assert.AreEqual(3, fixture.Replies!.Staged);
        Assert.IsTrue(fixture.Resources.TryAcquireBytes(NormalCapacity(fixture), out IDisposable? recovered));
        recovered!.Dispose();
    }

    [TestMethod]
    public async Task CancellingACombinedAdmissionJoinsNativeRequeueAndShutdownWithoutReleasingTheActiveRequest()
    {
        await using var fixture = new DispatchUnitFixture(requests: true);
        fixture.Transactions.Enqueue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requeueEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequeue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.OnHandle = async (_, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        Task<MessageDispatchResult> first = fixture.Dispatcher.DispatchAsync(fixture.Delivery(fixture.Message().CopyBytes()), guard.Token);
        using var memory = new ObservedMessageMemory(fixture.Message().CopyBytes());
        var waitingDelivery = fixture.Delivery(memory.Memory);
        waitingDelivery.OnRequeue = async token =>
        {
            Assert.IsFalse(token.IsCancellationRequested);
            requeueEntered.TrySetResult();
            await releaseRequeue.Task;
        };
        Task<MessageDispatchResult>? waiting = null;
        Task? stopping = null;
        try
        {
            await FirstHandlerEnteredAsync(first, entered.Task, guard.Token);
            waiting = fixture.Dispatcher.DispatchAsync(waitingDelivery, cancellation.Token);
            Assert.IsFalse(waiting.IsCompleted);
            cancellation.Cancel();
            await requeueEntered.Task.WaitAsync(guard.Token);
            stopping = fixture.Dispatcher.StopAsync();
            Assert.IsFalse(stopping.IsCompleted);
            Assert.IsFalse(waiting.IsCompleted, "Actual native requeue remains owned until it settles.");
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(0, memory.SpanReads);
            Assert.AreEqual(0, waitingDelivery.Stops);
            Assert.IsFalse(fixture.Resources.TryAcquireBytes(NormalCapacity(fixture), out _));
        }
        finally
        {
            release.TrySetResult();
            releaseRequeue.TrySetResult();
        }
        Assert.AreEqual(MessageDispatchOutcome.Cancelled, (await waiting!.WaitAsync(guard.Token)).Outcome);
        Assert.AreEqual(MessageDispatchOutcome.Cancelled, (await first.WaitAsync(guard.Token)).Outcome);
        await stopping!.WaitAsync(guard.Token);
        Assert.AreEqual(1, waitingDelivery.Requeues);
        Assert.AreEqual(0, waitingDelivery.Acknowledgements);
        Assert.IsTrue(fixture.Resources.TryAcquireBytes(NormalCapacity(fixture), out IDisposable? recovered));
        recovered!.Dispose();
    }

    private static int NormalCapacity(DispatchUnitFixture fixture) => checked((int)fixture.Snapshot.Settings.MaximumEncodedBufferBytes
        - 4 * fixture.Snapshot.Settings.Limits.MaximumEnvelopeBytes);

    private static async Task FirstHandlerEnteredAsync(Task<MessageDispatchResult> dispatch, Task entered, CancellationToken token)
    {
        Task first = await Task.WhenAny(entered, dispatch).WaitAsync(token);
        if (ReferenceEquals(first, dispatch))
        {
            MessageDispatchResult result = await dispatch;
            Assert.Fail($"The first request completed before handler entry: {result.Outcome}/{result.SafeCode}.");
        }
        await entered.WaitAsync(token);
    }
}
