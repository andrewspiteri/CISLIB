using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class RegisteredWriterTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static LogEventData Event(string name) => new() { EventName = name, Message = "captured" };
    private static LoggingOptions Options(int capacity = 2, int timeout = 5000) => new()
    { ConsoleLevel = CoreLogLevel.None, FileLevel = CoreLogLevel.None, QueueCapacity = capacity, ShutdownTimeoutMilliseconds = timeout };
    private static CoreLogRuntime Create(IEnumerable<LogWriterRegistration> registrations, LoggingOptions? options = null, Func<Func<Task>, Task>? launch = null) =>
        QueuedRuntimeFactory.CreateConfiguredQueued(new EventMetadata(), options ?? Options(), launchWorker: launch, writers: registrations);

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("console")]
    [DataRow("file")]
    [DataRow("bad\nname")]
    public void InvalidNamesFailBeforeAnyWriterIsAcquired(string name)
    {
        var writer = new Writer();
        Assert.ThrowsExactly<ArgumentException>(() => new LogWriterRegistration(name, writer));
        Assert.AreEqual(0, writer.Disposals);
    }

    [TestMethod]
    public void NullUndefinedDisabledAndDuplicateRegistrationsFailBeforeLaunching()
    {
        var writer = new Writer();
        Assert.ThrowsExactly<ArgumentNullException>(() => new LogWriterRegistration(null!, writer));
        Assert.ThrowsExactly<ArgumentNullException>(() => new LogWriterRegistration("custom", null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LogWriterRegistration("custom", writer, CoreLogLevel.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new LogWriterRegistration("custom", writer, (CoreLogLevel)99));
        int launched = 0;
        Task Launch(Func<Task> worker) { launched++; return Task.Run(worker); }
        Assert.ThrowsExactly<ArgumentException>(() => Create([new("same", writer), new("same", new Writer())], launch: Launch));
        Assert.ThrowsExactly<ArgumentException>(() => Create([new("one", writer), new("two", writer)], launch: Launch));
        Assert.ThrowsExactly<ArgumentException>(() => Create([null!], launch: Launch));
        Assert.AreEqual(0, launched);
    }

    [TestMethod]
    public async Task CapturedRegistrationsThresholdsAndPayloadParticipateInFlush()
    {
        var info = new Writer(); var warning = new Writer();
        var registrations = new List<LogWriterRegistration> { new("info", info), new("warning", warning, CoreLogLevel.Warning) };
        await using var runtime = Create(registrations);
        registrations.Clear();
        var attributes = new Dictionary<string, LogValue> { ["value"] = LogValue.Integer(7) };
        var data = Event("before"); data.Attributes = attributes;
        LogWriteResult result = runtime.CreateLogger("Custom.Writer").Write(CoreLogLevel.Information, data);
        data.EventName = "after"; attributes["value"] = LogValue.Integer(99);
        Assert.AreEqual(LogWriteOutcome.Accepted, result.Outcome);
        CollectionAssert.AreEqual(new[] { "info", "warning" }, result.Outputs.Select(item => item.OutputName).ToArray());
        Assert.AreEqual(LogOutputWriteStatus.Filtered, result.Outputs[1].Status);
        LogFlushResult flush = await runtime.FlushAsync(Limit, CancellationToken.None);
        Assert.IsTrue(flush.Succeeded);
        Assert.AreEqual(1L, flush.Outputs[0].Written);
        Assert.AreEqual(0L, flush.Outputs[1].Written);
        JsonNode record = JsonNode.Parse(info.Lines.Single())!;
        Assert.AreEqual("before", record["event_name"]!.GetValue<string>());
        Assert.AreEqual(7, record["attributes"]!["value"]!.GetValue<int>());
        Assert.IsTrue(info.Lines.Single().EndsWith('\n'));
        Assert.AreEqual(0, warning.Lines.Count);
        await runtime.StopAsync(CancellationToken.None);
        await runtime.Completion.WaitAsync(Limit);
        Assert.AreEqual(0, info.Disposals, "Borrowed writers remain application-owned.");
    }

    [TestMethod]
    public async Task BlockedDestinationDoesNotPreventIndependentProgressAndKeepsFixedFlushBoundary()
    {
        var entered = Signal(); var release = Signal(); var secondEntered = Signal(); var secondRelease = Signal(); var healthy = Signal();
        int calls = 0;
        var slow = new Writer(async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.TrySetResult(); await release.Task; }
            else { secondEntered.TrySetResult(); await secondRelease.Task; }
        });
        var fast = new Writer((_, _) => { healthy.TrySetResult(); return ValueTask.CompletedTask; });
        var runtime = Create([new("slow", slow), new("fast", fast)]);
        try
        {
            CoreLogger logger = runtime.CreateLogger("Custom.Writer");
            logger.Write(CoreLogLevel.Information, Event("first"));
            await entered.Task.WaitAsync(Limit); await healthy.Task.WaitAsync(Limit);
            Task<LogFlushResult> flush = runtime.FlushAsync(Limit, CancellationToken.None);
            Assert.IsFalse(flush.IsCompleted);
            logger.Write(CoreLogLevel.Information, Event("later"));
            release.TrySetResult();
            LogFlushResult result = await flush.WaitAsync(Limit);
            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(result.Outputs.All(output => output.AcceptedSequenceBoundary == 1 && output.Written == 1));
            await secondEntered.Task.WaitAsync(Limit);
            Assert.AreEqual(1, slow.Lines.Count);
        }
        finally { release.TrySetResult(); secondRelease.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public async Task WriterFaultIsIsolatedAndOwnedCleanupRunsOnce()
    {
        var failed = new Writer((_, _) => throw new IOException("private destination detail"));
        var healthy = new Writer();
        var runtime = Create([new("failed", failed, ownsWriter: true), new("healthy", healthy, ownsWriter: true)]);
        runtime.CreateLogger("Custom.Writer").Write(CoreLogLevel.Information, Event("first"));
        LogStopResult result = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1L, result.Outputs.Single(output => output.OutputName == "failed").Failed);
        Assert.AreEqual(1L, result.Outputs.Single(output => output.OutputName == "healthy").Written);
        Assert.AreEqual("WriteFailed", runtime.GetDiagnostics().Outputs.Single(output => output.OutputName == "failed").LastFaultCode);
        await Assert.ThrowsExactlyAsync<IOException>(() => runtime.Completion.WaitAsync(Limit));
        await runtime.DisposeAsync();
        Assert.AreEqual(1, failed.Disposals); Assert.AreEqual(1, healthy.Disposals);
    }

    [TestMethod]
    public async Task TimedOutStopDoesNotDisposeAnActiveWriterAndLateCleanupIsAwaitable()
    {
        var entered = Signal(); var release = Signal();
        var writer = new Writer(async (_, _) => { entered.TrySetResult(); await release.Task; });
        var runtime = Create([new("owned", writer, ownsWriter: true)], Options(timeout: 100));
        try
        {
            runtime.CreateLogger("Custom.Writer").Write(CoreLogLevel.Information, Event("pending"));
            await entered.Task.WaitAsync(Limit);
            LogStopResult stopped = await runtime.StopAsync(CancellationToken.None).WaitAsync(Limit);
            Assert.AreEqual(LogCompletion.TimedOut, stopped.Completion);
            Assert.AreEqual(0, writer.Disposals);
            Assert.IsFalse(runtime.Completion.IsCompleted);
            release.TrySetResult(); await runtime.Completion.WaitAsync(Limit);
            Assert.AreEqual(1, writer.Disposals);
            Assert.AreEqual(LogCompletion.TimedOut, runtime.GetDiagnostics().LastStopResult!.Completion);
        }
        finally { release.TrySetResult(); await runtime.DisposeAsync(); }
    }

    [TestMethod]
    public void StartupFailureLeavesCustomWriterOwnershipWithCaller()
    {
        var first = new Writer(); var second = new Writer(); int launches = 0;
        Task Launch(Func<Task> worker) => ++launches == 2 ? throw new IOException("launch") : Task.Run(worker);
        CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() =>
            Create([new("first", first, ownsWriter: true), new("second", second, ownsWriter: true)], launch: Launch));
        Assert.AreEqual(0, first.Disposals); Assert.AreEqual(0, second.Disposals);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public async Task PublicStartupUsesTheExternalWriterContract()
    {
        LoggingOptions options = EnvironmentOptions.Logging;
        CoreLogLevel? oldConsole = options.ConsoleLevel, oldFile = options.FileLevel;
        try
        {
            options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.None;
            var writer = new Writer();
            await using var runtime = CoreLogRuntime.Create([new LogWriterRegistration("external", writer)]);
            runtime.CreateLogger("Application").Write(CoreLogLevel.Warning, Event("public-startup"));
            Assert.IsTrue((await runtime.FlushAsync(Limit, CancellationToken.None)).Succeeded);
            Assert.AreEqual("public-startup", JsonNode.Parse(writer.Lines.Single())!["event_name"]!.GetValue<string>());
        }
        finally { options.ConsoleLevel = oldConsole; options.FileLevel = oldFile; }
    }

    private sealed class Writer(Func<string, CancellationToken, ValueTask>? write = null) : ILogWriter
    {
        internal ConcurrentQueue<string> Lines { get; } = new();
        internal int Disposals;
        public async ValueTask WriteAsync(string jsonLine, CancellationToken cancellationToken)
        {
            if (write is not null) { await write(jsonLine, cancellationToken); }
            Lines.Enqueue(jsonLine);
        }
        public ValueTask FlushAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
}
