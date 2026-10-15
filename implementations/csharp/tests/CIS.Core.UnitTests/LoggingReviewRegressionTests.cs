using System.Text;
using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.MicrosoftLogger;
using CIS.Logging.Rendering;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggingReviewRegressionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static LogEventData Data() => new() { EventName = "review", Message = "" };
    private static LoggingOptions Options() => new()
    {
        ConsoleLevel = CoreLogLevel.Information,
        FileLevel = CoreLogLevel.None,
        ConsoleFormat = LogOutputFormat.JsonLines,
        QueueCapacity = 3,
        MaxEntryBytes = 16384,
        ShutdownTimeoutMilliseconds = 5000
    };

    [TestMethod]
    [DataRow(100000L, 100001L)]
    [DataRow(5L, 6L)]
    [DataRow(5L, 5L)]
    public async Task LateFlushCannotCompleteWithinBudgetWhenTimerDispatchIsWithheld(long budgetTicks, long elapsedTicks)
    {
        var clock = new WithheldClock();
        var entered = Signal(); var release = Signal();
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), Options(), _ => ValueTask.CompletedTask, null, clock,
            consoleFlush: async () => { entered.TrySetResult(); await release.Task; });
        try
        {
            runtime.CreateLogger("review").Write(CoreLogLevel.Information, Data());
            Task<LogFlushResult> pending = runtime.FlushAsync(TimeSpan.FromTicks(budgetTicks), CancellationToken.None);
            await entered.Task.WaitAsync(Guard);
            clock.AdvanceWithoutDispatch(TimeSpan.FromTicks(elapsedTicks));
            release.TrySetResult();
            LogFlushResult result = await pending.WaitAsync(Guard);
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1L, result.Outputs.Single().Written);
            Assert.AreEqual(0L, result.Outputs.Single().Pending);
            Assert.IsTrue(result.Outputs.Single().WriterFlushSucceeded);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task TimelyFlushRemainsCompletedWhenItsCallerObservesItAfterTheDeadline()
    {
        var clock = new WithheldClock();
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), Options(), _ => ValueTask.CompletedTask, null, clock,
            consoleFlush: () =>
            {
                clock.AdvanceWithoutDispatch(TimeSpan.FromTicks(4));
                // The worker samples completion at tick 4; its caller observes at tick 24.
                clock.AdvanceAfterNextRead = TimeSpan.FromTicks(20);
                return ValueTask.CompletedTask;
            });
        try
        {
            LogFlushResult result = await runtime.FlushAsync(TimeSpan.FromTicks(5), CancellationToken.None).WaitAsync(Guard);
            Assert.IsTrue(clock.GetTimestamp() >= 24);
            Assert.AreEqual(LogCompletion.Completed, result.Completion);
            Assert.IsTrue(result.Succeeded);
        }
        finally { await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task StopUsesOriginalBudgetWithoutTimerDispatchAndNeverStartsLaterWork()
    {
        var clock = new WithheldClock();
        var entered = Signal(); var release = Signal(); int writes = 0, flushes = 0;
        LoggingOptions options = Options(); options.ShutdownTimeoutMilliseconds = 10;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, async _ =>
        {
            Interlocked.Increment(ref writes); entered.TrySetResult(); await release.Task;
        }, null, clock, consoleFlush: () => { Interlocked.Increment(ref flushes); return ValueTask.CompletedTask; });
        try
        {
            CoreLogger logger = runtime.CreateLogger("review");
            logger.Write(CoreLogLevel.Information, Data()); await entered.Task.WaitAsync(Guard);
            logger.Write(CoreLogLevel.Information, Data()); logger.Write(CoreLogLevel.Information, Data());
            LogOutputDiagnostics before = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(3L, before.Pending); Assert.AreEqual(1L, before.InFlight); Assert.AreEqual(2L, before.Buffered);
            Task<LogStopResult> original = runtime.StopAsync(CancellationToken.None);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => runtime.StopAsync(cancelled.Token));
            clock.AdvanceWithoutDispatch(TimeSpan.FromMilliseconds(11));
            release.TrySetResult();
            LogStopResult result = await original.WaitAsync(Guard);
            Assert.AreEqual(LogCompletion.TimedOut, result.Completion); Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, writes); Assert.AreEqual(0, flushes);
            Assert.AreEqual(2L, result.Outputs.Single().Abandoned);
            clock.AdvanceWithoutDispatch(TimeSpan.FromSeconds(1));
            Assert.AreSame(result, await runtime.StopAsync(CancellationToken.None));
            Assert.AreEqual(1L, before.InFlight); Assert.AreEqual(2L, before.Buffered);
            AssertIdentity(runtime.GetDiagnostics().Outputs.Single());
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task InFlightGaugeSurvivesDeadlineAbandonmentAndThenFallsAfterActualIo()
    {
        var clock = new WithheldClock(); var entered = Signal(); var release = Signal();
        LoggingOptions options = Options(); options.ShutdownTimeoutMilliseconds = 10;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options, async _ =>
        { entered.TrySetResult(); await release.Task; }, null, clock, consoleFlush: () => ValueTask.CompletedTask);
        try
        {
            CoreLogger logger = runtime.CreateLogger("review");
            logger.Write(CoreLogLevel.Information, Data()); await entered.Task.WaitAsync(Guard);
            logger.Write(CoreLogLevel.Information, Data()); logger.Write(CoreLogLevel.Information, Data());
            Task<LogStopResult> stop = runtime.StopAsync(CancellationToken.None);
            clock.AdvanceWithoutDispatch(TimeSpan.FromMilliseconds(11)); clock.DispatchTimers();
            LogStopResult frozen = await stop.WaitAsync(Guard);
            LogOutputDiagnostics blocked = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(1L, blocked.InFlight); Assert.AreEqual(1L, blocked.Pending); Assert.AreEqual(0L, blocked.Buffered);
            Assert.AreEqual(2L, blocked.Abandoned); AssertIdentity(blocked);
            release.TrySetResult(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            LogOutputDiagnostics final = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(0L, final.Pending); Assert.AreEqual(0L, final.InFlight); Assert.AreEqual(0L, final.Buffered); AssertIdentity(final);
            Assert.AreEqual(1L, frozen.Outputs.Single().Pending); Assert.AreEqual(1L, blocked.InFlight);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(-17, false)]
    [DataRow(42, false)]
    [DataRow(0, true)]
    [DataRow(-17, true)]
    public async Task ProviderEventIdAndExplicitFieldsSurviveFullScopesAndByteProjection(int id, bool oversizedMessage)
    {
        using var bytes = new MemoryStream();
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(), acquireConsole: _ => bytes);
        using var provider = new CoreLoggerProvider(runtime);
        var fields = Enumerable.Range(0, 1023).ToDictionary(i => "scope" + i, i => LogValue.Integer(i));
        fields["scope0"] = LogValue.Integer(22);
        fields.Remove("scope1"); fields["event_id"] = LogValue.Integer(-999);
        try
        {
            using (runtime.BeginScope(new OperationContextData { Attributes = fields }))
            {
                ILogger logger = provider.CreateLogger("review");
                logger.Log(LogLevel.Information, new EventId(id, "id"), new Dictionary<string, object?> { ["event_id"] = 999, ["explicit"] = 7, ["scope0"] = 99 }, null,
                    (_, _) => oversizedMessage ? new string('"', 8192) : "");
            }
            await runtime.StopAsync(CancellationToken.None);
            using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
            JsonElement attrs = json.RootElement.GetProperty("attributes");
            Assert.AreEqual(id, attrs.GetProperty("event_id").GetInt32());
            if (!oversizedMessage)
            {
                Assert.AreEqual(7, attrs.GetProperty("explicit").GetInt32());
                Assert.AreEqual(99, attrs.GetProperty("scope0").GetInt32());
                Assert.AreEqual(1023, attrs.EnumerateObject().Count());
            }
            Assert.IsTrue(json.RootElement.GetProperty("truncated").GetBoolean());
            Assert.AreEqual(1L, runtime.GetDiagnostics().TruncatedEventCount);
        }
        finally { await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HumanEnvelopeFailureLeavesTheIndependentJsonlOutputHealthy(bool humanFile)
    {
        using var root = new TemporaryDirectory(); using var bytes = new MemoryStream();
        var clock = new WithheldClock();
        EventMetadata small = Metadata("x");
        CapturedEventFacts smallFacts = EventProjection.Capture(small, new OperationScopeStore().Capture(), "review", CoreLogLevel.Information, Data(), clock)!;
        int hostLength = 16384 - EventEncoder.Encode(smallFacts, 16384).JsonLine.Length + 1;
        EventMetadata metadata = Metadata(new string('x', hostLength));
        CapturedEventFacts facts = EventProjection.Capture(metadata, new OperationScopeStore().Capture(), "review", CoreLogLevel.Information, Data(), clock)!;
        EncodedEvent canonical = EventEncoder.Encode(facts, 16384);
        Assert.AreEqual(16384, canonical.JsonLine.Length);
        Assert.ThrowsExactly<EventEnvelopeTooLargeException>(() => HumanRenderer.Render(canonical, HumanOutputDetail.Verbose, 16384));
        LoggingOptions options = Options(); options.FilePath = Path.Combine(root.Path, "review.log"); options.FileLevel = CoreLogLevel.Information;
        options.HumanDetail = HumanOutputDetail.Verbose;
        options.ConsoleFormat = humanFile ? LogOutputFormat.JsonLines : LogOutputFormat.Human;
        options.FileFormat = humanFile ? LogOutputFormat.Human : LogOutputFormat.JsonLines;
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(metadata, options, clock, _ => bytes);
        try
        {
            CoreLogger logger = runtime.CreateLogger("review");
            LogWriteResult result = logger.Write(CoreLogLevel.Information, Data());
            Assert.AreEqual(LogWriteOutcome.PartiallyAccepted, result.Outcome);
            Assert.AreEqual(LogOutputWriteStatus.Faulted, result.Outputs.Single(o => o.OutputName == (humanFile ? "file" : "console")).Status);
            Assert.AreEqual(LogWriteOutcome.PartiallyAccepted, logger.Write(CoreLogLevel.Information, Data()).Outcome);
            await runtime.StopAsync(CancellationToken.None);
            Assert.AreEqual(0L, runtime.GetDiagnostics().InvalidEventCount);
            Assert.AreEqual("RenderTooLarge", runtime.GetDiagnostics().Outputs.Single(o => o.OutputName == (humanFile ? "file" : "console")).LastFaultCode);
            string[] lines = Encoding.UTF8.GetString(humanFile ? bytes.ToArray() : File.ReadAllBytes(options.FilePath)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, lines.Length);
            foreach (string line in lines) { using JsonDocument row = JsonDocument.Parse(line); Assert.AreEqual(hostLength, row.RootElement.GetProperty("resource").GetProperty("host_name").GetString()!.Length); }
        }
        finally { await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow("string")]
    [DataRow("unsupported")]
    [DataRow("unicode")]
    [DataRow("operation")]
    [DataRow("operation-guid")]
    [DataRow("operation-number")]
    [DataRow("cycle")]
    [DataRow("nonfinite")]
    public async Task InvalidBorrowedScopesRejectOnceWithoutFallingBackToOuterIdentity(string kind)
    {
        using var bytes = new MemoryStream();
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(), acquireConsole: _ => bytes);
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("review");
        var host = new LoggerExternalScopeProvider(); provider.SetScopeProvider(host);
        var cycle = new List<object?>(); cycle.Add(cycle);
        object bad = kind switch
        {
            "string" => "unsupported bare scope",
            "unsupported" => new Dictionary<string, object?> { ["bad"] = new object() },
            "unicode" => new Dictionary<string, object?> { ["bad"] = "\ud800" },
            "operation" => new OperationContextData { OperationId = "bad\0id" },
            "operation-guid" => new Dictionary<string, object?> { ["operation_id"] = Guid.NewGuid() },
            "operation-number" => new Dictionary<string, object?> { ["operation_id"] = 42 },
            "cycle" => new Dictionary<string, object?> { ["bad"] = cycle },
            _ => new Dictionary<string, object?> { ["bad"] = double.NaN }
        };
        try
        {
            Assert.Throws<ArgumentException>(() => logger.BeginScope(bad));
            using (runtime.BeginScope(new OperationContextData { OperationId = "outer" }))
            using (host.Push(bad))
            { logger.Log(LogLevel.Information, new EventId(0), "state", null, (_, _) => "invalid"); }
            Assert.AreEqual(1L, runtime.GetDiagnostics().InvalidEventCount);
            Assert.AreEqual(0L, runtime.GetDiagnostics().TruncatedEventCount);
            Assert.AreEqual(0L, runtime.GetDiagnostics().Outputs.Single().Accepted);
            logger.Log(LogLevel.Information, new EventId(0), "state", null, (_, _) => "after");
            await runtime.StopAsync(CancellationToken.None);
            using JsonDocument record = JsonDocument.Parse(bytes.ToArray());
            Assert.AreEqual("after", record.RootElement.GetProperty("message").GetString());
            Assert.IsFalse(record.RootElement.TryGetProperty("operation_id", out _));
        }
        finally { await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task TimelyTerminalRenderingFaultSettlesItsBoundaryWithoutWorkerDispatch()
    {
        var clock = new WithheldClock(); using var bytes = new MemoryStream(); var startWorker = Signal();
        CapturedEventFacts baseline = EventProjection.Capture(Metadata("x"), new OperationScopeStore().Capture(), "review", CoreLogLevel.Information, Data(), clock)!;
        EventMetadata metadata = Metadata(new string('x', 16384 - EventEncoder.Encode(baseline, 16384).JsonLine.Length + 1));
        LoggingOptions options = Options(); options.ConsoleFormat = LogOutputFormat.Human; options.HumanDetail = HumanOutputDetail.Verbose;
        Task Launch(Func<Task> work) => Task.Run(async () => { await startWorker.Task; await work(); });
        CoreLogRuntime runtime = CoreLogRuntime.CreateConsoleQueued(metadata, options, timeProvider: clock, acquireConsole: _ => bytes, launchWorker: Launch);
        try
        {
            Task<LogFlushResult> pending = runtime.FlushAsync(TimeSpan.FromTicks(5), CancellationToken.None);
            clock.AdvanceWithoutDispatch(TimeSpan.FromTicks(4));
            Assert.AreEqual(LogWriteOutcome.Rejected, runtime.CreateLogger("review").Write(CoreLogLevel.Information, Data()).Outcome);
            LogFlushResult result = await pending.WaitAsync(Guard);
            Assert.AreEqual(LogCompletion.Completed, result.Completion); Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(LogWriterFlushStatus.Failed, result.Outputs.Single().WriterFlushStatus);
            Assert.AreEqual(0L, result.Outputs.Single().Pending); Assert.AreEqual(0, bytes.Length);
            Assert.IsFalse(runtime.QueuedOutputs!.Console!.Completion.IsCompleted);
        }
        finally { startWorker.TrySetResult(); await runtime.DisposeAsync(); }
    }
    [TestMethod]
    public async Task NullBorrowedOperationIdentityIsAbsentAndInheritsTheOuterIdentity()
    {
        using var bytes = new MemoryStream();
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), Options(), acquireConsole: _ => bytes);
        using var provider = new CoreLoggerProvider(runtime);
        ILogger logger = provider.CreateLogger("review");
        var host = new LoggerExternalScopeProvider();
        provider.SetScopeProvider(host);
        try
        {
            using (runtime.BeginScope(new OperationContextData { OperationId = "outer" }))
            using (host.Push(new Dictionary<string, object?> { ["operation_id"] = null }))
            {
                logger.LogInformation("null identity");
            }

            await runtime.StopAsync(CancellationToken.None);
            Assert.AreEqual(0L, runtime.GetDiagnostics().InvalidEventCount);
            using JsonDocument record = JsonDocument.Parse(bytes.ToArray());
            Assert.AreEqual("outer", record.RootElement.GetProperty("operation_id").GetString());
            Assert.IsFalse(record.RootElement.GetProperty("attributes").TryGetProperty("operation_id", out _));
        }
        finally { await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DataRow("FileMetadataCandidateUnavailable")]
    [DataRow("FileMetadataCandidateCleanupFailed")]
    public async Task SafeMetadataFaultSurvivesALaterRenderingFault(string code)
    {
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), Options(),
            _ => throw new CoreLogInitializationException("console", code), null);
        try
        {
            runtime.CreateLogger("review").Write(CoreLogLevel.Information, Data());
            OutputEventQueue queue = runtime.QueuedOutputs!.Console!;
            await Assert.ThrowsAsync<CoreLogInitializationException>(async () => await queue.Completion.WaitAsync(Guard));
            LogOutputDiagnostics first = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(code, first.LastFaultCode);
            queue.FaultForRendering(TimeProvider.System.GetTimestamp());
            LogOutputDiagnostics later = runtime.GetDiagnostics().Outputs.Single();
            Assert.AreEqual(code, later.LastFaultCode);
            Assert.AreEqual(first.LastFaultAtUtc, later.LastFaultAtUtc);
            Assert.AreEqual(1L, later.Failed);
            AssertIdentity(later);
        }
        finally { await runtime.DisposeAsync(); }
    }
    private static EventMetadata Metadata(string host) => new("app", "1", "1", "dev", EnvironmentKind.Console,
        "11111111-1111-1111-1111-111111111111", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "linux", 42, "c", "b", host, null, null, "x64", "x64");
    private static void AssertIdentity(LogOutputDiagnostics output) => Assert.AreEqual(output.Accepted, output.Written + output.Failed + output.Abandoned + output.Pending);

    // Clock progress and timer dispatch are deliberately independent, as on an overloaded host.
    private sealed class WithheldClock : TimeProvider
    {
        private readonly ManualTimeProvider timers = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        private long timestamp;
        private long dispatched;
        internal TimeSpan AdvanceAfterNextRead { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            long result = Interlocked.Read(ref timestamp);
            TimeSpan advance = AdvanceAfterNextRead; AdvanceAfterNextRead = TimeSpan.Zero;
            if (advance > TimeSpan.Zero) { AdvanceWithoutDispatch(advance); }
            return result;
        }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => timers.CreateTimer(callback, state, dueTime, period);
        internal void AdvanceWithoutDispatch(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
        internal void DispatchTimers()
        {
            long now = Interlocked.Read(ref timestamp);
            timers.Advance(TimeSpan.FromTicks(now - dispatched)); dispatched = now;
        }
    }
}
