using System.Collections.Concurrent;
using System.Reflection;
using CIS.Logging;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class IncomingFrontendTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EnablementNeedsOneInterestedHealthyDestinationAndIgnoresBindingOrder(bool reverse)
    {
        var information = new Writer();
        var errors = new Writer();
        LogWriterRegistration[] registrations = [new("information", information, CoreLogLevel.Information), new("errors", errors, CoreLogLevel.Error)];
        if (reverse) { Array.Reverse(registrations); }
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None },
            writers: registrations, incomingPipeline: true);
        CoreLogger logger = runtime.CreateLogger("Enablement");
        Assert.IsTrue(logger.IsEnabled(CoreLogLevel.Information));
        Assert.IsTrue(logger.IsEnabled(CoreLogLevel.Error));
        Assert.IsFalse(logger.IsEnabled(CoreLogLevel.Trace));
        Assert.IsFalse(logger.IsEnabled(CoreLogLevel.None));
        Assert.AreEqual(0, information.Lines.Count);
        Assert.AreEqual(0, errors.Lines.Count);
    }

    [TestMethod]
    public async Task EnabledPublicationDisabledAndInvalidPathsDoNotWaitForBackendAdmissionGate()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Launch(Func<Task> worker)
        {
            bool input = worker.Target is IncomingEventQueue;
            return Task.Run(async () => { if (input) { await start.Task; } await worker(); });
        }
        var writer = new Writer();
        var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = 3 },
            writers: [new("writer", writer)], incomingPipeline: true, launchWorker: Launch);
        object gate = typeof(CoreLogRuntime).GetField("admissionGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var logger = runtime.CreateLogger("Frontend");
        try
        {
            Task producer;
            lock (gate)
            {
                producer = Task.Run(() =>
                {
                    Assert.IsTrue(logger.IsEnabled(CoreLogLevel.Information));
                    Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("published").Outcome);
                    Assert.AreEqual(LogWriteOutcome.Filtered, logger.WriteLazy(CoreLogLevel.None,
                        () => throw new InvalidOperationException("must not run")).Outcome);
                    Assert.AreEqual("FactoryFailed", logger.WriteLazy(CoreLogLevel.Information,
                        () => throw new InvalidDataException("private")).ErrorCode);
                    Assert.AreEqual(LogWriteOutcome.InvalidEvent, logger.Write(CoreLogLevel.Information,
                        new LogEventData { EventName = "", Message = "invalid" }).Outcome);
                });
                Assert.IsTrue(producer.Wait(Limit), "No ordinary frontend branch can acquire the backend gate.");
            }
            await producer;
            Assert.AreEqual(2L, runtime.GetDiagnostics().InvalidEventCount);
            Assert.AreEqual(1L, runtime.GetDiagnostics().Outputs.Single().Filtered);
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task LegacyFactoryRunsOnceOnCallerAndOnlyWhenEnabled()
    {
        var writer = new Writer();
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None },
            writers: [new("writer", writer)], incomingPipeline: true);
        var logger = runtime.CreateLogger("Legacy");
        int calls = 0, caller = Environment.CurrentManagedThreadId, observed = -1;
        Assert.AreEqual(LogWriteOutcome.Accepted, logger.WriteLazy(CoreLogLevel.Information, () =>
        {
            calls++; observed = Environment.CurrentManagedThreadId;
            return new() { EventName = "legacy", Message = "captured" };
        }).Outcome);
        logger.WriteLazy(CoreLogLevel.None, () => { calls++; throw new InvalidDataException(); });
        Assert.AreEqual(1, calls); Assert.AreEqual(caller, observed);
        LogStopResult stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
        Assert.IsTrue(stopped.Succeeded); Assert.AreEqual(1, writer.Lines.Count);
        logger.WriteLazy(CoreLogLevel.Information, () => { calls++; throw new InvalidDataException(); });
        Assert.AreEqual(1, calls);
    }

    private sealed class Writer : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        { Lines.Enqueue(jsonLine); return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
