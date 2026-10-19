using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class InterpolationMeasurementsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void RecordOwnedCaptureAndConsumerFormattingCostsSeparately()
    {
        var metadata = new EventMetadata(); var scope = new OperationScopeStore().Capture();
        CapturedEventFacts facts = EventProjection.Capture(metadata, scope, "Measurements", CoreLogLevel.Information,
            new LogEventData { EventName = "measurement" })!;
        var rows = new List<object>();
        foreach (string mode in new[] { "ordinary", "wide", "excessive-precision" })
        {
            for (int index = 0; index < 32; index++) { _ = Capture(mode).Render(facts); }
            const int count = 512;
            var payloads = new CapturedInterpolation[count];
            long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            for (int index = 0; index < count; index++) { payloads[index] = Capture(mode); }
            long captureTicks = Stopwatch.GetTimestamp() - started;
            long captureBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            allocated = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
            int scalars = 0, truncated = 0;
            foreach (var payload in payloads)
            {
                var input = new CapturedEventFacts(facts.TimestampUtc, null, null, facts.Status, facts.EventName, string.Empty,
                    payload.Template, null, facts.Service, facts.Environment, facts.Version, facts.Component, facts.Resource,
                    facts.Attributes, null, payload.IsTruncated);
                CapturedEventFacts rendered = payload.Render(input);
                Assert.IsTrue(rendered.Message.Length <= 16384);
                scalars += rendered.Message.Length; truncated += rendered.IsTruncated ? 1 : 0;
            }
            long renderTicks = Stopwatch.GetTimestamp() - started;
            long renderBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.AreEqual(mode == "ordinary" ? 0 : count, truncated);
            rows.Add(new { mode, count, captureTicks, captureBytes, renderTicks, renderBytes, retainedUtf16Characters = scalars, truncated });
        }
        string path = Path.Combine(TestContext.TestResultsDirectory!, "interpolation-costs.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema = "cis.logging-interpolation-measurements/v1",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            stopwatchFrequency = Stopwatch.Frequency,
            rows,
            scope = "Actual owned capture and rendering helpers, warmed synchronous single-thread observations. No queue, I/O or application callback time. Render rows include assertion and immutable-facts construction cost. Allocations are current-thread observations, not retained memory or an allocation SLA. User-defined formatter cost is outside these built-in-scalar rows."
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.AddResultFile(path);
    }

    private static CapturedInterpolation Capture(string mode)
    {
        var handler = new LogInterpolatedStringHandler(0, 0);
        handler.AppendLiteral("Order ");
        handler.AppendFormatted(42, mode == "wide" ? int.MaxValue : 0, mode == "excessive-precision" ? "F999999999" : "D4");
        handler.AppendLiteral(" accepted");
        return handler.Freeze();
    }
}
