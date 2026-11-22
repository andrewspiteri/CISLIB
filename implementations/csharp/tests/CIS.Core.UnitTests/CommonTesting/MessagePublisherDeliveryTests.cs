using CIS.CommonTesting;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class MessagePublisherDeliveryTests
{
    [TestMethod]
    public async Task PublisherUsesExplicitFactsAndBoundedDetachedMetadataWithoutRetainingBodies()
    {
        await using var publisher = new ScriptedMessagePublisher(scriptCapacity: 1, recordingCapacity: 1);
        var result = new MessagePublicationResult(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient);
        publisher.EnqueueResult(result);
        Assert.ThrowsExactly<InvalidOperationException>(() => publisher.EnqueueResult(result));
        MessagePublication publication = Publication();
        Assert.AreSame(result, await publisher.PublishAsync(publication));
        ScriptedMessagePublication recorded = publisher.Calls.Single();
        Assert.AreEqual(publication.OutboxRecordId, recorded.RecordId);
        Assert.AreEqual(publication.Message.Envelope.MessageId, recorded.MessageId);
        Assert.AreEqual(publication.Message.Digest, recorded.Digest);
        Assert.AreEqual(publication.Message.Length, recorded.EncodedLength);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedMessagePublication>)publisher.Calls).Clear());
        publisher.EnqueueResult(result);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => publisher.PublishAsync(publication));
        Assert.AreEqual(1, publisher.PendingScripts, "A full recording boundary cannot consume the next script.");
    }

    [TestMethod]
    public async Task PublisherPreservesFaultedOceIdentityAndPreCancellationConsumesNoScript()
    {
        await using var publisher = new ScriptedMessagePublisher();
        using var caller = new CancellationTokenSource();
        var original = new OperationCanceledException(caller.Token);
        publisher.EnqueueException(original);
        Task<MessagePublicationResult> faulted = publisher.PublishAsync(Publication(), caller.Token);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => faulted));
        Assert.IsTrue(faulted.IsFaulted);
        publisher.EnqueueResult(new(MessagePublicationDisposition.Confirmed));
        caller.Cancel();
        Task<MessagePublicationResult> canceled = publisher.PublishAsync(Publication(), caller.Token);
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => canceled);
        Assert.AreEqual(caller.Token, observed.CancellationToken);
        Assert.IsTrue(canceled.IsCanceled);
        Assert.AreEqual(1, publisher.PendingScripts);
        Assert.AreEqual(1, publisher.Calls.Count);
    }

    [TestMethod]
    public async Task PublisherDisposalAndCallerCancellationCannotReleaseIgnoringOwnedWork()
    {
        var publisher = new ScriptedMessagePublisher();
        PendingMessagePublication pending = publisher.EnqueuePendingResult(new(MessagePublicationDisposition.Confirmed), observeCancellation: false);
        using var caller = new CancellationTokenSource();
        Task<MessagePublicationResult> publication = publisher.PublishAsync(Publication(), caller.Token);
        await pending.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        Task disposal = publisher.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(publication.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => publisher.PublishAsync(Publication()));
        }
        finally
        {
            pending.Release();
        }
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => publication);
        Assert.AreEqual(caller.Token, observed.CancellationToken);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(publication.IsCompleted);
        await publisher.DisposeAsync();
    }

    [TestMethod]
    public async Task DeliveryOwnsBoundedBytesAndMetadataButNeverProvidesTrustedAuthentication()
    {
        byte[] input = [1, 2, 3];
        var metadata = new MessageDeliveryMetadata("events", DeliveryOrdinal: 0, Generation: -1);
        await using var delivery = new ScriptedMessageDelivery(input, "A", metadata);
        input[0] = 9;
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, delivery.Body.ToArray());
        Assert.AreNotSame(metadata, delivery.Metadata);
        Assert.AreEqual(0, delivery.Metadata.DeliveryOrdinal, "Bounded invalid claimed metadata remains available for rejection tests.");
        Assert.IsNull(await delivery.GetNativeEvidenceAsync());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedMessageDelivery(new byte[16_386], "A", metadata, new(1)));
        Assert.ThrowsExactly<ArgumentException>(() => new ScriptedMessageDelivery(ReadOnlyMemory<byte>.Empty, "A", new(new string('x', 129))));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentIdenticalAckJoinsOneOperationAndDisposalWaitsForItsActualSettlement(bool cancel)
    {
        var delivery = new ScriptedMessageDelivery(new byte[] { 1 }, "A", new("events"));
        PendingMessageSettlement pending = delivery.EnqueuePending(ScriptedMessageSettlement.Acknowledge, observeCancellation: false);
        using var caller = new CancellationTokenSource();
        Task first = delivery.AcknowledgeAsync(caller.Token);
        await pending.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = delivery.AcknowledgeAsync();
        Assert.AreSame(first, second);
        if (cancel)
        {
            caller.Cancel();
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => delivery.RequeueAsync());
        Task disposal = delivery.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.IsNull(delivery.CompletedSettlement);
        }
        finally
        {
            pending.Release();
        }
        if (cancel)
        {
            OperationCanceledException canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => first);
            Assert.AreEqual(caller.Token, canceled.CancellationToken);
            Assert.IsNull(delivery.CompletedSettlement);
        }
        else
        {
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await second;
            Assert.AreEqual(ScriptedMessageSettlement.Acknowledge, delivery.CompletedSettlement);
        }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = delivery.Body);
        await delivery.DisposeAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QuarantineFactsDoNotInventAcknowledgementOrPermitAnotherSettlement(bool secured)
    {
        await using var delivery = new ScriptedMessageDelivery(ReadOnlyMemory<byte>.Empty, "A", new("events"));
        delivery.EnqueueQuarantine(secured);
        Assert.AreEqual(secured, await delivery.QuarantineMalformedAsync("fixture.malformed"));
        Assert.AreEqual(secured ? ScriptedMessageSettlement.Quarantine : (ScriptedMessageSettlement?)null, delivery.CompletedSettlement);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => delivery.AcknowledgeAsync());
        delivery.RequestConsumerStop("fixture.first");
        delivery.RequestConsumerStop("fixture.second");
        Assert.AreEqual("fixture.first", delivery.ConsumerStopCode);
        Assert.AreEqual(2, delivery.ConsumerStopRequests);
    }

    [TestMethod]
    public async Task DeliveryFaultedOceIsOriginalAndPreCanceledRequeueConsumesNoSettlement()
    {
        await using var faultedDelivery = new ScriptedMessageDelivery(new byte[] { 1 }, "A", new("events"));
        var original = new OperationCanceledException("scripted-programming-fault");
        faultedDelivery.EnqueueException(ScriptedMessageSettlement.Acknowledge, original);
        Task acknowledgement = faultedDelivery.AcknowledgeAsync();
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => acknowledgement));
        Assert.IsTrue(acknowledgement.IsFaulted);
        Assert.IsNull(faultedDelivery.CompletedSettlement);
        await using var delivery = new ScriptedMessageDelivery(new byte[] { 1 }, "A", new("events"));
        delivery.EnqueueRequeue();
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        OperationCanceledException canceled = await Assert.ThrowsAsync<OperationCanceledException>(() => delivery.RequeueAsync(caller.Token));
        Assert.AreEqual(caller.Token, canceled.CancellationToken);
        Assert.IsNull(delivery.AttemptedSettlement);
        await delivery.RequeueAsync();
        Assert.AreEqual(ScriptedMessageSettlement.Requeue, delivery.CompletedSettlement);
    }

    private static MessagePublication Publication()
    {
        EncodedMessage message = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Event, new("fixture.value", 1),
            "producer", "test", DateTimeOffset.UnixEpoch, "1"u8.ToArray()));
        return new(message, new("events"), MessageRecordPurpose.Application, 1, Guid.NewGuid());
    }
}
