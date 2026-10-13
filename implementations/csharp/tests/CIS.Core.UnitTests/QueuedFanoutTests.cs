using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class QueuedFanoutTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task RealRuntimeFanoutPublishesTicketsBeforeIndependentWriterWork(string id)
    { JsonObject row = Row(id); QueuedFanoutExpectations.Validate(row, await QueuedFanoutFixture.ExecuteAsync(row)); }
    [TestMethod]
    [DataRow("console", 7)]
    [DataRow("file", -1)]
    [DataRow("bytes", 1)]
    [DataRow("bytes", 16777217)]
    [DataRow("queue", 0)]
    [DataRow("queue", 65537)]
    public void InvalidRuntimeSettingsFailBeforeWriterWork(string field, int value)
    {
        var options = new LoggingOptions { FilePath = "control.jsonl" }; int writes = 0;
        if (field == "console") { options.ConsoleLevel = (CoreLogLevel)value; } else if (field == "file") { options.FileLevel = (CoreLogLevel)value; } else if (field == "queue") { options.QueueCapacity = value; } else { options.MaxEntryBytes = value; }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), options, _ => { writes++; return ValueTask.CompletedTask; }, _ => { writes++; return ValueTask.CompletedTask; }));
        Assert.AreEqual(0, writes);
    }
    [TestMethod]
    public void CreationValidatesRequiredFactsAndWriterPresence()
    {
        var metadata = EventQualificationFixture.Metadata().CaptureMetadata(); var options = new LoggingOptions();
        Assert.ThrowsExactly<ArgumentNullException>(() => CoreLogRuntime.CreateQueued(null!, options, null, null));
        Assert.ThrowsExactly<ArgumentNullException>(() => CoreLogRuntime.CreateQueued(metadata, null!, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => CoreLogRuntime.CreateQueued(metadata, options, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => CoreLogRuntime.CreateQueued(metadata, options, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask));
    }
    [TestMethod]
    public async Task SuccessfulTicketsRemainContiguousAcrossAFullQueue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var delivered = new List<AdmittedEvent>();
        var queue = new OutputEventQueue(1, async item => { if (delivered.Count == 0) { entered.TrySetResult(); await release.Task; } delivered.Add(item); if (delivered.Count == 2) { second.TrySetResult(); } });
        try
        {
            Assert.AreEqual(LogOutputWriteStatus.Accepted, queue.TryAdmit(Event(1), out long? first)); Assert.AreEqual(1L, first); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(LogOutputWriteStatus.Accepted, queue.TryAdmit(Event(2), out long? next)); Assert.AreEqual(2L, next);
            Assert.AreEqual(LogOutputWriteStatus.QueueFull, queue.TryAdmit(Event(3), out long? rejected)); Assert.IsNull(rejected); Assert.AreEqual(2L, queue.LastAcceptedSequence);
            release.TrySetResult(); await second.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.AreEqual(LogOutputWriteStatus.Accepted, queue.TryAdmit(Event(4), out long? after)); Assert.AreEqual(3L, after);
            queue.Complete(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, delivered.Select(item => item.AcceptedSequence).ToArray());
            Assert.AreEqual(LogOutputWriteStatus.Stopped, queue.TryAdmit(Event(5), out long? closed)); Assert.IsNull(closed); Assert.AreEqual(3L, queue.LastAcceptedSequence);
        }
        finally { release.TrySetResult(); queue.Complete(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    public async Task SequenceExhaustionDoesNotWrapOrInventAnotherAcceptance()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var delivered = new List<long>();
        var queue = new OutputEventQueue(1, async item => { entered.TrySetResult(); await release.Task; delivered.Add(item.AcceptedSequence); });
        typeof(OutputEventQueue).GetField("lastAcceptedSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue - 1);
        try
        {
            Assert.AreEqual(LogOutputWriteStatus.Accepted, queue.TryAdmit(Event(1), out long? last)); Assert.AreEqual(long.MaxValue, last); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(LogOutputWriteStatus.Faulted, queue.TryAdmit(Event(2), out long? rejected)); Assert.IsNull(rejected); Assert.AreEqual(long.MaxValue, queue.LastAcceptedSequence); Assert.IsTrue(queue.IsFaulted);
            release.TrySetResult(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); CollectionAssert.AreEqual(new[] { long.MaxValue }, delivered); Assert.AreEqual(0, queue.BufferedCount); Assert.AreEqual(1L, queue.UnavailableCount);
        }
        finally { release.TrySetResult(); queue.Complete(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    public async Task RuntimeExhaustionRejectsOnlyThatOutputAndReleasesQueuedWork()
    {
        var consoleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fileEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consoleWrites = new List<long>(); var fileWrites = new List<long>();
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { FilePath = "control.jsonl", QueueCapacity = 2 },
            async item => { if (consoleWrites.Count == 0) { consoleEntered.TrySetResult(); await release.Task; } consoleWrites.Add(item.AcceptedSequence); },
            async item => { if (fileWrites.Count == 0) { fileEntered.TrySetResult(); await release.Task; } fileWrites.Add(item.AcceptedSequence); });
        OutputEventQueue file = runtime.QueuedOutputs!.File!;
        // C# unit-only private ticket injection; accounting counts actual admissions, not an invented Int64 prehistory.
        typeof(OutputEventQueue).GetField("lastAcceptedSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file, long.MaxValue - 2);
        try
        {
            CoreLogger logger = runtime.CreateLogger("Exhaustion");
            LogWriteResult first = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "first" });
            Assert.AreEqual(LogWriteOutcome.Accepted, first.Outcome); Assert.AreEqual(long.MaxValue - 1, first.Outputs[1].AcceptedSequence);
            await consoleEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await fileEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            LogWriteResult queued = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "queued" });
            Assert.AreEqual(LogWriteOutcome.Accepted, queued.Outcome); Assert.AreEqual(long.MaxValue, queued.Outputs[1].AcceptedSequence); Assert.AreEqual(1, file.BufferedCount);
            LogWriteResult exhausted = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "exhausted" });
            Assert.AreEqual(LogWriteOutcome.PartiallyAccepted, exhausted.Outcome); Assert.AreEqual(3L, exhausted.Outputs[0].AcceptedSequence); Assert.AreEqual(LogOutputWriteStatus.Faulted, exhausted.Outputs[1].Status); Assert.IsNull(exhausted.Outputs[1].AcceptedSequence);
            Assert.AreEqual(long.MaxValue, file.LastAcceptedSequence); Assert.IsTrue(file.IsFaulted); Assert.AreEqual(1L, file.UnavailableCount);
            Assert.AreEqual(new OutputAccounting(Filtered: 0, Accepted: 2, QueueFull: 0, Unavailable: 1,
                Written: 0, Failed: 0, Abandoned: 1, Pending: 1, InFlight: 1, AcceptedSequence: long.MaxValue), Counts(file.CaptureAccounting()));
            runtime.CloseAdmission(); release.TrySetResult(); await runtime.QueuedOutputs.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, consoleWrites); CollectionAssert.AreEqual(new[] { long.MaxValue - 1 }, fileWrites); Assert.AreEqual(0, file.BufferedCount);
            Assert.AreEqual(new OutputAccounting(Filtered: 0, Accepted: 2, QueueFull: 0, Unavailable: 1,
                Written: 1, Failed: 0, Abandoned: 1, Pending: 0, InFlight: 0, AcceptedSequence: long.MaxValue), Counts(file.CaptureAccounting()));
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [TestMethod]
    public async Task WriterCallbackCanCloseAdmissionFromAnotherThread()
    {
        CoreLogRuntime? runtime = null; int written = 0;
        async ValueTask Write(AdmittedEvent item) { await Task.Run(runtime!.CloseAdmission).WaitAsync(TimeSpan.FromSeconds(10)); Interlocked.Increment(ref written); }
        runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { FilePath = "control.jsonl" }, Write, Write);
        LogWriteResult accepted = runtime.CreateLogger("Callback").Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "record" });
        try
        {
            await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); Assert.AreEqual(LogWriteOutcome.Accepted, accepted.Outcome); Assert.AreEqual(2, written);
            LogWriteResult stopped = runtime.CreateLogger("Callback").WriteLazy(CoreLogLevel.None, () => throw new AssertFailedException("Stopped factory ran.")); Assert.AreEqual(LogWriteOutcome.Stopped, stopped.Outcome); Assert.IsTrue(stopped.Outputs.All(output => output.Status == LogOutputWriteStatus.Stopped && output.AcceptedSequence is null));
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    public async Task ConcurrentClosureCannotSplitActualOutputFanout()
    {
        const int producers = 32; var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int arrivals = 0;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { FilePath = "control.jsonl", QueueCapacity = 64 }, async _ => await release.Task, async _ => await release.Task);
        CoreLogger logger = runtime.CreateLogger("Closure"); Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "seed" }).Outcome);
        Task<LogWriteResult>[] tasks = Enumerable.Range(1, producers).Select(index => Task.Run(async () => { if (Interlocked.Increment(ref arrivals) == producers) { ready.TrySetResult(); } await go.Task; return logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "record-" + index }); })).ToArray();
        Task close = Task.Run(async () => { await go.Task; runtime.CloseAdmission(); });
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); go.TrySetResult(); LogWriteResult[] results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)); await close.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (LogWriteResult result in results) { Assert.IsTrue(result.Outcome is LogWriteOutcome.Accepted or LogWriteOutcome.Stopped); Assert.AreEqual(2, result.Outputs.Count); Assert.IsTrue(result.Outputs.All(output => output.Status == (result.Outcome == LogWriteOutcome.Accepted ? LogOutputWriteStatus.Accepted : LogOutputWriteStatus.Stopped))); if (result.Outcome == LogWriteOutcome.Accepted) { Assert.AreEqual(result.Outputs[0].AcceptedSequence, result.Outputs[1].AcceptedSequence); } else { Assert.IsTrue(result.Outputs.All(output => output.AcceptedSequence is null)); } }
            Assert.AreEqual(1L + results.Count(result => result.Outcome == LogWriteOutcome.Accepted), runtime.QueuedOutputs!.Console!.LastAcceptedSequence); Assert.AreEqual(runtime.QueuedOutputs.Console.LastAcceptedSequence, runtime.QueuedOutputs.File!.LastAcceptedSequence);
        }
        finally { go.TrySetResult(); release.TrySetResult(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)); await close.WaitAsync(TimeSpan.FromSeconds(10)); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    public async Task InvalidCaptureDoesNotAllocateATicketOrReachWriters()
    {
        var received = new List<AdmittedEvent>(); CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), item => { received.Add(item); return ValueTask.CompletedTask; }, null);
        try
        {
            CoreLogger logger = runtime.CreateLogger("Invalid"); LogWriteResult first = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "first" }); LogWriteResult invalid = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "", Message = "invalid" }); LogWriteResult after = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "after" });
            Assert.AreEqual(LogWriteOutcome.InvalidEvent, invalid.Outcome); Assert.AreEqual(0, invalid.Outputs.Count); Assert.AreEqual(1L, first.Outputs[0].AcceptedSequence); Assert.AreEqual(2L, after.Outputs[0].AcceptedSequence); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); Assert.AreEqual(2, received.Count); Assert.AreEqual(1L, runtime.InvalidEventCount);
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    [DataRow("ticket")]
    [DataRow("gap")]
    [DataRow("factory")]
    [DataRow("clock")]
    [DataRow("gate")]
    [DataRow("copy")]
    [DataRow("stream")]
    [DataRow("binding")]
    [DataRow("fault")]
    [DataRow("order")]
    [DataRow("unbound")]
    public async Task ComparatorRejectsInventedTicketsRecaptureGateAndWireChanges(string fault)
    {
        JsonObject row = Row("FANOUT-BOTH"), actual = await QueuedFanoutFixture.ExecuteAsync(row);
        switch (fault)
        {
            case "ticket": actual["semantic"]!["results"]![0]!["outputs"]![0]!["ticket"] = null; break;
            case "gap": actual["semantic"]!["outputs"]!["console"]!["lastAcceptedSequence"] = 4; break;
            case "factory": actual["semantic"]!["factoryCalls"] = 2; break;
            case "clock": actual["semantic"]!["clockCalls"] = 6; break;
            case "gate": actual["semantic"]!["callbackGateHeld"] = true; break;
            case "copy": actual["semantic"]!["sharedCaptures"] = false; break;
            case "binding": actual["bindings"]![0]!["caller"] = 2; break;
            case "fault": actual["semantic"]!["outputs"]!["file"]!["faulted"] = true; break;
            case "unbound": actual["wires"]!["console"]!["attempted"]![0]!["ticket"] = 4L; break;
            case "order": actual["semantic"]!["outputs"]!["console"]!["attemptedTickets"] = new JsonArray(3, 2, 1); break;
            default: actual["wires"]!["console"]!["stream"] = Convert.ToBase64String([10]); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => QueuedFanoutExpectations.Validate(row, actual));
    }
    [TestMethod]
    public void CatalogDeclaresAllActualRuntimePhasesWithoutClaimingPhysicalDestinations()
    { JsonArray rows = Catalog()["cases"]!.AsArray(); Assert.AreEqual(12, rows.Count); Assert.AreEqual(rows.Count, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count()); CollectionAssert.AreEquivalent(new[] { "basic", "union", "saturation", "independent", "late-close", "late-fault", "early-fault", "mutate", "multi" }, rows.Select(row => row!["mode"]!.GetValue<string>()).Distinct().ToArray()); }
    private static EncodedEvent Event(int label) => new(Encoding.UTF8.GetBytes("{\"input\":" + label + "}\n"), false);
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static OutputAccounting Counts(OutputAccounting value) => value with { State = LogOutputState.Running, LastFaultCode = null, LastFaultAtUtc = null };
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "queued-fanout-cases.json")))!.AsObject();
}
