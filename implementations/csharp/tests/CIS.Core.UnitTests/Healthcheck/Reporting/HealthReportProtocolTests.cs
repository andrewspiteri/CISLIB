using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportProtocolTests
{
    [TestMethod]
    [DataRow("pipe-readiness-request.json", HealthReportView.Readiness)]
    [DataRow("pipe-additive-request.json", HealthReportView.Health)]
    public void RequestReaderAcceptsTheIndependentValidFixtures(string fixture, HealthReportView expected)
    {
        Assert.IsTrue(HealthReportProtocol.TryParseRequest(HealthReportFormatterTests.ReadFixture("valid", fixture), out HealthReportView view, out string? errorCode));
        Assert.AreEqual(expected, view);
        Assert.IsNull(errorCode);
    }

    [TestMethod]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\"}", HealthReportView.Health)]
    [DataRow("{\"protocolVersion\":1,\"view\":\"readiness\"}", HealthReportView.Readiness)]
    [DataRow("{\"protocolVersion\":1,\"view\":\"details\"}", HealthReportView.Details)]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"x\":true,\"X\":false}", HealthReportView.Health)]
    public void RequestReaderUsesCaseSensitiveNamedViewsAndAllowsAdditiveFields(string json, HealthReportView expected)
    {
        Assert.IsTrue(HealthReportProtocol.TryParseRequest(Encoding.UTF8.GetBytes(json), out HealthReportView view, out string? errorCode));
        Assert.AreEqual(expected, view);
        Assert.IsNull(errorCode);
    }

    [TestMethod]
    [DataRow("{\"protocolVersion\":2,\"view\":\"health\"}", "UnsupportedVersion")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"Health\"}", "InvalidView")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"other\"}", "InvalidView")]
    [DataRow("{\"protocolVersion\":1}", "InvalidRequest")]
    [DataRow("{\"view\":\"health\"}", "InvalidRequest")]
    [DataRow("{\"ProtocolVersion\":1,\"view\":\"health\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":\"1\",\"view\":\"health\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":true,\"view\":\"health\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":null}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":false}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"view\":\"details\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":{\"x\":1,\"x\":2}}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":[{\"x\":1,\"x\":2}]}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":\"\\ud800\"}", "InvalidRequest")]
    [DataRow("[]", "InvalidRequest")]
    [DataRow("null", "InvalidRequest")]
    [DataRow("{", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\",}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,/*x*/\"view\":\"health\"}", "InvalidRequest")]
    [DataRow("{\"protocolVersion\":1,\"view\":\"health\"}{}", "InvalidRequest")]
    public void RequestReaderRejectsInvalidVersionsTypesDuplicatesAndMalformedJson(string json, string expectedCode)
    {
        Assert.IsFalse(HealthReportProtocol.TryParseRequest(Encoding.UTF8.GetBytes(json), out _, out string? errorCode));
        Assert.AreEqual(expectedCode, errorCode);
    }

    [TestMethod]
    public void DuplicatePropertyInUnknownObjectIsRejectedUsingTheIndependentFixture()
    {
        Assert.IsFalse(HealthReportProtocol.TryParseRequest(HealthReportFormatterTests.ReadFixture("invalid", "duplicate-properties-request.txt"), out _, out string? errorCode));
        Assert.AreEqual("InvalidRequest", errorCode);
    }

    [TestMethod]
    public void UnknownAdditiveObjectsStillObserveTheMaximumNestingDepth()
    {
        string nested = "0";
        for (int depth = 0; depth < 16; depth++) { nested = "{\"x\":" + nested + "}"; }
        byte[] payload = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":" + nested + "}");

        Assert.IsFalse(HealthReportProtocol.TryParseRequest(payload, out _, out string? errorCode));
        Assert.AreEqual("InvalidRequest", errorCode);
    }

    [TestMethod]
    public void InvalidUtf8CannotBecomeAValidRequestThroughReplacementDecoding()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\",\"unknown\":\"");
        byte[] payload = [.. prefix, 0xC0, 0xAF, (byte)'"', (byte)'}'];
        Assert.IsFalse(HealthReportProtocol.TryParseRequest(payload, out _, out string? errorCode));
        Assert.AreEqual("InvalidFrame", errorCode);
    }

    [TestMethod]
    public void ResponseReaderAcceptsTheIndependentCompactFixtureAndSelectsReadiness()
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("valid", "pipe-readiness-response.json");
        HealthParsedResponse result = HealthReportProtocol.ParseResponse(payload, HealthReportView.Readiness, 4_194_304);
        using JsonDocument snapshot = JsonDocument.Parse(result.SnapshotUtf8);
        using JsonDocument expected = JsonDocument.Parse(HealthReportFormatterTests.ReadFixture("valid", "pending-readiness.json"));

        Assert.IsFalse(result.IsError);
        Assert.IsNull(result.ErrorCode);
        Assert.IsFalse(result.SelectedHealthy);
        Assert.IsTrue(JsonElement.DeepEquals(expected.RootElement, snapshot.RootElement));
    }

    [TestMethod]
    public void ResponseReaderAcceptsCompleteIndependentDetailsWithEveryPolicyAndScalarKind()
    {
        JsonObject envelope = DetailsEnvelope();
        HealthParsedResponse result = Parse(envelope, HealthReportView.Details);
        using JsonDocument snapshot = JsonDocument.Parse(result.SnapshotUtf8);

        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.SelectedHealthy);
        Assert.AreEqual(4, snapshot.RootElement.GetProperty("monitors").GetArrayLength());
        Assert.AreEqual("9223372036854775807", snapshot.RootElement.GetProperty("monitors")[1]
            .GetProperty("measurements")[0].GetProperty("value").GetString());
    }

    [TestMethod]
    [DataRow("ready")]
    [DataRow("aggregate")]
    [DataRow("revision")]
    public void ResponseReaderRejectsIndependentInvalidSnapshotFixtures(string scenario)
    {
        string fixture = scenario switch
        {
            "ready" => "ready-contradiction.json",
            "aggregate" => "aggregate-contradiction.json",
            _ => "noncanonical-revision.json"
        };
        JsonObject envelope = CompactEnvelope();
        envelope["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("invalid", fixture));
        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Readiness));
    }

    [TestMethod]
    public void ResponseReaderRejectsTheIndependentMismatchedViewFixture()
    {
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.ParseResponse(
            HealthReportFormatterTests.ReadFixture("invalid", "view-mismatch-response.json"), HealthReportView.Readiness, 4_194_304));
    }

    [TestMethod]
    [DataRow("protocol-version")]
    [DataRow("schema-version")]
    [DataRow("snapshot-version")]
    [DataRow("missing-kind")]
    [DataRow("missing-snapshot")]
    [DataRow("error-and-snapshot")]
    [DataRow("snapshot-null")]
    [DataRow("view-null")]
    [DataRow("revision-number")]
    [DataRow("revision-sign")]
    [DataRow("revision-leading-zero")]
    [DataRow("revision-empty")]
    [DataRow("revision-exponent")]
    [DataRow("ready")]
    [DataRow("health-enum")]
    [DataRow("active-too-large")]
    [DataRow("active-negative")]
    [DataRow("active-string")]
    [DataRow("critical-over-active")]
    [DataRow("failing-pending-over-active")]
    [DataRow("critical-failing-over-critical")]
    [DataRow("critical-failing-over-failing")]
    [DataRow("critical-intersection")]
    [DataRow("aggregate")]
    [DataRow("time-offset")]
    [DataRow("time-invalid-date")]
    [DataRow("time-too-precise")]
    [DataRow("time-naked-dot")]
    [DataRow("guid-upper")]
    [DataRow("guid-empty")]
    [DataRow("name-empty")]
    [DataRow("name-oversized")]
    public void ResponseReaderRejectsInvalidEnvelopeCountsIdentityAndScalarPrecision(string scenario)
    {
        JsonObject envelope = CompactEnvelope();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        JsonObject counts = snapshot["monitorCounts"]!.AsObject();
        JsonObject application = snapshot["application"]!.AsObject();
        switch (scenario)
        {
            case "protocol-version": envelope["protocolVersion"] = 2; break;
            case "schema-version": envelope["schemaVersion"] = 2; break;
            case "snapshot-version": snapshot["schemaVersion"] = 2; break;
            case "missing-kind": envelope.Remove("kind"); break;
            case "missing-snapshot": envelope.Remove("snapshot"); break;
            case "error-and-snapshot": envelope["error"] = new JsonObject { ["code"] = "ReportTooLarge" }; break;
            case "snapshot-null": envelope["snapshot"] = null; break;
            case "view-null": envelope["view"] = null; break;
            case "revision-number": snapshot["revision"] = 1; break;
            case "revision-sign": snapshot["revision"] = "+1"; break;
            case "revision-leading-zero": snapshot["revision"] = "01"; break;
            case "revision-empty": snapshot["revision"] = ""; break;
            case "revision-exponent": snapshot["revision"] = "1e2"; break;
            case "ready": snapshot["ready"] = true; break;
            case "health-enum": snapshot["healthStatus"] = "healthy"; break;
            case "active-too-large": counts["active"] = 65_537; break;
            case "active-negative": counts["active"] = -1; break;
            case "active-string": counts["active"] = "1"; break;
            case "critical-over-active": counts["critical"] = 2; break;
            case "failing-pending-over-active": counts["failing"] = 1; break;
            case "critical-failing-over-critical": counts["critical"] = 0; counts["criticalFailing"] = 1; counts["failing"] = 1; counts["pending"] = 0; break;
            case "critical-failing-over-failing": counts["criticalFailing"] = 1; counts["pending"] = 0; break;
            case "critical-intersection": counts["pending"] = 0; counts["failing"] = 1; break;
            case "aggregate": counts["criticalFailing"] = 1; counts["failing"] = 1; counts["pending"] = 0; break;
            case "time-offset": snapshot["capturedAtUtc"] = "2026-10-20T08:01:00+00:00"; break;
            case "time-invalid-date": snapshot["capturedAtUtc"] = "2026-02-30T08:01:00Z"; break;
            case "time-too-precise": snapshot["capturedAtUtc"] = "2026-10-20T08:01:00.12345678Z"; break;
            case "time-naked-dot": snapshot["capturedAtUtc"] = "2026-10-20T08:01:00.Z"; break;
            case "guid-upper": application["instanceId"] = "ABCDEF01-2345-6789-ABCD-EF0123456789"; break;
            case "guid-empty": application["instanceId"] = "00000000-0000-0000-0000-000000000000"; break;
            case "name-empty": application["name"] = ""; break;
            case "name-oversized": application["name"] = new string('x', 256); break;
            default: Assert.Fail("Unrecognized rejection scenario."); break;
        }
        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Readiness), scenario);
    }

    [TestMethod]
    [DataRow("missing-monitors")]
    [DataRow("partial-monitors")]
    [DataRow("duplicate-monitor")]
    [DataRow("wrong-monitor-count")]
    [DataRow("wrong-critical-count")]
    [DataRow("wrong-failing-count")]
    [DataRow("wrong-pending-count")]
    [DataRow("missing-measurements")]
    [DataRow("duplicate-measurement")]
    [DataRow("unordered-measurements")]
    [DataRow("integer-number")]
    [DataRow("integer-overflow")]
    [DataRow("integer-leading-zero")]
    [DataRow("integer-negative-zero")]
    [DataRow("unavailable-value")]
    [DataRow("boolean-string")]
    [DataRow("number-string")]
    [DataRow("diagnostic-required-flag")]
    [DataRow("numeric-missing")]
    [DataRow("numeric-null-value")]
    [DataRow("counter-number")]
    [DataRow("counter-overflow")]
    [DataRow("execution-zero")]
    [DataRow("duration-negative")]
    [DataRow("counter-streak-over-total")]
    [DataRow("counter-missing-saturation")]
    [DataRow("counter-false-saturation")]
    [DataRow("counter-last-outcome-uncounted")]
    [DataRow("counter-success-with-streak")]
    [DataRow("collection-error-without-time")]
    [DataRow("collection-time-without-error")]
    [DataRow("pending-numeric-with-value")]
    public void DetailsReaderRejectsPartialListsDuplicateKeysAndWrongScalarRepresentations(string scenario)
    {
        JsonObject envelope = DetailsEnvelope();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        JsonArray monitors = snapshot["monitors"]!.AsArray();
        JsonObject outcomes = monitors[1]!["outcomes"]!.AsObject();
        JsonArray measurements = monitors[2]!["measurements"]!.AsArray();
        switch (scenario)
        {
            case "missing-monitors": snapshot.Remove("monitors"); break;
            case "partial-monitors": monitors.RemoveAt(3); break;
            case "duplicate-monitor": monitors[1]!["monitorId"] = monitors[0]!["monitorId"]!.GetValue<string>(); break;
            case "wrong-monitor-count": snapshot["monitorCounts"]!["active"] = 5; break;
            case "wrong-critical-count": snapshot["monitorCounts"]!["critical"] = 1; break;
            case "wrong-failing-count": snapshot["monitorCounts"]!["failing"] = 2; break;
            case "wrong-pending-count": snapshot["monitorCounts"]!["pending"] = 1; break;
            case "missing-measurements": monitors[0]!.AsObject().Remove("measurements"); break;
            case "duplicate-measurement": measurements.Add(measurements[0]!.DeepClone()); break;
            case "unordered-measurements": JsonNode first = measurements[0]!.DeepClone(); measurements.RemoveAt(0); measurements.Add(first); break;
            case "integer-number": monitors[1]!["measurements"]![0]!["value"] = 1; break;
            case "integer-overflow": monitors[1]!["measurements"]![0]!["value"] = "9223372036854775808"; break;
            case "integer-leading-zero": monitors[1]!["measurements"]![0]!["value"] = "01"; break;
            case "integer-negative-zero": monitors[1]!["measurements"]![0]!["value"] = "-0"; break;
            case "unavailable-value": measurements[1]!["value"] = null; break;
            case "boolean-string": measurements[0]!["value"] = "true"; break;
            case "number-string": measurements[2]!["value"] = "0.5"; break;
            case "diagnostic-required-flag": monitors[1]!["diagnostic"]!.AsObject().Remove("isTruncated"); break;
            case "numeric-missing": monitors[2]!.AsObject().Remove("numeric"); break;
            case "numeric-null-value": monitors[2]!["numeric"]!["lastValue"] = null; break;
            case "counter-number": outcomes["failed"] = 3; break;
            case "counter-overflow": outcomes["failed"] = "9223372036854775808"; break;
            case "execution-zero": outcomes["execution"]!["executionId"] = "0"; break;
            case "duration-negative": outcomes["execution"]!["totalDurationMs"] = -1; break;
            case "counter-streak-over-total": outcomes["consecutiveFailures"] = "4"; break;
            case "counter-missing-saturation": outcomes["failed"] = "9223372036854775807"; break;
            case "counter-false-saturation": outcomes["countersSaturated"] = true; break;
            case "counter-last-outcome-uncounted": outcomes["lastOutcome"] = "Skipped"; break;
            case "counter-success-with-streak": outcomes["successful"] = "1"; outcomes["lastOutcome"] = "Succeeded"; break;
            case "collection-error-without-time": monitors[3]!["collection"]!.AsObject().Remove("lastErrorAtUtc"); break;
            case "collection-time-without-error": monitors[3]!["collection"]!.AsObject().Remove("lastError"); break;
            case "pending-numeric-with-value": monitors[2]!["state"] = "Pending"; snapshot["monitorCounts"]!["pending"] = 1; break;
            default: Assert.Fail("Unrecognized rejection scenario."); break;
        }
        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Details), scenario);
    }

    [TestMethod]
    [DataRow("not-invoked-handler-start")]
    [DataRow("not-invoked-handler-duration")]
    [DataRow("not-started-handler-completion")]
    [DataRow("not-started-cleanup-completion")]
    [DataRow("not-applicable-cleanup-duration")]
    [DataRow("successful-handler-error")]
    [DataRow("settlement-in-callback-phase")]
    [DataRow("settled-running-handler")]
    [DataRow("settled-running-cleanup")]
    public void ExecutionReaderRejectsImpossibleSecondaryAndSettlementFacts(string scenario)
    {
        JsonObject envelope = DetailsEnvelope();
        JsonObject execution = envelope["snapshot"]!["monitors"]![1]!["outcomes"]!["execution"]!.AsObject();
        switch (scenario)
        {
            case "not-invoked-handler-start":
                execution["errorHandlingStatus"] = "NotInvoked";
                execution["errorHandlingStartedAtUtc"] = "2026-10-20T08:03:00Z";
                break;
            case "not-invoked-handler-duration":
                execution["errorHandlingStatus"] = "NotInvoked";
                execution["errorHandlingDurationMs"] = 0;
                break;
            case "not-started-handler-completion":
                RemoveSettlement(execution);
                execution["errorHandlingStatus"] = "NotStarted";
                execution["errorHandlingCompletedAtUtc"] = "2026-10-20T08:03:00Z";
                break;
            case "not-started-cleanup-completion":
                RemoveSettlement(execution);
                execution["cleanupStatus"] = "NotStarted";
                break;
            case "not-applicable-cleanup-duration":
                execution["cleanupStatus"] = "NotApplicable";
                break;
            case "successful-handler-error":
                execution["errorHandlingStatus"] = "Succeeded";
                execution["errorHandlingCompletedAtUtc"] = "2026-10-20T08:03:00Z";
                execution["errorHandlingError"] = new JsonObject { ["code"] = "HandlerFailed", ["isTruncated"] = false };
                break;
            case "settlement-in-callback-phase": execution["phase"] = "Callback"; break;
            case "settled-running-handler": execution["errorHandlingStatus"] = "Running"; break;
            case "settled-running-cleanup": execution["cleanupStatus"] = "Running"; break;
            default: Assert.Fail("Unrecognized lifecycle rejection scenario."); break;
        }
        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Details), scenario);

        static void RemoveSettlement(JsonObject execution)
        {
            execution["phase"] = "Callback";
            execution.Remove("settledAtUtc");
            execution.Remove("totalDurationMs");
        }
    }

    [TestMethod]
    [DataRow("condition-pending")]
    [DataRow("failures-pending-success")]
    [DataRow("failures-pending-failure")]
    [DataRow("availability-pending-success")]
    [DataRow("availability-pending-failure")]
    [DataRow("failures-unhealthy-zero-streak")]
    [DataRow("numeric-healthy-no-value")]
    [DataRow("availability-healthy-without-success")]
    [DataRow("failures-healthy-without-evaluation")]
    public void PolicyReaderRejectsImpossibleStatesWithCoherentCompleteCounts(string scenario)
    {
        JsonObject envelope = DetailsEnvelope();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        JsonArray monitors = snapshot["monitors"]!.AsArray();
        JsonObject counts = snapshot["monitorCounts"]!.AsObject();
        switch (scenario)
        {
            case "condition-pending":
                monitors[0]!["state"] = "Pending";
                counts["pending"] = 1;
                break;
            case "failures-pending-success":
                monitors[1]!["state"] = "Pending";
                monitors[1]!.AsObject().Remove("diagnostic");
                SetOnlySuccessfulOutcome(monitors[1]!["outcomes"]!.AsObject());
                counts["failing"] = 0;
                counts["pending"] = 1;
                break;
            case "failures-pending-failure":
                monitors[1]!["state"] = "Pending";
                counts["failing"] = 0;
                counts["pending"] = 1;
                break;
            case "availability-pending-success":
                monitors[3]!["state"] = "Pending";
                monitors[3]!.AsObject().Remove("diagnostic");
                monitors[3]!.AsObject().Remove("collection");
                SetOnlySuccessfulOutcome(monitors[3]!["outcomes"]!.AsObject());
                counts["pending"] = 1;
                break;
            case "availability-pending-failure":
                monitors[3]!["state"] = "Pending";
                monitors[3]!["outcomes"]!["successful"] = "0";
                monitors[3]!["outcomes"]!.AsObject().Remove("lastSuccessAtUtc");
                counts["pending"] = 1;
                break;
            case "failures-unhealthy-zero-streak": monitors[1]!["outcomes"]!["consecutiveFailures"] = "0"; break;
            case "numeric-healthy-no-value":
                monitors[2]!["numeric"]!.AsObject().Remove("lastValue");
                monitors[2]!["numeric"]!.AsObject().Remove("lastValueAtUtc");
                break;
            case "availability-healthy-without-success":
                ClearOutcomeHistory(monitors[3]!.AsObject());
                break;
            case "failures-healthy-without-evaluation":
                monitors[1]!["state"] = "Healthy";
                ClearOutcomeHistory(monitors[1]!.AsObject());
                counts["failing"] = 0;
                break;
            default: Assert.Fail("Unrecognized policy rejection scenario."); break;
        }
        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Details), scenario);

        static void SetOnlySuccessfulOutcome(JsonObject outcomes)
        {
            outcomes["successful"] = "1";
            outcomes["failed"] = "0";
            outcomes["consecutiveFailures"] = "0";
            outcomes["lastOutcome"] = "Succeeded";
            outcomes["lastSuccessAtUtc"] = "2026-10-20T08:03:00Z";
            outcomes.Remove("lastFailureAtUtc");
            outcomes.Remove("lastFailureDiagnostic");
            outcomes.Remove("execution");
        }

        static void ClearOutcomeHistory(JsonObject monitor)
        {
            JsonObject outcomes = monitor["outcomes"]!.AsObject();
            outcomes["successful"] = "0";
            outcomes["failed"] = "0";
            outcomes["skipped"] = "0";
            outcomes["expectedCanceled"] = "0";
            outcomes["consecutiveFailures"] = "0";
            outcomes.Remove("lastOutcome");
            outcomes.Remove("lastSuccessAtUtc");
            outcomes.Remove("lastFailureAtUtc");
            outcomes.Remove("lastFailureDiagnostic");
            outcomes.Remove("execution");
            monitor.Remove("diagnostic");
            monitor.Remove("collection");
            monitor.Remove("lastObservationAtUtc");
        }
    }

    [TestMethod]
    [DataRow("failures", "Skipped")]
    [DataRow("failures", "ExpectedCancellation")]
    [DataRow("availability", "Skipped")]
    [DataRow("availability", "ExpectedCancellation")]
    public void SkipsAndExpectedCancellationDoNotInventAnInitialPolicyEvaluation(string policy, string outcome)
    {
        JsonObject envelope = DetailsEnvelope();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        int index = policy == "failures" ? 1 : 3;
        JsonObject monitor = snapshot["monitors"]![index]!.AsObject();
        JsonObject outcomes = monitor["outcomes"]!.AsObject();
        monitor["state"] = "Pending";
        monitor.Remove("diagnostic");
        monitor.Remove("collection");
        outcomes["successful"] = "0";
        outcomes["failed"] = "0";
        outcomes["skipped"] = outcome == "Skipped" ? "1" : "0";
        outcomes["expectedCanceled"] = outcome == "ExpectedCancellation" ? "1" : "0";
        outcomes["consecutiveFailures"] = "0";
        outcomes["lastOutcome"] = outcome;
        outcomes.Remove("lastSuccessAtUtc");
        outcomes.Remove("lastFailureAtUtc");
        outcomes.Remove("lastFailureDiagnostic");
        outcomes.Remove("execution");
        snapshot["monitorCounts"]!["pending"] = 1;
        if (policy == "failures") { snapshot["monitorCounts"]!["failing"] = 0; }

        HealthParsedResponse result = Parse(envelope, HealthReportView.Details);
        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.SelectedHealthy);
    }

    [TestMethod]
    public void NumericRequiredEvaluationErrorCanBeUnhealthyBeforeAnyValueExists()
    {
        JsonObject envelope = DetailsEnvelope();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        JsonObject monitor = snapshot["monitors"]![2]!.AsObject();
        monitor["state"] = "Unhealthy";
        monitor["diagnostic"] = new JsonObject { ["code"] = "EvaluationFailed", ["isTruncated"] = false };
        monitor["numeric"]!.AsObject().Remove("lastValue");
        monitor["numeric"]!.AsObject().Remove("lastValueAtUtc");
        snapshot["monitorCounts"]!["failing"] = 2;

        HealthParsedResponse result = Parse(envelope, HealthReportView.Details);
        Assert.IsFalse(result.IsError);
        Assert.IsTrue(result.SelectedHealthy);
    }

    [TestMethod]
    [DataRow("duplicate-root")]
    [DataRow("duplicate-unknown")]
    [DataRow("invalid-unicode")]
    [DataRow("nonfinite-double")]
    public void StrictResponseValidationPrecedesMaterializationOfUnknownFields(string scenario)
    {
        string payload = CompactEnvelope().ToJsonString();
        payload = scenario switch
        {
            "duplicate-root" => payload.Insert(1, "\"protocolVersion\":1,"),
            "duplicate-unknown" => payload.Insert(1, "\"unknown\":{\"x\":1,\"x\":2},"),
            "invalid-unicode" => payload.Insert(1, "\"unknown\":\"\\ud800\","),
            _ => DetailsEnvelope().ToJsonString().Replace("\"threshold\":10", "\"threshold\":1e9999", StringComparison.Ordinal)
        };
        HealthReportView view = scenario == "nonfinite-double" ? HealthReportView.Details : HealthReportView.Readiness;
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(payload), view, 4_194_304));
    }

    [TestMethod]
    public void RevisionRemainsAnArbitraryPrecisionIdentifierAndUnknownFieldsAreIgnorable()
    {
        JsonObject envelope = CompactEnvelope();
        string revision = new('9', 200);
        envelope["snapshot"]!["revision"] = revision;
        envelope["unknown"] = new JsonObject { ["extra"] = new JsonArray(1, 2, 3) };
        envelope["snapshot"]!["capturedAtUtc"] = "2026-10-20T08:01:00Z";
        HealthParsedResponse result = Parse(envelope, HealthReportView.Readiness);
        using JsonDocument snapshot = JsonDocument.Parse(result.SnapshotUtf8);

        Assert.AreEqual(revision, snapshot.RootElement.GetProperty("revision").GetString());
        Assert.IsFalse(result.SelectedHealthy);
    }

    [TestMethod]
    public void NumericEvaluationFailureMayRetainThePreviousInRangeValueWithoutReinterpretation()
    {
        JsonObject envelope = DetailsEnvelope();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        JsonObject numeric = snapshot["monitors"]![2]!.AsObject();
        numeric["state"] = "Unhealthy";
        numeric["diagnostic"] = new JsonObject { ["code"] = "EvaluationFailed", ["isTruncated"] = false };
        snapshot["monitorCounts"]!["failing"] = 2;
        HealthParsedResponse result = Parse(envelope, HealthReportView.Details);
        using JsonDocument document = JsonDocument.Parse(result.SnapshotUtf8);

        Assert.IsTrue(result.SelectedHealthy, "This numeric monitor is noncritical; the complete aggregate remains healthy.");
        JsonElement detail = document.RootElement.GetProperty("monitors")[2];
        Assert.AreEqual("Unhealthy", detail.GetProperty("state").GetString());
        Assert.AreEqual(10d, detail.GetProperty("numeric").GetProperty("lastValue").GetDouble());
        Assert.AreEqual("GreaterThan", detail.GetProperty("numeric").GetProperty("comparison").GetString());
    }

    [TestMethod]
    public void EveryValidErrorResponseSelectsFailureAndReturnsNoSnapshot()
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("valid", "pipe-invalid-request-response.json");
        HealthParsedResponse result = HealthReportProtocol.ParseResponse(payload, HealthReportView.Health, 4_194_304);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual("InvalidRequest", result.ErrorCode);
        Assert.IsFalse(result.SelectedHealthy);
        Assert.IsTrue(result.SnapshotUtf8.IsEmpty);
    }

    [TestMethod]
    [DataRow("unknown-code")]
    [DataRow("oversized-message")]
    [DataRow("valid-request-null-view")]
    public void ErrorEnvelopeStillRequiresKnownCodeBoundedTextAndValidViewDisposition(string scenario)
    {
        JsonObject envelope = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "pipe-invalid-request-response.json"))!.AsObject();
        switch (scenario)
        {
            case "unknown-code": envelope["error"]!["code"] = "AnythingElse"; break;
            case "oversized-message": envelope["error"]!["message"] = new string('x', 129); break;
            default: envelope["error"]!["code"] = "ReportTooLarge"; break;
        }
        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Health));
    }

    [TestMethod]
    public void ResponsePayloadLengthIsCheckedBeforeAcceptingAnOtherwiseValidReport()
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("valid", "pipe-readiness-response.json");
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.ParseResponse(payload, HealthReportView.Readiness, payload.Length - 1));
    }

    [TestMethod]
    public void RequestLengthBoundsAreFramingErrorsBeforeJsonInterpretation()
    {
        Assert.IsFalse(HealthReportProtocol.TryParseRequest(ReadOnlyMemory<byte>.Empty, out _, out string? emptyCode));
        Assert.AreEqual("InvalidFrame", emptyCode);
        Assert.IsFalse(HealthReportProtocol.TryParseRequest(new byte[513], out _, out string? oversizedCode));
        Assert.AreEqual("InvalidFrame", oversizedCode);
    }

    [TestMethod]
    public void CompleteRequestAtTheExact512ByteBoundaryIsAccepted()
    {
        string json = "{\"protocolVersion\":1,\"view\":\"health\"}";
        byte[] payload = Encoding.UTF8.GetBytes(json.PadRight(512, ' '));

        Assert.IsTrue(HealthReportProtocol.TryParseRequest(payload, out HealthReportView view, out string? errorCode));
        Assert.AreEqual(HealthReportView.Health, view);
        Assert.IsNull(errorCode);
    }

    [TestMethod]
    public void ParsedSnapshotOwnsItsBytesAfterTheInputBufferIsOverwritten()
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("valid", "pipe-readiness-response.json");
        HealthParsedResponse result = HealthReportProtocol.ParseResponse(payload, HealthReportView.Readiness, 4_194_304);
        byte[] original = result.SnapshotUtf8.ToArray();
        Array.Fill(payload, (byte)'x');

        CollectionAssert.AreEqual(original, result.SnapshotUtf8.ToArray());
        using JsonDocument document = JsonDocument.Parse(result.SnapshotUtf8);
        Assert.AreEqual("1", document.RootElement.GetProperty("revision").GetString());
    }

    [TestMethod]
    public void ResponseUnknownFieldsCannotExceedTheDepthBudget()
    {
        JsonObject envelope = CompactEnvelope();
        JsonObject nested = new() { ["value"] = 1 };
        for (int depth = 0; depth < 16; depth++) { nested = new JsonObject { ["nested"] = nested }; }
        envelope["unknown"] = nested;

        Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Readiness));
    }

    [TestMethod]
    public void FourBytePrefixUsesUnsignedLittleEndianPayloadLength()
    {
        byte[] payload = Enumerable.Range(0, 300).Select(value => (byte)value).ToArray();
        byte[] framed = HealthReportProtocol.EncodeFrame(payload, 512);

        CollectionAssert.AreEqual(new byte[] { 0x2C, 0x01, 0x00, 0x00 }, framed[..4]);
        Assert.AreEqual(304, framed.Length);
        CollectionAssert.AreEqual(payload, framed[4..]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(17)]
    public async Task FrameReaderReassemblesArbitraryPrefixAndBodyFragments(int fragmentSize)
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("valid", "pipe-readiness-response.json");
        byte[] prefix = new byte[] { (byte)payload.Length, (byte)(payload.Length >> 8), 0, 0 };
        using var stream = new FragmentedReadStream([.. prefix, .. payload], fragmentSize);
        byte[] actual = await HealthReportProtocol.ReadFrameAsync(stream, 4_194_304, CancellationToken.None);

        CollectionAssert.AreEqual(payload, actual);
        Assert.AreEqual((long)payload.Length + 4, stream.Position);
    }

    [TestMethod]
    [DataRow("empty-prefix")]
    [DataRow("short-prefix")]
    [DataRow("zero")]
    [DataRow("oversized")]
    [DataRow("unsigned-overflow")]
    [DataRow("truncated-body")]
    public async Task InvalidAndTruncatedFramesNeverReturnPartialPayloads(string scenario)
    {
        byte[] bytes = scenario switch
        {
            "empty-prefix" => [],
            "short-prefix" => [1, 0, 0],
            "zero" => [0, 0, 0, 0],
            "oversized" => [1, 2, 0, 0],
            "unsigned-overflow" => [255, 255, 255, 255],
            _ => [3, 0, 0, 0, (byte)'{', (byte)'}']
        };
        using var stream = new FragmentedReadStream(bytes, 1);
        await Assert.ThrowsExactlyAsync<FormatException>(() => HealthReportProtocol.ReadFrameAsync(stream, 512, CancellationToken.None));
        if (scenario is "zero" or "oversized" or "unsigned-overflow")
        {
            Assert.AreEqual(4L, stream.Position, "Reject an invalid declared length before reading or allocating its body.");
        }
    }

    [TestMethod]
    public async Task IndependentTruncatedRequestFixtureFailsAtTheFramingBoundary()
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("invalid", "truncated-request.txt");
        int claimedLength = payload.Length + 1;
        using var stream = new MemoryStream([(byte)claimedLength, (byte)(claimedLength >> 8), 0, 0, .. payload]);

        await Assert.ThrowsExactlyAsync<FormatException>(() => HealthReportProtocol.ReadFrameAsync(stream, 512, CancellationToken.None));
    }

    [TestMethod]
    public async Task FrameWriterEmitsExactlyOneLengthPrefixAndTheCompleteOriginalPayload()
    {
        byte[] payload = HealthReportFormatterTests.ReadFixture("valid", "pipe-readiness-request.json");
        using var stream = new MemoryStream();
        await HealthReportProtocol.WriteFrameAsync(stream, payload, 512, CancellationToken.None);
        byte[] actual = stream.ToArray();

        Assert.AreEqual(payload.Length + 4, actual.Length);
        Assert.AreEqual((byte)payload.Length, actual[0]);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, actual[1..4]);
        CollectionAssert.AreEqual(payload, actual[4..]);
    }

    [TestMethod]
    public async Task InvalidOutgoingLengthFailsBeforeAnyBytesAreWritten()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsExactlyAsync<FormatException>(() => HealthReportProtocol.WriteFrameAsync(stream, ReadOnlyMemory<byte>.Empty, 512, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<FormatException>(() => HealthReportProtocol.WriteFrameAsync(stream, new byte[513], 512, CancellationToken.None));
        Assert.AreEqual(0L, stream.Length);
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.EncodeFrame([], 512));
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.EncodeFrame(new byte[513], 512));
    }

    [TestMethod]
    public async Task CanceledFrameReadAndWritePreserveCancellationRatherThanReturningAnErrorReport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var read = new MemoryStream([1, 0, 0, 0, 42]);
        using var write = new MemoryStream();

        await Assert.ThrowsAsync<OperationCanceledException>(() => HealthReportProtocol.ReadFrameAsync(read, 512, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => HealthReportProtocol.WriteFrameAsync(write, new byte[] { 42 }, 512, cancellation.Token));
        Assert.AreEqual(0L, write.Length);
    }

    private static HealthParsedResponse Parse(JsonObject envelope, HealthReportView view)
        => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(envelope.ToJsonString()), view, 4_194_304);

    private static JsonObject CompactEnvelope()
        => JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "pipe-readiness-response.json"))!.AsObject();

    private static JsonObject DetailsEnvelope()
        => new()
        {
            ["protocolVersion"] = 1,
            ["schemaVersion"] = 1,
            ["view"] = "details",
            ["kind"] = "snapshot",
            ["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"))
        };

    private sealed class FragmentedReadStream(byte[] bytes, int fragmentSize) : Stream
    {
        private readonly MemoryStream inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, fragmentSize));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer[..Math.Min(buffer.Length, fragmentSize)], cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
