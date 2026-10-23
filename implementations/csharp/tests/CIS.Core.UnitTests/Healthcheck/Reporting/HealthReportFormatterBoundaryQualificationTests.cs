using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportFormatterBoundaryQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 15, 0, 0, TimeSpan.FromHours(2));
    private static readonly Guid Registration = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InitialRevisionZeroAndALargeUsableRevisionFitTheActualEncodedBudget(bool pipe)
    {
        await using var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "revision budget", InstanceId = Guid.NewGuid() }, new ManualTimeProvider(Epoch));
        HealthSnapshot initial = service.CaptureSnapshot();
        Assert.AreEqual(BigInteger.Zero, initial.Revision);
        HealthReportBuffer first = Format(initial, pipe, HealthReportView.Health, new HealthcheckOptions { MaxReportBytes = 1024 });
        Assert.IsFalse(first.IsError);
        using (JsonDocument document = JsonDocument.Parse(first.Utf8Bytes))
        { Assert.AreEqual("0", Root(document, pipe).GetProperty("revision").GetString()); }

        BigInteger revision = BigInteger.One << 512;
        var detached = new HealthSnapshot(new HealthSnapshotData
        {
            Revision = revision,
            CapturedAtUtc = initial.CapturedAtUtc,
            Application = initial.Application,
            HealthStatus = initial.HealthStatus,
            Ready = initial.Ready,
            MonitorCounts = initial.MonitorCounts,
            Monitors = initial.Monitors
        });
        HealthReportBuffer large = Format(detached, pipe, HealthReportView.Health, new HealthcheckOptions { MaxReportBytes = 1024 });
        Assert.IsFalse(large.IsError);
        Assert.IsTrue(large.Utf8Bytes.Length <= 1024);
        using JsonDocument parsed = JsonDocument.Parse(large.Utf8Bytes);
        Assert.AreEqual(revision.ToString(CultureInfo.InvariantCulture), Root(parsed, pipe).GetProperty("revision").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NumericFormattingPreservesTheActualValueObservationTimeInsteadOfTheCaptureTime(bool pipe)
    {
        var clock = new ManualTimeProvider(Epoch);
        await using var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "numeric time", InstanceId = Guid.NewGuid() }, clock);
        INumericThresholdMonitor monitor = service.CreateNumericThreshold(new() { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = 10, Unit = "items" });
        monitor.ReportValue(5);
        clock.Advance(TimeSpan.FromHours(1));

        HealthReportBuffer report = Format(service.CaptureSnapshot(), pipe);

        Assert.IsFalse(report.IsError);
        using JsonDocument document = JsonDocument.Parse(report.Utf8Bytes);
        JsonElement root = Root(document, pipe);
        Assert.AreEqual(5d, root.GetProperty("monitors")[0].GetProperty("numeric").GetProperty("lastValue").GetDouble());
        Assert.AreEqual(Utc(Epoch), root.GetProperty("monitors")[0].GetProperty("numeric").GetProperty("lastValueAtUtc").GetString());
        Assert.AreEqual(Utc(Epoch.AddHours(1)), root.GetProperty("capturedAtUtc").GetString());
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EveryMeasuredExecutionDurationMayActuallyBeZero(bool pipe)
    {
        await using var service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "zero duration", InstanceId = Guid.NewGuid() }, new ManualTimeProvider(Epoch));
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, new() { RegistrationId = Registration });
        try
        {
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            {
                RegistrationId = Registration,
                ExecutionId = 1,
                Kind = HealthExecutionFactKind.Outcome,
                OccurredAtUtc = Epoch,
                Outcome = HealthExecutionOutcome.Failed,
                SourcePhase = HealthExecutionPhase.Callback,
                OriginalError = new HealthDiagnostic("ActualError"),
                EligibleAtUtc = Epoch,
                AdmittedAtUtc = Epoch,
                ConditionStartedAtUtc = Epoch,
                CallbackStartedAtUtc = Epoch,
                WorkCompletedAtUtc = Epoch,
                QueueDelayMilliseconds = 0,
                ConditionDurationMilliseconds = 0,
                CallbackDurationMilliseconds = 0
            }));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = Registration, ExecutionId = 1, Kind = HealthExecutionFactKind.ErrorHandlingCompleted, OccurredAtUtc = Epoch, ErrorHandlingCompletedAtUtc = Epoch, ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded, ErrorHandlingDurationMilliseconds = 0 }));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = Registration, ExecutionId = 1, Kind = HealthExecutionFactKind.CleanupCompleted, OccurredAtUtc = Epoch, CleanupCompletedAtUtc = Epoch, CleanupStatus = HealthExecutionStepStatus.Succeeded, CleanupDurationMilliseconds = 0 }));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = Registration, ExecutionId = 1, Kind = HealthExecutionFactKind.Settled, OccurredAtUtc = Epoch, SettledAtUtc = Epoch, ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded, CleanupStatus = HealthExecutionStepStatus.Succeeded, TotalDurationMilliseconds = 0 }));

            HealthReportBuffer report = Format(service.CaptureSnapshot(), pipe);

            Assert.IsFalse(report.IsError);
            using JsonDocument document = JsonDocument.Parse(report.Utf8Bytes);
            JsonElement execution = Root(document, pipe).GetProperty("monitors")[0].GetProperty("outcomes").GetProperty("execution");
            foreach (string field in new[] { "queueDelayMs", "conditionDurationMs", "callbackDurationMs", "errorHandlingDurationMs", "cleanupDurationMs", "totalDurationMs" })
            { Assert.AreEqual(0d, execution.GetProperty(field).GetDouble(), field); }
            GC.KeepAlive(monitor);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    public static IEnumerable<object[]> DurationFields()
    {
        foreach (string field in new[] { "queue", "condition", "callback", "handler", "cleanup", "total" })
        { yield return [field, false]; yield return [field, true]; }
    }

    [TestMethod]
    [DynamicData(nameof(DurationFields))]
    public void InvalidDetachedNegativeDurationProducesACompleteSerializationError(string field, bool pipe)
    {
        Assert.IsFalse(Format(Snapshot(Counters(execution: Execution(null))), pipe).IsError);
        HealthReportBuffer report = Format(Snapshot(Counters(execution: Execution(field))), pipe);
        SerializationError(report, pipe);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EveryInvalidDetachedNegativeLifetimeCounterProducesACompleteSerializationError(bool pipe)
    {
        Assert.IsFalse(Format(Snapshot(Counters()), pipe).IsError);
        foreach (string field in new[] { "successful", "failed", "skipped", "expectedCanceled", "consecutiveFailures" })
        { SerializationError(Format(Snapshot(Counters(field)), pipe), pipe); }
    }

    [TestMethod]
    [DataRow(false, "state")]
    [DataRow(true, "state")]
    [DataRow(false, "policy")]
    [DataRow(true, "policy")]
    public void AnUnsupportedDetachedEnumProducesAnErrorInsteadOfANumericEnumName(bool pipe, string field)
    {
        Assert.IsFalse(Format(Snapshot(Counters()), pipe).IsError);
        SerializationError(Format(Snapshot(Counters(), field == "state" ? (MonitorState)999 : MonitorState.Unhealthy,
            field == "policy" ? (HealthPolicyKind)999 : HealthPolicyKind.ConsecutiveFailures), pipe), pipe);
    }

    [TestMethod]
    public void InvalidRequestErrorFormattingWritesAnExplicitNullViewThatTheProbeCanRead()
    {
        foreach (string code in new[] { "InvalidRequest", "InvalidFrame", "InvalidView", "UnsupportedVersion" })
        {
            HealthReportBuffer report = HealthReportFormatter.FormatError(code, pipe: true);
            using JsonDocument document = JsonDocument.Parse(report.Utf8Bytes);
            Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("view").ValueKind);
            HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(report.Utf8Bytes, HealthReportView.Health, 1024);
            Assert.IsTrue(parsed.IsError);
            Assert.AreEqual(code, parsed.ErrorCode);
        }
    }

    [TestMethod]
    public void BoundedWriterCanCommitAnEntireScratchBufferAtTheExactReportCap()
    {
        var writer = new BoundedHealthBuffer(4096);
        Memory<byte> scratch = writer.GetMemory(4096);
        Assert.AreEqual(4096, scratch.Length);
        scratch.Span.Fill(37);

        writer.Advance(scratch.Length);

        Assert.AreEqual(4096, writer.WrittenSpan.Length);
        Assert.IsTrue(writer.WrittenSpan.ToArray().All(value => value == 37));
    }

    [TestMethod]
    public void ARejectedNegativeAdvanceIdentifiesTheCallerCountArgumentAndPublishesNoBytes()
    {
        var writer = new BoundedHealthBuffer(4096);
        _ = writer.GetMemory(1);

        ArgumentOutOfRangeException failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.Advance(-1));

        Assert.AreEqual("count", failure.ParamName);
        Assert.AreEqual(0, writer.WrittenSpan.Length);
    }

    private static HealthExecutionSnapshot Execution(string? negative) => new(new HealthExecutionSnapshotData
    {
        RegistrationId = Registration,
        ExecutionId = 1,
        Phase = HealthExecutionPhase.Settled,
        SettledAtUtc = Epoch,
        ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded,
        CleanupStatus = HealthExecutionStepStatus.Succeeded,
        QueueDelayMilliseconds = negative == "queue" ? -1 : 0,
        ConditionDurationMilliseconds = negative == "condition" ? -1 : 0,
        CallbackDurationMilliseconds = negative == "callback" ? -1 : 0,
        ErrorHandlingDurationMilliseconds = negative == "handler" ? -1 : 0,
        CleanupDurationMilliseconds = negative == "cleanup" ? -1 : 0,
        TotalDurationMilliseconds = negative == "total" ? -1 : 0
    });

    private static HealthOutcomeSnapshotData Counters(string? negative = null, HealthExecutionSnapshot? execution = null) => new()
    {
        Successful = negative == "successful" ? -1 : 1,
        Failed = negative == "failed" ? -1 : 1,
        Skipped = negative == "skipped" ? -1 : 1,
        ExpectedCanceled = negative == "expectedCanceled" ? -1 : 1,
        ConsecutiveFailures = negative == "consecutiveFailures" ? -1 : 1,
        LastOutcome = HealthExecutionOutcome.Failed,
        Execution = execution
    };

    private static HealthSnapshot Snapshot(HealthOutcomeSnapshotData outcomes, MonitorState state = MonitorState.Unhealthy,
        HealthPolicyKind policy = HealthPolicyKind.ConsecutiveFailures) => new(new HealthSnapshotData
        {
            Revision = BigInteger.One,
            CapturedAtUtc = Epoch,
            Application = new HealthApplicationSnapshot(new HealthApplicationIdentity { Name = "detached formatter input", InstanceId = Guid.NewGuid() }, true, false),
            HealthStatus = HealthStatus.Healthy,
            Ready = true,
            MonitorCounts = new HealthMonitorCounts(1, 0, 1, 0, 0),
            Monitors = [new HealthMonitorSnapshot(new HealthMonitorSnapshotData
        { MonitorId = Guid.NewGuid(), Policy = policy, IsCritical = false, State = state, RegisteredAtUtc = Epoch, Outcomes = new HealthOutcomeSnapshot(outcomes) })]
        });

    private static HealthReportBuffer Format(HealthSnapshot snapshot, bool pipe, HealthReportView view = HealthReportView.Details, HealthcheckOptions? options = null)
        => pipe ? HealthReportFormatter.FormatPipeResponse(snapshot, view, options ?? new HealthcheckOptions())
            : HealthReportFormatter.FormatSnapshot(snapshot, view, options ?? new HealthcheckOptions());
    private static JsonElement Root(JsonDocument document, bool pipe) => pipe ? document.RootElement.GetProperty("snapshot") : document.RootElement;
    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static void SerializationError(HealthReportBuffer report, bool pipe)
    {
        Assert.IsTrue(report.IsError);
        Assert.AreEqual("SerializationFailed", report.ErrorCode);
        Assert.IsFalse(report.SelectedHealthy);
        Assert.IsTrue(report.Utf8Bytes.Length <= 1024);
        using JsonDocument document = JsonDocument.Parse(report.Utf8Bytes);
        Assert.AreEqual("error", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("SerializationFailed", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(document.RootElement.TryGetProperty("snapshot", out _));
        if (pipe) { Assert.IsTrue(HealthReportProtocol.ParseResponse(report.Utf8Bytes, HealthReportView.Details, 1024).IsError); }
    }

    private static async Task DisposeNoCollectionBindingAsync(IHealthExecutionBinding binding)
    {
        Task disposal = binding.DisposeAsync().AsTask();
        if (disposal.IsCompleted) { await disposal; }
    }
}
