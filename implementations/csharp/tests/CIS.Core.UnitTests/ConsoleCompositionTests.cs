using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ConsoleCompositionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Where(row => !row!["physical"]!.GetValue<bool>()).Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task FrozenConsoleChoicesDriveRealOwnedBytesAndLossBeforeAdmission(string id)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, id);
        ConsoleCompositionExpectations.Validate(catalog, row, await ConsoleCompositionFixture.ExecuteAsync(row));
    }
    [TestMethod]
    [DataRow("stream")]
    [DataRow("outcome")]
    [DataRow("ticket")]
    [DataRow("loss")]
    [DataRow("clock")]
    [DataRow("identity")]
    [DataRow("bom")]
    public async Task ConsoleMatcherRejectsChangedStreamFactsAndAccounting(string fault)
    {
        JsonObject catalog = Catalog(), row = Row(catalog, "CONSOLE-CONSOLE-110"), actual = await ConsoleCompositionFixture.ExecuteAsync(row);
        ConsoleCompositionExpectations.Damage(actual, fault);
        Assert.ThrowsExactly<InvalidDataException>(() => ConsoleCompositionExpectations.Validate(catalog, row, actual));
    }
    [TestMethod]
    [DataRow("ConsoleFormat")]
    [DataRow("HumanDetail")]
    [DataRow("ConsoleStream")]
    public void UndefinedConsoleChoicesFailBeforeSelectingAStream(string key)
    {
        var options = new LoggingOptions();
        PropertyInfo property = typeof(LoggingOptions).GetProperty(key)!; property.SetValue(options, Enum.ToObject(property.PropertyType, 999));
        int calls = 0;
        Assert.ThrowsExactly<ArgumentException>(() => CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => { calls++; return new MemoryStream(); }));
        Assert.AreEqual(0, calls);
    }
    [TestMethod]
    public void EnabledConsoleRequiresAnActualSelectedStream()
    {
        CoreLogInitializationException error = Assert.ThrowsExactly<CoreLogInitializationException>(() => CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions(), acquireConsole: _ => null!));
        Assert.AreEqual("console", error.OutputName); Assert.AreEqual("ConsoleInitializationFailed", error.ErrorCode); Assert.IsNull(error.InnerException);
    }
    private static readonly AsyncLocal<string?> Creator = new();
    private sealed class HeldStream(bool fail) : MemoryStream
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool InheritedContext;
        private int calls;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            InheritedContext |= Creator.Value is not null;
            if (Interlocked.Increment(ref calls) == 1) { Started.TrySetResult(); await Release.Task; }
            await base.WriteAsync(fail ? buffer[..5] : buffer, cancellationToken);
            if (fail) { throw new IOException("controlled-stream-private-payload"); }
        }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HeldOrPartiallyFailedConfiguredConsoleKeepsTheFileWorkerIndependent(bool fail)
    {
        using var console = new HeldStream(fail); using var file = new MemoryStream();
        var fileDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int fileCalls = 0;
        string? previous = Creator.Value; Creator.Value = "must-not-be-inherited";
        CoreLogRuntime runtime;
        try
        {
            runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), new LoggingOptions { FilePath = "controlled.jsonl", QueueCapacity = 2 },
                async item => { Assert.IsNull(item.PreparedLine); await file.WriteAsync(item.Event.JsonLine); if (++fileCalls == 3) { fileDone.TrySetResult(); } },
                acquireConsole: _ => console);
        }
        finally { Creator.Value = previous; }
        CoreLogger logger = runtime.CreateLogger("HeldConsole");
        try
        {
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "first", Message = "first" }).Outcome);
            await console.Started.Task.WaitAsync(Limit);
            foreach (string name in new[] { "second", "third" }) { Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, new LogEventData { EventName = name, Message = name }).Outcome); }
            await fileDone.Task.WaitAsync(Limit);
            Assert.IsTrue(SpinWait.SpinUntil(() => runtime.GetDiagnostics().Outputs[1].Written == 3, Limit));
            CoreLogDiagnostics held = runtime.GetDiagnostics();
            Assert.AreEqual(3L, held.Outputs[0].Pending); Assert.AreEqual(0L, held.Outputs[0].Written); Assert.AreEqual(3L, held.Outputs[1].Written);
            Assert.AreEqual(0L, held.InvalidEventCount); Assert.IsFalse(console.InheritedContext);
            runtime.CloseAdmission(); Assert.AreEqual(CoreLogState.Stopping, runtime.GetDiagnostics().State); console.Release.TrySetResult();
            if (fail) { await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Completion.WaitAsync(Limit)); }
            else { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
            CoreLogDiagnostics final = runtime.GetDiagnostics(); Assert.AreEqual(CoreLogState.Stopped, final.State);
            Assert.AreEqual(fail ? 1L : 0L, final.Outputs[0].Failed); Assert.AreEqual(fail ? 2L : 0L, final.Outputs[0].Abandoned);
            Assert.AreEqual(fail ? 0L : 3L, final.Outputs[0].Written); Assert.AreEqual(0L, final.Outputs[0].Pending);
            Assert.AreEqual(fail ? "WriteFailed" : null, final.Outputs[0].LastFaultCode); Assert.AreEqual(3L, final.Outputs[1].Written);
            Assert.AreEqual(fail ? 5 : 3, fail ? console.ToArray().Length : console.ToArray().Count(value => value == 10));
            Assert.AreEqual(3, file.ToArray().Count(value => value == 10)); Assert.AreEqual(3L, held.Outputs[0].Pending);
        }
        finally
        {
            console.Release.TrySetResult(); runtime.CloseAdmission();
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) when (fail) { }
        }
    }
    private static LogEventData LossData() => new()
    {
        EventName = "loss",
        Message = new string('\u200d', 8192),
        Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('\u200d', 8192)) }
    };
    private static LogEventData SmallData() => new() { EventName = "small", Message = "small" };
    private sealed class BlockingClock(int blockAt) : TimeProvider, IDisposable
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        private int calls;
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Increment(ref calls) == blockAt)
            {
                Started.TrySetResult();
                if (!Release.Wait(Limit)) { throw new TimeoutException("Clock release missing."); }
            }
            return new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero);
        }
        public void Dispose() => Release.Dispose();
    }
    [TestMethod]
    public async Task QueueFullPreservesPreparedDisplayLossAndOriginalFileCapture()
    {
        using var console = new HeldStream(false); using var file = new MemoryStream();
        var fileReady = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        int fileCalls = 0; bool lastCaptureLoss = true;
        CoreLogRuntime runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { FilePath = "controlled.jsonl", QueueCapacity = 1, ConsoleFormat = LogOutputFormat.Human, HumanDetail = HumanOutputDetail.Verbose },
            async item => { await file.WriteAsync(item.Event.JsonLine); lastCaptureLoss = item.Event.IsTruncated; fileReady[fileCalls++].TrySetResult(); }, acquireConsole: _ => console);
        CoreLogger logger = runtime.CreateLogger("QueueFullLoss");
        try
        {
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, SmallData()).Outcome);
            await console.Started.Task.WaitAsync(Limit); await fileReady[0].Task.WaitAsync(Limit);
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, SmallData()).Outcome); await fileReady[1].Task.WaitAsync(Limit);
            LogWriteResult result = logger.Write(CoreLogLevel.Information, LossData());
            Assert.AreEqual(LogWriteOutcome.PartiallyAccepted, result.Outcome); Assert.IsTrue(result.IsTruncated);
            Assert.AreEqual(LogOutputWriteStatus.QueueFull, result.Outputs[0].Status); Assert.IsNull(result.Outputs[0].AcceptedSequence);
            Assert.AreEqual(LogOutputWriteStatus.Accepted, result.Outputs[1].Status); Assert.AreEqual(3L, result.Outputs[1].AcceptedSequence);
            await fileReady[2].Task.WaitAsync(Limit); Assert.IsTrue(SpinWait.SpinUntil(() => runtime.GetDiagnostics().Outputs[1].Written == 3, Limit));
            CoreLogDiagnostics held = runtime.GetDiagnostics(); Assert.AreEqual(1L, held.TruncatedEventCount); Assert.AreEqual(2L, held.Outputs[0].Pending); Assert.AreEqual(1L, held.Outputs[0].QueueFull);
            Assert.IsFalse(lastCaptureLoss); JsonObject original = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(file.ToArray()).TrimEnd('\n').Split('\n')[^1])!.AsObject();
            Assert.IsNull(original["truncated"]); Assert.AreEqual(8192, original["attributes"]!["payload"]!.GetValue<string>().Length);
        }
        finally { console.Release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
    }
    [TestMethod]
    [DataRow("closure")]
    [DataRow("fault")]
    public async Task LateClosureOrFaultKeepsLossPreparedFromTheInitialHealthyInterest(string mode)
    {
        bool fault = mode == "fault"; using var clock = new BlockingClock(fault ? 2 : 1); using var console = new HeldStream(fault); using var file = new MemoryStream();
        CoreLogRuntime runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { FilePath = "controlled.jsonl", ConsoleFormat = LogOutputFormat.Human, HumanDetail = HumanOutputDetail.Verbose },
            item => file.WriteAsync(item.Event.JsonLine), clock, _ => console);
        CoreLogger logger = runtime.CreateLogger("LateLoss"); Task<LogWriteResult>? producer = null;
        try
        {
            if (fault) { Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, SmallData()).Outcome); await console.Started.Task.WaitAsync(Limit); }
            producer = Task.Run(() => logger.Write(CoreLogLevel.Information, LossData())); await clock.Started.Task.WaitAsync(Limit);
            Assert.AreEqual(0L, runtime.GetDiagnostics().TruncatedEventCount);
            if (fault)
            {
                console.Release.TrySetResult(); await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Console!.Completion.WaitAsync(Limit));
                Assert.AreEqual(LogOutputState.Faulted, runtime.GetDiagnostics().Outputs[0].State);
            }
            else { runtime.CloseAdmission(); }
            clock.Release.Set(); LogWriteResult result = await producer.WaitAsync(Limit);
            Assert.AreEqual(fault ? LogWriteOutcome.PartiallyAccepted : LogWriteOutcome.Stopped, result.Outcome); Assert.IsTrue(result.IsTruncated);
            Assert.AreEqual(fault ? LogOutputWriteStatus.Faulted : LogOutputWriteStatus.Stopped, result.Outputs[0].Status);
            Assert.AreEqual(1L, runtime.GetDiagnostics().TruncatedEventCount); Assert.AreEqual(0L, runtime.GetDiagnostics().InvalidEventCount);
            runtime.CloseAdmission();
            if (fault) { await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Completion.WaitAsync(Limit)); Assert.AreEqual(2L, runtime.GetDiagnostics().Outputs[1].Written); }
            else { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); Assert.AreEqual(0L, runtime.GetDiagnostics().Outputs[0].Accepted); Assert.AreEqual(0L, runtime.GetDiagnostics().Outputs[1].Accepted); }
        }
        finally
        {
            clock.Release.Set(); console.Release.TrySetResult(); runtime.CloseAdmission(); if (producer is not null) { await producer.WaitAsync(Limit); }
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) when (fault) { }
        }
    }
    [TestMethod]
    public async Task AlreadyFaultedConsoleAddsNoDisplayLossWhileFileKeepsTheFullEvent()
    {
        using var console = new HeldStream(true); using var file = new MemoryStream();
        CoreLogRuntime runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(),
            new LoggingOptions { FilePath = "controlled.jsonl", ConsoleFormat = LogOutputFormat.Human, HumanDetail = HumanOutputDetail.Verbose },
            item => file.WriteAsync(item.Event.JsonLine), acquireConsole: _ => console);
        CoreLogger logger = runtime.CreateLogger("FaultedLoss");
        try
        {
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, SmallData()).Outcome); await console.Started.Task.WaitAsync(Limit);
            console.Release.TrySetResult(); await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Console!.Completion.WaitAsync(Limit));
            LogWriteResult result = logger.Write(CoreLogLevel.Information, LossData()); Assert.AreEqual(LogWriteOutcome.PartiallyAccepted, result.Outcome);
            Assert.AreEqual(LogOutputWriteStatus.Faulted, result.Outputs[0].Status); Assert.IsFalse(result.IsTruncated);
            runtime.CloseAdmission(); await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Completion.WaitAsync(Limit));
            CoreLogDiagnostics final = runtime.GetDiagnostics(); Assert.AreEqual(0L, final.TruncatedEventCount); Assert.AreEqual(0L, final.InvalidEventCount); Assert.AreEqual(2L, final.Outputs[1].Written);
            JsonObject original = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(file.ToArray()).TrimEnd('\n').Split('\n')[^1])!.AsObject();
            Assert.IsNull(original["truncated"]); Assert.AreEqual(8192, original["message"]!.GetValue<string>().Length); Assert.AreEqual(8192, original["attributes"]!["payload"]!.GetValue<string>().Length);
        }
        finally { console.Release.TrySetResult(); runtime.CloseAdmission(); try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) { } }
    }
    private static string Folder() => Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!;
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), "console-composition-cases.json")))!.AsObject();
    private static JsonObject Row(JsonObject catalog, string id) => catalog["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
