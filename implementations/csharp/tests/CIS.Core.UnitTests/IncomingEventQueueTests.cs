using System.Collections.Concurrent;
using System.Reflection;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingEventQueueTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static CapturedEventFacts Facts(string name, TimeProvider? clock = null) => EventProjection.Capture(new EventMetadata(),
        new OperationScopeStore().Capture(), "Incoming", CoreLogLevel.Information,
        new LogEventData { EventName = name, Message = "owned" }, clock ?? TimeProvider.System)!;
    private static RuntimeFlushBoundary Empty(long _) => new(DateTimeOffset.UtcNow, 0, 0, null, null);
    private static void Close(IncomingEventQueue queue, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        queue.Complete(new(clock.GetTimestamp(), Limit, clock.TimestampFrequency), () => { });
    }

    [TestMethod]
    public async Task BlockedBackendDoesNotPreventPublicationAndWaitingReplacement()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var committed = new ConcurrentQueue<string>();
        var queue = new IncomingEventQueue(1, new object(), TimeProvider.System, item =>
        {
            if (item.Sequence == 1) { entered.Set(); Assert.IsTrue(release.Wait(Limit)); }
            return () => committed.Enqueue(item.Facts.EventName);
        }, Empty);
        try
        {
            Assert.AreEqual(1L, queue.TryAdmit(CoreLogLevel.Information, Facts("active")));
            Assert.IsTrue(entered.Wait(Limit));
            Assert.AreEqual(2L, queue.TryAdmit(CoreLogLevel.Information, Facts("evicted")));
            Assert.AreEqual(3L, queue.TryAdmit(CoreLogLevel.Information, Facts("replacement")));
            Assert.AreEqual(1L, queue.Capture().Evicted);
            Assert.AreEqual(1L, queue.Capture().Waiting);
            release.Set(); Close(queue); await queue.Completion.WaitAsync(Limit);
            CollectionAssert.AreEqual(new[] { "active", "replacement" }, committed.ToArray());
            IncomingAccounting counts = queue.Capture();
            Assert.AreEqual(3L, counts.Accepted); Assert.AreEqual(2L, counts.Consumed);
            Assert.AreEqual(2L, counts.Processed); Assert.AreEqual(0L, counts.Pending);
            Assert.IsTrue(counts.IsExact); Assert.IsNull(queue.TryAdmit(CoreLogLevel.Information, Facts("closed")));
        }
        finally { release.Set(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task FreshBatchIncludesPreCaptureArrivalsAndNewRequestsUseTheNextBatch()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var laterEntered = new ManualResetEventSlim(); using var laterRelease = new ManualResetEventSlim();
        var committed = new List<string>(); var captures = new List<string[]>();
        var queue = new IncomingEventQueue(2, new object(), TimeProvider.System, item =>
        {
            if (item.Sequence == 1) { entered.Set(); Assert.IsTrue(release.Wait(Limit)); }
            else { laterEntered.Set(); Assert.IsTrue(laterRelease.Wait(Limit)); }
            return () => committed.Add(item.Facts.EventName);
        }, stamp => { captures.Add(committed.ToArray()); return Empty(stamp); });
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("already-owned")); Assert.IsTrue(entered.Wait(Limit));
            var boundary = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
            Assert.AreSame(boundary, queue.RegisterBoundary(TimeProvider.System.GetTimestamp()));
            queue.TryAdmit(CoreLogLevel.Information, Facts("arrived-before-capture"));
            Assert.IsNull(queue.CaptureBatch(boundary));
            release.Set(); Assert.IsTrue(laterEntered.Wait(Limit));
            Assert.AreEqual(new IncomingBatchAccounting(1, 1, 0, 0, 0), queue.CaptureBatch(boundary));
            Assert.IsFalse(boundary.Ready.Task.IsCompleted, "Runtime output boundaries follow backend dispatch.");
            var next = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
            Assert.AreNotSame(boundary, next); Assert.IsNull(queue.CaptureBatch(next));
            laterRelease.Set(); await boundary.Ready.Task.WaitAsync(Limit); await next.Ready.Task.WaitAsync(Limit);
            CollectionAssert.AreEqual(new[] { "already-owned", "arrived-before-capture" }, captures.First());
            Assert.AreEqual(new IncomingBatchAccounting(0, 0, 0, 0, 0), queue.CaptureBatch(next));
            Assert.AreEqual(2L, queue.Capture(boundary).Processed);
        }
        finally { release.Set(); laterRelease.Set(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task ExpiryLeavesActiveOwnershipUnresolvedThenAbandonsOnlyWaitingWork()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int commits = 0, drains = 0;
        var queue = new IncomingEventQueue(2, new object(), TimeProvider.System, _ =>
        { entered.Set(); Assert.IsTrue(release.Wait(Limit)); return () => commits++; }, Empty);
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("active")); Assert.IsTrue(entered.Wait(Limit));
            queue.TryAdmit(CoreLogLevel.Information, Facts("waiting"));
            var final = queue.RegisterBoundary(TimeProvider.System.GetTimestamp(), final: true);
            queue.Complete(new(TimeProvider.System.GetTimestamp(), Limit, TimeProvider.System.TimestampFrequency), () => drains++);
            queue.Abandon(TimeProvider.System.GetTimestamp());
            IncomingAccounting timed = queue.Capture();
            Assert.AreEqual(1L, timed.Consumed); Assert.AreEqual(0L, timed.Abandoned);
            Assert.AreEqual(2L, timed.Pending); Assert.IsFalse(final.Ready.Task.IsCompleted);
            release.Set(); await queue.Completion.WaitAsync(Limit);
            Assert.AreEqual(1, commits); Assert.AreEqual(1, drains);
            Assert.AreEqual(1L, queue.Capture(final).Abandoned);
            Assert.AreEqual(1L, queue.Capture(final).Processed);
            Assert.AreEqual(0L, queue.Capture(final).Pending);
            Assert.AreEqual(0L, timed.Abandoned, "A returned value observation does not mutate.");
        }
        finally { release.Set(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task FailedPreparationHasItsOwnDispositionAndDoesNotBlockFollowingEvents()
    {
        var committed = new ConcurrentQueue<string>();
        var queue = new IncomingEventQueue(2, new object(), TimeProvider.System, item =>
            item.Facts.EventName == "bad" ? throw new InvalidDataException("private") : () => committed.Enqueue(item.Facts.EventName), Empty);
        queue.TryAdmit(CoreLogLevel.Information, Facts("bad")); queue.TryAdmit(CoreLogLevel.Information, Facts("good"));
        var boundary = queue.RegisterBoundary(TimeProvider.System.GetTimestamp(), final: true);
        Close(queue); await queue.Completion.WaitAsync(Limit);
        Assert.AreEqual(2L, queue.Capture(boundary).Consumed);
        Assert.AreEqual(1L, queue.Capture(boundary).Processed); Assert.AreEqual(1L, queue.Capture(boundary).Failed);
        CollectionAssert.AreEqual(new[] { "good" }, committed.ToArray());
    }

    [TestMethod]
    public async Task OriginalMonotonicStopBudgetPreventsNewClaimsAfterActivePreparationFinishes()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); int commits = 0;
        var queue = new IncomingEventQueue(2, new object(), clock, _ =>
        { entered.Set(); Assert.IsTrue(release.Wait(Limit)); return () => commits++; }, Empty);
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("active")); Assert.IsTrue(entered.Wait(Limit));
            queue.TryAdmit(CoreLogLevel.Information, Facts("waiting"));
            var boundary = queue.RegisterBoundary(clock.GetTimestamp(), final: true);
            queue.Complete(new(clock.GetTimestamp(), TimeSpan.FromSeconds(1), clock.TimestampFrequency), () => { });
            clock.Advance(TimeSpan.FromSeconds(2)); release.Set(); await queue.Completion.WaitAsync(Limit);
            Assert.AreEqual(1, commits); Assert.AreEqual(1L, queue.Capture(boundary).Abandoned);
            Assert.AreEqual(1L, queue.Capture(boundary).Consumed);
        }
        finally { release.Set(); Close(queue, clock); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task WorkerDoesNotRetainCreatorExecutionContext()
    {
        var ambient = new AsyncLocal<string?> { Value = "caller-context" };
        string? observed = "not-run";
        var queue = new IncomingEventQueue(1, new object(), TimeProvider.System, _ =>
        { observed = ambient.Value; return () => { }; }, Empty);
        queue.TryAdmit(CoreLogLevel.Information, Facts("context")); Close(queue);
        await queue.Completion.WaitAsync(Limit); Assert.IsNull(observed); Assert.AreEqual("caller-context", ambient.Value);
    }

    [TestMethod]
    public async Task ProducerNeverAcquiresTheControlGateOrNotifiesTheConsumer()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Launch(Func<Task> worker) => Task.Run(async () => { await start.Task; await worker(); });
        var queue = new IncomingEventQueue(3, new object(), TimeProvider.System, _ => () => { }, Empty, Launch);
        object gate = typeof(IncomingEventQueue).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        try
        {
            Task<long?> producer;
            lock (gate)
            {
                producer = Task.Run(() => queue.TryAdmit(CoreLogLevel.Information, Facts("independent")));
                Assert.IsTrue(producer.Wait(Limit), "The producer cannot depend on consumer/control coordination.");
            }
            Assert.AreEqual(1L, await producer);
        }
        finally { start.TrySetResult(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task SortingUsesEventCreationTimeRatherThanPublicationTicket()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Launch(Func<Task> worker) => Task.Run(async () => { await start.Task; await worker(); });
        var order = new ConcurrentQueue<long>();
        var queue = new IncomingEventQueue(3, new object(), TimeProvider.System, item => () => order.Enqueue(item.Sequence), Empty, Launch);
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
            CapturedEventFacts older = Facts("created-first", clock);
            clock.Advance(TimeSpan.FromTicks(1)); CapturedEventFacts newer = Facts("created-second", clock);
            Assert.AreEqual(1L, queue.TryAdmit(CoreLogLevel.Information, newer));
            Assert.AreEqual(2L, queue.TryAdmit(CoreLogLevel.Information, older));
            Close(queue); start.TrySetResult(); await queue.Completion.WaitAsync(Limit);
            CollectionAssert.AreEqual(new[] { 2L, 1L }, order.ToArray());
        }
        finally { start.TrySetResult(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task WorkerClockFailureClosesAndSettlesCapturedWaitingTargetsWithoutFaultingCompletion()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Launch(Func<Task> worker) => Task.Run(async () => { await start.Task; await worker(); });
        var clock = new BrokenClock();
        var queue = new IncomingEventQueue(2, new object(), clock, _ => throw new AssertFailedException("No claim may start"), Empty, Launch);
        queue.TryAdmit(CoreLogLevel.Information, Facts("waiting"));
        IncomingFlushBoundary boundary = queue.RegisterBoundary(0);
        start.TrySetResult(); await queue.Completion.WaitAsync(Limit);
        await boundary.Ready.Task.WaitAsync(Limit);
        IncomingBoundaryObservation observed = queue.Observe(boundary);
        Assert.AreEqual("IncomingWorkerFailed", observed.Failure);
        Assert.AreEqual(new IncomingBatchAccounting(1, 0, 0, 1, 0), observed.Batch);
        Assert.AreEqual(1L, observed.Counts.Abandoned); Assert.AreEqual(0L, observed.Counts.Consumed);
        Assert.IsTrue(queue.Completion.IsCompletedSuccessfully);
        Assert.IsNull(queue.TryAdmit(CoreLogLevel.Information, Facts("closed")));
    }

    [TestMethod]
    public async Task FailedOutputBoundaryCaptureReturnsAnExplicitFailureAndConsumerKeepsProgressing()
    {
        var queue = new IncomingEventQueue(2, new object(), TimeProvider.System, _ => () => { },
            _ => throw new InvalidDataException("private boundary detail"));
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("one"));
            IncomingFlushBoundary first = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
            await first.Ready.Task.WaitAsync(Limit);
            Assert.AreEqual("IncomingFlushFailed", queue.Observe(first).Failure);
            Assert.IsNull(queue.Observe(first).Outputs);
            Assert.AreEqual(2L, queue.TryAdmit(CoreLogLevel.Information, Facts("two")));
            Close(queue); await queue.Completion.WaitAsync(Limit);
            Assert.AreEqual(2L, queue.Capture().Processed); Assert.IsTrue(queue.Completion.IsCompletedSuccessfully);
        }
        finally { Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task IncomingTicketRolloverRemainsPositiveAndDoesNotStopAdmission()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Launch(Func<Task> worker) => Task.Run(async () => { await start.Task; await worker(); });
        var queue = new IncomingEventQueue(3, new object(), TimeProvider.System, _ => () => { }, Empty, Launch);
        try
        {
            typeof(IncomingEventQueue).GetField("tickets", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue - 1);
            Assert.AreEqual(long.MaxValue, queue.TryAdmit(CoreLogLevel.Information, Facts("last")));
            Assert.AreEqual(1L, queue.TryAdmit(CoreLogLevel.Information, Facts("wrapped")));
            Assert.AreEqual(2L, queue.TryAdmit(CoreLogLevel.Information, Facts("continued")));
            Assert.AreEqual(3L, queue.Capture().Accepted);
        }
        finally { start.TrySetResult(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    private sealed class BrokenClock : TimeProvider
    {
        public override long GetTimestamp() => throw new InvalidOperationException("private clock detail");
    }
}
