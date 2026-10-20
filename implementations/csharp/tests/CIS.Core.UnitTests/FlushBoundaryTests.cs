using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FlushBoundaryTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod]
    public async Task AFlushOnlyCancellableOutputReceivesAndObservesIoCancellation()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelled = Signal(), release = Signal();
        async ValueTask Flush(CancellationToken token)
        {
            using CancellationTokenRegistration registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult(token);
            await release.Task;
        }
        var queue = new OutputEventQueue(1, _ => ValueTask.CompletedTask, cancellableFlush: Flush);
        try
        {
            OutputFlushBoundary boundary = queue.RegisterFlushBoundary();
            CancellationToken token = await entered.Task.WaitAsync(Limit);
            Assert.IsTrue(token.CanBeCanceled);
            Assert.IsFalse(token.IsCancellationRequested);
            queue.RequestIoCancellation();
            await cancelled.Task.WaitAsync(Limit);
            Assert.IsTrue(token.IsCancellationRequested);
            release.TrySetResult();
            Assert.IsTrue(await boundary.Registration.WriterFlushCompletion.WaitAsync(Limit));
        }
        finally
        {
            release.TrySetResult();
            queue.Complete();
            await queue.Completion.WaitAsync(Limit);
        }
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("both")]
    [DataRow("saturated")]
    [DataRow("faulted")]
    [DataRow("closed")]
    [DataRow("concurrent")]
    [DataRow("physical")]
    public async Task InitialFactsAndNondroppableTargetsRemainFixedThroughActualWriterWork(string mode)
    { using var root = new TemporaryDirectory(); FlushBoundaryExpectations.Validate(mode, await FlushBoundaryFixture.ExecuteAsync(mode, root.Path)); }

    [TestMethod]
    public void AdmissionDoublesCannotInventRegisteredQueueFacts()
    {
        var runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None }, null, null);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.RegisterFlushBoundary());
    }

    [TestMethod]
    public async Task DisabledOutputsHaveNoTargetOrCallbackAndDoNotReadTheEventClock()
    {
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None }, null, null, new UnavailableClock());
        try
        {
            RuntimeFlushBoundary boundary = runtime.RegisterFlushBoundary();
            Assert.IsNull(boundary.Console); Assert.IsNull(boundary.File); Assert.AreEqual(0L, boundary.InvalidEventCount); Assert.AreEqual(0L, boundary.TruncatedEventCount);
            Assert.AreEqual(TimeSpan.Zero, boundary.BoundaryCapturedAtUtc.Offset);
            Assert.IsFalse(runtime.GetDiagnostics().AdmissionClosed);
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task AnUnspecifiedTrustedFlushCallbackDoesNotInventBufferConfirmation()
    {
        var queue = new OutputEventQueue(1, _ => ValueTask.CompletedTask);
        try { Assert.IsFalse(await queue.RegisterFlushBoundary().Registration.WriterFlushCompletion.WaitAsync(Limit)); Assert.AreEqual(0, queue.RegisteredFlushCount); }
        finally { queue.Complete(); await queue.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task ABlockedZeroBoundaryFlushPrecedesLaterWritesAndRunsOutsideAdmission()
    {
        var entered = Signal(); var release = Signal(); int writes = 0; CoreLogRuntime? runtime = null;
        using var bytes = new MemoryStream();
        runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions(), async item =>
        { await bytes.WriteAsync(item.Event.JsonLine); Interlocked.Increment(ref writes); }, null, consoleFlush: async () =>
        {
            await Task.Run(() => runtime!.GetDiagnostics()).WaitAsync(Limit);
            entered.TrySetResult(); await release.Task; await bytes.FlushAsync();
        });
        try
        {
            RuntimeFlushBoundary boundary = runtime.RegisterFlushBoundary(); await entered.Task.WaitAsync(Limit);
            Assert.AreEqual(0L, boundary.Console!.Value.AcceptedSequenceBoundary);
            Assert.AreEqual(LogWriteOutcome.Accepted, runtime.CreateLogger("Later").Write(CoreLogLevel.Information, Data()).Outcome);
            Assert.AreEqual(0, Volatile.Read(ref writes)); Assert.IsFalse(boundary.Console.Value.Registration.WriterFlushCompletion.IsCompleted);
            release.TrySetResult(); Assert.IsTrue(await boundary.Console.Value.Registration.WriterFlushCompletion.WaitAsync(Limit));
            runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); Assert.AreEqual(1, writes);
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
    }

    [TestMethod]
    public async Task FlushFailureDoesNotInventAnEventWriteFailure()
    {
        var entered = Signal(); var release = Signal(); using var bytes = new MemoryStream();
        var original = new IOException("private flush detail");
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions(), async item => await bytes.WriteAsync(item.Event.JsonLine), null,
            consoleFlush: async () => { entered.TrySetResult(); await release.Task; throw original; });
        try
        {
            runtime.CreateLogger("FlushFailure").Write(CoreLogLevel.Warning, Data()); RuntimeFlushBoundary boundary = runtime.RegisterFlushBoundary(); await entered.Task.WaitAsync(Limit);
            runtime.CreateLogger("Later").Write(CoreLogLevel.Warning, Data()); release.TrySetResult();
            Assert.IsFalse(await boundary.Console!.Value.Registration.WriterFlushCompletion.WaitAsync(Limit));
            IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Completion.WaitAsync(Limit)); Assert.AreSame(original, observed);
            OutputAccounting state = runtime.CaptureAccounting().Console!.Value;
            Assert.AreEqual(1L, state.Written); Assert.AreEqual(0L, state.Failed); Assert.AreEqual(1L, state.Abandoned); Assert.AreEqual("FlushFailed", state.LastFaultCode);
            Assert.AreEqual(0, runtime.QueuedOutputs!.Console!.RegisteredFlushCount);
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException error) when (ReferenceEquals(error, original)) { } }
    }

    [TestMethod]
    public async Task RegistrationDuringOwnedCleanupIsUnavailableAndCannotBeStranded()
    {
        var resource = new HeldCleanup(); int flushes = 0;
        var queue = new OutputEventQueue(1, _ => ValueTask.CompletedTask, ownedResource: resource, flush: () => { flushes++; return ValueTask.CompletedTask; });
        try
        {
            queue.Complete(); await resource.Entered.Task.WaitAsync(Limit);
            Assert.IsFalse(await queue.RegisterFlushBoundary().Registration.WriterFlushCompletion.WaitAsync(Limit));
            Assert.AreEqual(0, queue.RegisteredFlushCount); Assert.AreEqual(0, flushes);
        }
        finally { resource.Release.Set(); queue.Complete(); await queue.Completion.WaitAsync(Limit); resource.Release.Dispose(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnAlreadyStartedFlushSettlesByItsActualIoAfterAdmissionExhaustion(bool failFlush)
    {
        var entered = Signal(); var release = Signal(); using var bytes = new MemoryStream();
        var original = new IOException("private late flush failure");
        var queue = new OutputEventQueue(1, _ => ValueTask.CompletedTask, flush: async () => { entered.TrySetResult(); await release.Task; await bytes.FlushAsync(); if (failFlush) { throw original; } });
        foreach (string field in new[] { "lastAcceptedSequence", "acceptedCount", "writtenCount" }) { typeof(OutputEventQueue).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue); }
        try
        {
            OutputFlushBoundary first = queue.RegisterFlushBoundary(); await entered.Task.WaitAsync(Limit);
            Assert.AreEqual(LogOutputWriteStatus.Faulted, queue.TryAdmit(new EncodedEvent([], false), out long? sequence)); Assert.IsNull(sequence);
            OutputFlushBoundary second = queue.RegisterFlushBoundary(); Assert.AreSame(first.Registration, second.Registration); Assert.AreEqual(0L, first.Unavailable); Assert.AreEqual(1L, second.Unavailable);
            Assert.IsFalse(first.Registration.WriterFlushCompletion.IsCompleted); Assert.AreEqual(1, queue.RegisteredFlushCount);
            release.TrySetResult(); Assert.AreEqual(!failFlush, await first.Registration.WriterFlushCompletion.WaitAsync(Limit));
            if (failFlush) { Assert.AreSame(original, await Assert.ThrowsExactlyAsync<IOException>(() => queue.Completion.WaitAsync(Limit))); }
            else { await queue.Completion.WaitAsync(Limit); }
            Assert.AreEqual(0, queue.RegisteredFlushCount);
            OutputAccounting state = queue.CaptureAccounting(); Assert.AreEqual(0L, state.Failed);
            Assert.AreEqual(failFlush ? "FlushFailed" : "AdmissionExhausted", state.LastFaultCode);
        }
        finally { release.TrySetResult(); queue.Complete(); try { await queue.Completion.WaitAsync(Limit); } catch (IOException error) when (failFlush && ReferenceEquals(error, original)) { } }
    }
    private sealed class UnavailableClock : TimeProvider { public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Event clock must not be invoked."); }
    private sealed class HeldCleanup : IDisposable
    {
        internal readonly TaskCompletionSource Entered = Signal();
        internal readonly ManualResetEventSlim Release = new();
        public void Dispose() { Entered.TrySetResult(); if (!Release.Wait(Limit)) { throw new TimeoutException("Owned test cleanup was not released."); } }
    }
    private static LogEventData Data() => new() { EventName = "event", Message = "record" };
}
