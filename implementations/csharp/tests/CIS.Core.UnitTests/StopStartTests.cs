using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StopStartTests
{
    private static CoreLogRuntime Runtime(LoggingOptions options, Clock clock, Writer? console, Writer? file = null) =>
        CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, console is null ? null : console.WriteAsync, file is null ? null : file.WriteAsync, clock,
            consoleFlush: console is null ? null : console.FlushAsync, fileFlush: file is null ? null : file.FlushAsync);
    private static LoggingOptions Options(int timeout = 5000, bool console = true, bool file = false) => new()
    { ConsoleLevel = console ? CoreLogLevel.Information : CoreLogLevel.None, FileLevel = file ? CoreLogLevel.Information : CoreLogLevel.None, FilePath = file ? "controlled" : null, QueueCapacity = 3, MaxEntryBytes = 16384, ShutdownTimeoutMilliseconds = timeout };
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyStopClosesOnceAndAlreadyCancelledCallerStillInitiates(bool cancelled)
    {
        var clock = new Clock(); CoreLogRuntime runtime = Runtime(Options(console: false), clock, null);
        using var caller = new CancellationTokenSource(); if (cancelled) { caller.Cancel(); }
        Assert.IsNull(runtime.GetDiagnostics().LastStopResult);
        Task<LogStopResult> initial = runtime.StopAsync(caller.Token);
        if (cancelled)
        {
            OperationCanceledException error = await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await initial);
            Assert.AreEqual(caller.Token, error.CancellationToken);
        }
        LogStopResult result = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
        Assert.AreEqual(LogCompletion.Completed, result.Completion); Assert.IsTrue(result.Succeeded); Assert.AreEqual(0, result.Outputs.Count);
        Assert.IsTrue(runtime.GetDiagnostics().AdmissionClosed); Assert.AreSame(result, runtime.GetDiagnostics().LastStopResult);
        Assert.AreSame(result, await runtime.StopAsync(CancellationToken.None)); Assert.AreEqual(0, clock.Created);
    }
    [TestMethod]
    [DataRow(1)]
    [DataRow(5000)]
    [DataRow(300000)]
    public async Task OneOriginalDeadlineSurvivesRepeatedCallersAndOptionOrUtcChanges(int milliseconds)
    {
        var clock = new Clock(); using var writer = new Writer(); var options = Options(milliseconds);
        CoreLogRuntime runtime = Runtime(options, clock, writer);
        try
        {
            Accepted(runtime.CreateLogger("Stop").Write(CoreLogLevel.Information, Data("one"))); await writer.FirstWrite.Task.WaitAsync(Guard);
            options.ShutdownTimeoutMilliseconds = milliseconds == 1 ? 300000 : 1;
            Task<LogStopResult> first = runtime.StopAsync(CancellationToken.None);
            Assert.IsTrue(runtime.GetDiagnostics().AdmissionClosed); Assert.IsNull(runtime.GetDiagnostics().LastStopResult); Assert.AreEqual(1, clock.Created);
            options.ShutdownTimeoutMilliseconds = milliseconds == 1 ? 300000 : 1;
            Task<LogStopResult>[] joins = Enumerable.Range(0, 64).Select(_ => runtime.StopAsync(CancellationToken.None)).ToArray();
            Assert.IsTrue(joins.All(t => ReferenceEquals(t, first))); Assert.AreEqual(1, clock.Created);
            clock.ShiftUtc(-20); clock.Advance(TimeSpan.FromMilliseconds(milliseconds) - TimeSpan.FromTicks(1)); Assert.IsFalse(first.IsCompleted);
            clock.ShiftUtc(40); clock.Advance(TimeSpan.FromTicks(1));
            LogStopResult result = await first.WaitAsync(Guard); Assert.IsTrue(joins.All(t => t.IsCompletedSuccessfully));
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion); Assert.IsFalse(result.Succeeded); Assert.IsFalse(result.LossObserved);
            Assert.AreEqual(1L, result.Outputs[0].AcceptedSequenceBoundary); Assert.AreEqual(1L, result.Outputs[0].Pending); Assert.AreEqual(0L, result.Outputs[0].Written);
            Assert.AreSame(result, runtime.GetDiagnostics().LastStopResult); Assert.AreEqual(CoreLogState.Stopping, runtime.GetDiagnostics().State);
            writer.ReleaseAll(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            Assert.AreEqual(1L, runtime.GetDiagnostics().Outputs[0].Written); Assert.AreEqual(1L, result.Outputs[0].Pending);
            Assert.AreSame(result, await runtime.StopAsync(CancellationToken.None)); Assert.AreEqual(1, clock.Created);
        }
        finally { await Cleanup(runtime, writer); }
    }
    [TestMethod]
    public async Task AWrittenRecordWithBlockedFinalFlushRemainsTimedOutWithoutRewritingResult()
    {
        var clock = new Clock(); using var writer = new Writer(holdFirstWrite: false, holdFirstFlush: true);
        CoreLogRuntime runtime = Runtime(Options(10), clock, writer);
        try
        {
            Accepted(runtime.CreateLogger("Stop").Write(CoreLogLevel.Information, Data("one"))); await UntilAsync(() => runtime.QueuedOutputs!.Console!.CaptureAccounting().Written == 1);
            Task<LogStopResult> wait = runtime.StopAsync(CancellationToken.None); await writer.FirstFlush.Task.WaitAsync(Guard);
            clock.Advance(TimeSpan.FromMilliseconds(10)); LogStopResult result = await wait.WaitAsync(Guard);
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion); Assert.AreEqual(0L, result.Outputs[0].Pending); Assert.AreEqual(LogWriterFlushStatus.Pending, result.Outputs[0].WriterFlushStatus);
            writer.ReleaseAll(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            Assert.AreEqual(LogWriterFlushStatus.Pending, result.Outputs[0].WriterFlushStatus); Assert.AreSame(result, await runtime.StopAsync(CancellationToken.None));
        }
        finally { await Cleanup(runtime, writer); }
    }
    [TestMethod]
    public async Task NormalDualStopFlushesOriginalIndependentTargetsAndSharesOwnedResult()
    {
        var clock = new Clock(); using var console = new Writer(holdFirstWrite: false); using var file = new Writer(holdFirstWrite: false);
        CoreLogRuntime runtime = Runtime(Options(file: true), clock, console, file);
        try
        {
            Accepted(runtime.CreateLogger("Stop").Write(CoreLogLevel.Information, Data("one")));
            LogStopResult result = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
            Assert.AreEqual(LogCompletion.Completed, result.Completion); Assert.IsTrue(result.Succeeded); Assert.IsFalse(result.LossObserved);
            CollectionAssert.AreEqual(new[] { "console", "file" }, result.Outputs.Select(x => x.OutputName).ToArray());
            Assert.IsTrue(result.Outputs.All(o => o.AcceptedSequenceBoundary == 1 && o.Written == 1 && o.Pending == 0 && o.WriterFlushSucceeded));
            Assert.IsTrue(runtime.QueuedOutputs!.Completion.IsCompletedSuccessfully);
            Assert.AreSame(result, runtime.GetDiagnostics().LastStopResult);
            foreach (CoreLogLevel level in new[] { CoreLogLevel.None, CoreLogLevel.Information })
            {
                int factories = 0;
                LogWriteResult stopped = runtime.CreateLogger("Stop").WriteLazy(level, () => { factories++; return Data("late"); });
                Assert.AreEqual(LogWriteOutcome.Stopped, stopped.Outcome); Assert.AreEqual(0, factories);
            }
        }
        finally { await Cleanup(runtime, console, file); }
    }
    [TestMethod]
    public async Task TerminalFlushFailureReturnsActualFailedStopResult()
    {
        var clock = new Clock(); using var writer = new Writer(holdFirstWrite: false, failFirstFlush: true);
        CoreLogRuntime runtime = Runtime(Options(), clock, writer);
        try
        {
            Accepted(runtime.CreateLogger("Stop").Write(CoreLogLevel.Information, Data("one")));
            LogStopResult result = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
            Assert.AreEqual(LogCompletion.Completed, result.Completion); Assert.IsFalse(result.Succeeded); Assert.IsTrue(result.LossObserved);
            Assert.AreEqual(LogWriterFlushStatus.Failed, result.Outputs[0].WriterFlushStatus);
            Assert.AreEqual("FlushFailed", runtime.GetDiagnostics().Outputs[0].LastFaultCode);
        }
        finally { await Cleanup(runtime, writer); }
    }
    [TestMethod]
    [DataRow(0)]
    [DataRow(300001)]
    public void InvalidShutdownTimeoutIsRejectedBeforeOutputAcquisition(int milliseconds)
    {
        int acquired = 0;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(milliseconds),
            acquireConsole: _ => { acquired++; return new MemoryStream(); }));
        Assert.AreEqual(0, acquired);
    }
    [TestMethod]
    public async Task CancelledWaitDetachesFromLiveSharedStopAndPreservesInitialFacts()
    {
        var clock = new Clock(); using var writer = new Writer();
        CoreLogRuntime runtime = Runtime(Options(1000), clock, writer);
        try
        {
            CoreLogger logger = runtime.CreateLogger("Stop"); Assert.AreEqual(LogWriteOutcome.InvalidEvent, logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "" }).Outcome);
            Accepted(logger.Write(CoreLogLevel.Information, Data("one"))); await writer.FirstWrite.Task.WaitAsync(Guard);
            using var caller = new CancellationTokenSource(); caller.Cancel();
            Task<LogStopResult> cancelled = runtime.StopAsync(caller.Token);
            TaskCanceledException error = await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await cancelled); Assert.AreEqual(caller.Token, error.CancellationToken);
            Task<LogStopResult> live = runtime.StopAsync(CancellationToken.None); Assert.IsFalse(live.IsCompleted); Assert.AreEqual(1, clock.Created);
            writer.ReleaseAll(); LogStopResult result = await live.WaitAsync(Guard); Assert.AreEqual(1L, result.InvalidEventCount); Assert.IsTrue(result.Succeeded); Assert.IsTrue(result.LossObserved);
            Assert.AreSame(result, runtime.GetDiagnostics().LastStopResult);
        }
        finally { await Cleanup(runtime, writer); }
    }
}
