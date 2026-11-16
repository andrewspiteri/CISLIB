using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthHeartbeatFactWireTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("heartbeat-suppressed-details.json", true)]
    [DataRow("heartbeat-cleanup-failure-details.json", false)]
    public void AuthoredPortableHeartbeatFactsAreAcceptedWithoutChangingTheWireVersion(string fixture, bool healthy)
    {
        byte[] snapshot = HealthReportFormatterTests.ReadFixture("valid", fixture);
        HealthParsedResponse response = Parse(Envelope(snapshot));
        Assert.IsFalse(response.IsError);
        Assert.AreEqual(healthy, response.SelectedHealthy);
        using JsonDocument original = JsonDocument.Parse(snapshot);
        using JsonDocument observed = JsonDocument.Parse(response.SnapshotUtf8);
        Assert.IsTrue(JsonElement.DeepEquals(original.RootElement, observed.RootElement));
        Assert.AreEqual(1, observed.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public async Task ActualBinderFormatterAndReaderRetainDispatchAndBooleanFacts()
    {
        await using var service = new HealthcheckService(new HealthcheckOptions(), timeProvider: new ManualTimeProvider(Epoch));
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Dispatched,
            OccurredAtUtc = Epoch, DispatchedAtUtc = Epoch, DispatchDelayMilliseconds = 19.5
        });
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch.AddMilliseconds(2), WorkCompletedAtUtc = Epoch.AddMilliseconds(2),
            SourcePhase = HealthExecutionPhase.Callback, Outcome = HealthExecutionOutcome.Failed,
            ConditionResult = true, CallbackResult = true, CleanupStatus = HealthExecutionStepStatus.Failed,
            CleanupError = new("Heartbeat.Cleanup"), CleanupCompletedAtUtc = Epoch.AddMilliseconds(8)
        });
        HealthReportBuffer wire = HealthReportFormatter.FormatPipeResponse(service.CaptureSnapshot(), HealthReportView.Details, new());
        Assert.IsFalse(wire.IsError);
        HealthParsedResponse parsed = Parse(wire.Utf8Bytes);
        using JsonDocument document = JsonDocument.Parse(parsed.SnapshotUtf8);
        JsonElement execution = document.RootElement.GetProperty("monitors")[0].GetProperty("outcomes").GetProperty("execution");
        Assert.AreEqual(19.5, execution.GetProperty("dispatchDelayMs").GetDouble());
        Assert.AreEqual("2026-11-16T09:00:00.0000000Z", execution.GetProperty("dispatchedAtUtc").GetString());
        Assert.IsTrue(execution.GetProperty("conditionResult").GetBoolean());
        Assert.IsTrue(execution.GetProperty("callbackResult").GetBoolean());
        Assert.IsFalse(execution.TryGetProperty("suppressedAtUtc", out _));
        Assert.IsFalse(execution.TryGetProperty("expectedCancellationPhase", out _));
        Assert.AreEqual("1", document.RootElement.GetProperty("monitors")[0].GetProperty("outcomes").GetProperty("failed").GetString());
    }

    [TestMethod]
    [DataRow(HealthExecutionPhase.Condition)]
    [DataRow(HealthExecutionPhase.Callback)]
    public async Task AcknowledgedCancellationRemainsVisibleWhenFinalCleanupFails(HealthExecutionPhase phase)
    {
        await using var service = new HealthcheckService(new HealthcheckOptions(), timeProvider: new ManualTimeProvider(Epoch));
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = phase,
            ExpectedCancellationPhase = phase, Outcome = HealthExecutionOutcome.Failed,
            CleanupStatus = HealthExecutionStepStatus.Failed, CleanupError = new("Heartbeat.Cleanup")
        });
        HealthReportBuffer wire = HealthReportFormatter.FormatPipeResponse(service.CaptureSnapshot(), HealthReportView.Details, new());
        HealthParsedResponse parsed = Parse(wire.Utf8Bytes);
        using JsonDocument document = JsonDocument.Parse(parsed.SnapshotUtf8);
        JsonElement execution = document.RootElement.GetProperty("monitors")[0].GetProperty("outcomes").GetProperty("execution");
        Assert.AreEqual(phase.ToString(), execution.GetProperty("expectedCancellationPhase").GetString());
        Assert.IsFalse(execution.TryGetProperty(phase == HealthExecutionPhase.Condition ? "conditionResult" : "callbackResult", out _));
    }

    [TestMethod]
    [DataRow("null-result")]
    [DataRow("string-result")]
    [DataRow("null-dispatch")]
    [DataRow("negative-dispatch-delay")]
    [DataRow("null-suppression-reason")]
    [DataRow("missing-suppression-reason")]
    [DataRow("invalid-suppression-phase")]
    [DataRow("started-suppressed-callback")]
    [DataRow("false-condition-callback")]
    [DataRow("boolean-cancellation")]
    [DataRow("invalid-cancellation-phase")]
    public void ReaderRejectsMalformedOrContradictoryAddedContext(string scenario)
    {
        JsonObject snapshot = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "heartbeat-suppressed-details.json"))!.AsObject();
        JsonObject execution = snapshot["monitors"]![0]!["outcomes"]!["execution"]!.AsObject();
        switch (scenario)
        {
            case "null-result": execution["callbackResult"] = null; break;
            case "string-result": execution["conditionResult"] = "true"; break;
            case "null-dispatch": execution["dispatchedAtUtc"] = null; break;
            case "negative-dispatch-delay": execution["dispatchDelayMs"] = -1; break;
            case "null-suppression-reason": execution["suppressionReason"] = null; break;
            case "missing-suppression-reason": execution.Remove("suppressionReason"); break;
            case "invalid-suppression-phase": execution["suppressedPhase"] = "Settled"; break;
            case "started-suppressed-callback": execution["callbackStartedAtUtc"] = "2026-11-16T09:00:00.0200000Z"; break;
            case "false-condition-callback": execution["conditionResult"] = false; execution["callbackResult"] = true; break;
            case "boolean-cancellation": execution["conditionResult"] = true; execution["expectedCancellationPhase"] = "Condition"; break;
            case "invalid-cancellation-phase": execution["expectedCancellationPhase"] = "Cleanup"; break;
            default: Assert.Fail("Unknown added-context rejection case."); break;
        }
        Assert.ThrowsExactly<FormatException>(() => Parse(Envelope(Encoding.UTF8.GetBytes(snapshot.ToJsonString()))));
    }

    [TestMethod]
    public void AuthoredNullResultFixtureIsRejectedInsteadOfTreatedAsAbsent()
    {
        byte[] invalid = HealthReportFormatterTests.ReadFixture("invalid", "heartbeat-result-null.json");
        Assert.ThrowsExactly<FormatException>(() => Parse(Envelope(invalid)));
    }

    [TestMethod]
    public async Task LegacyExecutionOmitsEveryAddedOptionalProperty()
    {
        await using var service = new HealthcheckService(new HealthcheckOptions(), timeProvider: new ManualTimeProvider(Epoch));
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Succeeded
        });
        HealthReportBuffer formatted = HealthReportFormatter.FormatPipeResponse(service.CaptureSnapshot(), HealthReportView.Details, new());
        HealthParsedResponse parsed = Parse(formatted.Utf8Bytes);
        using JsonDocument document = JsonDocument.Parse(parsed.SnapshotUtf8);
        JsonElement execution = document.RootElement.GetProperty("monitors")[0].GetProperty("outcomes").GetProperty("execution");
        CollectionAssert.AreEqual(new[] { "registrationId", "executionId", "phase", "workCompletedAtUtc", "errorHandlingStatus", "cleanupStatus" },
            execution.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static byte[] Envelope(byte[] snapshot) => Encoding.UTF8.GetBytes(
        "{\"protocolVersion\":1,\"schemaVersion\":1,\"view\":\"details\",\"kind\":\"snapshot\",\"snapshot\":" + Encoding.UTF8.GetString(snapshot) + "}");
    private static HealthParsedResponse Parse(ReadOnlyMemory<byte> payload) => HealthReportProtocol.ParseResponse(payload, HealthReportView.Details, 4_194_304);
    private static IHealthExecutionBinding Bind(HealthcheckService service) => HealthExecutionBinder.Bind(service,
        new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 }, new() { RegistrationId = Guid.NewGuid() });
}
