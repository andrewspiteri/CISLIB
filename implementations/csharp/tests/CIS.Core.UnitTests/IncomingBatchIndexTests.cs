using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingBatchIndexTests
{
    private static IncomingEvent Event(int number, int created) => new(number, CoreLogLevel.Information,
        new CapturedEventFacts(DateTimeOffset.UnixEpoch.AddTicks(created), null, null, "Information", "event" + number,
            "owned", null, null, "service", "test", "1", "Index", LogValue.Null(), LogValue.Null(), null, false));

    [TestMethod]
    public void CapturedCreationOrderDoesNotCompactSlotsAndSkipsNewerReplacement()
    {
        var ring = new IncomingRingBuffer(3, new LoggingOptions());
        IncomingEvent late = Event(1, 30), early = Event(2, 10), middle = Event(3, 20), replacement = Event(4, 40);
        ring.TryPublish(late, out var first); ring.TryPublish(early, out var second); ring.TryPublish(middle, out var third);
        var index = new IncomingBatchIndex(3); index.Capture(ring);
        Assert.AreEqual(3, index.Count);
        Assert.AreSame(early, index.At(0).Item); Assert.AreSame(middle, index.At(1).Item); Assert.AreSame(late, index.At(2).Item);
        Assert.AreSame(late, ring.Read(first.Slot)); Assert.AreSame(early, ring.Read(second.Slot)); Assert.AreSame(middle, ring.Read(third.Slot));
        ring.Exchange(second.Slot, replacement, 1);
        Assert.IsFalse(ring.TryClaim(index.At(0).Slot, index.At(0).Item!)); index.Clear(0);
        Assert.IsNull(index.At(0).Item);
        Assert.IsTrue(ring.TryClaim(index.At(1).Slot, index.At(1).Item!)); index.Clear(1);
        Assert.IsTrue(ring.TryClaim(index.At(2).Slot, index.At(2).Item!)); index.Clear(2);
        Assert.AreSame(replacement, ring.Read(second.Slot));
        index.Capture(ring);
        Assert.AreEqual(1, index.Count); Assert.AreSame(replacement, index.At(0).Item);
        index.Release(); Assert.AreEqual(0, index.Count); Assert.IsNull(index.At(0).Item);
    }

    [TestMethod]
    public void DelayedOlderCreatedEventBelongsToLaterCaptureWithoutGapWait()
    {
        var ring = new IncomingRingBuffer(3, new LoggingOptions());
        IncomingEvent delayed = Event(1, 1), published = Event(2, 2);
        ring.TryPublish(published, out _);
        var index = new IncomingBatchIndex(3); index.Capture(ring);
        ring.TryPublish(delayed, out _);
        Assert.AreEqual(1, index.Count);
        Assert.IsTrue(ring.TryClaim(index.At(0).Slot, index.At(0).Item!));
        index.Capture(ring);
        Assert.AreEqual(1, index.Count); Assert.AreSame(delayed, index.At(0).Item);
    }

    [TestMethod]
    public void CreationTimestampExtremesCompareWithoutCounterOrSignedWrapOrdering()
    {
        IncomingEvent At(int number, DateTimeOffset timestamp) => new(number, CoreLogLevel.Information,
            new CapturedEventFacts(timestamp, null, null, "Information", "event" + number,
                "owned", null, null, "service", "test", "1", "Index", LogValue.Null(), LogValue.Null(), null, false));
        var ring = new IncomingRingBuffer(3, new LoggingOptions());
        IncomingEvent maximum = At(1, DateTimeOffset.MaxValue), minimum = At(2, DateTimeOffset.MinValue), middle = At(3, DateTimeOffset.UnixEpoch);
        ring.TryPublish(maximum, out _); ring.TryPublish(minimum, out _); ring.TryPublish(middle, out _);
        var sorted = new IncomingBatchIndex(3); sorted.Capture(ring);
        Assert.AreSame(minimum, sorted.At(0).Item); Assert.AreSame(middle, sorted.At(1).Item); Assert.AreSame(maximum, sorted.At(2).Item);
        Assert.AreEqual(0L, minimum.CreationTimestamp); Assert.AreEqual(DateTimeOffset.MaxValue.UtcTicks, maximum.CreationTimestamp);
    }

    [TestMethod]
    public void EqualCreationTimesUsePhysicalSlotOrderBeyondSmallSorts()
    {
        var ring = new IncomingRingBuffer(64, new LoggingOptions());
        for (int slot = 0; slot < 64; slot++) { ring.Exchange(slot, Event(slot, 42), 1); }
        var index = new IncomingBatchIndex(64); index.Capture(ring);
        Assert.AreEqual(64, index.Count);
        for (int slot = 0; slot < 64; slot++)
        { Assert.AreEqual(slot, index.At(slot).Slot); Assert.AreSame(ring.Read(slot), index.At(slot).Item); }
    }
}
