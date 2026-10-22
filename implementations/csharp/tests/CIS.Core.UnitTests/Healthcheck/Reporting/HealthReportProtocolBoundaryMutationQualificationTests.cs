using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportProtocolBoundaryMutationQualificationTests
{
    [TestMethod]
    [DataRow(0, 0, 0, 0, 0)]
    [DataRow(1, 0, 0, 0, 0)]
    [DataRow(1, 1, 0, 0, 0)]
    [DataRow(1, 0, 1, 0, 0)]
    [DataRow(1, 1, 1, 1, 0)]
    [DataRow(1, 0, 0, 0, 1)]
    [DataRow(1, 1, 0, 0, 1)]
    [DataRow(5, 3, 2, 0, 1)]
    [DataRow(5, 3, 4, 2, 1)]
    [DataRow(5, 3, 3, 1, 2)]
    [DataRow(65536, 65536, 65536, 65536, 0)]
    [DataRow(65536, 65536, 0, 0, 65536)]
    [DataRow(65536, 0, 65536, 0, 0)]
    [DataRow(65536, 0, 0, 0, 65536)]
    public void CompactCountsAcceptInclusiveCoherentBoundariesAndSelectTheRequestedHealthMeaning(int active, int critical, int failing, int criticalFailing, int pending)
    {
        foreach (bool startup in new[] { false, true })
        {
            foreach (bool stopping in new[] { false, true })
            {
                foreach (HealthReportView view in new[] { HealthReportView.Health, HealthReportView.Readiness })
                {
                    JsonObject envelope = Compact(view);
                    JsonObject snapshot = envelope["snapshot"]!.AsObject();
                    snapshot["monitorCounts"] = new JsonObject
                    { ["active"] = active, ["critical"] = critical, ["failing"] = failing, ["criticalFailing"] = criticalFailing, ["pending"] = pending };
                    bool healthy = criticalFailing == 0;
                    bool ready = startup && !stopping && healthy;
                    snapshot["healthStatus"] = healthy ? "Healthy" : "Unhealthy";
                    snapshot["ready"] = ready;
                    snapshot["application"]!["startupCompleted"] = startup;
                    snapshot["application"]!["isStopping"] = stopping;

                    HealthParsedResponse response = Parse(envelope, view);

                    Assert.IsFalse(response.IsError);
                    Assert.AreEqual(view == HealthReportView.Health ? healthy : ready, response.SelectedHealthy);
                    snapshot["ready"] = !ready;
                    Assert.ThrowsExactly<FormatException>(() => Parse(envelope, view));
                }
            }
        }
    }

    [TestMethod]
    [DataRow("ReportTooLarge", false)]
    [DataRow("SerializationFailed", false)]
    [DataRow("ReportingUnavailable", false)]
    [DataRow("DetailsDisabled", false)]
    [DataRow("InvalidView", true)]
    [DataRow("UnsupportedVersion", true)]
    [DataRow("InvalidRequest", true)]
    [DataRow("InvalidFrame", true)]
    public void ErrorViewsAndOptionalMessageTypesAreValidatedForEveryStablePipeCode(string code, bool allowsNullView)
    {
        foreach (HealthReportView view in Enum.GetValues<HealthReportView>())
        {
            var envelope = new JsonObject
            {
                ["protocolVersion"] = 1,
                ["schemaVersion"] = 1,
                ["kind"] = "error",
                ["view"] = ViewName(view),
                ["error"] = new JsonObject { ["code"] = code }
            };
            JsonObject error = envelope["error"]!.AsObject();
            foreach (string message in new[] { "", new string('m', 128), string.Concat(Enumerable.Repeat("\U0001F680", 128)) })
            {
                error["message"] = message;
                HealthParsedResponse response = Parse(envelope, view);
                Assert.IsTrue(response.IsError);
                Assert.AreEqual(code, response.ErrorCode);
                Assert.IsFalse(response.SelectedHealthy);
                Assert.IsTrue(response.SnapshotUtf8.IsEmpty);
            }
            foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create(true), new JsonObject(), JsonValue.Create(new string('m', 129)) })
            {
                error["message"] = invalid?.DeepClone();
                Assert.ThrowsExactly<FormatException>(() => Parse(envelope, view));
            }
            error.Remove("message");
            envelope["view"] = view == HealthReportView.Health ? "readiness" : "health";
            Assert.ThrowsExactly<FormatException>(() => Parse(envelope, view));
            envelope["view"] = null;
            if (allowsNullView)
            {
                HealthParsedResponse response = Parse(envelope, view);
                Assert.IsTrue(response.IsError);
                Assert.AreEqual(code, response.ErrorCode);
                Assert.IsFalse(response.SelectedHealthy);
            }
            else { Assert.ThrowsExactly<FormatException>(() => Parse(envelope, view)); }
        }
    }

    [TestMethod]
    [DataRow("application/name")]
    [DataRow("monitors/2/numeric/unit")]
    [DataRow("monitors/0/measurements/0/key")]
    [DataRow("monitors/0/measurements/0/unit")]
    public void RequiredNonblankTextRejectsWhitespaceIndependentlyOfItsLengthLimit(string path)
    {
        foreach (string blank in new[] { "", " ", "\t\r\n", "\u00a0\u2003" })
        {
            JsonObject envelope = Details();
            Set(envelope["snapshot"]!, path, blank);
            Assert.ThrowsExactly<FormatException>(() => Parse(envelope, HealthReportView.Details));
        }
    }

    private static JsonObject Details()
    {
        JsonObject snapshot = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"))!.AsObject();
        snapshot["monitors"]![0]!["measurements"] = new JsonArray(new JsonObject
        { ["key"] = "metric", ["valueKind"] = "String", ["value"] = "actual value", ["unit"] = "text", ["observedAtUtc"] = "2026-10-20T08:03:00Z", ["isDiagnostic"] = true });
        return Envelope(snapshot, HealthReportView.Details);
    }

    private static JsonObject Compact(HealthReportView view) => Envelope(JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "pending-readiness.json"))!.AsObject(), view);
    private static JsonObject Envelope(JsonObject snapshot, HealthReportView view) => new()
    { ["protocolVersion"] = 1, ["schemaVersion"] = 1, ["view"] = ViewName(view), ["kind"] = "snapshot", ["snapshot"] = snapshot };
    private static string ViewName(HealthReportView view) => view switch
    { HealthReportView.Health => "health", HealthReportView.Readiness => "readiness", HealthReportView.Details => "details", _ => throw new ArgumentOutOfRangeException(nameof(view)) };
    private static HealthParsedResponse Parse(JsonObject envelope, HealthReportView view) => HealthReportProtocol.ParseResponse(Encoding.UTF8.GetBytes(envelope.ToJsonString()), view, 4_194_304);
    private static void Set(JsonNode value, string path, string text)
    {
        string[] parts = path.Split('/');
        foreach (string part in parts[..^1]) { value = value is JsonArray array ? array[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]! : value[part]!; }
        value[parts[^1]] = text;
    }
}
