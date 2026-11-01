using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OutputQueueTests
{
    private static readonly Lazy<LoggingFixtureInputs> Inputs = new(() => new("output-queue-cases.json"));

    public static IEnumerable<object[]> Rows => Inputs.Value.Ids.Select(id => new object[] { id });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualQueuesKeepBoundedSequentialIndependentWorkers(string id)
    {
        JsonObject row = Row(id); OutputQueueExpectations.Validate(row, await OutputQueueFixture.ExecuteAsync(row));
    }
    [TestMethod]
    [DataRow(0)]
    [DataRow(65537)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void InvalidCapacityFailsBeforeStartingAnyWorker(int capacity)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OutputEventQueue(capacity, _ => ValueTask.CompletedTask));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OutputQueues(capacity, false, false, null, null));
    }
    [TestMethod]
    public void EnabledPresenceAndNullInputsAreValidatedBeforeWork()
    {
        Func<AdmittedEvent, ValueTask> write = _ => throw new AssertFailedException("Validation started work.");
        Assert.ThrowsExactly<ArgumentNullException>(() => new OutputEventQueue(1, null!));
        Assert.ThrowsExactly<ArgumentException>(() => new OutputQueues(1, true, false, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => new OutputQueues(1, false, true, null, null));
        Assert.ThrowsExactly<ArgumentException>(() => new OutputQueues(1, false, false, write, null));
        Assert.ThrowsExactly<ArgumentException>(() => new OutputQueues(1, true, false, write, write));
        var queue = new OutputEventQueue(1, write); try { Assert.ThrowsExactly<ArgumentNullException>(() => queue.TryEnqueue(null!)); } finally { queue.Complete(); }
        queue.Completion.GetAwaiter().GetResult();
    }
    [TestMethod]
    public async Task MaximumCapacityAndAlreadySuppressedCreationCloseCleanly()
    {
        OutputEventQueue queue;
        using (ExecutionContext.SuppressFlow()) { queue = new OutputEventQueue(65536, _ => ValueTask.CompletedTask); }
        queue.Complete(); queue.Complete(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, queue.BufferedCount); Assert.IsFalse(queue.TryEnqueue(Event(1)));
    }
    [TestMethod]
    public async Task FailedWriterClosesAdmissionReleasesUnattemptedWorkAndPreservesException()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new IOException("controlled failure"); int calls = 0;
        var queue = new OutputEventQueue(2, async _ => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; throw original; });
        try
        {
            Assert.IsTrue(queue.TryEnqueue(Event(0))); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(queue.TryEnqueue(Event(1))); Assert.IsTrue(queue.TryEnqueue(Event(2))); Assert.AreEqual(2, queue.BufferedCount);
            release.TrySetResult(); IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.AreSame(original, observed); Assert.IsFalse(queue.TryEnqueue(Event(3))); Assert.AreEqual(0, queue.BufferedCount); Assert.AreEqual(1, calls);
        }
        finally { release.TrySetResult(); queue.Complete(); try { await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException error) when (ReferenceEquals(error, original)) { } }
    }
    [TestMethod]
    public async Task SynchronousWriterFailureIsObservedOnlyOnWorkerCompletion()
    {
        var original = new IOException("synchronous controlled failure");
        var queue = new OutputEventQueue(1, _ => throw original);
        Assert.IsTrue(queue.TryEnqueue(Event(0)));
        IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() => queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreSame(original, observed); Assert.IsFalse(queue.TryEnqueue(Event(1))); Assert.AreEqual(0, queue.BufferedCount);
    }
    [TestMethod]
    public async Task ConcurrentProducersShareOnlyTheBoundedBufferAndOneWriter()
    {
        const int capacity = 16, producers = 64;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProducers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new List<EncodedEvent>(); var accepted = new ConcurrentBag<EncodedEvent>(); int arrivals = 0, active = 0, maximum = 0;
        var queue = new OutputEventQueue(capacity, async item =>
        {
            int now = Interlocked.Increment(ref active); OutputQueueFixture.RecordMaximum(ref maximum, now);
            try { if (delivered.Count == 0) { entered.TrySetResult(); await releaseWriter.Task; } delivered.Add(item.Event); }
            finally { Interlocked.Decrement(ref active); }
        });
        Task[] workers = [];
        EncodedEvent first = Event(0);
        try
        {
            Assert.IsTrue(queue.TryEnqueue(first)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            workers = Enumerable.Range(1, producers).Select(index => Task.Run(async () =>
            {
                EncodedEvent item = Event(index); if (Interlocked.Increment(ref arrivals) == producers) { ready.TrySetResult(); }
                await releaseProducers.Task; if (queue.TryEnqueue(item)) { accepted.Add(item); }
            })).ToArray();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); releaseProducers.TrySetResult(); await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(capacity, accepted.Count); Assert.AreEqual(capacity, queue.BufferedCount);
            queue.Complete(); releaseWriter.TrySetResult(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, maximum); Assert.AreSame(first, delivered[0]); Assert.AreEqual(capacity + 1, delivered.Count);
            CollectionAssert.AreEquivalent(accepted.ToArray(), delivered.Skip(1).ToArray()); Assert.AreEqual(0, queue.BufferedCount);
        }
        finally { releaseProducers.TrySetResult(); releaseWriter.TrySetResult(); queue.Complete(); await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10)); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    [DataRow("admission")]
    [DataRow("pending")]
    [DataRow("parallel")]
    [DataRow("inline")]
    [DataRow("ambient")]
    [DataRow("ownership")]
    [DataRow("bytes")]
    [DataRow("worker")]
    [DataRow("closed")]
    public async Task ComparatorRejectsChangedBoundsIsolationBytesAndOwnership(string fault)
    {
        JsonObject row = Row("QUEUE-BOTH-2"), actual = await OutputQueueFixture.ExecuteAsync(row), output = actual["outputs"]!["file"]!.AsObject();
        OutputQueueExpectations.Validate(row, actual);
        switch (fault)
        {
            case "admission": output["admissions"]![3] = true; break;
            case "pending": output["pendingAtBarrier"] = 3; break;
            case "parallel": output["maximumActive"] = 2; break;
            case "inline": output["inlineSeen"] = true; break;
            case "ambient": output["ambientSeen"] = true; break;
            case "ownership": output["ownedReferences"] = false; break;
            case "bytes": output["stream"] = Convert.ToBase64String([10]); break;
            case "worker": actual["workers"] = 1; break;
            default: output["admittedAfterComplete"] = true; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => OutputQueueExpectations.Validate(row, actual));
    }
    [TestMethod]
    [DataRow("worker-set")]
    [DataRow("schema")]
    [DataRow("event")]
    [DataRow("status")]
    [DataRow("component")]
    [DataRow("raw-cr")]
    [DataRow("framing")]
    public async Task QueueMatcherRejectsExtraWorkersAndChangedOriginalRecords(string fault)
    {
        JsonObject row = Row("QUEUE-BOTH-2"), actual = await OutputQueueFixture.ExecuteAsync(row);
        OutputQueueExpectations.Validate(row, actual);
        if (fault == "worker-set") { actual["outputs"]!["invented"] = new JsonObject(); }
        else
        {
            JsonNode output = actual["outputs"]!["file"]!;
            string raw = Encoding.UTF8.GetString(Convert.FromBase64String((string)output["stream"]!));
            if (fault == "framing") { raw += "\n"; }
            else if (fault == "raw-cr") { raw = raw[..^1] + "\r\n"; }
            else
            {
                string[] lines = raw.TrimEnd('\n').Split('\n');
                JsonObject record = JsonNode.Parse(lines[0])!.AsObject();
                if (fault == "schema") { record["schema_version"] = 2; }
                else { record[fault == "event" ? "event_name" : fault] = "changed"; }
                lines[0] = record.ToJsonString(); raw = string.Join("\n", lines) + "\n";
            }
            output["stream"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            output["expectedStream"] = output["stream"]!.DeepClone();
        }
        Assert.ThrowsExactly<InvalidDataException>(() => OutputQueueExpectations.Validate(row, actual));
    }

    [TestMethod]
    public void MatrixIncludesCapacityAndEnabledCombinationsPlusIndependentProgress()
    {
        JsonArray rows = Catalog()["cases"]!.AsArray(); Assert.AreEqual(11, rows.Count);
        Assert.AreEqual(rows.Count, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        foreach (int capacity in new[] { 1, 2, 8 })
        {
            Assert.AreEqual(3, rows.Count(row => row!["capacity"]!.GetValue<int>() == capacity && row["mode"]!.GetValue<string>() == "bounded" && row["expected"]!["workers"]!.GetValue<int>() > 0));
        }
        Assert.AreEqual(1, rows.Count(row => row!["mode"]!.GetValue<string>() == "independent"));
        Assert.AreEqual(1, rows.Count(row => row!["expected"]!["workers"]!.GetValue<int>() == 0));
    }
    private static EncodedEvent Event(int index) => new(System.Text.Encoding.UTF8.GetBytes("{\"index\":" + index + "}\n"), false);
    private static JsonObject Row(string id) => Inputs.Value.Row(id);
    private static JsonObject Catalog() => Inputs.Value.Catalog();
}
