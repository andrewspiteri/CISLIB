using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class RuntimeModuleHealthcheckTests
{
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public async Task RealCollectorEvaluationEmitsItsOwnOutcomeWithoutChangingHealthPolicy(bool fail, bool enabled)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var service = new HealthcheckService(new());
        await using var metrics = new MetricsRuntime(meter);
        if (enabled) { ((IMetricsSource)service).AttachMetrics(metrics); }
        await using var runner = new HealthCollectorRunner(service);
        await using IConditionMonitor condition = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
        var collector = new Collector(_ => fail ? Task.FromException<HealthCollectionResult>(new InvalidOperationException("Controlled evaluation failure."))
            : Task.FromResult(HealthCollectionResult.Condition(true)));
        await using IHealthCollectionRegistration registration = runner.Register(condition, collector, new());
        await runner.StartAsync().WaitAsync(MetricsTestSupport.Deadline);
        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(MetricsTestSupport.Deadline);
        Assert.AreEqual(fail, attempt.EvaluationFailed);
        Assert.AreEqual(1, collector.Calls);
        Assert.AreEqual(1, collector.Disposals);
        Assert.AreEqual(MonitorState.Unhealthy, service.CaptureSnapshot().Monitors.Single().State);
        MetricsPoint[] points = listener.Points("cis.healthcheck.evaluation.duration");
        Assert.AreEqual(enabled ? 1 : 0, points.Length);
        if (enabled) { MetricsTestSupport.AssertTags(points.Single(), new KeyValuePair<string, object?>("outcome", fail ? "error" : "success")); }
    }

    [TestMethod]
    public async Task SnapshotCollectionReadsCurrentReadyAndAggregateStatesWithoutInvokingACollector()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var service = new HealthcheckService(new());
        await using var metrics = new MetricsRuntime(meter);
        ((IMetricsSource)service).AttachMetrics(metrics);
        await using var runner = new HealthCollectorRunner(service);
        await using IConditionMonitor condition = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = true });
        var collector = new Collector(_ => Task.FromResult(HealthCollectionResult.Condition(false)));
        await using IHealthCollectionRegistration registration = runner.Register(condition, collector, new());
        service.MarkStartupCompleted();
        listener.Collect();
        Assert.AreEqual(0, collector.Calls);
        Assert.AreEqual(0d, (double)listener.Points("cis.healthcheck.ready").Last().Value);
        MetricsPoint unhealthy = listener.Points("cis.healthcheck.monitors").Single(point => point.Tags.Any(tag => Equals(tag.Value, "unhealthy")));
        Assert.AreEqual(1d, (double)unhealthy.Value);
        condition.Clear(); listener.Collect();
        Assert.AreEqual(1d, (double)listener.Points("cis.healthcheck.ready").Last().Value);
        Assert.AreEqual(0, collector.Calls);
        Assert.AreEqual(0, listener.Points("cis.healthcheck.evaluation.duration").Length);
    }

    [TestMethod]
    public async Task LateAttachmentDoesNotReplayAHeldEvaluationButLaterEvaluationsAreObserved()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var service = new HealthcheckService(new());
        await using var metrics = new MetricsRuntime(meter);
        await using var runner = new HealthCollectorRunner(service);
        await using IConditionMonitor condition = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
        var entered = MetricsTestSupport.Signal(); var release = MetricsTestSupport.Signal();
        var collector = new Collector(async _ => { entered.TrySetResult(); await release.Task; return HealthCollectionResult.Condition(false); });
        await using IHealthCollectionRegistration registration = runner.Register(condition, collector, new());
        await runner.StartAsync();
        try
        {
            Task<HealthCollectionAttempt> first = registration.CollectAsync();
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            ((IMetricsSource)service).AttachMetrics(metrics);
            release.TrySetResult(); await first.WaitAsync(MetricsTestSupport.Deadline);
            Assert.AreEqual(0, listener.Points("cis.healthcheck.evaluation.duration").Length);
            await registration.CollectAsync().WaitAsync(MetricsTestSupport.Deadline);
            Assert.AreEqual(1, listener.Points("cis.healthcheck.evaluation.duration").Length);
            Assert.AreEqual(2, collector.Calls);
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    public async Task CollectedMonitorIsAbsentFromMetricsBeforeTheRealSnapshotRetiresItWithoutScrapeSideEffects()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var service = new HealthcheckService(new());
        await using var metrics = new MetricsRuntime(meter);
        ((IMetricsSource)service).AttachMetrics(metrics);
        service.MarkStartupCompleted();
        (WeakReference abandoned, Task retired) = AbandonCriticalMonitor(service, listener);
        Assert.AreEqual(0d, (double)listener.Points("cis.healthcheck.ready").Single().Value);
        Assert.AreEqual(1d, (double)listener.Points("cis.healthcheck.monitors").Single(point =>
            point.Tags.Any(tag => Equals(tag.Value, "unhealthy"))).Value);
        // Match the existing Healthcheck weak-ownership test pattern: the
        // NoInlining factory prevents an incidental JIT local from owning it.
        for (int attempt = 0; attempt < 10 && abandoned.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        Assert.IsFalse(abandoned.IsAlive, "Establish actual collection before checking observations.");
        Assert.IsFalse(retired.IsCompleted);
        listener.Collect();
        Assert.AreEqual(1d, (double)listener.Points("cis.healthcheck.ready").Last().Value);
        Assert.IsTrue(listener.Points("cis.healthcheck.monitors").TakeLast(3).All(point => (double)point.Value == 0));
        Assert.IsFalse(retired.IsCompleted, "A scrape must not sweep or dispatch monitor retirement.");
        HealthSnapshot snapshot = service.CaptureSnapshot();
        Assert.IsTrue(snapshot.Ready);
        Assert.AreEqual(0, snapshot.Monitors.Count);
        await retired.WaitAsync(MetricsTestSupport.Deadline);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Handle, Task Retired) AbandonCriticalMonitor(HealthcheckService service, MetricsTestListener listener)
    {
        IConditionMonitor monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = true });
        listener.Collect();
        var result = (new WeakReference(monitor), monitor.WaitForDisposalAsync());
        GC.KeepAlive(monitor);
        return result;
    }

    private sealed class Collector(Func<CancellationToken, Task<HealthCollectionResult>> evaluate) : IHealthCollector
    {
        private int calls, disposals;
        internal int Calls => Volatile.Read(ref calls);
        internal int Disposals => Volatile.Read(ref disposals);
        private Task<HealthCollectionResult> Evaluate(CancellationToken token)
        { Interlocked.Increment(ref calls); return evaluate(token); }
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(this); return true; }
        private sealed class Lease(Collector owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            => owner.Evaluate(lifetimeCancellation);
            public ValueTask DisposeAsync() { Interlocked.Increment(ref owner.disposals); return ValueTask.CompletedTask; }
        }
    }
}
