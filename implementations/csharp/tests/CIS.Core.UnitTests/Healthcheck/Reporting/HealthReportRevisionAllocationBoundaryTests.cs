using System.Numerics;
using System.Text.Json;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportRevisionAllocationBoundaryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OversizedPositiveRevisionIsRejectedBeforeAllocatingItsDecimalRepresentation(bool pipe)
    {
        const int reportBudget = 1024;
        const long allocationBudget = 64L * reportBudget;
        await using var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "revision allocation boundary", InstanceId = Guid.NewGuid() });
        HealthSnapshot captured = service.CaptureSnapshot();
        // The already admitted detached-snapshot receiving boundary permits an
        // arbitrary-precision revision. Construct its storage before measuring:
        // its decimal text alone would exceed 76 KiB for a 1 KiB report budget.
        var oversized = new HealthSnapshot(new HealthSnapshotData
        {
            Revision = (BigInteger.One << 131_072) + BigInteger.One,
            CapturedAtUtc = captured.CapturedAtUtc,
            Application = captured.Application,
            HealthStatus = captured.HealthStatus,
            Ready = captured.Ready,
            MonitorCounts = captured.MonitorCounts,
            Monitors = captured.Monitors
        });
        var options = new HealthcheckOptions { MaxReportBytes = reportBudget };
        long[] validWarmupBytes = new long[4], oversizedWarmupBytes = new long[4], measuredBytes = new long[3];
        var reports = new HealthReportBuffer[measuredBytes.Length];

        // Warm both complete public formatting paths outside the samples. The
        // synchronous interval counts only allocations by this calling thread;
        // other tests, elapsed time and collection survival are not witnesses.
        for (int index = 0; index < validWarmupBytes.Length; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            _ = Format(captured, pipe, options);
            validWarmupBytes[index] = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            _ = Format(oversized, pipe, options);
            oversizedWarmupBytes[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        for (int index = 0; index < measuredBytes.Length; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            reports[index] = Format(oversized, pipe, options);
            measuredBytes[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        TestContext.WriteLine("revision-allocation pipe={0} reportBudget={1} allocationBudget={2} validWarmup=[{3}] oversizedWarmup=[{4}] measured=[{5}]",
            pipe, reportBudget, allocationBudget, string.Join(',', validWarmupBytes), string.Join(',', oversizedWarmupBytes), string.Join(',', measuredBytes));
        foreach (HealthReportBuffer report in reports)
        {
            Assert.IsTrue(report.IsError);
            Assert.AreEqual("ReportTooLarge", report.ErrorCode);
            Assert.IsFalse(report.SelectedHealthy);
            Assert.IsTrue(report.Utf8Bytes.Length is > 0 and <= reportBudget);
            using JsonDocument document = JsonDocument.Parse(report.Utf8Bytes);
            Assert.AreEqual("error", document.RootElement.GetProperty("kind").GetString());
            Assert.AreEqual("ReportTooLarge", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        foreach (long allocatedBytes in measuredBytes)
        {
            // 64 times the complete report budget leaves broad runtime/error-
            // buffer headroom while excluding the oversized decimal string.
            Assert.IsTrue(allocatedBytes <= allocationBudget,
                $"An oversized revision must be rejected before allocating its decimal text: {allocatedBytes} bytes allocated for a {reportBudget}-byte report budget.");
        }

        HealthReportBuffer valid = Format(captured, pipe, options);
        Assert.IsFalse(valid.IsError);
        Assert.IsNull(valid.ErrorCode);
        Assert.IsTrue(valid.Utf8Bytes.Length is > 0 and <= reportBudget);
        Assert.AreEqual(captured.Revision, service.CaptureSnapshot().Revision);
    }

    private static HealthReportBuffer Format(HealthSnapshot snapshot, bool pipe, HealthcheckOptions options) => pipe
        ? HealthReportFormatter.FormatPipeResponse(snapshot, HealthReportView.Health, options)
        : HealthReportFormatter.FormatSnapshot(snapshot, HealthReportView.Health, options);
}
