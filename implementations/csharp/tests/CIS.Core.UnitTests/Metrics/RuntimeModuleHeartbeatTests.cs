using System.Diagnostics.Metrics;
using CIS.Heartbeat;
using CIS.Metrics;
using CIS.Tests.UnitTests.Heartbeat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class RuntimeModuleHeartbeatTests
{
    [TestMethod]
    [DataRow("success", true)]
    [DataRow("error", true)]
    [DataRow("false", true)]
    [DataRow("cancelled", true)]
    [DataRow("skipped", true)]
    [DataRow("success", false)]
    public async Task RealHeartbeatOutcomesEmitOneDurationAndBalancedActiveWorkWithoutChangingCallbacks(string outcome, bool enabled)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var scope = new HeartbeatTestScope();
        if (enabled) { ((IMetricsSource)scope.Runtime).AttachMetrics(metrics); }
        using var cancellation = new CancellationTokenSource();
        var original = new InvalidOperationException("Controlled subscriber failure.");
        var probe = new HeartbeatProbe
        {
            Callback = _ => outcome switch
            {
                "error" => Task.FromException<bool>(original), "false" => Task.FromResult(false),
                "cancelled" => Task.FromCanceled<bool>(cancellation.Token), _ => Task.FromResult(true)
            }
        };
        if (outcome == "cancelled") { cancellation.Cancel(); }
        HeartbeatRecordingObserver observer = scope.Register(probe,
            condition: outcome == "skipped" ? _ => Task.FromResult(false) : null,
            executionCancellation: cancellation.Token);
        scope.Activate(); scope.Executor.RunNext(); scope.WaitSettled(observer);
        Assert.AreEqual(outcome == "skipped" ? 0 : 1, probe.Calls);
        Assert.AreEqual(outcome == "error" ? 1 : 0, probe.Errors);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
        MetricsPoint[] durations = listener.Points("cis.heartbeat.execution.duration");
        Assert.AreEqual(enabled ? 1 : 0, durations.Length);
        if (enabled)
        {
            string expected = outcome == "false" ? "error" : outcome;
            MetricsTestSupport.AssertTags(durations.Single(), new KeyValuePair<string, object?>("outcome", expected));
            CollectionAssert.AreEqual(new[] { 1L, -1L }, listener.Points("cis.heartbeat.active.executions").Select(point => (long)point.Value).ToArray());
        }
    }

    [TestMethod]
    public async Task ActiveExecutionAndDurationIncludeHeldRecoveryCleanupAndReporting()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var scope = new HeartbeatTestScope();
        ((IMetricsSource)scope.Runtime).AttachMetrics(metrics);
        var callbackRelease = scope.Hold();
        var errorEntered = MetricsTestSupport.Signal(); var errorRelease = scope.Hold();
        var cleanupEntered = MetricsTestSupport.Signal(); var cleanupRelease = scope.Hold();
        var reportEntered = MetricsTestSupport.Signal(); var reportRelease = scope.Hold();
        var probe = new HeartbeatProbe
        {
            Callback = async _ => { await callbackRelease.Task; throw new InvalidOperationException("Controlled failure."); },
            Error = async _ => { errorEntered.TrySetResult(); await errorRelease.Task; }
        };
        var observer = new HeartbeatRecordingObserver
        { Flush = async () => { reportEntered.TrySetResult(); await reportRelease.Task; } };
        scope.Register(probe, cleanup: async () => { cleanupEntered.TrySetResult(); await cleanupRelease.Task; }, observer: observer);
        scope.Activate(); scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => probe.Calls == 1, "Actual callback did not start.");
        Assert.AreEqual(1L, listener.Points("cis.heartbeat.active.executions").Sum(point => (long)point.Value));
        callbackRelease.TrySetResult();
        await errorEntered.Task.WaitAsync(MetricsTestSupport.Deadline);
        Assert.AreEqual(0, listener.Points("cis.heartbeat.execution.duration").Length);
        errorRelease.TrySetResult();
        await cleanupEntered.Task.WaitAsync(MetricsTestSupport.Deadline);
        Assert.AreEqual(1L, listener.Points("cis.heartbeat.active.executions").Sum(point => (long)point.Value));
        cleanupRelease.TrySetResult();
        await reportEntered.Task.WaitAsync(MetricsTestSupport.Deadline);
        Assert.AreEqual(0, listener.Points("cis.heartbeat.execution.duration").Length);
        reportRelease.TrySetResult(); scope.WaitSettled(observer);
        Assert.AreEqual(0L, listener.Points("cis.heartbeat.active.executions").Sum(point => (long)point.Value));
        MetricsTestSupport.AssertTags(listener.Points("cis.heartbeat.execution.duration").Single(), new KeyValuePair<string, object?>("outcome", "error"));
    }

    [TestMethod]
    public async Task AttachingWhileAnExecutionIsHeldDoesNotEmitAnUnmatchedDecrementOrReplayThatExecution()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var metrics = new MetricsRuntime(meter);
        await using var scope = new HeartbeatTestScope();
        var release = scope.Hold();
        var first = new HeartbeatProbe { Callback = async _ => { await release.Task; return true; } };
        HeartbeatRecordingObserver initial = scope.Register(first);
        scope.Activate(); scope.Executor.RunNext();
        HeartbeatTestScope.WaitUntil(() => first.Calls == 1, "First actual execution did not start.");
        ((IMetricsSource)scope.Runtime).AttachMetrics(metrics);
        release.TrySetResult(); scope.WaitSettled(initial);
        Assert.AreEqual(0, listener.Points("cis.heartbeat.active.executions").Length);
        Assert.AreEqual(0, listener.Points("cis.heartbeat.execution.duration").Length);
        var later = new HeartbeatProbe();
        HeartbeatRecordingObserver next = scope.Register(later);
        HeartbeatTestScope.WaitUntil(() => scope.Executor.PendingCount == 1, "Later execution was not posted.");
        scope.Executor.RunNext(); scope.WaitSettled(next);
        Assert.AreEqual(1, later.Calls);
        CollectionAssert.AreEqual(new[] { 1L, -1L }, listener.Points("cis.heartbeat.active.executions").Select(point => (long)point.Value).ToArray());
        Assert.AreEqual(1, listener.Points("cis.heartbeat.execution.duration").Length);
    }

    [TestMethod]
    public async Task ThrowingMetricBackendCannotBecomeAHeartbeatInfrastructureOrSubscriberFailure()
    {
        using var meter = new Meter("CIS.Metrics");
        using var faulty = new MeterListener();
        faulty.InstrumentPublished = (instrument, listener) =>
        { if (ReferenceEquals(instrument.Meter, meter)) { listener.EnableMeasurementEvents(instrument); } };
        faulty.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("Metrics backend."));
        faulty.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("Metrics backend."));
        faulty.Start();
        await using var metrics = new MetricsRuntime(meter);
        await using var scope = new HeartbeatTestScope();
        ((IMetricsSource)scope.Runtime).AttachMetrics(metrics);
        var probe = new HeartbeatProbe();
        HeartbeatRecordingObserver observer = scope.Register(probe);
        scope.Activate(); scope.Executor.RunNext(); scope.WaitSettled(observer);
        Assert.AreEqual(1, probe.Calls); Assert.AreEqual(0, probe.Errors);
        Assert.AreEqual(HeartbeatResult.Succeeded, observer.Last(HeartbeatEvent.Outcome).Notification.Outcome);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
    }
}
