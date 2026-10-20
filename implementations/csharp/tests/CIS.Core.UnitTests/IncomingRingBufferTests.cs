using System.Reflection;
using CIS.Configuration;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingRingBufferTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static IncomingEvent Event(int number, CoreLogLevel level = CoreLogLevel.Information, int? created = null)
    {
        var facts = EventProjection.Capture(new EventMetadata(), new OperationScopeStore().Capture(), "Ring", level,
            new LogEventData { EventName = "event" + number, Message = "owned" }, TimeProvider.System)!;
        var owned = new CapturedEventFacts(DateTimeOffset.UnixEpoch.AddTicks(created ?? number), facts.TraceId,
            facts.SpanId, facts.Status, facts.EventName, facts.Message, facts.MessageTemplate, facts.OperationId,
            facts.Service, facts.Environment, facts.Version, facts.Component, facts.Resource, facts.Attributes, facts.Exception, false);
        return new(number, level, owned);
    }

    private static LoggingOptions FullScan() => new()
    {
        IncomingScanPercentage = 100,
        IncomingTraceScanPercentage = null,
        IncomingDebugScanPercentage = null,
        IncomingInformationScanPercentage = null,
        IncomingWarningScanPercentage = null,
        IncomingErrorScanPercentage = null,
        IncomingCriticalScanPercentage = null
    };

    private static IncomingPublication Publish(IncomingRingBuffer ring, IncomingEvent item)
    {
        Assert.AreEqual(IncomingAdmission.Accepted, ring.TryPublish(item, out var publication));
        Assert.IsGreaterThanOrEqualTo(1, publication.Probes);
        Assert.IsLessThanOrEqualTo(ring.Budget(item.Level), publication.Probes);
        return publication;
    }

    private static void Seed(object owner, string field, long value) => owner.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(7)]
    public void RepeatedWrapPublishesEveryEventAndAccountsForActualRemovals(int capacity)
    {
        var ring = new IncomingRingBuffer(capacity, FullScan());
        for (int index = 1; index <= 2000; index++) { Publish(ring, Event(index)); }
        IncomingRingSnapshot counts = ring.Capture();
        Assert.AreEqual(2000L, counts.Admitted);
        Assert.AreEqual(2000L - capacity, counts.Evicted);
        Assert.AreEqual((long)capacity, counts.Waiting);
        Assert.AreEqual(0L, counts.ActivePublishers);
        for (int index = 0; index < capacity; index++) { ring.AbandonSlot(index); }
        counts = ring.Capture();
        Assert.AreEqual(counts.Admitted, counts.Consumed + counts.Evicted + counts.Abandoned + counts.Waiting);
        Assert.AreEqual(0L, counts.Waiting);
    }

    [TestMethod]
    public void DefaultProfileFallbackCeilingAndStartupCaptureAreExplicit()
    {
        var settings = new LoggingOptions();
        var ring = new IncomingRingBuffer(10, settings);
        CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6, 7 }, Enumerable.Range(0, 6).Select(level => ring.Budget((CoreLogLevel)level)).ToArray());
        settings.IncomingDebugScanPercentage = null; settings.IncomingScanPercentage = 41;
        var changed = new IncomingRingBuffer(3, settings);
        Assert.AreEqual(2, changed.Budget(CoreLogLevel.Debug));
        Assert.AreEqual(3, ring.Budget(CoreLogLevel.Debug), "Existing ring retains its startup budgets.");
        var smallest = new IncomingRingBuffer(1, FullScan());
        Assert.AreEqual(1, smallest.Budget(CoreLogLevel.Trace));
    }

    [TestMethod]
    public void SearchStopsAtObservedNullAndNeverExtendsBeyondItsWindow()
    {
        var ring = new IncomingRingBuffer(10, new LoggingOptions());
        Assert.AreEqual(1, Publish(ring, Event(1)).Probes);
        for (int index = 0; index < 3; index++) { ring.Exchange(index, Event(index + 2, CoreLogLevel.Critical), 1); }
        Seed(ring, "cursor", -1);
        IncomingPublication result = Publish(ring, Event(20, CoreLogLevel.Debug));
        Assert.AreEqual(3, result.Probes);
        Assert.IsNotNull(result.Displaced, "A free slot outside K does not extend the scan.");
        Assert.AreEqual(CoreLogLevel.Critical, result.Displaced.Level);
    }

    [TestMethod]
    public void StableP01UsesLowestSeverityThenEventCreationTime()
    {
        var ring = new IncomingRingBuffer(3, FullScan());
        IncomingEvent oldTrace = Event(1, CoreLogLevel.Trace, created: 1);
        IncomingEvent newerTrace = Event(2, CoreLogLevel.Trace, created: 20);
        Publish(ring, Event(3, CoreLogLevel.Error, created: 0));
        Publish(ring, newerTrace); Publish(ring, oldTrace);
        Assert.AreSame(oldTrace, Publish(ring, Event(4, CoreLogLevel.Error)).Displaced);
        Assert.AreSame(newerTrace, Publish(ring, Event(5, CoreLogLevel.Trace)).Displaced);
        Assert.AreEqual(2L, ring.EvictedAt(CoreLogLevel.Trace));
    }

    [TestMethod]
    public void AllHigherPrioritiesStillAdmitByLowestInspectedSeverityAndOldestCreation()
    {
        var ring = new IncomingRingBuffer(3, FullScan());
        Publish(ring, Event(1, CoreLogLevel.Critical, created: 0));
        IncomingEvent oldError = Event(2, CoreLogLevel.Error, created: 1);
        Publish(ring, oldError); Publish(ring, Event(3, CoreLogLevel.Error, created: 2));
        Assert.AreSame(oldError, Publish(ring, Event(4, CoreLogLevel.Trace)).Displaced);
        Assert.AreEqual(4L, ring.Capture().Admitted);
    }

    [TestMethod]
    public void FinalExchangeCountsActualReplacementAndStaleClaimLeavesNewOccupant()
    {
        var ring = new IncomingRingBuffer(1, FullScan());
        IncomingEvent initial = Event(1, CoreLogLevel.Trace), raced = Event(2, CoreLogLevel.Critical), incoming = Event(3);
        Publish(ring, initial);
        int selected = ring.SelectSlot(incoming, out int probes);
        Publish(ring, raced);
        IncomingPublication actual = ring.Exchange(selected, incoming, probes);
        Assert.AreSame(raced, actual.Displaced);
        Assert.AreEqual(1L, ring.EvictedAt(CoreLogLevel.Critical));
        Assert.IsFalse(ring.TryClaim(0, initial));
        Assert.IsFalse(ring.TryClaim(0, raced));
        Assert.AreSame(incoming, ring.Read(0));
        Assert.IsTrue(ring.TryClaim(0, incoming));
        Assert.IsFalse(ring.TryClaim(0, incoming));
        Assert.AreEqual(new IncomingRingSnapshot(3, 1, 2, 0, 0, 0, false), ring.Capture());
    }

    [TestMethod]
    public void SelectedNullMayBecomeOccupiedBeforeExchangeAndClaimMayWinFirst()
    {
        var ring = new IncomingRingBuffer(1, FullScan());
        IncomingEvent first = Event(1), second = Event(2), third = Event(3);
        int slot = ring.SelectSlot(second, out int probes);
        Publish(ring, first);
        Assert.AreSame(first, ring.Exchange(slot, second, probes).Displaced);
        slot = ring.SelectSlot(third, out probes);
        Assert.IsTrue(ring.TryClaim(slot, second));
        Assert.IsNull(ring.Exchange(slot, third, probes).Displaced);
        Assert.AreEqual(1L, ring.Capture().Evicted);
        Assert.AreEqual(1L, ring.Capture().Consumed);
    }

    [TestMethod]
    public async Task EnrolledPublisherMayFinishAfterCloseWhileOtherProducersKeepProgressing()
    {
        var ring = new IncomingRingBuffer(3, FullScan());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Task paused = Task.Run(() =>
        {
            Assert.IsTrue(ring.TryEnterPublisher());
            try
            {
                IncomingEvent item = Event(1);
                int slot = ring.SelectSlot(item, out int probes);
                entered.Set(); Assert.IsTrue(release.Wait(Limit));
                ring.Exchange(slot, item, probes);
            }
            finally { ring.ExitPublisher(); }
        });
        try
        {
            Assert.IsTrue(entered.Wait(Limit));
            for (int index = 2; index <= 100; index++) { Publish(ring, Event(index)); }
            ring.Close();
            Assert.AreEqual(1L, ring.ActivePublishers);
            Assert.AreEqual(IncomingAdmission.Stopped, ring.TryPublish(Event(101), out _));
            Assert.IsFalse(ring.TryEnterPublisher());
        }
        finally { release.Set(); await paused.WaitAsync(Limit); }
        Assert.AreEqual(100L, ring.Capture().Admitted);
        Assert.AreEqual(0L, ring.ActivePublishers);
        for (int index = 0; index < ring.Capacity; index++) { ring.AbandonSlot(index); }
        Assert.AreEqual(100L, ring.Capture().Evicted + ring.Capture().Abandoned);
    }

    [TestMethod]
    public async Task CompetingPublishersAndConsumerHaveExactlyOneRemovalOwnerPerOccurrence()
    {
        const int producers = 4, eventsPerProducer = 1500;
        var ring = new IncomingRingBuffer(7, new LoggingOptions());
        var removed = new System.Collections.Concurrent.ConcurrentDictionary<IncomingEvent, byte>(ReferenceEqualityComparer.Instance);
        int done = 0;
        using var start = new Barrier(producers + 1);
        Task[] workers = Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
        {
            try
            {
                Assert.IsTrue(start.SignalAndWait(Limit));
                for (int index = 1; index <= eventsPerProducer; index++)
                {
                    IncomingPublication result = Publish(ring, Event(producer * eventsPerProducer + index, (CoreLogLevel)(index % 6)));
                    if (result.Displaced is { } old) { Assert.IsTrue(removed.TryAdd(old, 0)); }
                }
            }
            finally { Interlocked.Increment(ref done); }
        })).ToArray();
        Assert.IsTrue(start.SignalAndWait(Limit));
        while (Volatile.Read(ref done) != producers)
        {
            for (int slot = 0; slot < ring.Capacity; slot++)
            { if (ring.Read(slot) is { } item && ring.TryClaim(slot, item)) { Assert.IsTrue(removed.TryAdd(item, 0)); } }
            await Task.Yield();
        }
        await Task.WhenAll(workers).WaitAsync(Limit);
        for (int slot = 0; slot < ring.Capacity; slot++)
        { if (ring.AbandonSlot(slot) is { } item) { Assert.IsTrue(removed.TryAdd(item, 0)); } }
        IncomingRingSnapshot counts = ring.Capture();
        Assert.AreEqual((long)producers * eventsPerProducer, counts.Admitted);
        Assert.AreEqual(counts.Admitted, counts.Consumed + counts.Evicted + counts.Abandoned);
        Assert.AreEqual(producers * eventsPerProducer, removed.Count);
        Assert.AreEqual(0L, counts.Waiting);
    }

    [TestMethod]
    [DataRow(long.MaxValue - 1)]
    [DataRow(-2L)]
    public void CursorRolloverNeverChangesEventAgeOrProducesInvalidIndex(long seed)
    {
        var ring = new IncomingRingBuffer(3, FullScan());
        Seed(ring, "cursor", seed);
        for (int index = 1; index < 30; index++)
        {
            IncomingEvent item = Event(index, created: 30 - index);
            long key = item.CreationTimestamp;
            IncomingPublication result = Publish(ring, item);
            Assert.IsTrue(result.Slot is >= 0 and < 3);
            Assert.AreEqual(key, item.CreationTimestamp);
        }
    }

    [TestMethod]
    public void CounterSaturationIsStickyAndDoesNotWrapToInventedSmallCounts()
    {
        var counter = new AtomicLogCounter();
        Seed(counter, "value", long.MaxValue - 1);
        counter.Increment(); counter.Increment(); counter.Increment();
        Assert.AreEqual(long.MaxValue, counter.Value); Assert.IsTrue(counter.IsSaturated);
        Seed(counter, "value", -1); counter.Increment(); counter.Increment();
        Assert.AreEqual(long.MaxValue, counter.Value); Assert.IsTrue(counter.IsSaturated);
    }

    [TestMethod]
    public void SaturatedAdmissionCounterNeverRejectsAndRemainsExplicit()
    {
        var ring = new IncomingRingBuffer(1, FullScan());
        object counter = typeof(IncomingRingBuffer).GetField("admitted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ring)!;
        Seed(counter, "value", long.MaxValue - 1);
        Publish(ring, Event(1)); Publish(ring, Event(2)); Publish(ring, Event(3));
        Assert.AreEqual(long.MaxValue, ring.Capture().Admitted);
        Assert.IsTrue(ring.Capture().IsSaturated);
        Assert.AreEqual(2L, ring.Capture().Evicted);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(101)]
    public void InvalidPercentagesFailBeforeWorkerOrSlotCreation(int percentage)
    {
        var settings = FullScan(); settings.IncomingScanPercentage = percentage;
        Assert.ThrowsExactly<CoreConfigurationException>(() => new IncomingRingBuffer(3, settings));
        settings = FullScan(); settings.IncomingTraceScanPercentage = percentage;
        Assert.ThrowsExactly<CoreConfigurationException>(() => new IncomingRingBuffer(3, settings));
        settings = FullScan(); settings.IncomingPollIntervalMilliseconds = 0;
        Assert.ThrowsExactly<CoreConfigurationException>(() => new IncomingRingBuffer(3, settings));
    }

    [TestMethod]
    public void InvalidEventDoesNotEnrollOrInventAdmission()
    {
        var ring = new IncomingRingBuffer(1, FullScan());
        Assert.AreEqual(IncomingAdmission.Invalid, ring.TryPublish(null, out _));
        Assert.AreEqual(IncomingAdmission.Invalid, ring.TryPublish(Event(1) with { Level = CoreLogLevel.None }, out _));
        Assert.AreEqual(0L, ring.Capture().Admitted); Assert.AreEqual(0L, ring.ActivePublishers);
    }

    [TestMethod]
    public void MaximumCapacityIsInclusiveAndKeepsRoundedBudgets()
    {
        var ring = new IncomingRingBuffer(65536, new LoggingOptions());
        Assert.AreEqual(65536, ring.Capacity);
        Assert.AreEqual(13108, ring.Budget(CoreLogLevel.Trace));
        Assert.AreEqual(45876, ring.Budget(CoreLogLevel.Critical));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(65537)]
    public void InvalidCapacityHasAnExplicitParameterFailure(int capacity)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IncomingRingBuffer(capacity, new LoggingOptions()));
        Assert.AreEqual("capacity", error.ParamName);
    }

    [TestMethod]
    public void MissingOptionsHasAnExplicitParameterFailure()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() => new IncomingRingBuffer(1, null!));
        Assert.AreEqual("options", error.ParamName);
    }

    [TestMethod]
    public void ConditionalAbandonmentAccountsOnlyTheActualExpectedOwner()
    {
        var ring = new IncomingRingBuffer(1, FullScan());
        IncomingEvent expected = Event(1), stale = Event(2);
        var published = Publish(ring, expected);
        Assert.IsFalse(ring.TryAbandon(published.Slot, stale));
        Assert.AreSame(expected, ring.Read(published.Slot));
        Assert.AreEqual(0L, ring.Capture().Abandoned); Assert.AreEqual(1L, ring.Capture().Waiting);
        Assert.IsTrue(ring.TryAbandon(published.Slot, expected));
        Assert.IsNull(ring.Read(published.Slot));
        Assert.AreEqual(new IncomingRingSnapshot(1, 0, 0, 1, 0, 0, false), ring.Capture());
        Assert.IsFalse(ring.TryAbandon(published.Slot, expected));
        Assert.AreEqual(new IncomingRingSnapshot(1, 0, 0, 1, 0, 0, false), ring.Capture());
    }
}
