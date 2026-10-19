using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class InterpolationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task CompilerSelectsHandlersForEverySeverityAndRetainsExistingCallShapes()
    {
        var writer = new Writer();
        await using var runtime = Create(writer);
        var log = runtime.CreateLogger("Interpolation");
        var values = new Dictionary<string, LogValue> { ["OrderId"] = LogValue.Integer(42) };
        int id = 42;
        log.Trace($"trace {id}"); log.Debug($"debug {id}"); log.Info($"info {id}");
        log.Warn($"warn {id}"); log.Error($"error {id}"); log.Critical($"critical {id}");
        log.Info($"Order {id} accepted", values);
        log.Info("order.accepted", $"Order {id} accepted", values);
        log.Info("named", "ordinary");
        string alreadyFormatted = $"ordinary {id}"; log.Info(alreadyFormatted);
        Func<string, LogWriteResult> oldGroup = log.Info; oldGroup("method group");
        Assert.AreEqual(LogWriteOutcome.InvalidEvent, log.Info("named", null!).Outcome);
        log.Write(CoreLogLevel.Information, $"general {id}");
        log.Write(CoreLogLevel.Information, "general.named", $"general {id}", values);
        log.Info($"constant", values);
        log.Info("named.interpolation", $"named {id}");
        System.Linq.Expressions.Expression<Func<LogWriteResult>> expression = () => log.Info((string)$"expression {id}");
        expression.Compile()();
        await Drain(runtime);
        JsonElement[] records = writer.Records;
        Assert.AreEqual(16, records.Length);
        for (int index = 0; index < 6; index++)
        {
            Assert.AreEqual(new[] { "trace", "debug", "info", "warn", "error", "critical" }[index], records[index].GetProperty("status").GetString());
            StringAssert.Contains(records[index].GetProperty("message_template").GetString()!, "{0}");
        }
        Assert.AreEqual("Order 42 accepted", records[6].GetProperty("message").GetString());
        Assert.AreEqual(42, records[6].GetProperty("attributes").GetProperty("OrderId").GetInt32());
        Assert.AreEqual("order.accepted", records[7].GetProperty("event_name").GetString());
        Assert.IsFalse(records[8].TryGetProperty("message_template", out _));
        Assert.IsFalse(records[9].TryGetProperty("message_template", out _));
        Assert.AreEqual("named.interpolation", records[14].GetProperty("event_name").GetString());
        Assert.AreEqual("named {0}", records[14].GetProperty("message_template").GetString());
        Assert.AreEqual("expression 42", records[15].GetProperty("message").GetString());
        Assert.IsFalse(records[15].TryGetProperty("message_template", out _));
    }

    [TestMethod]
    public async Task StandardFormattingAlignmentBracesNullAndRawStringsUseInvariantCulture()
    {
        var writer = new Writer(); await using var runtime = Create(writer, consumerCulture: CultureInfo.GetCultureInfo("fr-FR"));
        var logger = runtime.CreateLogger("Formatting");
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            decimal amount = 1234.5m; int id = 42; string? empty = null;
            var date = new DateTime(2026, 10, 19, 13, 14, 15, DateTimeKind.Utc);
            logger.Info($"{{order}} {id:X4}|{id,5}|{id,-5}|{amount:N2}|{date:yyyy-MM-dd}|{empty}|{true}");
            logger.Info($$"""{"id": {{id}}, "amount": "{{amount:F1}}"}""");
        }
        finally { CultureInfo.CurrentCulture = before; }
        await Drain(runtime);
        Assert.AreEqual("{order} 002A|   42|42   |1,234.50|2026-10-19||True", Message(writer, 0));
        Assert.AreEqual("{\"id\": 42, \"amount\": \"1234.5\"}", Message(writer, 1));
    }

    [TestMethod]
    public async Task UserFormattingRunsOnceOnCallerAndOnlyOwnedTextSurvivesPublication()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        var logger = runtime.CreateLogger("Owned");
        var value = new MutableValue { Text = "original" };
        int caller = Environment.CurrentManagedThreadId;
        LogWriteResult result = logger.Info($"object {value:custom}");
        Assert.AreEqual(LogWriteOutcome.Accepted, result.Outcome);
        Assert.AreEqual(1, value.Calls); Assert.AreEqual(caller, value.Thread);
        value.Text = "changed";
        start.SetResult(); await Drain(runtime);
        Assert.AreEqual("object original/custom", Message(writer, 0)); Assert.AreEqual(1, value.Calls);
    }

    [TestMethod]
    public async Task RecoverableCaptureFailureReturnsSafeResultWithoutRetainingException()
    {
        var writer = new Writer(); await using var runtime = Create(writer);
        var logger = runtime.CreateLogger("Failure");
        var result = logger.Info($"value {new ThrowingValue()}");
        Assert.AreEqual(LogWriteOutcome.InvalidEvent, result.Outcome);
        Assert.AreEqual("InterpolationCaptureFailed", result.ErrorCode);
        logger.Info($"next {42}"); await Drain(runtime);
        Assert.AreEqual(1, writer.Records.Length); Assert.AreEqual(1L, runtime.GetDiagnostics().InvalidEventCount);
    }

    [TestMethod]
    public async Task NativeFormatFailureOccursInConsumerAndDoesNotFaultLaterEventsOrWriters()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        var logger = runtime.CreateLogger("NativeFailure"); int id = 42;
        Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info($"bad {id:Q}").Outcome);
        Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info($"good {id:D4}").Outcome);
        Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.Failed);
        start.SetResult(); LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
        Assert.IsTrue(stop.LossObserved); Assert.AreEqual(1L, stop.Incoming!.Failed);
        Assert.AreEqual("good 0042", Message(writer, 0));
        await using var outputOnly = CoreLogRuntime.CreateQueued(new(), new LoggingOptions
        { ConsoleLevel = CoreLogLevel.Information, FileLevel = CoreLogLevel.None }, _ => ValueTask.CompletedTask, null);
        LogWriteResult invalid = outputOnly.CreateLogger("InternalFormatFailure").Info($"bad {id:Q}");
        Assert.AreEqual(LogWriteOutcome.InvalidEvent, invalid.Outcome);
        Assert.AreEqual("InterpolationFormatFailed", invalid.ErrorCode);
        Assert.AreEqual(1L, outputOnly.GetDiagnostics().InvalidEventCount);
        Assert.IsNull(outputOnly.GetDiagnostics().Incoming);
    }

    [TestMethod]
    public async Task SpanAndCopiedHandlersCannotMutateAlreadyPublishedOccurrences()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        var logger = runtime.CreateLogger("Freeze");
        CaptureSpan(logger);
        var handler = new LogInterpolatedStringHandler(0, 0); handler.AppendLiteral("first");
        logger.Info(ref handler); handler.AppendLiteral(" second"); logger.Info(ref handler);
        start.SetResult(); await Drain(runtime);
        CollectionAssert.AreEquivalent(new[] { "span original", "first", "first second" }, writer.Records.Select(row => row.GetProperty("message").GetString()).ToArray());
    }

    private static void CaptureSpan(CoreLogger logger)
    {
        Span<char> text = stackalloc char[] { 'o', 'r', 'i', 'g', 'i', 'n', 'a', 'l' };
        logger.Info($"span {text}"); text.Fill('x');
    }

    [TestMethod]
    public async Task RichEventCopiesAttributesOperationExceptionScopeAndActivityAtTheCall()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        var logger = runtime.CreateLogger("Context");
        var attributes = new Dictionary<string, LogValue> { ["id"] = LogValue.Integer(42) };
        using var activity = new System.Diagnostics.Activity("test").SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start();
        string trace = activity.TraceId.ToHexString();
        using (runtime.BeginScope(new OperationContextData { OperationId = "scope", Attributes = new Dictionary<string, LogValue> { ["scope"] = LogValue.Boolean(true) } }))
        {
            logger.Write(CoreLogLevel.Error, $"order {42}", context: new LogEventData
            { EventName = "order.retry", Attributes = attributes, Exception = new InvalidOperationException("failure"), OperationId = "operation" });
        }
        attributes["id"] = LogValue.Integer(99); activity.Stop(); start.SetResult(); await Drain(runtime);
        JsonElement row = writer.Records.Single();
        Assert.AreEqual("order.retry", row.GetProperty("event_name").GetString());
        Assert.AreEqual("operation", row.GetProperty("operation_id").GetString());
        Assert.AreEqual(trace, row.GetProperty("trace_id").GetString());
        Assert.AreEqual(42, row.GetProperty("attributes").GetProperty("id").GetInt32());
        Assert.IsTrue(row.GetProperty("attributes").GetProperty("scope").GetBoolean());
        Assert.AreEqual("failure", row.GetProperty("exception").GetProperty("message").GetString());
    }

    [TestMethod]
    public async Task ConsumerRendersBeforeInterceptorsAndNeverReconstructsRedactedContent()
    {
        var writer = new Writer();
        var processing = new LogProcessingOptions
        {
            Interceptors = [item =>
            {
                Assert.AreEqual("secret 42", item.Record.GetProperty("message").GetString());
                return new() { Message = "redacted", ClearMessageTemplate = true, Attributes = new Dictionary<string, LogValue>() };
            }],
            Filter = item => item.Record.GetProperty("message").GetString() == "redacted"
        };
        await using var runtime = Create(writer, processing: processing);
        runtime.CreateLogger("Redaction").Info($"secret {42}"); await Drain(runtime);
        Assert.AreEqual("redacted", Message(writer, 0));
        Assert.IsFalse(writer.Records[0].TryGetProperty("message_template", out _));
    }

    [TestMethod]
    public async Task EvictedInvalidFormatNeverRunsInTheConsumer()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start, capacity: 1);
        var logger = runtime.CreateLogger("Eviction"); int value = 42;
        logger.Info($"bad {value:Q}"); logger.Info($"survivor {value}");
        start.SetResult();
        var stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
        Assert.IsTrue(stop.LossObserved);
        Assert.AreEqual("survivor 42", Message(writer, 0));
        Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.Evicted);
        Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.Failed);
    }

    [TestMethod]
    public async Task UnicodeWidthPrecisionAndPartLimitsProduceBoundedTruncatedRecords()
    {
        var writer = new Writer(); await using var runtime = Create(writer);
        var logger = runtime.CreateLogger("Bounds");
        string emoji = string.Concat(Enumerable.Repeat("😀", 9000));
        Assert.IsTrue(logger.Info($"{emoji}").IsTruncated);
        var right = new LogInterpolatedStringHandler(0, 1); right.AppendFormatted(42, int.MaxValue); logger.Info(ref right);
        var left = new LogInterpolatedStringHandler(0, 1); left.AppendFormatted(42, int.MinValue); logger.Info(ref left);
        Assert.IsTrue(logger.Info($"precision {42:F999999999}").IsTruncated);
        var handler = new LogInterpolatedStringHandler(int.MaxValue, int.MaxValue);
        for (int index = 0; index < 2000; index++) { handler.AppendFormatted(1); }
        Assert.IsTrue(logger.Info(ref handler).IsTruncated);
        await Drain(runtime);
        foreach (var row in writer.Records)
        {
            Assert.IsTrue(row.GetProperty("truncated").GetBoolean());
            Assert.IsTrue(row.GetProperty("message").GetString()!.EnumerateRunes().Count() <= 8192);
        }
        string retained = Message(writer, 0);
        Assert.IsTrue(retained.Length > 0);
        Assert.AreEqual(string.Concat(Enumerable.Repeat("😀", retained.EnumerateRunes().Count())), retained);
        foreach (var row in writer.Records) { Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(row.GetRawText()) + 1 <= 65536); }
        Assert.AreEqual(5L, runtime.GetDiagnostics().TruncatedEventCount);
    }

    [TestMethod]
    public async Task InvalidUnicodeAndConflictingRichTextHaveExplicitInvalidResults()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Invalid");
        string invalid = "bad\ud800";
        Assert.AreEqual("InvalidUnicode", logger.Info($"{invalid}").ErrorCode);
        Assert.AreEqual("ConflictingInterpolationInput", logger.Write(CoreLogLevel.Information, $"{42}",
            new LogEventData { EventName = "event", Message = "other" }).ErrorCode);
        await Drain(runtime); Assert.AreEqual(0, writer.Records.Length);
    }

    [TestMethod]
    public async Task ExpressionsExecuteInOrderOnCallerEvenWhenDisabledAndAwaitIsSupported()
    {
        var writer = new Writer(); await using var runtime = Create(writer);
        var logger = runtime.CreateLogger("Expressions"); int calls = 0;
        int Next() => ++calls;
        logger.Info($"{Next()} {Next()}");
        logger.Info($"await {await Task.FromResult(42)}");
        Assert.AreEqual(LogWriteOutcome.Filtered, logger.Write(CoreLogLevel.None, $"disabled {Next()}").Outcome);
        await Drain(runtime);
        Assert.AreEqual(LogWriteOutcome.Stopped, logger.Info($"stopped {Next()}").Outcome);
        Assert.AreEqual(4, calls); Assert.AreEqual("1 2", Message(writer, 0)); Assert.AreEqual("await 42", Message(writer, 1));
    }

    [TestMethod]
    public async Task ConcurrentCallsKeepEachMessageAndOwnedAttributeSnapshotTogether()
    {
        var writer = new Writer(); await using var runtime = Create(writer, capacity: 512);
        var logger = runtime.CreateLogger("Concurrent");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
        {
            for (int index = 0; index < 100; index++)
            {
                int id = producer * 100 + index;
                var values = new Dictionary<string, LogValue> { ["id"] = LogValue.Integer(id) };
                Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info($"event {id:D4}", values).Outcome);
                values["id"] = LogValue.Integer(-1);
            }
        })));
        await Drain(runtime);
        Assert.AreEqual(400, writer.Records.Length);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 400).ToArray(), writer.Records.Select(row => row.GetProperty("attributes").GetProperty("id").GetInt32()).ToArray());
        foreach (var row in writer.Records)
        {
            int id = row.GetProperty("attributes").GetProperty("id").GetInt32();
            Assert.AreEqual("event " + id.ToString("D4", CultureInfo.InvariantCulture), row.GetProperty("message").GetString());
        }
    }

    [TestMethod]
    public async Task AllBuiltInScalarSnapshotsRetainNativeFormattingAndFullIntegerRange()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("Scalars");
        object[] values = [byte.MaxValue, sbyte.MinValue, short.MinValue, ushort.MaxValue, int.MinValue, uint.MaxValue,
            long.MinValue, ulong.MaxValue, Int128.MinValue, UInt128.MaxValue, (nint)42, (nuint)42, (Half)1.5,
            1.25f, 1.25d, 1.25m, Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            new DateOnly(2026, 10, 19), new TimeOnly(13, 14, 15), TimeSpan.FromSeconds(42),
            new DateTimeOffset(2026, 10, 19, 13, 14, 15, TimeSpan.Zero), true, 'x'];
        string[] expected = values.Select(value => value is IFormattable formatted
            ? formatted.ToString(null, CultureInfo.InvariantCulture) : value.ToString()!).ToArray();
        foreach (var value in values) { logger.Info($"{value}"); }
        await Drain(runtime);
        CollectionAssert.AreEqual(expected, writer.Records.Select(row => row.GetProperty("message").GetString()).ToArray());
    }

    [TestMethod]
    public async Task SpanValidationIncludesMalformedOmittedSuffixAndEmptyArgumentsDoNotInventTruncation()
    {
        var writer = new Writer(); await using var runtime = Create(writer); var logger = runtime.CreateLogger("SpanBounds");
        Assert.AreEqual("InvalidUnicode", InvalidSpan(logger).ErrorCode);
        string full = new('x', 8192); string? empty = null;
        Assert.IsFalse(logger.Info($"{full}{empty}").IsTruncated);
        await Drain(runtime);
        Assert.IsFalse(writer.Records.Single().TryGetProperty("truncated", out _));
    }

    private static LogWriteResult InvalidSpan(CoreLogger logger)
    {
        Span<char> value = new char[9001]; value.Fill('x'); value[^1] = '\ud800';
        return logger.Info($"{value}");
    }

    [TestMethod]
    [DataRow(LogOutputFormat.Human)]
    [DataRow(LogOutputFormat.JsonLines)]
    public async Task ConsoleFormatsReceiveTheSameRenderedMessageWithExistingEscaping(LogOutputFormat format)
    {
        using var stream = new MemoryStream();
        await using var runtime = QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.Trace, FileLevel = CoreLogLevel.None, ConsoleFormat = format },
            acquireConsole: _ => stream, incomingPipeline: true);
        int id = 42; runtime.CreateLogger("Console").Info($"Order {id:D4}\naccepted");
        await Drain(runtime);
        string text = Encoding.UTF8.GetString(stream.ToArray());
        if (format == LogOutputFormat.JsonLines)
        {
            using var parsed = JsonDocument.Parse(text);
            Assert.AreEqual("Order 0042\naccepted", parsed.RootElement.GetProperty("message").GetString());
        }
        else
        {
            StringAssert.Contains(text, "Order 0042"); StringAssert.Contains(text, "\\naccepted");
        }
    }

    [TestMethod]
    public async Task ExplicitDuplicateKeysContinueToSuppressInterpolatedOccurrencesAfterDispatch()
    {
        var writer = new Writer();
        var processing = new LogProcessingOptions
        {
            DuplicateSuppression = new(item => item.Record.GetProperty("attributes").GetProperty("id").GetRawText(), TimeSpan.FromMinutes(1))
        };
        await using var runtime = Create(writer, processing: processing);
        var logger = runtime.CreateLogger("Duplicate");
        var values = new Dictionary<string, LogValue> { ["id"] = LogValue.Integer(42) };
        logger.Info($"Order {42} accepted", values); logger.Info($"Order {42} accepted", values);
        await Drain(runtime);
        Assert.AreEqual(1, writer.Records.Length); Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.DuplicateSuppressed);
    }

    [TestMethod]
    public async Task UserObjectCanBeCollectedWhileItsOccurrenceIsStillWaiting()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        WeakReference reference = PublishCollectible(runtime.CreateLogger("Collectible"));
        try
        {
            for (int count = 0; count < 3 && reference.IsAlive; count++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Assert.IsFalse(reference.IsAlive, "The queued occurrence must not retain the caller's formatting object.");
        }
        finally { start.TrySetResult(); }
        await Drain(runtime); Assert.AreEqual("original/", Message(writer, 0));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PublishCollectible(CoreLogger logger)
    {
        var value = new MutableValue { Text = "original" }; logger.Info($"{value}"); return new(value);
    }

    [TestMethod]
    public async Task CallerBoxAliasesCannotChangePublishedScalarSnapshots()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new Writer(); await using var runtime = Create(writer, start: start);
        object number = 42;
        object date = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        try
        {
            runtime.CreateLogger("Boxed").Info($"{number} {date:yyyy-MM-dd}");
            Unsafe.Unbox<int>(number) = 99;
            Unsafe.Unbox<DateTimeOffset>(date) = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
        finally { start.TrySetResult(); }
        await Drain(runtime);
        Assert.AreEqual("42 2026-10-19", Message(writer, 0));
    }

    [TestMethod]
    public async Task FlushWaitsForInterpolatedDispatchAndWriterFlush()
    {
        var writer = new Writer { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var runtime = Create(writer);
        runtime.CreateLogger("Flush").Info($"order {42}");
        await writer.Entered.Task.WaitAsync(Limit);
        Task<LogFlushResult> flush = runtime.FlushAsync(Limit, CancellationToken.None);
        Assert.IsFalse(flush.IsCompleted); writer.Hold.SetResult();
        Assert.IsTrue((await flush.WaitAsync(Limit)).Succeeded); Assert.AreEqual("order 42", Message(writer, 0));
    }

    private static CoreLogRuntime Create(Writer writer, TaskCompletionSource? start = null, int capacity = 64, LogProcessingOptions? processing = null, CultureInfo? consumerCulture = null)
    {
        Task Launch(Func<Task> worker)
        {
            bool incoming = worker.Target is IncomingEventQueue;
            return Task.Run(async () =>
            {
                if (incoming && consumerCulture is not null)
                { CultureInfo.CurrentCulture = consumerCulture; CultureInfo.CurrentUICulture = consumerCulture; }
                if (incoming && start is not null) { await start.Task; }
                await worker();
            });
        }
        return QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(),
            new LoggingOptions { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = capacity },
            writers: [new("writer", writer, CoreLogLevel.Trace)], incomingPipeline: true, processing: processing, launchWorker: Launch);
    }
    private static async Task Drain(CoreLogRuntime runtime)
        => Assert.IsTrue((await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit)).Succeeded);
    private static string Message(Writer writer, int index) => writer.Records[index].GetProperty("message").GetString()!;
    private sealed class Writer : ILogWriter
    {
        private readonly ConcurrentQueue<JsonElement> records = new();
        internal JsonElement[] Records => records.ToArray();
        internal TaskCompletionSource? Hold { get; init; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(); if (Hold is not null) { await Hold.Task; }
            using var parsed = JsonDocument.Parse(jsonLine); records.Enqueue(parsed.RootElement.Clone());
        }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class MutableValue : IFormattable
    {
        internal string Text { get; set; } = string.Empty;
        internal int Calls { get; private set; }
        internal int Thread { get; private set; }
        public string ToString(string? format, IFormatProvider? formatProvider)
        { Calls++; Thread = Environment.CurrentManagedThreadId; return Text + "/" + format; }
    }
    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("private failure");
    }
}
