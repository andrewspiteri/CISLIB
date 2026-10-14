using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StopDrainTests
{
    public static IEnumerable<object[]> Modes => StopDrainFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualQueuedDrainDistinguishesAbandonmentFromUnresolvedIo(string mode)
    {
        using var root = new TemporaryDirectory();
        StopDrainExpectations.Validate(mode, await StopDrainFixture.ExecuteAsync(mode, root.Path));
    }
    [TestMethod]
    public async Task FileQueueCancellationKeepsOwnedResourceUntilItsActualWriteSettles()
    {
        object gate = new(); var clock = new Clock(); var entered = Signal(); var release = Signal();
        CancellationToken token = default; int writes = 0, flushes = 0; var resource = new OwnedResource();
        var queues = new OutputQueues(3, false, true, null, _ => ValueTask.CompletedTask, gate, fileResource: resource,
            fileFlush: () => { flushes++; return ValueTask.CompletedTask; },
            fileCancellableWrite: async (_, cancellation) => { token = cancellation; writes++; entered.TrySetResult(); await release.Task; });
        var options = new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.Information, FilePath = "controlled", MaxEntryBytes = 16384, ShutdownTimeoutMilliseconds = 10 };
        var runtime = new CoreLogRuntime(ConsoleCompositionFixture.Metadata(), options, null, queues.File, clock, gate, queues);
        try
        {
            CoreLogger logger = runtime.CreateLogger("File"); Accepted(logger.Write(CoreLogLevel.Information, Data("one")));
            await entered.Task.WaitAsync(Guard);
            Accepted(logger.Write(CoreLogLevel.Information, Data("two"))); Accepted(logger.Write(CoreLogLevel.Information, Data("three")));
            Task<LogStopResult> stop = runtime.StopAsync(CancellationToken.None); clock.Advance(TimeSpan.FromMilliseconds(10));
            LogStopResult result = await stop.WaitAsync(Guard);
            Assert.IsTrue(token.IsCancellationRequested); Assert.IsFalse(resource.Disposed);
            Assert.AreEqual(2L, result.Outputs[0].Abandoned); Assert.AreEqual(1L, result.Outputs[0].Pending);
            Assert.AreEqual(LogWriterFlushStatus.Failed, result.Outputs[0].WriterFlushStatus);
            Assert.AreEqual(0, queues.File!.RegisteredFlushCount);
            release.TrySetResult(); await queues.Completion.WaitAsync(Guard);
            Assert.IsTrue(resource.Disposed); Assert.AreEqual(1, writes); Assert.AreEqual(0, flushes);
            Assert.AreEqual(1L, result.Outputs[0].Pending); Assert.AreEqual(1L, runtime.GetDiagnostics().Outputs[0].Written);
        }
        finally { release.TrySetResult(); queues.Complete(); await queues.Completion.WaitAsync(Guard); }
    }
    private sealed class OwnedResource : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
