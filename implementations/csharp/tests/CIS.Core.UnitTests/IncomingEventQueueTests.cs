using System.Collections.Concurrent;
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
    private static CapturedEventFacts Facts(string name) => EventProjection.Capture(new EventMetadata(),
        new OperationScopeStore().Capture(), "Incoming", CoreLogLevel.Information,
        new LogEventData { EventName = name, Message = "owned" }, TimeProvider.System)!;
    private static RuntimeFlushBoundary Empty(long _) => new(DateTimeOffset.UtcNow, 0, 0, null, null);
    private static void Close(IncomingEventQueue queue, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        queue.Complete(new(clock.GetTimestamp(), Limit, clock.TimestampFrequency), () => { });
    }

    [TestMethod]
    public async Task BoundedAdmissionReturnsWhilePreparationIsBlockedAndRejectsNewWork()
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
            Assert.AreEqual(1L, queue.TryAdmit(CoreLogLevel.Information, Facts("first")));
            Assert.IsTrue(entered.Wait(Limit));
            Assert.AreEqual(2L, queue.TryAdmit(CoreLogLevel.Information, Facts("second")));
            Assert.IsNull(queue.TryAdmit(CoreLogLevel.Information, Facts("rejected")));
            Assert.AreEqual(new IncomingAccounting(2, 0, 0, 0, 2, 1), queue.Capture());
            release.Set(); Close(queue); await queue.Completion.WaitAsync(Limit);
            CollectionAssert.AreEqual(new[] { "first", "second" }, committed.ToArray());
            Assert.AreEqual(new IncomingAccounting(2, 2, 0, 0, 0, 1), queue.Capture());
            Assert.IsNull(queue.TryAdmit(CoreLogLevel.Information, Facts("closed")));
        }
        finally { release.Set(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task FlushControlCapturesDownstreamBeforeLaterInputAndCoalescesPendingTargets()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var laterEntered = new ManualResetEventSlim(); using var laterRelease = new ManualResetEventSlim();
        var committed = new List<string>(); var captures = new List<string[]>();
        var gate = new object();
        var queue = new IncomingEventQueue(2, gate, TimeProvider.System, item =>
        {
            if (item.Sequence == 1) { entered.Set(); Assert.IsTrue(release.Wait(Limit)); }
            else { laterEntered.Set(); Assert.IsTrue(laterRelease.Wait(Limit)); }
            return () => committed.Add(item.Facts.EventName);
        }, stamp => { captures.Add(committed.ToArray()); return Empty(stamp); });
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("covered")); Assert.IsTrue(entered.Wait(Limit));
            var boundary = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
            Assert.AreSame(boundary, queue.RegisterBoundary(TimeProvider.System.GetTimestamp()));
            queue.TryAdmit(CoreLogLevel.Information, Facts("later"));
            Assert.IsFalse(boundary.Ready.Task.IsCompleted);
            release.Set(); await boundary.Ready.Task.WaitAsync(Limit);
            Assert.IsTrue(laterEntered.Wait(Limit));
            Assert.AreEqual(new IncomingAccounting(1, 1, 0, 0, 0, 0), queue.Capture(boundary));
            CollectionAssert.AreEqual(new[] { "covered" }, captures.Single());
            laterRelease.Set(); Close(queue); await queue.Completion.WaitAsync(Limit);
            Assert.AreEqual(1L, queue.Capture(boundary).Processed, "Later input cannot change the captured prefix.");
        }
        finally { release.Set(); laterRelease.Set(); Close(queue); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task ExpiryDiscardsActivePreparationAndQueuedRecordsWithoutLateOutputAdmission()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int commits = 0, drains = 0;
        var queue = new IncomingEventQueue(2, new object(), TimeProvider.System, _ =>
        { entered.Set(); Assert.IsTrue(release.Wait(Limit)); return () => commits++; }, Empty);
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("active")); Assert.IsTrue(entered.Wait(Limit));
            var first = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
            queue.TryAdmit(CoreLogLevel.Information, Facts("buffered"));
            var final = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
            queue.Complete(new(TimeProvider.System.GetTimestamp(), Limit, TimeProvider.System.TimestampFrequency), () => drains++);
            queue.Abandon(TimeProvider.System.GetTimestamp());
            await final.Ready.Task.WaitAsync(Limit);
            Assert.AreEqual(new IncomingAccounting(1, 0, 0, 1, 0, 0), queue.Capture(first));
            Assert.AreEqual(new IncomingAccounting(2, 0, 0, 2, 0, 0), queue.Capture(final));
            Assert.AreEqual(1, drains); Assert.IsFalse(queue.Completion.IsCompleted);
            release.Set(); await queue.Completion.WaitAsync(Limit);
            Assert.AreEqual(0, commits); Assert.AreEqual(1, drains);
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
        var boundary = queue.RegisterBoundary(TimeProvider.System.GetTimestamp());
        Close(queue); await queue.Completion.WaitAsync(Limit);
        Assert.AreEqual(new IncomingAccounting(2, 1, 1, 0, 0, 0), queue.Capture(boundary));
        CollectionAssert.AreEqual(new[] { "good" }, committed.ToArray());
    }

    [TestMethod]
    public async Task WorkerChecksOriginalMonotonicBudgetBeforeCommittingPreparedEvent()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); int commits = 0;
        var queue = new IncomingEventQueue(1, new object(), clock, _ =>
        { entered.Set(); Assert.IsTrue(release.Wait(Limit)); return () => commits++; }, Empty);
        try
        {
            queue.TryAdmit(CoreLogLevel.Information, Facts("late")); Assert.IsTrue(entered.Wait(Limit));
            var boundary = queue.RegisterBoundary(clock.GetTimestamp());
            queue.Complete(new(clock.GetTimestamp(), TimeSpan.FromSeconds(1), clock.TimestampFrequency), () => { });
            clock.Advance(TimeSpan.FromSeconds(2)); release.Set(); await queue.Completion.WaitAsync(Limit);
            Assert.AreEqual(0, commits); Assert.AreEqual(1L, queue.Capture(boundary).Abandoned);
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
}
