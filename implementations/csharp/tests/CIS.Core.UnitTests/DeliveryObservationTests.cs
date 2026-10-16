using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class DeliveryObservationTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task CompletePartialAndUnresolvedStreamsKeepTruthfulBoundaries(string id)
    { JsonObject row = Row(id); DeliveryObservationExpectations.Validate(row, await DeliveryObservationFixture.ExecuteAsync(row)); }
    [TestMethod]
    [DataRow("early-written")]
    [DataRow("lost-inflight")]
    [DataRow("partial-as-written")]
    [DataRow("full-as-written")]
    [DataRow("cancel-as-failed")]
    [DataRow("global-drift")]
    [DataRow("ticket-drift")]
    [DataRow("retry")]
    [DataRow("bytes")]
    [DataRow("owned-bytes")]
    public async Task DeliveryMatcherRejectsPrematureConfirmationAndUntruthfulObservations(string fault)
    {
        string id = fault == "full-as-written" ? "DELIVERY-FILE-COMPLETE-FAILURE" : fault == "partial-as-written" ? "DELIVERY-CONSOLE-PARTIAL-FAILURE" : fault == "cancel-as-failed" ? "DELIVERY-CONSOLE-PARTIAL-CANCEL" : "DELIVERY-BOTH-PARTIAL-SUCCESS";
        JsonObject row = Row(id), actual = await DeliveryObservationFixture.ExecuteAsync(row);
        DeliveryObservationExpectations.Validate(row, actual);
        string name = fault == "full-as-written" ? "file" : "console";
        JsonNode held = actual["checkpoints"]![1]!["observation"]!["outputs"]![name]!, final = actual["checkpoints"]![5]!["observation"]!["outputs"]![name]!;
        JsonNode wire = actual["wires"]![name]!;
        switch (fault)
        {
            case "early-written": held["written"] = "1"; held["pending"] = "2"; break;
            case "lost-inflight": held["inFlight"] = "0"; break;
            case "partial-as-written": case "full-as-written": final["written"] = "1"; final["failed"] = "0"; wire["confirmed"] = new JsonArray(1L); break;
            case "cancel-as-failed": actual["checkpoints"]![2]!["observation"]!["outputs"]![name]!["failed"] = "1"; break;
            case "global-drift": actual["checkpoints"]![0]!["observation"]!["invalid"] = "1"; break;
            case "ticket-drift": held["sequence"] = "4"; break;
            case "retry": wire["attempted"]!.AsArray().Add(wire["attempted"]![0]!.DeepClone()); break;
            case "bytes": wire["stream"] = Convert.ToBase64String(Convert.FromBase64String(wire["stream"]!.GetValue<string>())[..^1]); break;
            default: wire["attempted"]![0]!["after"] = Convert.ToBase64String([0x0a]); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => DeliveryObservationExpectations.Validate(row, actual));
    }
    [TestMethod]
    public void LiveAccountingRefusesToInventFactsForAdmissionDoubles()
    {
        var runtime = new CoreLogRuntime(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { ConsoleLevel = CoreLogLevel.None }, null, null);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CaptureAccounting());
    }
    [TestMethod]
    public async Task SynchronousTerminalWriteFailureIsFailedWithItsOriginalException()
    {
        var original = new IOException("synchronous writer failure"); int calls = 0;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions(), _ => { Interlocked.Increment(ref calls); throw original; }, null);
        try
        {
            runtime.CreateLogger("SyncDelivery").Write(CoreLogLevel.Warning, new LogEventData { EventName = "event", Message = "record" });
            IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10))); Assert.AreSame(original, observed);
            RuntimeAccounting boundary = runtime.CaptureAccounting(); OutputAccounting output = boundary.Console!.Value;
            Assert.AreEqual(1L, output.AcceptedSequence); Assert.AreEqual(1L, output.Accepted); Assert.AreEqual(0L, output.Written); Assert.AreEqual(1L, output.Failed); Assert.AreEqual(0L, output.Abandoned); Assert.AreEqual(0, output.Pending); Assert.AreEqual(0, output.InFlight); Assert.AreEqual(1, calls);
            Assert.IsNull(boundary.File); Assert.IsFalse(boundary.AdmissionClosed);
        }
        finally { runtime.CloseAdmission(); try { await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException error) when (ReferenceEquals(error, original)) { } }
    }
    [TestMethod]
    public async Task LiveRuntimeBoundariesStayConsistentDuringConcurrentFanoutAndGlobalLoss()
    {
        var consoleReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var fileReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int arrivals = 0;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { FilePath = "control.jsonl", QueueCapacity = 128 },
            async _ => { consoleReady.TrySetResult(); await release.Task; }, async _ => { fileReady.TrySetResult(); await release.Task; });
        CoreLogger logger = runtime.CreateLogger("ConcurrentBoundary"); Task[] producers = []; var snapshots = new List<RuntimeAccounting>();
        try
        {
            logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "event", Message = "seed" }); await Task.WhenAll(consoleReady.Task, fileReady.Task).WaitAsync(TimeSpan.FromSeconds(10)); snapshots.Add(runtime.CaptureAccounting());
            producers = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 32) { ready.TrySetResult(); }
                await go.Task;
                for (int item = 0; item < 4; item++) { Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "event", Message = "record-" + index + "-" + item }).Outcome); }
                Assert.AreEqual(LogWriteOutcome.InvalidEvent, logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "", Message = "invalid" }).Outcome);
            })).ToArray();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); go.TrySetResult();
            for (int index = 0; index < 500; index++) { snapshots.Add(runtime.CaptureAccounting()); await Task.Yield(); }
            await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10)); RuntimeAccounting held = runtime.CaptureAccounting(); snapshots.Add(held);
            Assert.AreEqual(129L, held.Console!.Value.Accepted); Assert.AreEqual(32L, held.InvalidEventCount); Assert.AreEqual(0L, held.TruncatedEventCount);
            foreach (RuntimeAccounting snapshot in snapshots)
            {
                OutputAccounting console = snapshot.Console!.Value, file = snapshot.File!.Value;
                Assert.AreEqual(console.AcceptedSequence, file.AcceptedSequence); Assert.AreEqual(console.Accepted, file.Accepted);
                foreach (OutputAccounting value in new[] { console, file }) { Assert.AreEqual(value.AcceptedSequence, value.Accepted); Assert.AreEqual(value.Accepted, value.Written + value.Failed + value.Abandoned + value.Pending); Assert.AreEqual(1, value.InFlight); Assert.AreEqual(0L, value.Written); Assert.IsTrue(value.Pending is >= 1 and <= 129); }
                Assert.IsTrue(snapshot.InvalidEventCount is >= 0 and <= 32); Assert.IsFalse(snapshot.AdmissionClosed);
            }
            runtime.CloseAdmission(); release.TrySetResult(); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); RuntimeAccounting final = runtime.CaptureAccounting();
            Assert.IsTrue(final.AdmissionClosed); Assert.AreEqual(129L, final.Console!.Value.Written); Assert.AreEqual(129L, final.File!.Value.Written); Assert.AreEqual(0, final.Console.Value.Pending); Assert.AreEqual(0, final.File.Value.Pending); Assert.AreEqual(32L, final.InvalidEventCount); Assert.AreEqual(1L, snapshots[0].Console!.Value.Accepted); Assert.AreEqual(0L, snapshots[0].InvalidEventCount);
        }
        finally { go.TrySetResult(); release.TrySetResult(); runtime.CloseAdmission(); await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10)); await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "delivery-observation-cases.json")))!.AsObject();
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
