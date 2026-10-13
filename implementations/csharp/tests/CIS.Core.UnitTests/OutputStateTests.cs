using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OutputStateTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task RealOutputTransitionsRetainSafeFaultsAndIsolateHealthyWorkers(string id)
    { JsonObject row = Row(id); OutputStateExpectations.Validate(row, await OutputStateFixture.ExecuteAsync(row)); }
    [TestMethod]
    [DataRow("fault")]
    [DataRow("time")]
    [DataRow("state")]
    [DataRow("abandon")]
    [DataRow("retry")]
    [DataRow("private")]
    [DataRow("owned")]
    public async Task StateMatcherRejectsLostFaultsRetriesAndRewrittenBoundaries(string fault)
    {
        JsonObject row = Row("STATE-ISOLATE-FILE"), actual = await OutputStateFixture.ExecuteAsync(row);
        OutputStateExpectations.Damage(actual, fault);
        Assert.ThrowsExactly<InvalidDataException>(() => OutputStateExpectations.Validate(row, actual));
    }
    [TestMethod]
    [DataRow("io")]
    [DataRow("cancel")]
    [DataRow("access")]
    [DataRow("unexpected")]
    public async Task SynchronousWriterErrorsHaveOneSafeCodeAndOriginalCompletion(string kind)
    {
        Exception original = kind switch { "io" => new IOException("private path"), "cancel" => new OperationCanceledException("private token"), "access" => new UnauthorizedAccessException("private identity"), _ => new InvalidOperationException("private payload") };
        int calls = 0; DateTimeOffset before = DateTimeOffset.UtcNow;
        var queue = new OutputEventQueue(1, _ => { Interlocked.Increment(ref calls); throw original; });
        try
        {
            Assert.IsTrue(queue.TryEnqueue(Event()));
            Exception observed = await Assert.ThrowsAsync<Exception>(async () => await queue.Completion.WaitAsync(Limit)); Assert.AreSame(original, observed);
            OutputAccounting fault = queue.CaptureAccounting(); Assert.AreEqual(LogOutputState.Faulted, fault.State); Assert.AreEqual("WriteFailed", fault.LastFaultCode);
            Assert.IsTrue(fault.LastFaultAtUtc >= before && fault.LastFaultAtUtc <= DateTimeOffset.UtcNow); Assert.AreEqual(TimeSpan.Zero, fault.LastFaultAtUtc!.Value.Offset);
            Assert.AreEqual(1L, fault.Accepted); Assert.AreEqual(1L, fault.Failed); Assert.AreEqual(0L, fault.Written); Assert.AreEqual(0L, fault.Abandoned); Assert.AreEqual(0, fault.Pending);
            queue.Complete(); OutputAccounting stopped = queue.CaptureAccounting(); Assert.AreEqual(LogOutputState.Stopped, stopped.State); Assert.AreEqual(fault with { State = LogOutputState.Stopped }, stopped);
            Assert.AreEqual(1, calls);
        }
        finally { queue.Complete(); try { await queue.Completion.WaitAsync(Limit); } catch (Exception error) when (ReferenceEquals(error, original)) { } }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdmissionExhaustionAbandonsOnlyUnstartedWorkAndRetainsLastFault(bool failInFlight)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new IOException("private in-flight failure"); int calls = 0;
        var queue = new OutputEventQueue(2, async _ => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; if (failInFlight) { throw original; } });
        typeof(OutputEventQueue).GetField("lastAcceptedSequence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue - 2);
        try
        {
            Assert.IsTrue(queue.TryEnqueue(Event())); await entered.Task.WaitAsync(Limit); Assert.IsTrue(queue.TryEnqueue(Event()));
            Assert.AreEqual(LogOutputWriteStatus.Faulted, queue.TryAdmit(Event(), out long? ticket)); Assert.IsNull(ticket);
            OutputAccounting fault = queue.CaptureAccounting(); Assert.AreEqual(LogOutputState.Faulted, fault.State); Assert.AreEqual("AdmissionExhausted", fault.LastFaultCode); Assert.IsNotNull(fault.LastFaultAtUtc);
            Assert.AreEqual(2L, fault.Accepted); Assert.AreEqual(1L, fault.Abandoned); Assert.AreEqual(1, fault.Pending); Assert.AreEqual(1, fault.InFlight); Assert.AreEqual(0L, fault.Failed); Assert.AreEqual(0L, fault.Written);
            queue.Complete(); Assert.AreEqual(LogOutputState.Stopping, queue.CaptureAccounting().State); release.TrySetResult();
            try { await queue.Completion.WaitAsync(Limit); Assert.IsFalse(failInFlight); } catch (Exception error) when (ReferenceEquals(error, original)) { Assert.IsTrue(failInFlight); }
            OutputAccounting stopped = queue.CaptureAccounting(); Assert.AreEqual(LogOutputState.Stopped, stopped.State); Assert.AreEqual(failInFlight ? "WriteFailed" : "AdmissionExhausted", stopped.LastFaultCode);
            Assert.AreEqual(failInFlight ? 1L : 0L, stopped.Failed); Assert.AreEqual(failInFlight ? 0L : 1L, stopped.Written); Assert.AreEqual(1L, stopped.Abandoned); Assert.AreEqual(0, stopped.Pending); Assert.AreEqual(1, calls);
            Assert.AreEqual("AdmissionExhausted", fault.LastFaultCode); Assert.AreEqual(1, fault.Pending);
            if (!failInFlight) { Assert.AreEqual(fault.LastFaultAtUtc, stopped.LastFaultAtUtc); }
        }
        finally { release.TrySetResult(); queue.Complete(); try { await queue.Completion.WaitAsync(Limit); } catch (Exception error) when (ReferenceEquals(error, original)) { } }
    }
    [TestMethod]
    public async Task ConcurrentFaultAndStopCannotExposeTerminalFaultWithUnclassifiedQueuedWork()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new IOException("private race"); int calls = 0;
        var queue = new OutputEventQueue(2, async _ => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; throw original; });
        Task[] stoppers = []; Task? observer = null; var samples = new List<OutputAccounting>();
        try
        {
            queue.TryEnqueue(Event()); await entered.Task.WaitAsync(Limit); queue.TryEnqueue(Event()); queue.TryEnqueue(Event()); OutputAccounting held = queue.CaptureAccounting();
            stoppers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () => { await start.Task; queue.Complete(); })).ToArray();
            observer = Task.Run(async () => { await start.Task; for (int index = 0; index < 500; index++) { samples.Add(queue.CaptureAccounting()); await Task.Yield(); } });
            start.TrySetResult(); release.TrySetResult(); await Task.WhenAll(stoppers.Append(observer)).WaitAsync(Limit);
            try { await queue.Completion.WaitAsync(Limit); Assert.Fail("Original fault missing."); } catch (Exception error) when (ReferenceEquals(error, original)) { }
            samples.Add(queue.CaptureAccounting());
            foreach (OutputAccounting counts in samples)
            {
                Assert.AreEqual(3L, counts.Accepted); Assert.AreEqual(counts.Accepted, counts.Written + counts.Failed + counts.Abandoned + counts.Pending);
                if (counts.LastFaultCode is not null) { Assert.AreEqual("WriteFailed", counts.LastFaultCode); Assert.IsNotNull(counts.LastFaultAtUtc); Assert.AreEqual(1L, counts.Failed); Assert.AreEqual(2L, counts.Abandoned); Assert.AreEqual(0, counts.Pending); Assert.AreEqual(0, counts.InFlight); }
            }
            OutputAccounting final = samples.Last(); Assert.AreEqual(LogOutputState.Stopped, final.State); Assert.AreEqual("WriteFailed", final.LastFaultCode); Assert.AreEqual(1, calls);
            Assert.AreEqual(LogOutputState.Running, held.State); Assert.IsNull(held.LastFaultCode); Assert.AreEqual(3, held.Pending);
        }
        finally { start.TrySetResult(); release.TrySetResult(); queue.Complete(); await Task.WhenAll(stoppers).WaitAsync(Limit); if (observer is not null) { await observer.WaitAsync(Limit); } try { await queue.Completion.WaitAsync(Limit); } catch (Exception error) when (ReferenceEquals(error, original)) { } }
    }
    [TestMethod]
    public void StateNumbersMatchSelectedContract()
    {
        Assert.AreEqual(0, (int)LogOutputState.Running); Assert.AreEqual(1, (int)LogOutputState.Faulted);
        Assert.AreEqual(2, (int)LogOutputState.Stopping); Assert.AreEqual(3, (int)LogOutputState.Stopped);
        Assert.AreEqual(4, Enum.GetValues<LogOutputState>().Length);
    }
    private static EncodedEvent Event() => new("{\"event\":1}\n"u8.ToArray(), false);
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "output-state-cases.json")))!.AsObject();
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
