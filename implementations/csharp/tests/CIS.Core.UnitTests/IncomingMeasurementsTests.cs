using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Observations of real implementation paths; no latency/CPU/allocation pass threshold is invented.
[TestClass]
[TestCategory("PerformanceObservation")]
[DoNotParallelize]
public sealed class IncomingMeasurementsTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Configurations =>
        from capacity in new[] { 10, 1024, LoggingOptions.MaximumQueueCapacity }
        from level in Enumerable.Range(0, 6)
        select new object[] { capacity, (CoreLogLevel)level };

    private static CapturedEventFacts Facts() => EventProjection.Capture(new EventMetadata(),
        new OperationScopeStore().Capture(), "Measurement", CoreLogLevel.Information,
        new LogEventData { EventName = "measurement", Message = "owned" }, TimeProvider.System)!;

    [TestMethod]
    [DynamicData(nameof(Configurations))]
    public async Task RecordQuietFullContendedAndCompleteCallerCosts(int capacity, CoreLogLevel level)
    {
        var rows = new List<object>();
        foreach (string mode in new[] { "quiet", "full", "contended" }) { rows.Add(await RingAsync(capacity, level, mode)); }
        rows.Add(await CallerAsync(capacity, level));
        rows.Add(await CallerAsync(capacity, level, withLegacyFactory: true));
        string path = Path.Combine(TestContext.TestResultsDirectory!, $"incoming-{capacity}-{level}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            schema = "cis.logging-frontend-measurements/v1",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            capacity,
            level = level.ToString(),
            scanProfile = "20/30/40/50/60/70; fallback 30",
            idlePollMilliseconds = 1,
            measurements = rows,
            limits = "One Windows/native process observation, not a statistical or release gate. Ring latency excludes payload capture; thread allocation includes occurrence construction. Quiet driver claims after each call; full driver pauses consumption; contended driver uses four producers and one polling consumer of the real ring. Process CPU includes driver/consumer/CLR work. Logical producer RMW counts are derived from actual admissions/evictions and audited source sites (6 per admission plus 1 per eviction), not hardware instruction counters. Complete-caller rows include owned capture with the actual runtime consumer paused; the legacy-factory row separately records its simple callback's duration within total caller latency. These are not arbitrary application callback costs. Sampling buffers belong to the harness, not production storage."
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.AddResultFile(path);
    }

    private static async Task<object> RingAsync(int capacity, CoreLogLevel level, string mode)
    {
        const int iterations = 4000;
        var ring = new IncomingRingBuffer(capacity, new LoggingOptions());
        CapturedEventFacts facts = Facts();
        for (int index = 0; index < 64; index++)
        {
            var item = new IncomingEvent(index + 1, level, facts);
            ring.TryPublish(item, out var warm); ring.TryClaim(warm.Slot, item);
        }
        if (mode != "quiet")
        { for (int index = 0; index < capacity; index++) { ring.TryPublish(new(index + 1, level, facts), out _); } }
        IncomingRingSnapshot initial = ring.Capture();
        int producers = mode == "contended" ? 4 : 1, finish = 0;
        Thread? consumer = null;
        if (mode == "contended")
        {
            consumer = new Thread(() =>
            {
                while (Volatile.Read(ref finish) == 0)
                {
                    bool found = false;
                    for (int slot = 0; slot < capacity; slot++)
                    { if (ring.Read(slot) is { } item && ring.TryClaim(slot, item)) { found = true; } }
                    if (!found) { Thread.Sleep(1); }
                }
            })
            { IsBackground = true };
            consumer.Start();
        }
        using var barrier = new Barrier(producers + 1);
        var samples = new long[producers][];
        var allocations = new long[producers];
        var maxProbes = new int[producers];
        Task[] jobs = Enumerable.Range(0, producers).Select(producer => Task.Run(() =>
        {
            samples[producer] = new long[iterations];
            Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < iterations; index++)
            {
                var item = new IncomingEvent(index + 1, level, facts);
                long began = Stopwatch.GetTimestamp();
                IncomingAdmission result = ring.TryPublish(item, out var publication);
                samples[producer][index] = Stopwatch.GetTimestamp() - began;
                if (result != IncomingAdmission.Accepted) { throw new InvalidDataException("Measurement admission failed."); }
                maxProbes[producer] = Math.Max(maxProbes[producer], publication.Probes);
                if (mode == "quiet" && !ring.TryClaim(publication.Slot, item)) { throw new InvalidDataException("Quiet claim failed."); }
            }
            allocations[producer] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        })).ToArray();
        using var process = Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime;
        long started = Stopwatch.GetTimestamp();
        try
        {
            Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { Volatile.Write(ref finish, 1); consumer?.Join(); }
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        double cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        IncomingRingSnapshot observed = ring.Capture();
        long admitted = observed.Admitted - initial.Admitted, evicted = observed.Evicted - initial.Evicted;
        long consumed = observed.Consumed - initial.Consumed;
        Assert.AreEqual((long)iterations * producers, admitted);
        Assert.IsLessThanOrEqualTo(ring.Budget(level), maxProbes.Max());
        for (int slot = 0; slot < capacity; slot++) { ring.AbandonSlot(slot); }
        return new
        {
            path = "owned-ring-publication",
            mode,
            producers,
            iterationsPerProducer = iterations,
            effectiveK = ring.Budget(level),
            maximumObservedProbes = maxProbes.Max(),
            publicationExchanges = admitted,
            sourceDerivedLogicalProducerRmw = 6 * admitted + evicted,
            admitted,
            evicted,
            consumed,
            observedConsumerDrainPerSecond = consumed / elapsed.TotalSeconds,
            elapsedMilliseconds = elapsed.TotalMilliseconds,
            processCpuMilliseconds = cpuMilliseconds,
            producerAllocatedBytes = allocations.Sum(),
            latency = Latency(samples.SelectMany(value => value).ToArray())
        };
    }

    private static async Task<object> CallerAsync(int capacity, CoreLogLevel level, bool withLegacyFactory = false)
    {
        const int iterations = 1000;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int launches = 0;
        Task Launch(Func<Task> worker)
        {
            bool input = ++launches == 2;
            return Task.Run(async () => { if (input) { await start.Task; } await worker(); });
        }
        var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = capacity, ShutdownTimeoutMilliseconds = 30000 },
            writers: [new("measurement", new Sink(), CoreLogLevel.Trace)], incomingPipeline: true, launchWorker: Launch);
        try
        {
            CoreLogger logger = runtime.CreateLogger("CompleteCaller");
            var data = new LogEventData { EventName = "measurement", Message = "owned" };
            for (int index = 0; index < capacity + 64; index++) { logger.Write(level, data); }
            var samples = new long[iterations];
            var callbackSamples = new long[iterations]; int callbacks = 0;
            LogEventData Factory()
            {
                long beganCallback = Stopwatch.GetTimestamp();
                try { return new() { EventName = "measurement", Message = "factory owned" }; }
                finally { callbackSamples[callbacks++] = Stopwatch.GetTimestamp() - beganCallback; }
            }
            Func<LogEventData> factory = Factory;
            using var process = Process.GetCurrentProcess(); TimeSpan cpu = process.TotalProcessorTime;
            long allocated = GC.GetAllocatedBytesForCurrentThread(), began = Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++)
            {
                long call = Stopwatch.GetTimestamp();
                LogWriteResult result = withLegacyFactory ? logger.WriteLazy(level, factory) : logger.Write(level, data);
                samples[index] = Stopwatch.GetTimestamp() - call;
                if (result.Outcome != LogWriteOutcome.Accepted) { throw new InvalidDataException("Caller admission failed."); }
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            double elapsed = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
            double usedCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            Assert.AreEqual(withLegacyFactory ? iterations : 0, callbacks);
            return new
            {
                path = withLegacyFactory ? "complete-caller-including-legacy-factory-and-capture" : "complete-caller-including-capture",
                mode = "full-consumer-paused",
                producers = 1,
                iterations,
                elapsedMilliseconds = elapsed,
                processCpuMilliseconds = usedCpu,
                callerAllocatedBytes = bytes,
                legacyFactoryCalls = callbacks,
                legacyFactoryLatency = withLegacyFactory ? Latency(callbackSamples) : null,
                backendDispatch = "Consumer gated throughout the measured caller window; no backend dispatch executes.",
                latency = Latency(samples)
            };
        }
        finally { start.TrySetResult(); await runtime.DisposeAsync(); await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
    }

    private static object Latency(long[] samples)
    {
        Array.Sort(samples);
        double Micros(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
        return new
        {
            units = "microseconds",
            p50 = Micros(samples[samples.Length / 2]),
            p95 = Micros(samples[(int)(samples.Length * .95)]),
            p99 = Micros(samples[(int)(samples.Length * .99)]),
            maximum = Micros(samples[^1])
        };
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(10)]
    public async Task RecordActualRuntimeIdlePollingAndQuietWriterLatency(int intervalMilliseconds)
    {
        var sink = new LatencySink();
        var options = new LoggingOptions
        {
            ConsoleLevel = CoreLogLevel.None,
            FileLevel = CoreLogLevel.None,
            IncomingPollIntervalMilliseconds = intervalMilliseconds
        };
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), options,
            writers: [new("measurement", sink)], incomingPipeline: true);
        CoreLogger logger = runtime.CreateLogger("QuietPolling");
        using var process = Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime; long idle = Stopwatch.GetTimestamp();
        await Task.Delay(200); // Measurement window, not concurrency proof.
        double idleElapsed = Stopwatch.GetElapsedTime(idle).TotalMilliseconds;
        double idleCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var samples = new long[32];
        for (int index = 0; index < samples.Length; index++)
        {
            await Task.Delay(intervalMilliseconds * 2); // Permit real idle polls between quiet samples.
            var observed = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref sink.Next, observed);
            long started = Stopwatch.GetTimestamp();
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info("quiet sample").Outcome);
            samples[index] = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10)) - started;
        }
        string path = Path.Combine(TestContext.TestResultsDirectory!, $"incoming-poll-{intervalMilliseconds}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            schema = "cis.logging-frontend-polling/v1",
            intervalMilliseconds,
            idleElapsedMilliseconds = idleElapsed,
            idleProcessCpuMilliseconds = idleCpu,
            latency = Latency(samples),
            scope = "Actual runtime: call start to custom writer callback after quiet periods; includes capture, lazy polling, backend encoding and writer scheduling. Idle process CPU includes CLR/harness work. One observation, no latency threshold or hard scheduling bound."
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.AddResultFile(path);
    }

    [TestMethod]
    public void HeldIndexAndRotatingSlotsRetainAtMostTheirExplicitOccurrenceBounds()
    {
        const int capacity = 7;
        var ring = new IncomingRingBuffer(capacity, new LoggingOptions());
        var references = new List<WeakReference<IncomingEvent>>();
        for (int index = 0; index < capacity; index++) { references.Add(PublishWeak(ring, index)); }
        var sorted = new IncomingBatchIndex(capacity); sorted.Capture(ring);
        for (int index = capacity; index < 4000; index++) { references.Add(PublishWeak(ring, index)); }
        Collect(); int retained = references.Count(reference => reference.TryGetTarget(out _));
        Assert.IsLessThanOrEqualTo(2 * capacity, retained, "Only current slots and the held old index may retain occurrences.");
        Assert.IsGreaterThan(capacity, retained, "The old index really retains displaced occurrences in addition to the ring.");
        sorted.Release(); ClearRing(ring); Collect();
        int released = references.Count(reference => reference.TryGetTarget(out _));
        Assert.AreEqual(0, released);
        string path = Path.Combine(TestContext.TestResultsDirectory!, "incoming-memory.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema = "cis.logging-frontend-memory/v1",
            capacity,
            publications = references.Count,
            retainedWithHeldIndex = retained,
            retainedAfterRelease = released,
            scope = "Forced-GC occurrence reachability: sole test producer, fixed ring and held old index; no in-flight consumer."
        }));
        TestContext.AddResultFile(path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<IncomingEvent> PublishWeak(IncomingRingBuffer ring, int number)
    {
        var item = new IncomingEvent(number + 1, CoreLogLevel.Information, Facts());
        ring.TryPublish(item, out _);
        return new(item);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearRing(IncomingRingBuffer ring)
    { for (int slot = 0; slot < ring.Capacity; slot++) { ring.AbandonSlot(slot); } }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private sealed class Sink : ILogWriter
    {
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class LatencySink : ILogWriter
    {
        internal TaskCompletionSource<long>? Next;
        public ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        { Volatile.Read(ref Next)?.TrySetResult(Stopwatch.GetTimestamp()); return ValueTask.CompletedTask; }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
