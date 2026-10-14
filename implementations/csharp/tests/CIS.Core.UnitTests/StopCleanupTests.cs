using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StopCleanupTests
{
    public static IEnumerable<object[]> Modes => StopCleanupFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task CleanupWaitsForRealIoAndPreservesErrors(string mode)
    {
        var report = await StopCleanupFixture.ExecuteAsync(mode);
        StopCleanupExpectations.Validate(mode, report);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StartupKeepsItsSafeFailureWhenWorkerCleanupThrows(bool timerFailure)
    {
        using var root = new TemporaryDirectory(); using var console = new MemoryStream();
        var options = new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.Information,
            FileLevel = CoreLogLevel.Information,
            FilePath = Path.Combine(root.Path, "active.jsonl"),
            MaxEntryBytes = 16384
        };
        int launched = 0, settled = 0;
        Task Launch(Func<Task> worker)
        {
            if (Interlocked.Increment(ref launched) == 2 && !timerFailure) { throw new IOException("launch-private"); }
            return Task.Run(async () => { await worker(); Interlocked.Increment(ref settled); throw new IOException("cleanup-private"); });
        }
        CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() =>
        {
            if (timerFailure) { _ = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, new FailingTimerClock(), _ => console, Launch); }
            else { _ = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, _ => ValueTask.CompletedTask, acquireConsole: _ => console, launchWorker: Launch); }
        });
        Assert.AreEqual("file", error.OutputName);
        Assert.AreEqual(timerFailure ? "FileFlushTimerInitializationFailed" : "WorkerInitializationFailed", error.ErrorCode);
        Assert.AreEqual(timerFailure ? 2 : 1, settled); Assert.IsTrue(console.CanWrite);
        if (timerFailure)
        {
            using var lease = new FileStream(options.FilePath + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.AreEqual(0L, new FileInfo(options.FilePath).Length);
        }
    }
    private sealed class FailingTimerClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => throw new IOException("timer-private");
    }
}
