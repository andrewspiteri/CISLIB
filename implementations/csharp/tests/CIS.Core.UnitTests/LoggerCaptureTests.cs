using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class LoggerCaptureTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void SharedCaptureCasesQualifyLazyRejectionOutcomesAndOutputOwnedTickets(string id)
    {
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        LoggerCaptureExpectations.Validate(row, LoggerCaptureFixture.Execute(row));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task GlobalLossCountersSaturateInsteadOfWrapping(bool truncated, bool alreadyFull)
    {
        var output = new LoggerCaptureSources.Output(Enumerable.Repeat(LogOutputWriteStatus.Accepted, 16).ToArray(), 0);
        CoreLogRuntime runtime = Runtime(output);
        string field = truncated ? "truncatedEventCount" : "invalidEventCount";
        typeof(CoreLogRuntime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, alreadyFull ? long.MaxValue : long.MaxValue - 1);
        CoreLogger logger = runtime.CreateLogger("Counter");
        LogWriteResult[] results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => logger.Write(CoreLogLevel.Information, LoggerCaptureSources.Data(truncated ? "long" : "name-empty"))))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.AreEqual(long.MaxValue, truncated ? runtime.TruncatedEventCount : runtime.InvalidEventCount);
        Assert.AreEqual(0L, truncated ? runtime.InvalidEventCount : runtime.TruncatedEventCount);
        Assert.IsTrue(results.All(result => result.Outcome == (truncated ? LogWriteOutcome.Accepted : LogWriteOutcome.InvalidEvent)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentWritesCountEachCaptureAndFactoryOnce(bool truncated)
    {
        const int count = 64;
        var output = new LoggerCaptureSources.Output(Enumerable.Repeat(LogOutputWriteStatus.Accepted, count).ToArray(), 0);
        CoreLogRuntime runtime = Runtime(output);
        CoreLogger logger = runtime.CreateLogger("Counter");
        int factories = 0;
        LogWriteResult[] results = await Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(() => logger.WriteLazy(CoreLogLevel.Information, () =>
        {
            Interlocked.Increment(ref factories);
            return LoggerCaptureSources.Data(truncated ? "long" : "name-empty");
        })))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.AreEqual(count, factories);
        Assert.AreEqual((long)count, truncated ? runtime.TruncatedEventCount : runtime.InvalidEventCount);
        Assert.AreEqual(0L, truncated ? runtime.InvalidEventCount : runtime.TruncatedEventCount);
        if (truncated)
        {
            CollectionAssert.AreEqual(Enumerable.Range(1, count).Select(value => (long)value).ToArray(), results.Select(result => result.Outputs[0].AcceptedSequence!.Value).Order().ToArray());
        }
        else { Assert.IsTrue(results.All(result => result.Outcome == LogWriteOutcome.InvalidEvent && result.Outputs.Count == 0)); }
    }

    [TestMethod]
    [DataRow(0, -99)]
    [DataRow(0, 0)]
    [DataRow(0, -1)]
    [DataRow(2, 1)]
    [DataRow(3, 1)]
    [DataRow(1, -99)]
    [DataRow(4, -99)]
    [DataRow(999, -99)]
    public void ProtocolInvalidOutputCannotInventOrHideTickets(int status, int sequence)
    {
        var output = new ProtocolOutput((LogOutputWriteStatus)status, sequence == -99 ? null : sequence);
        CoreLogRuntime runtime = Runtime(output);
        int factories = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.CreateLogger("Protocol").WriteLazy(CoreLogLevel.Information, () =>
        {
            factories++;
            return LoggerCaptureSources.Data("valid");
        }));
        Assert.AreEqual(1, factories);
        Assert.AreEqual(1, output.Calls);
        Assert.AreEqual(0L, runtime.InvalidEventCount);
        Assert.AreEqual(0L, runtime.TruncatedEventCount);
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("balanced-factory")]
    [DataRow("global")]
    [DataRow("ticket")]
    [DataRow("utf8")]
    [DataRow("invalid-ticket")]
    [DataRow("loss")]
    [DataRow("exception")]
    public void ComparatorRejectsFalseCaptureAndTicketObservations(string fault)
    {
        string id = fault == "loss" ? "CAPTURE-TRUNCATED-REJECTED-LAZY" : fault == "exception" ? "CAPTURE-THROW-IO" : "CAPTURE-NAME-NULL-LAZY";
        JsonObject row = Catalog()["cases"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        JsonObject actual = LoggerCaptureFixture.Execute(row);
        LoggerCaptureExpectations.Validate(row, actual);
        switch (fault)
        {
            case "invalid-ticket": actual["results"]![1]!["outputs"] = new JsonArray(new JsonObject { ["name"] = "console", ["status"] = "Accepted", ["sequence"] = 12L }); break;
            case "loss": actual["truncatedCount"] = 0; break;
            case "exception": actual["exceptionSame"]![1] = false; break;
            case "factory": actual["factoryCalls"] = 4; break;
            case "balanced-factory": actual["factoryInvocations"]![0] = 2; actual["factoryInvocations"]![1] = 0; break;
            case "global": actual["invalidCount"] = 2; break;
            case "ticket": actual["results"]![2]!["outputs"]![0]!["sequence"] = 13; break;
            case "utf8":
                byte[] bytes = Convert.FromBase64String(actual["wires"]![0]![0]!.GetValue<string>());
                bytes[0] = 0xFF; actual["wires"]![0]![0] = Convert.ToBase64String(bytes);
                break;
        }
        if (fault == "utf8") { Assert.ThrowsExactly<System.Text.DecoderFallbackException>(() => LoggerCaptureExpectations.Validate(row, actual)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => LoggerCaptureExpectations.Validate(row, actual)); }
    }

    private sealed class ProtocolOutput(LogOutputWriteStatus status, long? ticket) : ILogOutputAdmission
    {
        internal int Calls { get; private set; }
        public bool IsFaulted => false;
        public LogOutputWriteStatus TryAdmit(EncodedEvent captured, out long? acceptedSequence) { Calls++; acceptedSequence = ticket; return status; }
        public void RecordFiltered() { }
        public void RecordUnavailable() { }
    }
    private static CoreLogRuntime Runtime(ILogOutputAdmission output) => new(EventQualificationFixture.Metadata().CaptureMetadata(), new LoggingOptions { MaxEntryBytes = 16384 }, output, null);
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "logger-capture-cases.json")))!.AsObject();
}
