using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using CIS.Metrics;
using CIS.Tests.UnitTests.Metrics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReadinessSourceTests
{
    [TestMethod]
    public async Task ReadinessOnlyLossPreservesLivenessAndOrdinaryRequestPolicy()
    {
        var prerequisite = new ReadinessSource();
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "managed",
            InstanceId = Guid.NewGuid()
        },
            TimeProvider.System, [prerequisite]);
        using var meter = new Meter("Managed.Readiness");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        ((IMetricsSource)health).AttachMetrics(metrics);
        Assert.IsFalse(health.CaptureSnapshot().Ready);
        Assert.IsFalse(health.CaptureSnapshot().RequestsAllowed);
        health.MarkStartupCompleted();

        HealthSnapshot closed = health.CaptureSnapshot();
        Assert.AreEqual(HealthStatus.Healthy, closed.HealthStatus);
        Assert.IsFalse(closed.Ready);
        Assert.IsTrue(closed.RequestsAllowed);
        listener.Collect();
        Assert.AreEqual(0d, (double)listener.Points("cis.healthcheck.ready").Last().Value);

        prerequisite.Ready = true;
        Assert.IsTrue(health.CaptureSnapshot().Ready);
        listener.Collect();
        Assert.AreEqual(1d, (double)listener.Points("cis.healthcheck.ready").Last().Value);
        health.BeginStopping();
        Assert.IsFalse(health.CaptureSnapshot().Ready);
        Assert.IsFalse(health.CaptureSnapshot().RequestsAllowed);
    }

    [TestMethod]
    public async Task ReadinessSourceCannotOverrideExistingCriticalFailure()
    {
        var prerequisite = new ReadinessSource { Ready = true };
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "managed",
            InstanceId = Guid.NewGuid()
        },
            TimeProvider.System, [prerequisite]);
        await using IConditionMonitor monitor = health.CreateCondition(new()
        {
            IsCritical = true,
            InitiallyFlagged = true
        });
        health.MarkStartupCompleted();

        HealthSnapshot snapshot = health.CaptureSnapshot();
        Assert.AreEqual(HealthStatus.Unhealthy, snapshot.HealthStatus);
        Assert.IsFalse(snapshot.Ready);
        Assert.IsFalse(snapshot.RequestsAllowed);
    }

    [TestMethod]
    [DataRow(HealthReportView.Health, true)]
    [DataRow(HealthReportView.Readiness, false)]
    public async Task ActualReadinessOnlySnapshotRoundtripsThroughTheMatchingPipeReaderWithoutChangingLiveness(
        HealthReportView view, bool selectedHealthy)
    {
        var prerequisite = new ReadinessSource();
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "managed",
            InstanceId = Guid.NewGuid()
        },
            TimeProvider.System, [prerequisite]);
        health.MarkStartupCompleted();
        HealthSnapshot snapshot = health.CaptureSnapshot();
        HealthReportBuffer formatted = HealthReportFormatter.FormatPipeResponse(snapshot, view, new());
        Assert.IsFalse(formatted.IsError);

        HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(formatted.Utf8Bytes, view, 4_194_304);

        Assert.IsFalse(parsed.IsError);
        Assert.AreEqual(selectedHealthy, parsed.SelectedHealthy);
        using JsonDocument json = JsonDocument.Parse(parsed.SnapshotUtf8);
        Assert.AreEqual("Healthy", json.RootElement.GetProperty("healthStatus").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("ready").GetBoolean());
        Assert.IsTrue(json.RootElement.GetProperty("application").GetProperty("startupCompleted").GetBoolean());
        Assert.IsFalse(json.RootElement.TryGetProperty("requestsAllowed", out _), "The facade's base request policy remains internal.");
    }

    [TestMethod]
    public async Task ChangedObservedReadinessAdvancesPublicationRevisionWhileRepeatedReadsKeepTheirRevision()
    {
        var prerequisite = new ReadinessSource { Ready = true };
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "managed",
            InstanceId = Guid.NewGuid()
        },
            TimeProvider.System, [prerequisite]);
        health.MarkStartupCompleted();
        HealthSnapshot original = health.CaptureSnapshot();
        prerequisite.Ready = false;

        HealthSnapshot closed = health.CaptureSnapshot();
        HealthSnapshot repeated = health.CaptureSnapshot();

        Assert.IsTrue(closed.Revision > original.Revision);
        Assert.AreEqual(closed.Revision, repeated.Revision);
        Assert.IsFalse(closed.Ready);
        Assert.AreEqual(HealthStatus.Healthy, closed.HealthStatus);
        Assert.IsTrue(closed.RequestsAllowed);
        prerequisite.Ready = true;
        HealthSnapshot reopened = health.CaptureSnapshot();
        Assert.IsTrue(reopened.Revision > closed.Revision);
        Assert.IsTrue(reopened.Ready);
        Assert.IsTrue(original.Ready, "Earlier detached observations remain immutable.");
        Assert.IsFalse(closed.Ready);
    }

    [TestMethod]
    public void CapturingSourcesHasFiniteCardinalityAndNeverRunsTheirProperties()
    {
        int reads = 0;
        IEnumerable<IHealthReadinessSource> Infinite()
        {
            while (true)
            {
                reads++;
                yield return new ReadinessSource();
            }
        }
        Assert.ThrowsExactly<ArgumentException>(() => new HealthcheckService(new(), null, null, Infinite()));
        Assert.AreEqual(33, reads);
    }

    private sealed class ReadinessSource : IHealthReadinessSource
    {
        internal bool Ready;
        public bool IsReady => Ready;
    }
}
