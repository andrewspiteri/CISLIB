using System.Numerics;
using System.Text.Json;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportMalformedRevisionBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NegativeDetachedRevisionAtTheConservativePreflightBoundaryRetainsSerializationFailure(bool pipe)
    {
        var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "malformed revision boundary", InstanceId = Guid.NewGuid() });
        try
        {
            HealthSnapshot captured = service.CaptureSnapshot();
            const int maximumBytes = 1024;
            // Snapshot-data validation is an already admitted formatter receiving
            // boundary. This is not a possible revision issued by the registry.
            // A non-power-of-two negative value has exactly 4,096 significant
            // bits here; the current conservative size check admits equality.
            BigInteger malformedRevision = -((BigInteger.One << (maximumBytes * 4)) - BigInteger.One);
            Assert.AreEqual(4096L, malformedRevision.GetBitLength());
            var detached = new HealthSnapshot(new HealthSnapshotData
            {
                Revision = malformedRevision,
                CapturedAtUtc = captured.CapturedAtUtc,
                Application = captured.Application,
                HealthStatus = captured.HealthStatus,
                Ready = captured.Ready,
                MonitorCounts = captured.MonitorCounts,
                Monitors = captured.Monitors
            });
            var options = new HealthcheckOptions { MaxReportBytes = maximumBytes };

            HealthReportBuffer report = pipe
                ? HealthReportFormatter.FormatPipeResponse(detached, HealthReportView.Health, options)
                : HealthReportFormatter.FormatSnapshot(detached, HealthReportView.Health, options);

            Assert.IsTrue(report.IsError);
            Assert.AreEqual("SerializationFailed", report.ErrorCode);
            Assert.IsFalse(report.SelectedHealthy);
            Assert.IsTrue(report.Utf8Bytes.Length is > 0 and <= maximumBytes);
            using (JsonDocument document = JsonDocument.Parse(report.Utf8Bytes))
            {
                JsonElement root = document.RootElement;
                Assert.AreEqual("error", root.GetProperty("kind").GetString());
                Assert.AreEqual("SerializationFailed", root.GetProperty("error").GetProperty("code").GetString());
                if (pipe)
                {
                    Assert.AreEqual(1, root.GetProperty("protocolVersion").GetInt32());
                    Assert.AreEqual("health", root.GetProperty("view").GetString());
                }
            }

            // The same real captured data and budget still produce a successful
            // report. Formatting a detached invalid value cannot alter the source.
            HealthReportBuffer valid = pipe
                ? HealthReportFormatter.FormatPipeResponse(captured, HealthReportView.Health, options)
                : HealthReportFormatter.FormatSnapshot(captured, HealthReportView.Health, options);
            Assert.IsFalse(valid.IsError);
            Assert.IsNull(valid.ErrorCode);
            Assert.AreEqual(captured.Revision, service.CaptureSnapshot().Revision);
        }
        finally
        {
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
