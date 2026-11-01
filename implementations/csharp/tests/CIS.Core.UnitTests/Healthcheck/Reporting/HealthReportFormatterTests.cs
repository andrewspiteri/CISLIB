using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using CIS.Configuration;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportFormatterTests
{
    private static readonly DateTimeOffset Captured = new(2026, 10, 20, 8, 1, 0, TimeSpan.Zero);
    private static readonly Guid InstanceId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [TestMethod]
    [DataRow(HealthReportView.Health, "pending-health.json", true)]
    [DataRow(HealthReportView.Readiness, "pending-readiness.json", false)]
    public void CompactOutputMatchesTheIndependentFrozenPendingFixture(HealthReportView view, string fixture, bool selectedHealthy)
    {
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(PendingSnapshot(), view, new HealthcheckOptions());
        using JsonDocument actual = JsonDocument.Parse(result.Utf8Bytes);
        using JsonDocument expected = JsonDocument.Parse(ReadFixture("valid", fixture));

        Assert.IsTrue(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.IsFalse(result.IsError);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual(selectedHealthy, result.SelectedHealthy);
        Assert.IsFalse(actual.RootElement.TryGetProperty("monitors", out _));
        CollectionAssert.AreEqual(new[] { "schemaVersion", "revision", "capturedAtUtc", "application", "healthStatus", "ready", "monitorCounts" },
            actual.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public void PipePayloadMatchesTheIndependentFrozenReadinessEnvelope()
    {
        HealthReportBuffer result = HealthReportFormatter.FormatPipeResponse(PendingSnapshot(), HealthReportView.Readiness, new HealthcheckOptions());
        using JsonDocument actual = JsonDocument.Parse(result.Utf8Bytes);
        using JsonDocument expected = JsonDocument.Parse(ReadFixture("valid", "pipe-readiness-response.json"));

        Assert.IsTrue(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.IsFalse(result.SelectedHealthy);
        Assert.AreEqual((byte)'{', result.Utf8Bytes.Span[0], "The formatter returns a UTF-8 payload; framing belongs to the transport.");
        CollectionAssert.AreEqual(new[] { "protocolVersion", "schemaVersion", "view", "kind", "snapshot" },
            actual.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    [DataRow(HealthReportView.Health, "health")]
    [DataRow(HealthReportView.Readiness, "readiness")]
    [DataRow(HealthReportView.Details, "details")]
    public void PipeAndHttpUseTheIdenticalSelectedSnapshotWriter(HealthReportView view, string wireView)
    {
        HealthSnapshot snapshot = PendingSnapshot();
        var options = new HealthcheckOptions();
        HealthReportBuffer http = HealthReportFormatter.FormatSnapshot(snapshot, view, options);
        HealthReportBuffer pipe = HealthReportFormatter.FormatPipeResponse(snapshot, view, options);
        using JsonDocument httpDocument = JsonDocument.Parse(http.Utf8Bytes);
        using JsonDocument pipeDocument = JsonDocument.Parse(pipe.Utf8Bytes);

        Assert.IsTrue(JsonElement.DeepEquals(httpDocument.RootElement, pipeDocument.RootElement.GetProperty("snapshot")));
        Assert.AreEqual(wireView, pipeDocument.RootElement.GetProperty("view").GetString());
        Assert.AreEqual("snapshot", pipeDocument.RootElement.GetProperty("kind").GetString());
        Assert.IsFalse(pipeDocument.RootElement.TryGetProperty("error", out _));
        Assert.AreEqual(http.SelectedHealthy, pipe.SelectedHealthy);
    }

    [TestMethod]
    [DataRow(HealthReportView.Health, false)]
    [DataRow(HealthReportView.Readiness, false)]
    [DataRow(HealthReportView.Details, true)]
    public async Task OnlyDetailsIncludesTheCompleteMonitorList(HealthReportView view, bool hasDetails)
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor first = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = true, Name = "first" });
        IAvailabilityMonitor second = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true, Name = "second" });
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(service.CaptureSnapshot(), view, new HealthcheckOptions());
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement root = document.RootElement;

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(hasDetails, root.TryGetProperty("monitors", out JsonElement monitors));
        Assert.AreEqual(2, root.GetProperty("monitorCounts").GetProperty("active").GetInt32());
        Assert.AreEqual(1, root.GetProperty("monitorCounts").GetProperty("failing").GetInt32());
        Assert.AreEqual("Healthy", root.GetProperty("healthStatus").GetString());
        if (hasDetails)
        {
            Assert.AreEqual(2, monitors.GetArrayLength());
            Assert.AreEqual(first.MonitorId.ToString("D"), monitors[0].GetProperty("monitorId").GetString());
            Assert.AreEqual(second.MonitorId.ToString("D"), monitors[1].GetProperty("monitorId").GetString());
            Assert.AreEqual("Unhealthy", monitors[0].GetProperty("state").GetString());
            Assert.AreEqual("Pending", monitors[1].GetProperty("state").GetString());
            Assert.AreEqual(0, monitors[0].GetProperty("measurements").GetArrayLength());
        }
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [TestMethod]
    public async Task FormattingDoesNotReevaluateAvailabilityFromItsLatestFailedOutcome()
    {
        await using HealthcheckService service = CreateService();
        IAvailabilityMonitor monitor = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        monitor.ReportPublished();
        monitor.ReportRefreshFailure(AcceptedDiagnostic("RefreshFailed", "Old usable data remains available."));
        service.MarkStartupCompleted();
        HealthSnapshot snapshot = service.CaptureSnapshot();
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions());
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement detail = document.RootElement.GetProperty("monitors")[0];

        Assert.IsTrue(result.SelectedHealthy);
        Assert.AreEqual("Healthy", detail.GetProperty("state").GetString());
        Assert.AreEqual("Failed", detail.GetProperty("outcomes").GetProperty("lastOutcome").GetString());
        Assert.AreEqual("1", detail.GetProperty("outcomes").GetProperty("successful").GetString());
        Assert.AreEqual("1", detail.GetProperty("outcomes").GetProperty("failed").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.AreEqual(snapshot.Revision, service.CaptureSnapshot().Revision);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task NumericPolicyAndAllDiagnosticScalarKindsRetainTheirWireTypes()
    {
        await using HealthcheckService service = CreateService();
        INumericThresholdMonitor monitor = service.CreateNumericThreshold(new NumericThresholdMonitorOptions
        {
            IsCritical = true,
            Comparison = NumericComparison.GreaterThanOrEqual,
            Threshold = 10.25,
            Unit = "items"
        });
        monitor.ReportValue(10.25);
        monitor.ReportMeasurement(HealthMeasurement.Unavailable("a-unavailable", Captured, "ms"));
        monitor.ReportMeasurement(HealthMeasurement.String("b-text", "cached", Captured));
        monitor.ReportMeasurement(HealthMeasurement.Boolean("c-boolean", false, Captured));
        monitor.ReportMeasurement(HealthMeasurement.Integer("d-integer", long.MinValue, Captured));
        monitor.ReportMeasurement(HealthMeasurement.Number("e-number", double.MaxValue, Captured, "ratio"));
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(service.CaptureSnapshot(), HealthReportView.Details, new HealthcheckOptions());
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement detail = document.RootElement.GetProperty("monitors")[0];
        JsonElement numeric = detail.GetProperty("numeric");
        JsonElement measurements = detail.GetProperty("measurements");

        Assert.AreEqual("GreaterThanOrEqual", numeric.GetProperty("comparison").GetString());
        Assert.AreEqual(10.25, numeric.GetProperty("threshold").GetDouble());
        Assert.AreEqual(10.25, numeric.GetProperty("lastValue").GetDouble());
        Assert.AreEqual("items", numeric.GetProperty("unit").GetString());
        Assert.AreEqual("Unhealthy", detail.GetProperty("state").GetString());
        Assert.AreEqual("Unavailable", measurements[0].GetProperty("valueKind").GetString());
        Assert.IsFalse(measurements[0].TryGetProperty("value", out _));
        Assert.AreEqual("cached", measurements[1].GetProperty("value").GetString());
        Assert.IsFalse(measurements[2].GetProperty("value").GetBoolean());
        Assert.AreEqual("-9223372036854775808", measurements[3].GetProperty("value").GetString());
        Assert.AreEqual(JsonValueKind.Number, measurements[4].GetProperty("value").ValueKind);
        Assert.AreEqual(double.MaxValue, measurements[4].GetProperty("value").GetDouble());
        Assert.IsFalse(measurements[1].TryGetProperty("unit", out _));
        foreach (JsonElement measurement in measurements.EnumerateArray())
        {
            Assert.IsTrue(measurement.GetProperty("isDiagnostic").GetBoolean());
            Assert.AreEqual("2026-10-20T08:01:00.0000000Z", measurement.GetProperty("observedAtUtc").GetString());
        }
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task AbsentOptionalFieldsAreOmittedRatherThanWrittenAsNullOrZero()
    {
        await using HealthcheckService service = CreateService();
        INumericThresholdMonitor monitor = service.CreateNumericThreshold(new NumericThresholdMonitorOptions
        {
            IsCritical = false,
            Comparison = NumericComparison.LessThan,
            Threshold = 1,
            Unit = "items"
        });
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(service.CaptureSnapshot(), HealthReportView.Details, new HealthcheckOptions());
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement root = document.RootElement;
        JsonElement detail = root.GetProperty("monitors")[0];
        foreach (string absent in new[] { "buildVersion", "serviceVersion", "hostName", "osDescription" })
        {
            Assert.IsFalse(root.GetProperty("application").TryGetProperty(absent, out _), absent);
        }
        foreach (string absent in new[] { "name", "lastObservationAtUtc", "diagnostic", "outcomes", "collection" })
        {
            Assert.IsFalse(detail.TryGetProperty(absent, out _), absent);
        }
        Assert.IsFalse(detail.GetProperty("numeric").TryGetProperty("lastValue", out _));
        Assert.IsFalse(detail.GetProperty("numeric").TryGetProperty("lastValueAtUtc", out _));
        Assert.AreEqual(0, detail.GetProperty("measurements").GetArrayLength());
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task EscapedControlTextAndSupplementaryUnicodeRoundTripAsStrictUtf8()
    {
        const string text = "quote\" slash\\ newline\n tab\t control\u0001 globe\U0001F30D <&>";
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false, Name = text });
        monitor.Flag(AcceptedDiagnostic(code: "Failure", message: text, exceptionType: "Detached.Type"));
        monitor.ReportMeasurement(HealthMeasurement.String("text", text, Captured));
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(service.CaptureSnapshot(), HealthReportView.Details, new HealthcheckOptions());
        string utf8 = new UTF8Encoding(false, true).GetString(result.Utf8Bytes.Span);
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement detail = document.RootElement.GetProperty("monitors")[0];

        Assert.IsFalse(utf8.StartsWith('\uFEFF'));
        Assert.IsFalse(utf8.Contains('\u0001'));
        Assert.AreEqual(text, detail.GetProperty("name").GetString());
        Assert.AreEqual(text, detail.GetProperty("diagnostic").GetProperty("message").GetString());
        Assert.AreEqual(text, detail.GetProperty("measurements")[0].GetProperty("value").GetString());
        Assert.IsFalse(detail.GetProperty("diagnostic").GetProperty("isTruncated").GetBoolean());
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public void RevisionInt64CountersAndExecutionIdentityNeverPassThroughFloatingPoint()
    {
        const string revision = "12345678901234567890123456789012345678901234567890";
        var execution = new HealthExecutionSnapshot(new HealthExecutionSnapshotData
        {
            RegistrationId = Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789"),
            ExecutionId = long.MaxValue,
            Phase = HealthExecutionPhase.Settled,
            SettledAtUtc = Captured,
            TotalDurationMilliseconds = 0.125,
            ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable,
            CleanupStatus = HealthExecutionStepStatus.Succeeded
        });
        var monitor = new HealthMonitorSnapshot(new HealthMonitorSnapshotData
        {
            MonitorId = Guid.Parse("ABCDEF02-2345-6789-ABCD-EF0123456789"),
            Policy = HealthPolicyKind.ConsecutiveFailures,
            State = MonitorState.Unhealthy,
            IsCritical = false,
            RegisteredAtUtc = Captured,
            Outcomes = new HealthOutcomeSnapshot(new HealthOutcomeSnapshotData
            {
                Successful = long.MaxValue,
                Failed = long.MaxValue,
                Skipped = long.MaxValue,
                ExpectedCanceled = long.MaxValue,
                ConsecutiveFailures = long.MaxValue,
                CountersSaturated = true,
                LastOutcome = HealthExecutionOutcome.ExpectedCancellation,
                Execution = execution
            }),
            Measurements = [HealthMeasurement.Integer("max", long.MaxValue, Captured)]
        });
        HealthSnapshot snapshot = Snapshot([monitor], BigInteger.Parse(revision, CultureInfo.InvariantCulture), new HealthMonitorCounts(1, 0, 1, 0, 0));
        HealthReportBuffer result = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions());
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);
        JsonElement detail = document.RootElement.GetProperty("monitors")[0];
        JsonElement outcomes = detail.GetProperty("outcomes");

        Assert.AreEqual(revision, document.RootElement.GetProperty("revision").GetString());
        foreach (string counter in new[] { "successful", "failed", "skipped", "expectedCanceled", "consecutiveFailures" })
        {
            Assert.AreEqual("9223372036854775807", outcomes.GetProperty(counter).GetString(), counter);
        }
        Assert.AreEqual("9223372036854775807", outcomes.GetProperty("execution").GetProperty("executionId").GetString());
        Assert.AreEqual("abcdef01-2345-6789-abcd-ef0123456789", outcomes.GetProperty("execution").GetProperty("registrationId").GetString());
        Assert.AreEqual("abcdef02-2345-6789-abcd-ef0123456789", detail.GetProperty("monitorId").GetString());
        Assert.AreEqual(0.125, outcomes.GetProperty("execution").GetProperty("totalDurationMs").GetDouble());
        Assert.IsTrue(outcomes.GetProperty("countersSaturated").GetBoolean());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LimitFailureProducesOneCompleteBoundedErrorWithNoPartialSnapshot(bool pipe)
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions
        {
            IsCritical = false,
            InitiallyFlagged = false,
            Name = string.Concat(Enumerable.Repeat("\U0001F30D", 128))
        });
        monitor.ReportMeasurement(HealthMeasurement.String("large", new string('"', 1_024), Captured));
        HealthReportBuffer result = Format(service.CaptureSnapshot(), HealthReportView.Details, new HealthcheckOptions { MaxReportBytes = 1_024 }, pipe);
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);

        Assert.IsTrue(result.IsError);
        Assert.IsFalse(result.SelectedHealthy);
        Assert.AreEqual("ReportTooLarge", result.ErrorCode);
        Assert.IsTrue(result.Utf8Bytes.Length <= 1_024);
        Assert.AreEqual("error", document.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("ReportTooLarge", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(document.RootElement.TryGetProperty("snapshot", out _));
        Assert.IsFalse(document.RootElement.TryGetProperty("monitors", out _));
        if (pipe)
        {
            Assert.AreEqual("details", document.RootElement.GetProperty("view").GetString());
            Assert.AreEqual(1, document.RootElement.GetProperty("protocolVersion").GetInt32());
        }
        else
        {
            using JsonDocument fixture = JsonDocument.Parse(ReadFixture("valid", "report-too-large.json"));
            Assert.IsTrue(JsonElement.DeepEquals(fixture.RootElement, document.RootElement));
        }
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task ExactEncodedBoundaryIncludesPipeEnvelopeAndEscapedBytes()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false });
        monitor.ReportMeasurement(HealthMeasurement.String("padding", new string('"', 600), Captured));
        HealthSnapshot snapshot = service.CaptureSnapshot();
        HealthReportBuffer unbounded = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions());
        int exact = unbounded.Utf8Bytes.Length;
        Assert.IsTrue(exact > 1_024);
        HealthReportBuffer atLimit = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions { MaxReportBytes = exact });
        HealthReportBuffer belowLimit = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions { MaxReportBytes = exact - 1 });
        HealthReportBuffer pipeAtHttpLimit = HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Details, new HealthcheckOptions { MaxReportBytes = exact });

        Assert.IsFalse(atLimit.IsError);
        CollectionAssert.AreEqual(unbounded.Utf8Bytes.ToArray(), atLimit.Utf8Bytes.ToArray());
        Assert.AreEqual("ReportTooLarge", belowLimit.ErrorCode);
        Assert.AreEqual("ReportTooLarge", pipeAtHttpLimit.ErrorCode);
        HealthReportBuffer fullPipe = HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Details, new HealthcheckOptions());
        Assert.IsFalse(HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Details,
            new HealthcheckOptions { MaxReportBytes = fullPipe.Utf8Bytes.Length }).IsError);
        Assert.AreEqual("ReportTooLarge", HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Details,
            new HealthcheckOptions { MaxReportBytes = fullPipe.Utf8Bytes.Length - 1 }).ErrorCode);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task FormattingFrozenSnapshotAfterMutationAndDisposalPreservesItsOriginalBytes()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
        HealthSnapshot snapshot = service.CaptureSnapshot();
        HealthReportBuffer before = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions());
        byte[] original = before.Utf8Bytes.ToArray();
        monitor.Flag(AcceptedDiagnostic("LaterFailure"));
        service.MarkStartupCompleted();
        await service.DisposeAsync();
        HealthReportBuffer after = HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Details, new HealthcheckOptions());

        CollectionAssert.AreEqual(original, before.Utf8Bytes.ToArray());
        CollectionAssert.AreEqual(original, after.Utf8Bytes.ToArray());
        Assert.IsTrue(after.SelectedHealthy);
        using JsonDocument document = JsonDocument.Parse(after.Utf8Bytes);
        Assert.AreEqual("Healthy", document.RootElement.GetProperty("monitors")[0].GetProperty("state").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FormatterRejectsNullArgumentsAndUndefinedViews(bool pipe)
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Format(null!, HealthReportView.Health, new HealthcheckOptions(), pipe));
        Assert.ThrowsExactly<ArgumentNullException>(() => Format(PendingSnapshot(), HealthReportView.Health, null!, pipe));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Format(PendingSnapshot(), (HealthReportView)999, new HealthcheckOptions(), pipe));
    }

    [TestMethod]
    [DataRow(false, "bytes")]
    [DataRow(true, "bytes")]
    [DataRow(false, "disabled-pipe")]
    [DataRow(true, "disabled-pipe")]
    [DataRow(false, "clients")]
    [DataRow(true, "clients")]
    public void FormatterValidatesAllSuppliedConfigurationEvenWhenTheTransportIsDisabled(bool pipe, string scenario)
    {
        HealthcheckOptions options = scenario switch
        {
            "bytes" => new HealthcheckOptions { MaxReportBytes = 1_023 },
            "disabled-pipe" => new HealthcheckOptions { PipeEnabled = false, PipeName = "anonymous" },
            _ => new HealthcheckOptions { PipeMaxClients = 0 }
        };
        Assert.ThrowsExactly<CoreConfigurationException>(() => Format(PendingSnapshot(), HealthReportView.Health, options, pipe));
    }

    [TestMethod]
    [DataRow(false, "version")]
    [DataRow(true, "version")]
    [DataRow(false, "revision")]
    [DataRow(true, "revision")]
    public void UnserializableSnapshotProducesAnIndependentCompleteTransportError(bool pipe, string scenario)
    {
        var snapshot = new HealthSnapshot(new HealthSnapshotData
        {
            SchemaVersion = scenario == "version" ? 2 : 1,
            Revision = scenario == "revision" ? BigInteger.MinusOne : BigInteger.One,
            CapturedAtUtc = Captured,
            Application = new HealthApplicationSnapshot(new HealthApplicationIdentity { Name = "invalid-internal-input", InstanceId = InstanceId }, true, false),
            HealthStatus = HealthStatus.Healthy,
            Ready = true,
            MonitorCounts = new HealthMonitorCounts(0, 0, 0, 0, 0)
        });
        HealthReportBuffer result = Format(snapshot, HealthReportView.Health, new HealthcheckOptions(), pipe);
        using JsonDocument document = JsonDocument.Parse(result.Utf8Bytes);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual("SerializationFailed", result.ErrorCode);
        Assert.IsFalse(result.SelectedHealthy);
        Assert.AreEqual("SerializationFailed", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(document.RootElement.TryGetProperty("snapshot", out _));
        Assert.IsTrue(result.Utf8Bytes.Length <= 1_024);
    }

    internal static byte[] ReadFixture(string category, string name) => QualificationFixtureInputs.ReadHealthcheck(category, name);

    internal static HealthSnapshot PendingSnapshot()
    {
        var monitor = new HealthMonitorSnapshot(new HealthMonitorSnapshotData
        {
            MonitorId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Policy = HealthPolicyKind.FirstSuccessAvailability,
            IsCritical = true,
            State = MonitorState.Pending,
            RegisteredAtUtc = Captured,
            Outcomes = new HealthOutcomeSnapshot(new HealthOutcomeSnapshotData())
        });
        return Snapshot([monitor], BigInteger.One, new HealthMonitorCounts(1, 1, 0, 0, 1), fixtureIdentity: true);
    }

    private static HealthSnapshot Snapshot(IEnumerable<HealthMonitorSnapshot> monitors, BigInteger revision, HealthMonitorCounts counts, bool fixtureIdentity = false)
        => new(new HealthSnapshotData
        {
            Revision = revision,
            CapturedAtUtc = Captured,
            Application = new HealthApplicationSnapshot(new HealthApplicationIdentity
            {
                Name = "healthcheck-foundation-fixture",
                InstanceId = InstanceId,
                BuildVersion = fixtureIdentity ? "0.1.0-dev.1" : null,
                ServiceVersion = fixtureIdentity ? "pilot" : null
            }, startupCompleted: false, isStopping: false),
            HealthStatus = HealthStatus.Healthy,
            Ready = false,
            MonitorCounts = counts,
            Monitors = monitors
        });

    private static HealthcheckService CreateService()
        => new(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "formatter-tests", InstanceId = InstanceId }, new ManualTimeProvider(Captured));

    private static HealthReportBuffer Format(HealthSnapshot snapshot, HealthReportView view, HealthcheckOptions options, bool pipe)
        => pipe ? HealthReportFormatter.FormatPipeResponse(snapshot, view, options) : HealthReportFormatter.FormatSnapshot(snapshot, view, options);
    private static HealthDiagnostic AcceptedDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

}
