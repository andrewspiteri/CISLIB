using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Small reproducible observations, not statistical benchmarks or release performance gates.
[TestClass]
[TestCategory("PerformanceObservation")]
[DoNotParallelize]
public sealed class LoggingReviewMeasurements
{
    public TestContext TestContext { get; set; } = null!;
    private const int Iterations = 1000;
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task ObserveRealRuntimeAllocationsThroughputAndBoundedQueueRetention()
    {
        var rows = new List<object>();
        foreach (string mode in new[] { "disabled", "jsonl", "scoped-jsonl", "human-normal", "human-verbose", "dual-human", "large-attributes", "exception", "rotation" })
        { rows.Add(await RuntimeAsync(mode)); }
        foreach ((int capacity, int entryLimit) in new[] { (128, 16384), (1024, 65536) })
        { rows.Add(await SaturationAsync(capacity, entryLimit)); }
        string path = Path.Combine(TestContext.TestResultsDirectory!, "logging-review-measurements.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            schema = "cis.logging-review-measurements/v1",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            iterations = Iterations,
            measurements = rows,
            limits = "One process/run; total managed allocations include workers. Warm-up is 64 records per runtime mode. Throughput includes batch flush and physical file I/O where enabled. Retention is a forced-GC managed-heap delta while one writer is held. No statistical or production-memory guarantee."
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.AddResultFile(path);
    }

    private static async Task<object> RuntimeAsync(string mode)
    {
        using var root = new TemporaryDirectory(); using var stream = new MemoryStream();
        bool disabled = mode == "disabled", file = mode is "dual-human" or "rotation";
        var options = new LoggingOptions
        {
            ConsoleLevel = disabled || mode == "rotation" ? CoreLogLevel.None : CoreLogLevel.Information,
            FileLevel = file ? CoreLogLevel.Information : CoreLogLevel.None,
            FilePath = file ? Path.Combine(root.Path, "measure.log") : null,
            ConsoleFormat = mode.Contains("human", StringComparison.Ordinal) ? LogOutputFormat.Human : LogOutputFormat.JsonLines,
            FileFormat = mode == "dual-human" ? LogOutputFormat.Human : LogOutputFormat.JsonLines,
            HumanDetail = mode == "human-normal" ? HumanOutputDetail.Normal : HumanOutputDetail.Verbose,
            FileMaxBytes = mode == "rotation" ? 65536 : 10485760,
            FileRetainedFiles = 2,
            QueueCapacity = 128,
            MaxEntryBytes = 65536
        };
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, clock, _ => stream);
        CoreLogger logger = runtime.CreateLogger("measure");
        var data = new LogEventData { EventName = "measure", Message = mode == "rotation" ? new string('m', 8192) : "hello" };
        if (mode == "large-attributes") { data.Attributes = Enumerable.Range(0, 100).ToDictionary(i => "field" + i, _ => LogValue.String(new string('x', 256))); }
        if (mode == "exception") { data.Exception = new InvalidOperationException("outer", new ArgumentException("inner")); }
        IDisposable? scope = mode == "scoped-jsonl" ? runtime.BeginScope(new OperationContextData
        { OperationId = "operation", Attributes = new Dictionary<string, LogValue> { ["owner"] = LogValue.String("scope") } }) : null;
        async Task WriteAsync(int count)
        {
            for (int i = 0; i < count; i++)
            {
                LogWriteResult result = logger.Write(CoreLogLevel.Information, data);
                Assert.AreEqual(disabled ? LogWriteOutcome.Filtered : LogWriteOutcome.Accepted, result.Outcome);
                if (i % 64 == 63) { Assert.IsTrue((await runtime.FlushAsync(Guard, CancellationToken.None)).Succeeded); }
            }
            Assert.IsTrue((await runtime.FlushAsync(Guard, CancellationToken.None)).Succeeded);
        }
        try
        {
            await WriteAsync(64); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long allocated = GC.GetTotalAllocatedBytes(precise: true); var watch = Stopwatch.StartNew();
            await WriteAsync(Iterations); watch.Stop(); allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            Assert.IsTrue((await runtime.StopAsync(CancellationToken.None)).Succeeded);
            return new
            {
                mode,
                records = Iterations,
                milliseconds = watch.Elapsed.TotalMilliseconds,
                recordsPerSecond = Iterations / watch.Elapsed.TotalSeconds,
                allocatedBytes = allocated,
                allocatedBytesPerRecord = (double)allocated / Iterations,
                rotations = mode == "rotation" ? Directory.EnumerateFiles(root.Path, "*.closed").Count() : 0
            };
        }
        finally { scope?.Dispose(); await runtime.DisposeAsync(); }
    }

    private static async Task<object> SaturationAsync(int capacity, int entryLimit)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var options = new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.Information,
            FileLevel = CoreLogLevel.None,
            QueueCapacity = capacity,
            MaxEntryBytes = entryLimit
        };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, async _ =>
        { entered.TrySetResult(); await release.Task; }, null, clock, consoleFlush: () => ValueTask.CompletedTask);
        var data = new LogEventData
        {
            EventName = "saturation",
            Message = new string('\0', 8192),
            Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('\0', 8192)) }
        };
        int recordBytes = EventEncoder.Encode(EventProjection.Capture(ConsoleCompositionFixture.Metadata(), new OperationScopeStore().Capture(), "measure", CoreLogLevel.Information, data, clock)!, entryLimit).JsonLine.Length;
        try
        {
            CoreLogger logger = runtime.CreateLogger("measure");
            long before = GC.GetTotalMemory(forceFullCollection: true);
            long allocated = GC.GetTotalAllocatedBytes(precise: true); var watch = Stopwatch.StartNew();
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, data).Outcome);
            await entered.Task.WaitAsync(Guard);
            for (int i = 0; i < capacity; i++) { Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, data).Outcome); }
            Assert.AreEqual(LogWriteOutcome.Rejected, logger.Write(CoreLogLevel.Information, data).Outcome);
            watch.Stop(); allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            long retained = GC.GetTotalMemory(forceFullCollection: true) - before;
            LogOutputDiagnostics counts = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(capacity + 1L, counts.Pending); Assert.AreEqual(1L, counts.InFlight); Assert.AreEqual(capacity, counts.Buffered);
            return new
            {
                mode = "saturation",
                capacity,
                entryLimit,
                actualRecordBytes = recordBytes,
                pending = counts.Pending,
                payloadBytes = (long)recordBytes * counts.Pending,
                retainedManagedHeapDeltaBytes = retained,
                allocatedBytes = allocated,
                milliseconds = watch.Elapsed.TotalMilliseconds,
                configuredPayloadCeilingBytes = (long)capacity * entryLimit
            };
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); }
    }
}
