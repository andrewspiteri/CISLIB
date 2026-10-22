using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportProtocolMutationQualificationTests
{
    private const string Time = "2026-10-20T08:03:00Z";
    private const string Execution = "monitors/1/outcomes/execution/";
    private static readonly string[] Diagnostics =
    [
        "monitors/1/diagnostic", "monitors/1/outcomes/lastFailureDiagnostic", "monitors/3/collection/lastError",
        Execution + "originalError", Execution + "errorHandlingError", Execution + "cleanupError", Execution + "integrationError"
    ];

    public static IEnumerable<object[]> TextFields()
    {
        yield return ["application/name", 255];
        foreach (string field in new[] { "buildVersion", "serviceVersion" }) { yield return ["application/" + field, 512]; }
        yield return ["application/hostName", 255];
        yield return ["application/osDescription", 1024];
        yield return ["monitors/0/name", 128];
        yield return ["monitors/2/numeric/unit", 32];
        yield return ["monitors/0/measurements/0/key", 128];
        yield return ["monitors/0/measurements/0/unit", 32];
        yield return ["monitors/0/measurements/0/value", 1024];
        foreach (string path in Diagnostics)
        {
            yield return [path + "/code", 64];
            yield return [path + "/message", 1024];
            yield return [path + "/exceptionType", 256];
        }
    }

    [TestMethod]
    [DynamicData(nameof(TextFields))]
    public void EveryPublishedTextFieldChecksItsOwnScalarLimitAndJsonType(string path, int maximum)
    {
        JsonObject envelope = Details();
        Set(envelope, path, new string('x', maximum));
        Assert.IsFalse(Parse(envelope).IsError, path);
        Set(envelope, path, string.Concat(Enumerable.Repeat("\U0001F680", maximum)));
        Assert.IsFalse(Parse(envelope).IsError, path + " counts Unicode scalars");
        Set(envelope, path, new string('x', maximum + 1));
        Reject(envelope, path + " oversized");
        Set(envelope, path, null);
        Reject(envelope, path + " null");
        Set(envelope, path, true);
        Reject(envelope, path + " boolean");
    }

    public static IEnumerable<object[]> DiagnosticFields() => Diagnostics.Select(path => new object[] { path });

    [TestMethod]
    [DynamicData(nameof(DiagnosticFields))]
    public void EveryDiagnosticRequiresAnObjectAndItsOwnBooleanTruncationFlag(string path)
    {
        JsonObject envelope = Details();
        Set(envelope, path, new JsonObject { ["isTruncated"] = false });
        Assert.IsFalse(Parse(envelope).IsError);
        foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create("reason"), JsonValue.Create(1), new JsonArray() })
        {
            Set(envelope, path, invalid);
            Reject(envelope, path);
        }
        Set(envelope, path, new JsonObject());
        Reject(envelope, path + " missing flag");
        Set(envelope, path, new JsonObject { ["isTruncated"] = "false" });
        Reject(envelope, path + " wrong flag type");
    }

    public static IEnumerable<object[]> TimestampFields()
    {
        foreach (string path in new[] { "capturedAtUtc", "monitors/0/registeredAtUtc", "monitors/0/lastObservationAtUtc",
            "monitors/0/measurements/0/observedAtUtc", "monitors/2/numeric/lastValueAtUtc", "monitors/3/outcomes/lastSuccessAtUtc",
            "monitors/3/outcomes/lastFailureAtUtc", "monitors/3/collection/lastCompletedAtUtc", "monitors/3/collection/lastErrorAtUtc" })
        { yield return [path]; }
        foreach (string field in new[] { "eligibleAtUtc", "admittedAtUtc", "conditionStartedAtUtc", "callbackStartedAtUtc", "workCompletedAtUtc",
            "errorHandlingStartedAtUtc", "errorHandlingCompletedAtUtc", "cleanupCompletedAtUtc", "settledAtUtc" })
        { yield return [Execution + field]; }
    }

    [TestMethod]
    [DynamicData(nameof(TimestampFields))]
    public void EveryTimestampAcceptsAllPublishedPrecisionsAndRejectsMalformedOrOffsetValues(string path)
    {
        JsonObject envelope = Details();
        for (int precision = 0; precision <= 7; precision++)
        {
            string value = precision == 0 ? Time : "2026-10-20T08:03:00." + "1234567"[..precision] + "Z";
            Set(envelope, path, value);
            Assert.IsFalse(Parse(envelope).IsError, path + " precision " + precision.ToString(CultureInfo.InvariantCulture));
        }
        foreach (string invalid in new[] { "2026-02-30T08:03:00Z", "2026-10-20T08:03:00+00:00", "2026-10-20T08:03:00.Z", "2026-10-20T08:03:00.12345678Z", "2026-10-20T08:03:00" })
        { Set(envelope, path, invalid); Reject(envelope, path); }
        Set(envelope, path, null);
        Reject(envelope, path + " null");
    }

    public static IEnumerable<object[]> DurationFields() => new[] { "queueDelayMs", "conditionDurationMs", "callbackDurationMs", "errorHandlingDurationMs", "cleanupDurationMs", "totalDurationMs" }
        .Select(field => new object[] { field });

    [TestMethod]
    [DynamicData(nameof(DurationFields))]
    public void EveryMeasuredDurationAcceptsZeroAndFractionalValuesButRejectsNegativeOrWrongTypes(string field)
    {
        JsonObject envelope = Details();
        foreach (double value in new[] { 0d, 0.125d, double.MaxValue })
        { Set(envelope, Execution + field, value); Assert.IsFalse(Parse(envelope).IsError, field); }
        foreach (JsonNode? invalid in new JsonNode?[] { JsonValue.Create(-0.125d), JsonValue.Create("1"), JsonValue.Create(true), null, JsonNode.Parse("1e999") })
        { Set(envelope, Execution + field, invalid); Reject(envelope, field); }
    }

    [TestMethod]
    [DataRow("healthStatus", "0")]
    [DataRow("monitors/0/policy", "0")]
    [DataRow("monitors/0/state", "1")]
    [DataRow("monitors/2/numeric/comparison", "0")]
    [DataRow("monitors/0/measurements/0/valueKind", "1")]
    [DataRow("monitors/1/outcomes/lastOutcome", "1")]
    [DataRow("monitors/1/outcomes/execution/phase", "7")]
    [DataRow("monitors/1/outcomes/execution/errorHandlingStatus", "3")]
    [DataRow("monitors/1/outcomes/execution/cleanupStatus", "3")]
    public void EnumFieldsRequireCanonicalNamedStringsEvenWhenNumericTextDenotesTheirCurrentValue(string path, string numeric)
    {
        JsonObject envelope = Details();
        Assert.IsFalse(Parse(envelope).IsError);
        Set(envelope, path, numeric);
        Reject(envelope, path);
        Set(envelope, path, "Unknown");
        Reject(envelope, path);
        Set(envelope, path, "");
        Reject(envelope, path);
    }

    [TestMethod]
    [DataRow("-9223372036854775808")]
    [DataRow("-1")]
    [DataRow("0")]
    [DataRow("9223372036854775807")]
    public void SignedIntegerMeasurementsAcceptTheirEntireInt64Domain(string value)
    {
        JsonObject envelope = Details();
        Set(envelope, "monitors/1/measurements/0/value", value);
        Assert.IsFalse(Parse(envelope).IsError);
    }

    public static IEnumerable<object[]> CounterFields() => new[] { "successful", "failed", "skipped", "expectedCanceled", "consecutiveFailures" }.Select(field => new object[] { field });

    [TestMethod]
    [DynamicData(nameof(CounterFields))]
    public void EveryLifetimeCounterRequiresAnUnsignedCanonicalInt64String(string field)
    {
        JsonObject envelope = Details();
        foreach (string invalid in new[] { "-1", "+1", "01", "-0", "9223372036854775808", "1.0", "" })
        { Set(envelope, "monitors/1/outcomes/" + field, invalid); Reject(envelope, field); }
    }

    public static IEnumerable<object[]> SecondaryContradictions()
    {
        foreach (string status in new[] { "NotStarted", "Running", "Succeeded", "NotApplicable" })
        {
            yield return ["errorHandlingStatus", status, "errorHandlingError"];
            yield return ["cleanupStatus", status, "cleanupError"];
        }
        foreach (string field in new[] { "errorHandlingStartedAtUtc", "errorHandlingCompletedAtUtc", "errorHandlingDurationMs" })
        { yield return ["errorHandlingStatus", "NotStarted", field]; }
        yield return ["errorHandlingStatus", "Running", "errorHandlingCompletedAtUtc"];
        foreach (string status in new[] { "NotApplicable", "NotInvoked" })
        {
            yield return ["errorHandlingStatus", status, "errorHandlingStartedAtUtc"];
            yield return ["errorHandlingStatus", status, "errorHandlingDurationMs"];
            yield return ["cleanupStatus", status, "cleanupDurationMs"];
        }
        yield return ["cleanupStatus", "NotStarted", "cleanupCompletedAtUtc"];
        yield return ["cleanupStatus", "Running", "cleanupCompletedAtUtc"];
    }

    [TestMethod]
    [DynamicData(nameof(SecondaryContradictions))]
    public void SecondaryStatusContradictionsAreRejectedIndependently(string statusField, string status, string forbiddenField)
    {
        JsonObject envelope = PartialExecution();
        Set(envelope, Execution + statusField, status);
        Assert.IsFalse(Parse(envelope).IsError, "valid control for " + status);
        JsonNode value = forbiddenField.EndsWith("Error", StringComparison.Ordinal) ? Diagnostic()
            : forbiddenField.EndsWith("Ms", StringComparison.Ordinal) ? JsonValue.Create(1d)! : JsonValue.Create(Time)!;
        Set(envelope, Execution + forbiddenField, value);
        Reject(envelope, status + " with " + forbiddenField);
    }

    [TestMethod]
    [DataRow("errorHandlingStatus", "errorHandlingStartedAtUtc")]
    [DataRow("cleanupStatus", "workCompletedAtUtc")]
    public void AnUnsettledRunningSecondaryStepIsAValidObservation(string statusField, string observedTime)
    {
        JsonObject envelope = PartialExecution();
        Set(envelope, Execution + statusField, "Running");
        Set(envelope, Execution + observedTime, Time);
        Assert.IsFalse(Parse(envelope).IsError);
    }

    [TestMethod]
    [DataRow("Settled")]
    [DataRow("Retired")]
    public void CompleteSettledObservationsPermitOnlyTerminalSecondaryStatuses(string phase)
    {
        foreach (string status in new[] { "Succeeded", "Failed", "ExpectedCancellation", "NotApplicable", "NotInvoked" })
        {
            JsonObject envelope = PartialExecution();
            Set(envelope, Execution + "phase", phase);
            Set(envelope, Execution + "settledAtUtc", Time);
            Set(envelope, Execution + "totalDurationMs", 0d);
            Set(envelope, Execution + "errorHandlingStatus", status);
            Set(envelope, Execution + "cleanupStatus", status);
            Assert.IsFalse(Parse(envelope).IsError, phase + " " + status);
        }
    }

    [TestMethod]
    public void ARealCriticalFailureIsCountedAndReportedAsUnhealthyInDetails()
    {
        JsonObject envelope = Details();
        JsonObject snapshot = envelope["snapshot"]!.AsObject();
        snapshot["monitors"]![0]!["state"] = "Unhealthy";
        snapshot["monitorCounts"]!["failing"] = 2;
        snapshot["monitorCounts"]!["criticalFailing"] = 1;
        snapshot["healthStatus"] = "Unhealthy";
        snapshot["ready"] = false;
        Assert.IsFalse(Parse(envelope).SelectedHealthy);
    }

    [TestMethod]
    public void DiagnosticStringMeasurementsPreserveEscapedControlsAndUnknownFieldsRemainStrict()
    {
        JsonObject envelope = Details();
        Set(envelope, "monitors/0/measurements/0/value", "line\n\t\u0001end");
        Assert.IsFalse(Parse(envelope).IsError);
        string valid = envelope.ToJsonString();
        foreach (string additive in new[] { ",\"unknown\":\"\\ud800\"", ",\"unknown\":{\"\\ud800\":1}", ",\"unknown\":[\"\\udfff\"]" })
        {
            string invalid = valid[..^1] + additive + "}";
            Assert.ThrowsExactly<FormatException>(() => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(invalid), HealthReportView.Details, 4_194_304));
        }
    }

    [TestMethod]
    public void StableErrorCodesAreAcceptedOnlyByTheirActualTransport()
    {
        string[] common = ["ReportTooLarge", "SerializationFailed", "ReportingUnavailable"];
        string[] pipeOnly = ["DetailsDisabled", "InvalidView", "UnsupportedVersion", "InvalidRequest", "InvalidFrame"];
        string[] httpOnly = ["Forbidden", "NotFound", "MethodNotAllowed"];
        foreach (string code in common.Concat(pipeOnly).Concat(httpOnly).Append("Unknown"))
        {
            foreach (bool pipe in new[] { false, true })
            {
                bool allowed = common.Contains(code, StringComparer.Ordinal) || (pipe ? pipeOnly : httpOnly).Contains(code, StringComparer.Ordinal);
                Assert.AreEqual(allowed, HealthReportProtocol.IsErrorCode(code, pipe), code);
                if (!allowed) { Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthReportFormatter.FormatError(code, pipe)); continue; }
                HealthReportBuffer error = HealthReportFormatter.FormatError(code, pipe, HealthReportView.Health);
                using JsonDocument document = JsonDocument.Parse(error.Utf8Bytes);
                Assert.AreEqual(code, document.RootElement.GetProperty("error").GetProperty("code").GetString());
                Assert.IsTrue(error.IsError);
                Assert.IsFalse(error.SelectedHealthy);
                Assert.AreEqual(pipe, document.RootElement.TryGetProperty("protocolVersion", out _));
                if (pipe) { Assert.AreEqual(code, HealthReportProtocol.ParseResponse(error.Utf8Bytes, HealthReportView.Health, 1024).ErrorCode); }
            }
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(16_777_216)]
    public async Task FramingAcceptsBothInclusiveMaximumConfigurationEndpoints(int maximum)
    {
        byte[] encoded = HealthReportProtocol.EncodeFrame([42], maximum);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 42 }, encoded);
        using var input = new MemoryStream(encoded);
        CollectionAssert.AreEqual(new byte[] { 42 }, await HealthReportProtocol.ReadFrameAsync(input, maximum, CancellationToken.None));
        using var output = new MemoryStream();
        await HealthReportProtocol.WriteFrameAsync(output, new byte[] { 42 }, maximum, CancellationToken.None);
        CollectionAssert.AreEqual(encoded, output.ToArray());
    }

    [TestMethod]
    public async Task FramingAcceptsAnExactlyFullMultibyteLengthAndPreservesAllPayloadBytes()
    {
        byte[] payload = Enumerable.Range(0, 257).Select(index => (byte)index).ToArray();
        byte[] encoded = HealthReportProtocol.EncodeFrame(payload, payload.Length);
        Assert.AreEqual(257u, BinaryPrimitives.ReadUInt32LittleEndian(encoded));
        using var input = new MemoryStream(encoded);
        CollectionAssert.AreEqual(payload, await HealthReportProtocol.ReadFrameAsync(input, payload.Length, CancellationToken.None));
        using var output = new MemoryStream();
        await HealthReportProtocol.WriteFrameAsync(output, payload, payload.Length, CancellationToken.None);
        CollectionAssert.AreEqual(encoded, output.ToArray());
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(16_777_217)]
    public async Task InvalidFrameMaximumIsRejectedBeforeReadingOrWriting(int maximum)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthReportProtocol.EncodeFrame([42], maximum));
        using var input = new MemoryStream(new byte[] { 1, 0, 0, 0, 42 });
        using var output = new MemoryStream();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => HealthReportProtocol.ReadFrameAsync(input, maximum, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => HealthReportProtocol.WriteFrameAsync(output, new byte[] { 42 }, maximum, CancellationToken.None));
        Assert.AreEqual(0L, input.Position);
        Assert.AreEqual(0L, output.Length);
    }

    private static JsonObject Details()
    {
        var envelope = new JsonObject
        {
            ["protocolVersion"] = 1,
            ["schemaVersion"] = 1,
            ["view"] = "details",
            ["kind"] = "snapshot",
            ["snapshot"] = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"))
        };
        envelope["snapshot"]!["monitors"]![0]!["measurements"]!.AsArray().Add(new JsonObject
        {
            ["key"] = "text",
            ["valueKind"] = "String",
            ["value"] = "value",
            ["unit"] = "items",
            ["observedAtUtc"] = Time,
            ["isDiagnostic"] = true
        });
        Set(envelope, Execution + "errorHandlingStatus", "Failed");
        Set(envelope, Execution + "cleanupStatus", "Failed");
        foreach (string path in Diagnostics) { Set(envelope, path, Diagnostic()); }
        return envelope;
    }

    private static JsonObject PartialExecution()
    {
        JsonObject envelope = Details();
        JsonObject execution = Node(envelope, "monitors/1/outcomes/execution").AsObject();
        foreach (string key in execution.Select(pair => pair.Key).Where(key => key.EndsWith("AtUtc", StringComparison.Ordinal) || key.EndsWith("Ms", StringComparison.Ordinal)
            || key is "errorHandlingError" or "cleanupError").ToArray()) { execution.Remove(key); }
        execution["phase"] = "Callback";
        execution["errorHandlingStatus"] = "NotStarted";
        execution["cleanupStatus"] = "NotStarted";
        return envelope;
    }

    private static JsonObject Diagnostic() => new() { ["code"] = "Observed", ["message"] = "Detached reason", ["isTruncated"] = false };
    private static HealthParsedResponse Parse(JsonObject envelope) => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(envelope.ToJsonString()), HealthReportView.Details, 4_194_304);
    private static void Reject(JsonObject envelope, string reason) => Assert.ThrowsExactly<FormatException>(() => Parse(envelope), reason);
    private static JsonNode Node(JsonObject envelope, string path)
    {
        JsonNode current = envelope["snapshot"]!;
        foreach (string part in path.Split('/')) { current = current is JsonArray array ? array[int.Parse(part, CultureInfo.InvariantCulture)]! : current[part]!; }
        return current;
    }
    private static void Set(JsonObject envelope, string path, JsonNode? value)
    {
        int separator = path.LastIndexOf('/');
        JsonObject parent = separator < 0 ? envelope["snapshot"]!.AsObject() : Node(envelope, path[..separator]).AsObject();
        parent[separator < 0 ? path : path[(separator + 1)..]] = value?.DeepClone();
    }
}
