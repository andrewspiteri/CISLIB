using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class OutputAccountingTests
{
    private static readonly Lazy<LoggingFixtureInputs> Inputs = new(() => new("output-accounting-cases.json"));

    public static IEnumerable<object[]> Rows => Inputs.Value.Ids.Select(id => new object[] { id });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task ActualWorkersKeepCumulativeCountsAndUnresolvedGauges(string id)
    { JsonObject row = Row(id); OutputAccountingExpectations.Validate(row, await OutputAccountingFixture.ExecuteAsync(row)); }
    [TestMethod]
    [DataRow("negative")]
    [DataRow("disposition")]
    [DataRow("attempts")]
    [DataRow("rejected")]
    [DataRow("inflight")]
    [DataRow("dequeued")]
    [DataRow("snapshot")]
    [DataRow("invalid")]
    [DataRow("retry")]
    public async Task AccountingMatcherRejectsInventedLostOrMutatedCounts(string fault)
    {
        JsonObject row = Row("ACCOUNT-BOTH"), actual = await OutputAccountingFixture.ExecuteAsync(row);
        OutputAccountingExpectations.Validate(row, actual);
        JsonNode counts = actual["checkpoints"]![1]!["outputs"]!["console"]!;
        switch (fault)
        {
            case "negative": counts["filtered"] = "-1"; break;
            case "disposition": counts["accepted"] = "4"; break;
            case "attempts": counts["attempts"] = "3"; break;
            case "rejected": counts["rejected"] = "0"; break;
            case "inflight": counts["inFlight"] = "2"; break;
            case "dequeued": counts["written"] = "1"; counts["pending"] = "2"; break;
            case "snapshot": actual["checkpoints"]![0]!["outputs"]!["console"] = counts.DeepClone(); break;
            case "invalid": actual["checkpoints"]![1]!["invalid"] = "4"; break;
            default: actual["writers"]!["console"]!["attempted"]!.AsArray().Add(1L); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => OutputAccountingExpectations.Validate(row, actual));
    }
    [TestMethod]
    [DataRow("filteredCount")]
    [DataRow("unavailableCount")]
    [DataRow("queueFullCount")]
    public async Task RejectionCountersAndAggregatesSaturateWithoutWrapping(string field)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { QueueCapacity = 1 }, async _ => { entered.TrySetResult(); await release.Task; }, null);
        OutputEventQueue queue = runtime.QueuedOutputs!.Console!;
        try
        {
            CoreLogger logger = runtime.CreateLogger("Saturation"); LogEventData data = new() { EventName = "event", Message = "record" };
            logger.Write(CoreLogLevel.Warning, data); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); logger.Write(CoreLogLevel.Warning, data);
            // Unit-only private seeding models representational exhaustion, not executed Int64 history.
            typeof(OutputEventQueue).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(queue, long.MaxValue - 1);
            for (int index = 0; index < 2; index++)
            {
                if (field == "filteredCount") { queue.RecordFiltered(); }
                else if (field == "unavailableCount") { queue.RecordUnavailable(); }
                else { Assert.AreEqual(LogWriteOutcome.Rejected, logger.Write(CoreLogLevel.Warning, data).Outcome); }
            }
            OutputAccounting counts = queue.CaptureAccounting(); Assert.AreEqual(2L, counts.Accepted); Assert.AreEqual(2, counts.Pending); Assert.AreEqual(1, counts.InFlight);
            if (field == "filteredCount") { Assert.AreEqual(long.MaxValue, counts.Filtered); Assert.AreEqual(2L, counts.AdmissionAttempts); }
            else { Assert.AreEqual(long.MaxValue, counts.Rejected); Assert.AreEqual(long.MaxValue, counts.AdmissionAttempts); Assert.AreEqual(long.MaxValue, field == "unavailableCount" ? counts.Unavailable : counts.QueueFull); }
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    [TestMethod]
    public async Task ConcurrentAdmissionsPreserveOneConsistentAcceptedPartition()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { QueueCapacity = 8 }, async _ => { entered.TrySetResult(); await release.Task; }, null);
        CoreLogger logger = runtime.CreateLogger("ConcurrentAccounting"); OutputEventQueue queue = runtime.QueuedOutputs!.Console!;
        var snapshots = new List<OutputAccounting>();
        try
        {
            logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "event", Message = "seed" }); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task<LogWriteResult>[] producers = Enumerable.Range(0, 64).Select(index => Task.Run(() => logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "event", Message = "record-" + index }))).ToArray();
            foreach (Task<LogWriteResult> task in producers) { await task.WaitAsync(TimeSpan.FromSeconds(10)); snapshots.Add(queue.CaptureAccounting()); }
            LogWriteResult[] results = await Task.WhenAll(producers); OutputAccounting held = queue.CaptureAccounting();
            Assert.AreEqual(9L, held.Accepted); Assert.AreEqual(56L, held.QueueFull); Assert.AreEqual(65L, held.AdmissionAttempts); Assert.AreEqual(9, held.Pending); Assert.AreEqual(1, held.InFlight);
            Assert.AreEqual(8, results.Count(result => result.Outcome == LogWriteOutcome.Accepted)); Assert.AreEqual(56, results.Count(result => result.Outcome == LogWriteOutcome.Rejected));
            CollectionAssert.AreEquivalent(Enumerable.Range(2, 8).Select(value => (long)value).ToArray(),
                results.Where(result => result.Outcome == LogWriteOutcome.Accepted).Select(result => result.Outputs[0].AcceptedSequence!.Value).ToArray());
            release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs.Completion.WaitAsync(TimeSpan.FromSeconds(10)); OutputAccounting final = queue.CaptureAccounting();
            Assert.AreEqual(9L, final.Written); Assert.AreEqual(0, final.Pending); Assert.AreEqual(0, final.InFlight); Assert.AreEqual(9, held.Pending);
            foreach (OutputAccounting value in snapshots) { Assert.AreEqual(value.Accepted, value.Written + value.Failed + value.Abandoned + value.Pending); Assert.AreEqual(value.AdmissionAttempts, value.Accepted + value.QueueFull + value.Unavailable); Assert.IsTrue(value.Pending <= 9 && value.InFlight == 1 && value.Written == 0); }
        }
        finally { release.TrySetResult(); runtime.CloseAdmission(); await runtime.QueuedOutputs.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
    private static JsonObject Catalog() => Inputs.Value.Catalog();
    private static JsonObject Row(string id) => Inputs.Value.Row(id);
}
