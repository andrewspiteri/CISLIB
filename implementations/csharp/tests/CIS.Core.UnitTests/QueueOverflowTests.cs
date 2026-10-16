using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class QueueOverflowTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task SaturationPreservesAcceptedWorkAndIndependentResults(string id)
    { JsonObject row = Row(id); QueuedFanoutExpectations.Validate(row, await QueuedFanoutFixture.ExecuteAsync(row)); }

    [TestMethod]
    public async Task FullHealthyOutputStillCapturesEligibleLazyInputWithoutWaiting()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = new List<AdmittedEvent>(); int calls = 0;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { QueueCapacity = 1 }, async item => { started.TrySetResult(); await release.Task; written.Add(item); }, null);
        CoreLogger logger = runtime.CreateLogger("Overflow");
        Task<LogWriteResult>? pendingAdmission = null;
        LogEventData Content(string message) => new() { EventName = "overflow.event", Message = message };
        try
        {
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, Content("first")).Outcome);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(LogWriteOutcome.Accepted, logger.Write(CoreLogLevel.Information, Content("second")).Outcome);
            Assert.IsTrue(logger.IsEnabled(CoreLogLevel.Information));
            pendingAdmission = Task.Run(() => logger.WriteLazy(CoreLogLevel.Information, () => { calls++; return Content("rejected"); }));
            LogWriteResult full = await pendingAdmission.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, calls); Assert.AreEqual(LogWriteOutcome.Rejected, full.Outcome);
            Assert.AreEqual(LogOutputWriteStatus.QueueFull, full.Outputs.Single().Status); Assert.IsNull(full.Outputs.Single().AcceptedSequence);
            Assert.AreEqual(1, runtime.QueuedOutputs!.Console!.BufferedCount); Assert.AreEqual(2L, runtime.QueuedOutputs.Console.LastAcceptedSequence); Assert.IsFalse(runtime.QueuedOutputs.Console.IsFaulted);
            LogWriteResult invalid = logger.Write(CoreLogLevel.Information, new LogEventData { EventName = "", Message = "invalid" });
            Assert.AreEqual(LogWriteOutcome.InvalidEvent, invalid.Outcome); Assert.AreEqual(0, invalid.Outputs.Count); Assert.AreEqual(2L, runtime.QueuedOutputs.Console.LastAcceptedSequence);
            runtime.CloseAdmission(); release.TrySetResult(); await runtime.QueuedOutputs.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            CollectionAssert.AreEqual(new long[] { 1, 2 }, written.Select(item => item.AcceptedSequence).ToArray());
            CollectionAssert.AreEqual(new[] { "first", "second" }, written.Select(item => JsonNode.Parse(item.Event.JsonLine.Span)!["message"]!.GetValue<string>()).ToArray());
        }
        finally { runtime.CloseAdmission(); release.TrySetResult(); if (pendingAdmission is not null) { await pendingAdmission.WaitAsync(TimeSpan.FromSeconds(10)); } await runtime.QueuedOutputs!.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [TestMethod]
    [DataRow("eviction")]
    [DataRow("lost-old")]
    [DataRow("false-acceptance")]
    public async Task OverflowComparatorRejectsEvictionLossAndFalseAcceptance(string fault)
    {
        JsonObject row = Row("OVERFLOW-BOTH-FULL"), actual = await QueuedFanoutFixture.ExecuteAsync(row);
        QueuedFanoutExpectations.Validate(row, actual);
        if (fault == "eviction")
        {
            JsonNode wire = actual["wires"]!["console"]!; JsonNode item = wire["attempted"]![0]!;
            JsonNode body = JsonNode.Parse(Convert.FromBase64String(item["wire"]!.GetValue<string>()))!;
            body["message"] = "record-3"; body["attributes"]!["input"] = 3;
            string raw = Convert.ToBase64String(Encoding.UTF8.GetBytes(body.ToJsonString() + "\n")); item["wire"] = raw; item["after"] = raw;
            wire["stream"] = Convert.ToBase64String(wire["attempted"]!.AsArray().SelectMany(record => Convert.FromBase64String(record!["wire"]!.GetValue<string>())).ToArray());
        }
        else if (fault == "lost-old") { actual["semantic"]!["outputs"]!["console"]!["writtenTickets"] = new JsonArray(2L); }
        else { actual["semantic"]!["results"]![2]!["outcome"] = "Accepted"; }
        Assert.ThrowsExactly<InvalidDataException>(() => QueuedFanoutExpectations.Validate(row, actual));
    }

    [TestMethod]
    public void CatalogNamesBothSaturationDirectionsRecoveryFaultsFilteringAndDisabledOutputs()
    {
        JsonArray rows = Catalog()["cases"]!.AsArray(); Assert.AreEqual(12, rows.Count); Assert.AreEqual(12, rows.Select(row => row!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(new[] { "basic", "file-fault-overflow", "console-fault-overflow", "independent-file", "recover-console", "recover-file" }, rows.Select(row => row!["mode"]!.GetValue<string>()).Distinct().ToArray());
    }
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "queue-overflow-cases.json")))!.AsObject();
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
