using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class DisabledMetricsTests
{
    [TestMethod]
    public void CoreDisabledMetricsSupplyUsableHandlesWithoutPublishingNativeInstrumentsOrInvokingCallbacks()
    {
        using var listener = new MeterListener();
        int publications = 0, callbacks = 0;
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Name.StartsWith("app.disabled_core.", StringComparison.Ordinal)) { publications++; }
        };
        listener.Start();
        IMetrics metrics = DisabledMetrics.Instance;
        metrics.CreateCounter(new("app.disabled_core.count")).Add();
        metrics.CreateUpDownCounter(new("app.disabled_core.active")).Add(-1);
        metrics.CreateGauge(new("app.disabled_core.state")).Set(3);
        metrics.CreateHistogram(new("app.disabled_core.size", MetricUnit.Bytes, histogramBounds: [1, 8])).Record(2);
        CIS.Metrics.ITimer timer = metrics.CreateTimer(new("app.disabled_core.duration", MetricUnit.Seconds));
        timer.Record(0.5);
        using ITimingScope scope = timer.Start();
        double elapsed = scope.Stop();
        Assert.IsTrue(double.IsFinite(elapsed) && elapsed >= 0);
        Assert.AreEqual(elapsed, scope.Stop());
        metrics.RegisterObservableGauge(new("app.disabled_core.snapshot"), () =>
        { callbacks++; return [new(1)]; });
        listener.RecordObservableInstruments();
        Assert.AreEqual(0, publications);
        Assert.AreEqual(0, callbacks);
    }

    [TestMethod]
    public void TheCoreFallbackDoesNotRetainAnObservableCallbackOwner()
    {
        IMetrics retained = DisabledMetrics.Instance;
        WeakReference owner = RegisterUnretainedOwner(retained);
        for (int attempt = 0; attempt < 10 && owner.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        Assert.IsFalse(owner.IsAlive, "A disabled singleton must not keep a callback owner's host graph alive.");
        GC.KeepAlive(retained);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterUnretainedOwner(IMetrics metrics)
    {
        var owner = new CallbackOwner();
        metrics.RegisterObservableGauge(new("app.disabled_core.owner"), owner.Observe);
        return new WeakReference(owner);
    }

    private sealed class CallbackOwner
    {
        internal IReadOnlyList<MetricObservation> Observe()
            => throw new AssertFailedException("Disabled observation must never invoke user code.");
    }
}
