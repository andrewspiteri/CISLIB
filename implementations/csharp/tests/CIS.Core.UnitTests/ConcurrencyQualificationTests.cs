using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConcurrencyQualificationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidOnlyConcurrentCallsKeepZeroTargetsAndFreezeLossAtEachBoundary(bool dual)
    {
        var clock = new Clock(); using var console = new Writer(holdFirstWrite: false, holdFirstFlush: true); using var file = new Writer(holdFirstWrite: false, holdFirstFlush: true);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = dual ? CoreLogLevel.Warning : CoreLogLevel.None, FilePath = dual ? "controlled" : null, QueueCapacity = 2 },
            console.WriteAsync, dual ? file.WriteAsync : null, clock, console.FlushAsync, dual ? file.FlushAsync : null);
        try
        {
            CoreLogger logger = runtime.CreateLogger("InvalidOnly");
            Task<LogFlushResult> original = runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await console.FirstFlush.Task.WaitAsync(Guard);
            if (dual) { await file.FirstFlush.Task.WaitAsync(Guard); }
            Assert.IsFalse(original.IsCompleted);
            var go = Signal(); int factories = 0;
            Task<LogWriteResult>[] callers = Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
            {
                await go.Task;
                return index % 2 == 0 ? logger.Write(CoreLogLevel.Error, new() { EventName = "", Message = "invalid" })
                    : logger.WriteLazy(CoreLogLevel.Error, () => { Interlocked.Increment(ref factories); return null!; });
            })).ToArray();
            go.TrySetResult(); LogWriteResult[] outcomes = await Task.WhenAll(callers).WaitAsync(Guard);
            Assert.IsTrue(outcomes.All(r => r.Outcome == LogWriteOutcome.InvalidEvent && r.Outputs.Count == 0));
            Assert.AreEqual(32, factories); Assert.AreEqual(64L, runtime.GetDiagnostics().InvalidEventCount);
            Assert.IsTrue(runtime.GetDiagnostics().Outputs.All(o => o.Accepted == 0 && o.AdmissionAttempts == 0 && o.Pending == 0));
            Task<LogFlushResult> afterLoss = runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.IsFalse(afterLoss.IsCompleted);
            Assert.AreEqual(1, runtime.QueuedOutputs!.Console!.RegisteredFlushCount);
            if (dual) { Assert.AreEqual(1, runtime.QueuedOutputs.File!.RegisteredFlushCount); }
            console.ReleaseAll(); file.ReleaseAll();
            LogFlushResult early = await original.WaitAsync(Guard), late = await afterLoss.WaitAsync(Guard);
            Assert.IsTrue(early.Succeeded && !early.LossObserved); Assert.AreEqual(0L, early.InvalidEventCount);
            Assert.IsTrue(late.Succeeded && late.LossObserved); Assert.AreEqual(64L, late.InvalidEventCount);
            Assert.AreEqual(dual ? 2 : 1, late.Outputs.Count);
            Assert.IsTrue(early.Outputs.Concat(late.Outputs).All(o => o.AcceptedSequenceBoundary == 0 && o.Written == 0 && o.Failed == 0 && o.Abandoned == 0 && o.Pending == 0));
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
            Assert.IsTrue(stop.Succeeded && stop.LossObserved); Assert.AreEqual(64L, stop.InvalidEventCount);
            Assert.IsTrue(stop.Outputs.All(o => o.AcceptedSequenceBoundary == 0));
            Assert.AreEqual(0, console.Writes); Assert.AreEqual(0, file.Writes); Assert.AreEqual(0L, console.Bytes.Length); Assert.AreEqual(0L, file.Bytes.Length);
            Assert.AreEqual(0L, early.InvalidEventCount); Assert.IsFalse(early.LossObserved);
        }
        finally { console.ReleaseAll(); file.ReleaseAll(); await runtime.DisposeAsync().AsTask().WaitAsync(Guard); }
    }

    [TestMethod]
    public async Task PublicPendingGaugeFallsRefillsAndSettlesWhileACapturedProducerLosesToStop()
    {
        var clock = new Clock(); using var writer = new Writer(holdSecondWrite: true);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.None, QueueCapacity = 2 }, writer.WriteAsync, null, clock, writer.FlushAsync);
        var captureEntered = Signal(); var releaseCapture = Signal(); Task<LogWriteResult>? captured = null;
        try
        {
            CoreLogger logger = runtime.CreateLogger("PendingGauge"); var pending = new List<long>();
            void Observe(long expected)
            {
                LogOutputDiagnostics output = runtime.GetDiagnostics().Outputs.Single();
                Assert.AreEqual(expected, output.Pending); Assert.AreEqual(output.Accepted, output.Written + output.Failed + output.Abandoned + output.Pending); pending.Add(output.Pending);
            }
            Accepted(logger.Write(CoreLogLevel.Information, Data("first"))); await writer.FirstWrite.Task.WaitAsync(Guard); Observe(1);
            Accepted(logger.Write(CoreLogLevel.Information, Data("second"))); Accepted(logger.Write(CoreLogLevel.Information, Data("third"))); Observe(3);
            LogOutputDiagnostics frozen = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(LogWriteOutcome.Rejected, logger.Write(CoreLogLevel.Information, Data("full")).Outcome);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { await runtime.FlushAsync(TimeSpan.FromSeconds(5), cancelled.Token); Assert.Fail("Expected cancellation."); }
                catch (OperationCanceledException error) { Assert.AreEqual(cancelled.Token, error.CancellationToken); }
                Assert.AreEqual(1, runtime.QueuedOutputs!.Console!.RegisteredFlushCount);
                Assert.AreEqual(0, QueueGauge(runtime.QueuedOutputs.Console)["attached"]!.GetValue<int>()); Assert.AreEqual(0, clock.Created);
            }
            writer.ReleaseFirst.TrySetResult(); await writer.SecondWrite.Task.WaitAsync(Guard); Observe(2);
            Accepted(logger.Write(CoreLogLevel.Information, Data("refill"))); Observe(3);
            captured = Task.Run(() => logger.WriteLazy(CoreLogLevel.Information, () =>
            { captureEntered.TrySetResult(); Require(releaseCapture.Task.Wait(Guard)); return Data("late-capture"); }));
            await captureEntered.Task.WaitAsync(Guard);
            Task<LogStopResult> shared = runtime.StopAsync(CancellationToken.None); Assert.IsTrue(runtime.GetDiagnostics().AdmissionClosed);
            releaseCapture.TrySetResult(); Assert.AreEqual(LogWriteOutcome.Stopped, (await captured.WaitAsync(Guard)).Outcome);
            int stoppedFactories = 0;
            Assert.AreEqual(LogWriteOutcome.Stopped, logger.WriteLazy(CoreLogLevel.None, () => { stoppedFactories++; return Data("never"); }).Outcome); Assert.AreEqual(0, stoppedFactories);
            writer.ReleaseAll(); LogStopResult stop = await shared.WaitAsync(Guard); Observe(0);
            Assert.IsTrue(stop.Succeeded && stop.LossObserved); Assert.AreEqual(0L, stop.InvalidEventCount);
            Assert.AreEqual(4L, stop.Outputs.Single().Written); Assert.AreEqual(4L, stop.Outputs.Single().AcceptedSequenceBoundary);
            Assert.AreEqual(1L, stop.Outputs.Single().Rejections.QueueFull); Assert.AreEqual(0L, stop.Outputs.Single().Rejections.Unavailable);
            Assert.AreEqual(3L, frozen.Pending); Assert.AreEqual(0L, frozen.Written);
            CollectionAssert.AreEqual(new long[] { 1, 3, 2, 3, 0 }, pending);
            Assert.AreSame(shared, runtime.StopAsync(CancellationToken.None)); Assert.AreSame(stop, runtime.GetDiagnostics().LastStopResult);
            Assert.AreEqual(1, writer.Maximum); Assert.AreEqual(4, writer.Writes);
        }
        finally
        {
            releaseCapture.TrySetResult(); writer.ReleaseAll();
            if (captured is not null) { await captured.WaitAsync(Guard); }
            await runtime.DisposeAsync().AsTask().WaitAsync(Guard);
        }
    }
}
