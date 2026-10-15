using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using CIS.Logging.Runtime;
using CIS.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProcessingPolicyTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static LogEventData Data(string name) => new() { EventName = name, Message = name };
    private static CoreLogRuntime Create(LogProcessingOptions options, Writer[] writers, TimeProvider? clock = null, int timeout = 5000, int capacity = 16) =>
        QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions
        { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = capacity, ShutdownTimeoutMilliseconds = timeout },
        timeProvider: clock, writers: writers.Select((writer, index) => new LogWriterRegistration("writer" + index, writer, CoreLogLevel.Trace)), processing: options);
    private static async Task<LogFlushResult> Drain(CoreLogRuntime runtime) => await runtime.FlushAsync(Limit, CancellationToken.None).WaitAsync(Limit);

    [TestMethod]
    public async Task RedactionPrecedesAllPoliciesAndChangesAreOwnedAndStartupCaptured()
    {
        var calls = new List<string>(); var replacement = new Dictionary<string, LogValue> { ["customer"] = LogValue.Integer(7) };
        LogProcessingEvent? retained = null;
        var interceptors = new List<Func<LogProcessingEvent, LogEventChanges>>
        {
            value => { calls.Add("redact"); return new() { Message = "safe", ClearMessageTemplate = true, RemoveException = true, Attributes = replacement }; },
            value => { calls.Add("second"); retained = value; replacement["customer"] = LogValue.Integer(99); return new(); }
        };
        var options = new LogProcessingOptions
        {
            Interceptors = interceptors,
            Filter = value => { calls.Add("filter"); Assert.AreEqual("safe", value.Message); return true; },
            Route = (name, value) => { calls.Add(name); Assert.IsFalse(value.Record.TryGetProperty("exception", out _)); return name == "writer0"; },
            DuplicateSuppression = new(value => { calls.Add("key"); return value.Message; }, TimeSpan.FromSeconds(1))
        };
        var first = new Writer(); var second = new Writer(); await using var runtime = Create(options, [first, second]);
        interceptors.Clear(); options.Filter = _ => false; options.Route = (_, _) => false; options.DuplicateSuppression = null;
        using (runtime.BeginScope(new OperationContextData { OperationId = "op", Attributes = new Dictionary<string, LogValue> { ["secret"] = LogValue.String("private") } }))
        { runtime.CreateLogger("Policies").Info(new LogEventData { EventName = "safe.event", Message = "private", MessageTemplate = "private", Exception = new Exception("private") }); }
        var result = await Drain(runtime);
        Assert.IsTrue(result.Succeeded); Assert.IsFalse(result.LossObserved);
        CollectionAssert.AreEqual(new[] { "redact", "second", "filter", "writer0", "writer1", "key" }, calls.ToArray());
        Assert.HasCount(0, second.Lines); Assert.AreEqual(1L, result.Outputs[1].Rejections.Filtered);
        string line = first.Lines.Single(); Assert.IsFalse(line.Contains("private", StringComparison.Ordinal));
        JsonNode record = JsonNode.Parse(line)!;
        Assert.AreEqual(7, record["attributes"]!["customer"]!.GetValue<int>());
        Assert.AreEqual("op", record["operation_id"]!.GetValue<string>());
        Assert.AreEqual("safe.event", retained!.EventName); Assert.AreEqual("Policies", retained.Component);
        Assert.AreEqual(CoreLogLevel.Information, retained.Level);
        Assert.AreEqual(7, retained.Record.GetProperty("attributes").GetProperty("customer").GetInt32());
    }

    [TestMethod]
    [DataRow("interceptor")]
    [DataRow("filter")]
    [DataRow("route")]
    [DataRow("key")]
    [DataRow("unicode")]
    [DataRow("null")]
    public async Task CallbackFailuresFailClosedAndLaterEventsProgress(string stage)
    {
        bool Bad(LogProcessingEvent value) => value.EventName == "bad";
        var options = new LogProcessingOptions
        {
            Interceptors = [value =>
            {
                if (Bad(value) && stage == "interceptor") { throw new IOException("secret"); }
                if (Bad(value) && stage == "null") { return null!; }
                return new() { Message = Bad(value) && stage == "unicode" ? "\ud800" : "safe", RemoveException = true };
            }],
            Filter = value => Bad(value) && stage == "filter" ? throw new IOException("secret") : true,
            Route = (_, value) => Bad(value) && stage == "route" ? throw new IOException("secret") : true,
            DuplicateSuppression = new(value => Bad(value) && stage == "key" ? throw new IOException("secret") : null, TimeSpan.FromSeconds(1))
        };
        var writer = new Writer(); await using var runtime = Create(options, [writer]); var logger = runtime.CreateLogger("Failures");
        Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info(Data("bad")).Outcome);
        logger.Info(Data("good"));
        var result = await Drain(runtime);
        Assert.IsFalse(result.Succeeded); Assert.IsTrue(result.LossObserved);
        Assert.AreEqual(2L, result.Incoming!.Accepted); Assert.AreEqual(1L, result.Incoming.Failed); Assert.AreEqual(1L, result.Incoming.Processed);
        Assert.AreEqual("good", JsonNode.Parse(writer.Lines.Single())!["event_name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task FiltersAndAllDestinationExclusionsAreIntentionalSuppression()
    {
        var writer = new Writer(); await using var runtime = Create(new()
        { Filter = value => value.EventName != "global", Route = (_, value) => value.EventName != "route" }, [writer]);
        var logger = runtime.CreateLogger("Filters"); logger.Info(Data("global")); logger.Info(Data("route")); logger.Info(Data("included"));
        var result = await Drain(runtime);
        Assert.IsTrue(result.Succeeded); Assert.IsFalse(result.LossObserved);
        Assert.AreEqual(3L, result.Incoming!.Processed); Assert.AreEqual(2L, result.Incoming.Suppressed);
        Assert.AreEqual(0L, result.Incoming.DuplicateSuppressed); Assert.AreEqual(2L, result.Outputs.Single().Rejections.Filtered);
        Assert.HasCount(1, writer.Lines);
    }

    [TestMethod]
    public async Task DuplicateWindowIsMonotonicNonSlidingAndCapacityIsBounded()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow); var writer = new Writer();
        await using var runtime = Create(new() { DuplicateSuppression = new(value => value.EventName, TimeSpan.FromSeconds(10), 2) }, [writer], clock);
        async Task Emit(string name) { runtime.CreateLogger("Duplicate").Info(Data(name)); Assert.IsTrue((await Drain(runtime)).Succeeded); }
        await Emit("a"); await Emit("a");
        clock.SetUtcNow(clock.GetUtcNow().AddDays(-10)); clock.Advance(TimeSpan.FromSeconds(9)); await Emit("a");
        clock.Advance(TimeSpan.FromSeconds(1)); await Emit("a"); await Emit("b"); await Emit("c"); await Emit("a");
        var counts = runtime.GetDiagnostics().Incoming!;
        Assert.AreEqual(7L, counts.Processed); Assert.AreEqual(2L, counts.DuplicateSuppressed); Assert.AreEqual(2L, counts.Suppressed);
        Assert.AreEqual(2L, counts.DuplicateKeyEvictions); Assert.HasCount(5, writer.Lines);
    }

    [TestMethod]
    public async Task ApplicationKeysDistinguishCustomerSeverityAndExceptionFacts()
    {
        var writer = new Writer();
        await using var runtime = Create(new()
        {
            DuplicateSuppression = new(value => string.Join('|', value.Level, value.Record.GetProperty("attributes").GetProperty("customer").GetInt32(),
                value.Record.TryGetProperty("exception", out var error) ? error.GetRawText() : "none"), TimeSpan.FromMinutes(1))
        }, [writer]);
        void Emit(int customer, CoreLogLevel level, string? error = null) => runtime.CreateLogger("Keys").Write(level,
            new LogEventData { EventName = "same.template", MessageTemplate = "Customer {customer}", Message = "same", Attributes = new Dictionary<string, LogValue> { ["customer"] = LogValue.Integer(customer) }, Exception = error is null ? null : new Exception(error) });
        Emit(1, CoreLogLevel.Information); Emit(1, CoreLogLevel.Information); Emit(2, CoreLogLevel.Information);
        Emit(1, CoreLogLevel.Warning); Emit(1, CoreLogLevel.Information, "first"); Emit(1, CoreLogLevel.Information, "second");
        var result = await Drain(runtime); Assert.IsTrue(result.Succeeded); Assert.HasCount(5, writer.Lines);
        Assert.AreEqual(1L, result.Incoming!.DuplicateSuppressed);
    }

    [TestMethod]
    public async Task DuplicatePolicyIsOffByDefaultAndNullKeysBypassIt()
    {
        foreach (var options in new[] { new LogProcessingOptions(), new() { DuplicateSuppression = new(_ => null, TimeSpan.FromMinutes(1)) } })
        {
            var writer = new Writer(); await using var runtime = Create(options, [writer]);
            runtime.CreateLogger("Defaults").Info("repeat"); runtime.CreateLogger("Defaults").Info("repeat");
            Assert.IsTrue((await Drain(runtime)).Succeeded); Assert.HasCount(2, writer.Lines);
        }
    }

    [TestMethod]
    public async Task RecursiveFactoriesAreSkippedAndDiagnosticsDoNotDeadlock()
    {
        CoreLogger? logger = null; CoreLogRuntime? runtime = null; LogWriteResult? recursive = null; bool enabled = true;
        var writer = new Writer(); runtime = Create(new()
        {
            Interceptors = [value =>
        {
            enabled = logger!.IsEnabled(CoreLogLevel.Information);
            recursive = logger.InfoLazy(() => throw new AssertFailedException("Recursive factory executed"));
            Assert.IsNotNull(Task.Run(runtime!.GetDiagnostics).WaitAsync(Limit).GetAwaiter().GetResult(), "No admission lock may be held around callbacks.");
            Assert.ThrowsExactly<InvalidOperationException>(() => runtime.FlushAsync(Limit, CancellationToken.None));
            Assert.ThrowsExactly<InvalidOperationException>(() => runtime.StopAsync(CancellationToken.None));
            return new();
        }]
        }, [writer]);
        await using (runtime)
        {
            logger = runtime.CreateLogger("Recursive"); logger.Info("outer"); var result = await Drain(runtime);
            Assert.IsFalse(enabled); Assert.AreEqual("RecursiveProcessing", recursive!.ErrorCode);
            Assert.AreEqual(1L, result.InvalidEventCount); Assert.HasCount(1, writer.Lines);
        }
    }

    [TestMethod]
    public async Task StopAbandonsBlockedInterceptionAndPreventsLateWriterAdmission()
    {
        var entered = Signal(); var release = Signal(); var writer = new Writer();
        var runtime = Create(new() { Interceptors = [_ => { entered.TrySetResult(); release.Task.WaitAsync(Limit).GetAwaiter().GetResult(); return new(); }] }, [writer], timeout: 100);
        try
        {
            runtime.CreateLogger("Deadline").Info("held"); await entered.Task.WaitAsync(Limit);
            var stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, stopped.Completion); Assert.AreEqual(1L, stopped.Incoming!.Abandoned);
            Assert.IsFalse(runtime.Completion.IsCompleted); release.TrySetResult(); await runtime.Completion.WaitAsync(Limit);
            Assert.HasCount(0, writer.Lines); Assert.AreEqual(1L, stopped.Incoming.Abandoned);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task MicrosoftProviderUsesTheSameRedactionAndDuplicatePipeline()
    {
        var writer = new Writer(); await using var runtime = Create(new()
        {
            Interceptors = [_ => new() { Message = "redacted", ClearMessageTemplate = true, RemoveException = true, Attributes = new Dictionary<string, LogValue> { ["event_id"] = LogValue.String("invalid override") } }],
            DuplicateSuppression = new(value => value.Record.GetProperty("attributes").GetProperty("event_id").GetRawText(), TimeSpan.FromMinutes(1))
        }, [writer]);
        using var provider = new CoreLoggerProvider(runtime); var logger = provider.CreateLogger("Microsoft");
        logger.LogInformation(new EventId(42, "provider.event"), "secret {Customer}", 7);
        logger.LogInformation(new EventId(42, "provider.event"), "secret {Customer}", 7);
        var result = await Drain(runtime); Assert.IsTrue(result.Succeeded); Assert.AreEqual(1L, result.Incoming!.DuplicateSuppressed);
        Assert.AreEqual("redacted", JsonNode.Parse(writer.Lines.Single())!["message"]!.GetValue<string>());
        Assert.AreEqual(42, JsonNode.Parse(writer.Lines.Single())!["attributes"]!["event_id"]!.GetValue<int>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterceptorTruncationIsCountedOnce(bool alreadyTruncated)
    {
        var writer = new Writer(); await using var runtime = Create(new() { Interceptors = [_ => new() { Message = new string('x', 9000) }] }, [writer]);
        runtime.CreateLogger("Bounds").Info(alreadyTruncated ? new string('x', 9000) : "short"); var result = await Drain(runtime);
        Assert.AreEqual(1L, result.TruncatedEventCount); Assert.IsTrue(JsonNode.Parse(writer.Lines.Single())!["truncated"]!.GetValue<bool>());
    }

    [TestMethod]
    [DataRow(1024, true)]
    [DataRow(1025, false)]
    public async Task DuplicateKeysHaveAnExplicitMemoryBound(int length, bool valid)
    {
        var writer = new Writer(); await using var runtime = Create(new() { DuplicateSuppression = new(_ => new string('k', length), TimeSpan.FromSeconds(1)) }, [writer]);
        runtime.CreateLogger("Keys").Info("bounded"); var result = await Drain(runtime);
        Assert.AreEqual(valid, result.Succeeded); Assert.AreEqual(valid ? 0L : 1L, result.Incoming!.Failed);
        Assert.AreEqual(valid ? 1 : 0, writer.Lines.Count);
    }

    [TestMethod]
    public async Task WriterRejectionDoesNotReserveADuplicateKey()
    {
        var entered = Signal(); var release = Signal();
        var writer = new Writer(async () => { entered.TrySetResult(); await release.Task; });
        var runtime = Create(new() { DuplicateSuppression = new(value => value.EventName, TimeSpan.FromMinutes(1)) }, [writer], capacity: 1);
        try
        {
            var logger = runtime.CreateLogger("Retry");
            async Task Emit(string name)
            {
                Assert.AreEqual(LogWriteOutcome.Accepted, logger.Info(Data(name)).Outcome);
                async Task Settled() { while (runtime.GetDiagnostics().Incoming!.Pending != 0) { await Task.Delay(1); } }
                await Settled().WaitAsync(Limit);
            }
            await Emit("active"); await entered.Task.WaitAsync(Limit); await Emit("buffered"); await Emit("retry");
            Assert.AreEqual(1L, runtime.GetDiagnostics().Incoming!.RejectedByWriters);
            release.TrySetResult(); await Drain(runtime); await Emit("retry"); await Drain(runtime);
            Assert.AreEqual(0L, runtime.GetDiagnostics().Incoming!.DuplicateSuppressed);
            CollectionAssert.AreEqual(new[] { "active", "buffered", "retry" }, writer.Lines.Select(line => JsonNode.Parse(line)!["event_name"]!.GetValue<string>()).ToArray());
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task PublicProcessingStartupRoutesAfterConfiguredThresholds()
    {
        var settings = EnvironmentOptions.Logging; var oldConsole = settings.ConsoleLevel; var oldFile = settings.FileLevel;
        try
        {
            settings.ConsoleLevel = CoreLogLevel.None; settings.FileLevel = CoreLogLevel.None;
            var info = new Writer(); var warning = new Writer(); var routes = new List<string>();
            await using var runtime = CoreLogRuntime.Create(new LogProcessingOptions { Route = (name, _) => { routes.Add(name); return true; } },
                [new("info", info), new("warning", warning, CoreLogLevel.Warning)]);
            runtime.CreateLogger("Thresholds").Info("information"); Assert.IsTrue((await Drain(runtime)).Succeeded);
            CollectionAssert.AreEqual(new[] { "info" }, routes.ToArray()); Assert.HasCount(1, info.Lines); Assert.HasCount(0, warning.Lines);
        }
        finally { settings.ConsoleLevel = oldConsole; settings.FileLevel = oldFile; }
    }

    [TestMethod]
    public void InvalidPoliciesFailBeforeLaunchingWorkers()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new LogDuplicateSuppression(null!, TimeSpan.FromSeconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LogDuplicateSuppression(_ => "key", TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LogDuplicateSuppression(_ => "key", TimeSpan.FromSeconds(1), 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LogDuplicateSuppression(_ => "key", TimeSpan.FromSeconds(1), 65537));
        int launches = 0;
        Assert.ThrowsExactly<ArgumentException>(() => QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), new LoggingOptions(),
            launchWorker: worker => { launches++; return Task.Run(worker); }, processing: new() { Interceptors = [null!] }));
        Assert.AreEqual(0, launches);
    }

    [TestMethod]
    public async Task CallbackViewsAreByteBoundedAndOmittedDataCannotReappear()
    {
        var writer = new Writer(); int callbacks = 0;
        await using var runtime = Create(new()
        {
            Interceptors = [value =>
        {
            callbacks++;
            Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(value.Record.GetRawText()) < new LoggingOptions().MaxEntryBytes);
            Assert.IsTrue(value.Record.GetProperty("truncated").GetBoolean());
            Assert.IsFalse(value.Record.GetProperty("attributes").TryGetProperty("z_secret", out _));
            return new() { Message = "" };
        }, value =>
        {
            callbacks++;
            Assert.IsFalse(value.Record.GetProperty("attributes").TryGetProperty("z_secret", out _));
            return new();
        }]
        }, [writer]);
        runtime.CreateLogger("Bounds").Info(new LogEventData
        {
            EventName = "bounded",
            Message = new string('x', 8192),
            Attributes = new Dictionary<string, LogValue> { ["a_large"] = LogValue.String(new string('y', 2_000_000)), ["z_secret"] = LogValue.String("must stay omitted") }
        });
        var result = await Drain(runtime); Assert.IsTrue(result.Succeeded); Assert.AreEqual(2, callbacks);
        Assert.AreEqual(1L, result.TruncatedEventCount);
        Assert.IsFalse(JsonNode.Parse(writer.Lines.Single())!["attributes"]!.AsObject().ContainsKey("z_secret"));
    }

    private sealed class Writer(Func<ValueTask>? beforeWrite = null) : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        public async ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        { if (beforeWrite is not null) { await beforeWrite(); } Lines.Enqueue(jsonLine); }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
