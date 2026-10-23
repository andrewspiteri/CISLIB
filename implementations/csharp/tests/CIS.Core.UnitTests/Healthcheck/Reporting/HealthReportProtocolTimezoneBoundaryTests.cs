using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
[DoNotParallelize]
public sealed class HealthReportProtocolTimezoneBoundaryTests
{
    [TestMethod]
    [DataRow("Etc/GMT-14", 14, "0001-01-01T00:00:00Z")]
    [DataRow("Etc/GMT+12", -12, "9999-12-31T23:59:59.9999999Z")]
    public void ValidUtcCalendarEndpointsDoNotDependOnTheReceivingProcessLocalOffset(string timeZone, int offsetHours, string timestamp)
    {
        bool processTimeZoneSupported = OperatingSystem.IsLinux();
        string? originalTimeZone = Environment.GetEnvironmentVariable("TZ");
        try
        {
            if (processTimeZoneSupported)
            {
                Environment.SetEnvironmentVariable("TZ", timeZone);
                TimeZoneInfo.ClearCachedData();
                DateTime calendarDate = DateTime.ParseExact(timestamp,
                    ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None);
                Assert.AreEqual(TimeSpan.FromHours(offsetHours), TimeZoneInfo.Local.GetUtcOffset(calendarDate),
                    "The Linux process must observe the requested local offset for this receiving boundary.");
            }

            JsonNode snapshot = JsonNode.Parse(HealthReportFormatterTests.ReadFixture("valid", "pending-readiness.json"))!;
            snapshot["capturedAtUtc"] = timestamp;
            var envelope = new JsonObject
            {
                ["protocolVersion"] = 1,
                ["schemaVersion"] = 1,
                ["view"] = "health",
                ["kind"] = "snapshot",
                ["snapshot"] = snapshot
            };
            byte[] payload = Encoding.UTF8.GetBytes(envelope.ToJsonString());
            FormatException? rejection = null;
            HealthParsedResponse? response = null;
            try { response = HealthReportProtocol.ParseResponse(payload, HealthReportView.Health, 4_194_304); }
            catch (FormatException error) { rejection = error; }

            Assert.IsNull(rejection,
                "A valid UTC calendar endpoint must be accepted independently of the receiving process local offset. " + rejection);
            Assert.IsNotNull(response);
            Assert.IsFalse(response.IsError);
            Assert.IsTrue(response.SelectedHealthy);
            using JsonDocument detached = JsonDocument.Parse(response.SnapshotUtf8);
            Assert.AreEqual(timestamp, detached.RootElement.GetProperty("capturedAtUtc").GetString());
        }
        finally
        {
            if (processTimeZoneSupported)
            {
                Environment.SetEnvironmentVariable("TZ", originalTimeZone);
                TimeZoneInfo.ClearCachedData();
            }
        }
    }
}
