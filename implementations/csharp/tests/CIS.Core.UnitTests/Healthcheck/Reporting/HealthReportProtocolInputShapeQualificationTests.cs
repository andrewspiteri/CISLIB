using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportProtocolInputShapeQualificationTests
{
    [TestMethod]
    public void AnOtherwiseValidResponseFitsExactlyAtItsCompleteEncodedLength()
    {
        byte[] bytes = Encode(Compact());
        Assert.IsFalse(HealthReportProtocol.ParseResponse(bytes, HealthReportView.Health, bytes.Length).IsError);
        Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.ParseResponse(bytes, HealthReportView.Health, bytes.Length - 1));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(16_777_217)]
    public void ResponseParsingRejectsInvalidConfiguredCapsBeforeInterpretingPayload(int maximum)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthReportProtocol.ParseResponse(Encode(Compact()), HealthReportView.Health, maximum));
    }

    [TestMethod]
    public void ResponseParsingRejectsAnUndefinedExpectedViewAsAnArgumentError()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthReportProtocol.ParseResponse(Encode(Compact()), (HealthReportView)999, 4_194_304));
    }

    [TestMethod]
    public async Task FramingRequiresAStreamForBothReadingAndWriting()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => HealthReportProtocol.ReadFrameAsync(null!, 1024, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => HealthReportProtocol.WriteFrameAsync(null!, new byte[] { 1 }, 1024, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("snapshot")]
    [DataRow("code")]
    public void ErrorEnvelopesCannotHideASnapshotOrAnUnknownCodeBehindAValidNamedView(string fault)
    {
        var envelope = new JsonObject
        {
            ["protocolVersion"] = 1,
            ["schemaVersion"] = 1,
            ["view"] = "health",
            ["kind"] = "error",
            ["error"] = new JsonObject { ["code"] = "InvalidRequest" }
        };
        Assert.IsTrue(Parse(envelope, HealthReportView.Health).IsError);
        if (fault == "snapshot") { envelope["snapshot"] = Compact()["snapshot"]!.DeepClone(); }
        else { envelope["error"]!["code"] = "AnythingElse"; }
        Reject(envelope, HealthReportView.Health);
    }

    [TestMethod]
    public void ASelectedSnapshotCannotContainAnotherEnvelopeKind()
    {
        JsonObject envelope = Details();
        Assert.IsFalse(Parse(envelope).IsError);
        envelope["snapshot"]!["kind"] = "snapshot";
        Reject(envelope);
    }

    [TestMethod]
    [DataRow("-1")]
    [DataRow("-9223372036854775808")]
    [DataRow("-0")]
    public void RevisionIsUnsignedEvenThoughItsPrecisionIsUnbounded(string negative)
    {
        JsonObject envelope = Compact();
        foreach (string valid in new[] { "0", "184467440737095516160000000000000000000000000000000" })
        {
            envelope["snapshot"]!["revision"] = valid;
            Assert.IsFalse(Parse(envelope, HealthReportView.Health).IsError);
        }
        envelope["snapshot"]!["revision"] = negative;
        Reject(envelope, HealthReportView.Health);
    }

    [TestMethod]
    [DataRow("critical-failing")]
    [DataRow("pending")]
    public void CompactCountContradictionsAreRejectedWithACoherentUnhealthyAggregate(string fault)
    {
        JsonObject envelope = Compact();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        snapshot["healthStatus"] = "Unhealthy";
        snapshot["ready"] = false;
        snapshot["monitorCounts"] = fault == "critical-failing"
            ? new JsonObject { ["active"] = 4, ["critical"] = 1, ["failing"] = 3, ["criticalFailing"] = 1, ["pending"] = 0 }
            : new JsonObject { ["active"] = 4, ["critical"] = 2, ["failing"] = 2, ["criticalFailing"] = 1, ["pending"] = 2 };
        Assert.IsFalse(Parse(envelope, HealthReportView.Health).SelectedHealthy);

        snapshot["monitorCounts"]![fault == "critical-failing" ? "criticalFailing" : "pending"] = fault == "critical-failing" ? 2 : 3;

        Reject(envelope, HealthReportView.Health);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NumericValueAndItsObservationTimeMustBePresentTogether(bool keepValue)
    {
        JsonObject envelope = Details();
        JsonObject monitor = envelope["snapshot"]!["monitors"]![2]!.AsObject();
        Assert.IsFalse(Parse(envelope).IsError);
        monitor["numeric"]!.AsObject().Remove(keepValue ? "lastValueAtUtc" : "lastValue");
        if (!keepValue)
        {
            monitor["state"] = "Unhealthy";
            envelope["snapshot"]!["monitorCounts"]!["failing"] = 2;
        }
        Reject(envelope);
    }

    [TestMethod]
    [DataRow("condition")]
    [DataRow("failures")]
    [DataRow("availability")]
    public void PolicySpecificOutcomeObjectsAppearExactlyWhereApplicable(string policy)
    {
        JsonObject envelope = Details();
        JsonArray monitors = envelope["snapshot"]!["monitors"]!.AsArray();
        Assert.IsFalse(Parse(envelope).IsError);
        if (policy == "condition") { monitors[0]!["outcomes"] = monitors[1]!["outcomes"]!.DeepClone(); }
        else { monitors[policy == "failures" ? 1 : 3]!.AsObject().Remove("outcomes"); }
        Reject(envelope);
    }

    [TestMethod]
    public void MeasurementArrayAcceptsExactly32DistinctKeysAndRejectsThe33rd()
    {
        JsonObject envelope = Details();
        var measurements = new JsonArray();
        for (int index = 0; index < 32; index++) { measurements.Add(Measurement(index)); }
        envelope["snapshot"]!["monitors"]![0]!["measurements"] = measurements;
        Assert.IsFalse(Parse(envelope).IsError);

        measurements.Add(Measurement(32));

        Reject(envelope);
    }

    [TestMethod]
    public void AnOptionalMeasurementMustActuallyBeMarkedDiagnostic()
    {
        JsonObject envelope = Details();
        Assert.IsFalse(Parse(envelope).IsError);
        envelope["snapshot"]!["monitors"]![1]!["measurements"]![0]!["isDiagnostic"] = false;
        Reject(envelope);
    }

    [TestMethod]
    public void TotalDurationCannotClaimSettlementBeforeAnActualSettlementTimeExists()
    {
        JsonObject envelope = Details();
        var execution = new JsonObject
        {
            ["registrationId"] = "30000000-0000-0000-0000-000000000001",
            ["executionId"] = "3",
            ["phase"] = "Callback",
            ["errorHandlingStatus"] = "NotStarted",
            ["cleanupStatus"] = "NotStarted"
        };
        envelope["snapshot"]!["monitors"]![1]!["outcomes"]!["execution"] = execution;
        Assert.IsFalse(Parse(envelope).IsError);
        execution["totalDurationMs"] = 0;
        Reject(envelope);
        execution["phase"] = "Settled";
        execution["settledAtUtc"] = "2026-10-20T08:03:00Z";
        execution["errorHandlingStatus"] = "NotApplicable";
        execution["cleanupStatus"] = "NotApplicable";
        Assert.IsFalse(Parse(envelope).IsError);
    }

    [TestMethod]
    [DataRow("healthStatus")]
    [DataRow("monitors/0/policy")]
    [DataRow("monitors/0/state")]
    [DataRow("monitors/2/numeric/comparison")]
    [DataRow("monitors/0/measurements/0/valueKind")]
    [DataRow("monitors/1/outcomes/lastOutcome")]
    [DataRow("monitors/1/outcomes/execution/phase")]
    [DataRow("monitors/1/outcomes/execution/errorHandlingStatus")]
    [DataRow("monitors/1/outcomes/execution/cleanupStatus")]
    public void UndefinedNumericEnumsAreRejectedEvenWhenTheNumberHasNoNamedValue(string path)
    {
        JsonObject envelope = Details();
        envelope["snapshot"]!["monitors"]![0]!["measurements"] = new JsonArray(Measurement(0));
        if (path == "healthStatus")
        {
            envelope["snapshot"]!["monitors"]![0]!["state"] = "Unhealthy";
            envelope["snapshot"]!["monitorCounts"]!["failing"] = 2;
            envelope["snapshot"]!["monitorCounts"]!["criticalFailing"] = 1;
            envelope["snapshot"]!["healthStatus"] = "Unhealthy";
            envelope["snapshot"]!["ready"] = false;
        }
        if (path.EndsWith("/phase", StringComparison.Ordinal))
        {
            envelope["snapshot"]!["monitors"]![1]!["outcomes"]!["execution"] = new JsonObject
            {
                ["registrationId"] = "30000000-0000-0000-0000-000000000001",
                ["executionId"] = "3",
                ["phase"] = "Callback",
                ["errorHandlingStatus"] = "NotStarted",
                ["cleanupStatus"] = "NotStarted"
            };
        }
        Assert.IsFalse(Parse(envelope).IsError);

        Set(envelope["snapshot"]!, path, "999");

        Reject(envelope);
    }

    [TestMethod]
    public void UnknownSiblingObjectsDoNotShareTheirPropertyUniquenessSets()
    {
        JsonObject envelope = Details();
        envelope["first"] = new JsonObject { ["shared"] = 1 };
        envelope["shared"] = 2;
        envelope["second"] = new JsonObject { ["shared"] = 3 };
        Assert.IsFalse(Parse(envelope).IsError);
    }

    [TestMethod]
    public void UnknownPropertyNamesMayContainEscapedValidControlScalars()
    {
        JsonObject envelope = Details();
        envelope["line\n\t\u0001"] = new JsonObject { ["nested\r\0"] = true };
        Assert.IsFalse(Parse(envelope).IsError);
        byte[] request = Encoding.UTF8.GetBytes(new JsonObject { ["protocolVersion"] = 1, ["view"] = "health", ["line\n\0"] = 1 }.ToJsonString());
        Assert.IsTrue(HealthReportProtocol.TryParseRequest(request, out HealthReportView view, out string? error));
        Assert.AreEqual(HealthReportView.Health, view);
        Assert.IsNull(error);
    }

    [TestMethod]
    public void UtcTimestampValidationAcceptsBothCalendarEndpoints()
    {
        JsonObject envelope = Compact();
        foreach (string time in new[] { "0001-01-01T00:00:00Z", "9999-12-31T23:59:59.9999999Z" })
        {
            envelope["snapshot"]!["capturedAtUtc"] = time;
            Assert.IsFalse(Parse(envelope, HealthReportView.Health).IsError);
        }
    }

    private static JsonObject Measurement(int index) => new()
    { ["key"] = "metric-" + index.ToString("D2", CultureInfo.InvariantCulture), ["valueKind"] = "Integer", ["value"] = index.ToString(CultureInfo.InvariantCulture), ["observedAtUtc"] = "2026-10-20T08:03:00Z", ["isDiagnostic"] = true };
    private static JsonObject Details() => Envelope(JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"))!, "details");
    private static JsonObject Compact() => Envelope(JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "pending-readiness.json"))!, "health");
    private static JsonObject Envelope(JsonNode snapshot, string view) => new() { ["protocolVersion"] = 1, ["schemaVersion"] = 1, ["view"] = view, ["kind"] = "snapshot", ["snapshot"] = snapshot };
    private static HealthParsedResponse Parse(JsonObject envelope, HealthReportView view = HealthReportView.Details) => HealthReportProtocol.ParseResponse(Encode(envelope), view, 4_194_304);
    private static byte[] Encode(JsonObject envelope) => Encoding.UTF8.GetBytes(envelope.ToJsonString());
    private static void Reject(JsonObject envelope, HealthReportView view = HealthReportView.Details) => Assert.ThrowsExactly<FormatException>(() => Parse(envelope, view));
    private static void Set(JsonNode value, string path, string text)
    {
        string[] parts = path.Split('/');
        foreach (string part in parts[..^1]) { value = value is JsonArray array ? array[int.Parse(part, CultureInfo.InvariantCulture)]! : value[part]!; }
        value[parts[^1]] = text;
    }
}
