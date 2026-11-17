using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class RuntimeModuleLoggingTests
{
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public async Task FrontendReturnAndBackendPolicyMetricsAreDistinctAndPreserveRealLoggingDispositions(bool enabled, bool suppress)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var writer = new Writer();
        CoreLogRuntime runtime = Create(writer, processing: new() { Filter = _ => !suppress });
        try
        {
            if (enabled) { ((IMetricsSource)runtime).AttachMetrics(metrics); }
            CoreLogger logger = runtime.CreateLogger("PRIVATE arbitrary logger category");
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, Event("normal")).Outcome);
            Assert.AreEqual(LogWriteOutcome.Filtered, logger.Write(CoreLogLevel.Debug, Event("filtered")).Outcome);
            LogFlushResult flush = await runtime.FlushAsync(MetricsTestSupport.Deadline, CancellationToken.None);
            Assert.IsTrue(flush.Succeeded);
            Assert.AreEqual(suppress ? 0 : 1, writer.Lines.Count);
            Assert.AreEqual(suppress ? 1L : 0L, runtime.GetDiagnostics().Incoming!.Suppressed);
            Assert.AreEqual(enabled ? 2 : 0, listener.Points("cis.logging.records").Length);
            if (enabled)
            {
                Assert.AreEqual(1L, Sum(listener, "cis.logging.records", "outcome", "accepted"));
                Assert.AreEqual(1L, Sum(listener, "cis.logging.records", "outcome", "filtered"));
                MetricsTestSupport.AssertTags(listener.Points("cis.logging.processing.records").Single(), new KeyValuePair<string, object?>("outcome", suppress ? "suppressed" : "routed"));
                Assert.IsTrue(listener.Instruments.All(instrument => !instrument.Name.Contains("PRIVATE", StringComparison.Ordinal)));
            }
        }
        finally { await runtime.DisposeAsync(); await runtime.Completion.WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnActualWriterFailureRetainsItsOriginalExceptionAndOnlyItsEntryBindingRecordsTheFailure(bool lateAttach)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var entered = MetricsTestSupport.Signal(); var release = MetricsTestSupport.Signal();
        var original = new IOException("Controlled writer failure.");
        var writer = new Writer(async (_, _) => { entered.TrySetResult(); await release.Task; throw original; });
        CoreLogRuntime runtime = Create(writer);
        try
        {
            if (!lateAttach) { ((IMetricsSource)runtime).AttachMetrics(metrics); }
            Assert.AreEqual(LogWriteOutcome.Accepted, runtime.CreateLogger("Writer").Write(CoreLogLevel.Information, Event("failure")).Outcome);
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            if (lateAttach) { ((IMetricsSource)runtime).AttachMetrics(metrics); }
            release.TrySetResult();
            await runtime.StopAsync(CancellationToken.None).WaitAsync(MetricsTestSupport.Deadline);
            IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => runtime.Completion.WaitAsync(MetricsTestSupport.Deadline));
            Assert.AreSame(original, observed);
            Assert.AreEqual(1L, runtime.GetDiagnostics().Outputs.Single().Failed);
            Assert.AreEqual(lateAttach ? 0 : 1, listener.Points("cis.logging.writer.failures").Length);
        }
        finally
        {
            release.TrySetResult();
            await runtime.DisposeAsync();
            try { await runtime.Completion.WaitAsync(MetricsTestSupport.Deadline); }
            catch (IOException error) when (ReferenceEquals(error, original)) { }
        }
    }

    [TestMethod]
    public async Task RealFrontendOverwriteIsCountedAsDiscardWithoutChangingRotatingAdmissionOrInventingQueueRejection()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var release = MetricsTestSupport.Signal();
        var writer = new Writer();
        CoreLogRuntime runtime = Create(writer, incomingStart: release.Task);
        try
        {
            ((IMetricsSource)runtime).AttachMetrics(metrics);
            CoreLogger logger = runtime.CreateLogger("Overwrite");
            for (int index = 0; index < 3; index++)
            { Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, Event("event" + index)).Outcome); }
            Assert.AreEqual(2L, Sum(listener, "cis.logging.discarded.records", "reason", "overwritten"));
            release.TrySetResult();
            LogFlushResult flushed = await runtime.FlushAsync(MetricsTestSupport.Deadline, CancellationToken.None);
            Assert.IsTrue(flushed.Succeeded);
            Assert.AreEqual(3L, runtime.GetDiagnostics().Incoming!.Accepted);
            Assert.AreEqual(2L, runtime.GetDiagnostics().Incoming!.Evicted);
            Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.QueueFull);
            Assert.AreEqual(1, writer.Lines.Count);
            Assert.AreEqual(1L, Sum(listener, "cis.logging.processing.records", "outcome", "routed"));
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); await runtime.Completion.WaitAsync(MetricsTestSupport.Deadline); }
    }

    [TestMethod]
    public async Task ActualFrontendRemovalCountsShutdownLossOnlyWhenItRemovesAnOccurrence()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        var ring = new IncomingRingBuffer(1, new LoggingOptions());
        ring.AttachMetrics(new LoggingMetrics(metrics));
        CapturedEventFacts facts = EventProjection.Capture(new EventMetadata(), new OperationScopeStore().Capture(), "Removal",
            CoreLogLevel.Information, Event("removed"), TimeProvider.System)!;
        Assert.AreEqual(IncomingAdmission.Accepted, ring.TryPublish(new(1, CoreLogLevel.Information, facts), out _));
        Assert.IsNotNull(ring.AbandonSlot(0));
        Assert.IsNull(ring.AbandonSlot(0));
        Assert.AreEqual(1L, Sum(listener, "cis.logging.discarded.records", "reason", "shutdown"));
        Assert.AreEqual(0L, Sum(listener, "cis.logging.discarded.records", "reason", "overwritten"));
    }

    private static long Sum(MetricsTestListener listener, string name, string key, string value)
        => listener.Points(name).Where(point => point.Tags.Any(tag => tag.Key == key && Equals(tag.Value, value))).Sum(point => (long)point.Value);
    private static LogEventData Event(string name) => new() { EventName = name, Message = "Owned logging data." };

    private static CoreLogRuntime Create(Writer writer, Task? incomingStart = null, LogProcessingOptions? processing = null)
    {
        int launches = 0;
        Task Launch(Func<Task> work)
        {
            bool incoming = Interlocked.Increment(ref launches) == 2;
            return Task.Run(async () => { if (incoming && incomingStart is not null) { await incomingStart; } await work(); });
        }
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = 1,
            IncomingInformationScanPercentage = 100, ShutdownTimeoutMilliseconds = 5_000
        }, writers: [new("owned-writer", writer)], launchWorker: Launch, incomingPipeline: true, processing: processing);
    }

    private sealed class Writer(Func<string, CancellationToken, ValueTask>? write = null) : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public async ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        { if (write is not null) { await write(jsonLine, cancellationToken); } Lines.Enqueue(jsonLine); }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
