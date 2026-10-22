using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportProtocolEmptyTimestampQualificationTests
{
    [TestMethod]
    [DataRow("capturedAtUtc")]
    [DataRow("monitors/0/lastObservationAtUtc")]
    [DataRow("monitors/1/outcomes/execution/settledAtUtc")]
    public void EmptyTimestampAtEachReportLevelIsRejectedAsAProtocolFormatError(string path)
    {
        JsonObject envelope = DetailsEnvelope();
        Assert.IsFalse(Parse(envelope).IsError);

        // The field remains present: an empty timestamp is malformed, rather
        // than an omitted optional observation or a different JSON scalar type.
        string[] parts = path.Split('/');
        JsonNode parent = envelope["snapshot"]!;
        foreach (string part in parts[..^1])
        {
            parent = parent is JsonArray array
                ? array[int.Parse(part, CultureInfo.InvariantCulture)]!
                : parent[part]!;
        }
        parent[parts[^1]] = string.Empty;

        Assert.ThrowsExactly<FormatException>(() => Parse(envelope));
    }

    private static JsonObject DetailsEnvelope()
    {
        JsonNode snapshot = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "healthy-after-startup-details.json"))!;
        return new JsonObject
        {
            ["protocolVersion"] = 1,
            ["schemaVersion"] = 1,
            ["view"] = "details",
            ["kind"] = "snapshot",
            ["snapshot"] = snapshot
        };
    }

    private static HealthParsedResponse Parse(JsonObject envelope) => HealthReportProtocol.ParseResponse(
        Encoding.UTF8.GetBytes(envelope.ToJsonString()), HealthReportView.Details, 4_194_304);
}
