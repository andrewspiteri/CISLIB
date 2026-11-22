using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageResourceBudgetTests
{
    private const int ByteCapacity = 100_000;

    [TestMethod]
    public async Task IndependentRuntimeRootsShareEveryProcessCapacityAndCanReacquireReleasedSlots()
    {
        await using var first = new MessagingResourceBudget(Settings());
        await using var second = new MessagingResourceBudget(Settings());
        Assert.IsTrue(first.TryAcquireDelivery(out IDisposable? delivery));
        using (delivery)
        {
            Assert.IsFalse(second.TryAcquireDelivery(out _));
        }
        Assert.IsTrue(second.TryAcquireDelivery(out IDisposable? replacementDelivery));
        using (replacementDelivery)
        {
            Assert.IsTrue(first.TryAcquireRequest(out IDisposable? request));
            using (request)
            {
                Assert.IsFalse(second.TryAcquireRequest(out _));
            }
            Assert.IsTrue(second.TryAcquireRequest(out IDisposable? replacementRequest));
            using (replacementRequest)
            {
                Assert.IsTrue(first.TryAcquirePublication(out IDisposable? publication));
                using (publication)
                {
                    Assert.IsFalse(second.TryAcquirePublication(out _));
                }
                Assert.IsTrue(second.TryAcquirePublication(out IDisposable? replacementPublication));
                using (replacementPublication)
                {
                    Assert.IsTrue(first.TryAcquireBytes(ByteCapacity, out IDisposable? bytes));
                    using (bytes)
                    {
                        Assert.IsFalse(second.TryAcquireBytes(1, out _));
                    }
                    Assert.IsTrue(second.TryAcquireBytes(ByteCapacity, out IDisposable? replacementBytes));
                    replacementBytes!.Dispose();
                }
            }
        }
    }

    [TestMethod]
    public async Task CancellingAByteWaitDoesNotReleaseTheBufferAlreadyInUseOrLeakWaitingCapacity()
    {
        await using var first = new MessagingResourceBudget(Settings());
        await using var second = new MessagingResourceBudget(Settings());
        using IDisposable held = await first.AcquireAsync(ByteCapacity);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> waiting = second.AcquireAsync(1, cancellation.Token).AsTask();
        Assert.IsFalse(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        Assert.IsFalse(second.TryAcquireBytes(1, out _));
        held.Dispose();
        using IDisposable replacement = await second.AcquireAsync(ByteCapacity);
        Assert.IsFalse(first.TryAcquireBytes(1, out _));
    }

    [TestMethod]
    public async Task ReleasingTheActualLeaseWakesABlockedPeerWithoutLostAdmission()
    {
        await using var first = new MessagingResourceBudget(Settings());
        await using var second = new MessagingResourceBudget(Settings());
        using IDisposable held = await first.AcquireAsync(ByteCapacity);
        Task<IDisposable> waiting = second.AcquireAsync(ByteCapacity).AsTask();
        Assert.IsFalse(waiting.IsCompleted);
        held.Dispose();
        using IDisposable acquired = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(first.TryAcquireBytes(1, out _));
    }

    [TestMethod]
    public async Task DisposingAnOwnerStopsItsWaitsButDrainsHeldBuffersBeforeReleasingItsProfile()
    {
        var first = new MessagingResourceBudget(Settings());
        using IDisposable held = await first.AcquireAsync(ByteCapacity);
        Task<IDisposable> waiting = first.AcquireAsync(1).AsTask();
        Task disposing = first.DisposeAsync().AsTask();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => waiting);
        Assert.IsFalse(disposing.IsCompleted);
        Assert.IsFalse(first.TryAcquireDelivery(out _));
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
        {
            _ = first.AcquireAsync(1);
        });
        await RejectMismatchedProfileAsync(Settings(deliveries: 2));
        held.Dispose();
        await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        await first.DisposeAsync();
        await using var changedProfile = new MessagingResourceBudget(Settings(deliveries: 2));
        Assert.IsTrue(changedProfile.TryAcquireBytes(ByteCapacity, out IDisposable? replacement));
        replacement!.Dispose();
    }

    [TestMethod]
    public async Task MismatchedActiveProfilesFailBeforeAdmissionAndAnIdlePeerKeepsTheSharedProfile()
    {
        var first = new MessagingResourceBudget(Settings());
        await using var second = new MessagingResourceBudget(Settings());
        await RejectMismatchedProfileAsync(Settings(requests: 2));
        await RejectMismatchedProfileAsync(Settings(publications: 2));
        await RejectMismatchedProfileAsync(Settings(bytes: ByteCapacity + 1));
        await first.DisposeAsync();
        await RejectMismatchedProfileAsync(Settings(deliveries: 2));
        Assert.IsTrue(second.TryAcquireDelivery(out IDisposable? delivery));
        delivery!.Dispose();
    }

    [TestMethod]
    public async Task InvalidOrPrecancelledReservationsAllocateNoCapacity()
    {
        await using var budget = new MessagingResourceBudget(Settings());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => budget.TryAcquireBytes(0, out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => budget.TryAcquireBytes(ByteCapacity + 1, out _));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
        {
            _ = budget.AcquireAsync(ByteCapacity, cancelled.Token);
        });
        using IDisposable lease = await budget.AcquireAsync(ByteCapacity);
        Assert.IsFalse(budget.TryAcquireBytes(1, out _));
    }

    [TestMethod]
    public async Task WaitingAndTinyBufferLeaseObjectsHaveIndependentFiniteAdmissionBounds()
    {
        await using var budget = new MessagingResourceBudget(Settings());
        using IDisposable held = await budget.AcquireAsync(ByteCapacity);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable>[] waiting = Enumerable.Range(0, 128)
            .Select(_ => budget.AcquireAsync(1, cancellation.Token).AsTask()).ToArray();
        Assert.IsTrue(waiting.All(task => !task.IsCompleted));
        Assert.ThrowsExactly<MessagingAdmissionCapacityException>(() =>
        {
            _ = budget.AcquireAsync(1);
        });
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.WhenAll(waiting));
        held.Dispose();
        var leases = new List<IDisposable>();
        try
        {
            for (int index = 0; index < 12; index++)
            {
                Assert.IsTrue(budget.TryAcquireBytes(1, out IDisposable? lease));
                leases.Add(lease!);
            }
            Assert.IsFalse(budget.TryAcquireBytes(1, out _));
            leases[0].Dispose();
            Assert.IsTrue(budget.TryAcquireBytes(1, out IDisposable? replacement));
            replacement!.Dispose();
        }
        finally
        {
            foreach (IDisposable lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task ConcurrentRepeatedLeaseDisposalReleasesCapacityExactlyOnce()
    {
        await using var budget = new MessagingResourceBudget(Settings());
        using IDisposable held = await budget.AcquireAsync(ByteCapacity);
        Parallel.For(0, 16, _ => held.Dispose());
        using IDisposable replacement = await budget.AcquireAsync(ByteCapacity);
        Assert.IsFalse(budget.TryAcquireBytes(1, out _));
    }

    [TestMethod]
    public async Task SharedDeliverySaturationWaitsAndResumesWhenTheActualPeerLeaseSettles()
    {
        await using var first = new MessagingResourceBudget(Settings());
        await using var second = new MessagingResourceBudget(Settings());
        using IDisposable held = await first.AcquireDeliveryAsync();
        Task<IDisposable> waiting = second.AcquireDeliveryAsync().AsTask();
        Assert.IsFalse(waiting.IsCompleted);
        held.Dispose();
        using IDisposable admitted = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(first.TryAcquireDelivery(out _));
    }

    [TestMethod]
    public async Task ClosingAnOwnerCancelsItsDeliveryWaitWithoutReleasingThePeersAcceptedWork()
    {
        await using var first = new MessagingResourceBudget(Settings());
        var second = new MessagingResourceBudget(Settings());
        using IDisposable held = await first.AcquireDeliveryAsync();
        Task<IDisposable> waiting = second.AcquireDeliveryAsync().AsTask();
        Task closing = second.DisposeAsync().AsTask();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => waiting);
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(first.TryAcquireDelivery(out _));
        held.Dispose();
        using IDisposable replacement = await first.AcquireDeliveryAsync();
    }

    [TestMethod]
    public async Task RpcReplyLaneAndEncodedHeadroomRemainAvailableInsideTheSharedTotalsWhenNormalWorkIsFull()
    {
        MessagingSettings settings = Settings(deliveries: 2, bytes: 200_000);
        await using var first = new MessagingResourceBudget(settings, true);
        await using var second = new MessagingResourceBudget(settings, true);
        int reserved = 4 * settings.Limits.MaximumEnvelopeBytes;
        using IDisposable business = await first.AcquireDeliveryAsync();
        using IDisposable normal = await first.AcquireAsync(200_000 - reserved);
        Assert.IsFalse(second.TryAcquireDelivery(out _));
        Assert.IsFalse(second.TryAcquireBytes(1, out _));
        using IDisposable replyWork = await second.AcquireReplyDeliveryAsync();
        using IDisposable replyBytes = await second.AcquireReplyBytesAsync(reserved);
        using var cancel = new CancellationTokenSource();
        Task<IDisposable> waiting = first.AcquireReplyBytesAsync(1, cancel.Token).AsTask();
        Assert.IsFalse(waiting.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        Assert.ThrowsExactly<InvalidOperationException>(() => new MessagingResourceBudget(settings));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => first.TryAcquireBytes(200_000, out _));
    }

    [TestMethod]
    public void InvalidRpcProfilesFailBeforeAnyWorkCanMultiplyOrStarveReplyIngress()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingResourceBudget(Settings(), true));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagingResourceBudget(Settings(deliveries: 2), true));
    }

    private static MessagingSettings Settings(int deliveries = 1, int requests = 1, int publications = 1,
        long bytes = ByteCapacity) => new MessagingOptions
        {
            MaximumPayloadBytes = 16,
            MaximumConcurrentDeliveries = deliveries,
            MaximumOutstandingRequests = requests,
            PublisherConcurrency = publications,
            MaximumEncodedBufferBytes = bytes
        }.Capture();

    private static async Task RejectMismatchedProfileAsync(MessagingSettings settings)
    {
        MessagingResourceBudget? unexpected = null;
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => unexpected = new(settings));
        }
        finally
        {
            if (unexpected is not null)
            {
                await unexpected.DisposeAsync();
            }
        }
    }
}
