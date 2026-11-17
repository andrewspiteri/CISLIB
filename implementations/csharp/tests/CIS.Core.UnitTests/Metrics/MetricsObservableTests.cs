using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using CIS.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class MetricsObservableTests
{
    [TestMethod]
    public async Task EquivalentObservableRegistrationReusesOneCallbackAndConflictingCallbacksFailClearly()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        int calls = 0;
        Func<IReadOnlyList<MetricObservation>> callback = () =>
        { calls++; return [new(2, new(("operation", "read"))), new(-3, new(("operation", "write")))]; };
        var definition = new MetricDefinition("app.snapshot", allowedDimensionKeys: ["operation"]);
        runtime.RegisterObservableGauge(definition, callback);
        runtime.RegisterObservableGauge(definition, callback);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.RegisterObservableGauge(definition, () => [new(99)]));
        listener.Collect();
        listener.Collect();
        Assert.AreEqual(2, calls);
        Assert.AreEqual(1, listener.Instruments.Count);
        CollectionAssert.AreEqual(new[] { 2d, -3d, 2d, -3d }, listener.Points("app.snapshot").Select(point => (double)point.Value).ToArray());
    }

    [TestMethod]
    [DataRow("throw")]
    [DataRow("null")]
    public async Task AnOrdinaryCallbackFailureOrMissingSnapshotOmitsItWithoutBreakingAnotherGauge(string invalid)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var diagnostics = new ConcurrentQueue<string>();
        await using var runtime = new MetricsRuntime(meter, diagnostic: diagnostics.Enqueue);
        runtime.RegisterObservableGauge(new("app.bad", allowedDimensionKeys: ["operation"]), () => invalid switch
        {
            "throw" => throw new InvalidOperationException("PRIVATE callback detail"),
            "null" => null!,
            _ => throw new AssertFailedException("Unknown controlled callback failure.")
        });
        runtime.RegisterObservableGauge(new("app.good"), () => [new(42)]);
        listener.Collect();
        listener.Collect();
        Assert.AreEqual(0, listener.Points("app.bad").Length, "A failed or unavailable callback must not publish data.");
        CollectionAssert.AreEqual(new[] { 42d, 42d }, listener.Points("app.good").Select(point => (double)point.Value).ToArray());
        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(value => !value.Contains("PRIVATE", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task InvalidObservableMeasurementsAreDroppedIndividuallyWhileValidSiblingObservationsRemainAvailable()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        var diagnostics = new ConcurrentQueue<string>();
        await using var runtime = new MetricsRuntime(meter, diagnostic: diagnostics.Enqueue);
        runtime.RegisterObservableGauge(new("app.mixed", allowedDimensionKeys: ["operation"]), () =>
        [new(1, new(("operation", "read"))), new(double.NaN),
            new(2, new(("unknown", "PRIVATE value"))), new(3, new(("operation", "write")))]);
        listener.Collect();
        CollectionAssert.AreEqual(new[] { 1d, 3d }, listener.Points("app.mixed").Select(point => (double)point.Value).ToArray());
        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsTrue(diagnostics.All(value => !value.Contains("PRIVATE", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AnOverLimitSnapshotIsRejectedFromItsFirstCountBeforeAnyValueAccess()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter, new() { MaxObservableMeasurements = 2 });
        int countReads = 0, valueReads = 0;
        var observations = new ControlledObservations(
            () => Interlocked.Increment(ref countReads) == 1 ? 3 : 0,
            _ => { Interlocked.Increment(ref valueReads); return new(17); });
        runtime.RegisterObservableGauge(new("app.capped"), () => observations);
        listener.Collect();
        Assert.AreEqual(1, countReads);
        Assert.AreEqual(0, valueReads);
        Assert.AreEqual(0, listener.Points("app.capped").Length);
    }

    [TestMethod]
    public async Task StopJoinsAHeldActualCallbackAndLaterCollectionCannotInvokeItsOwner()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var entered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        int calls = 0;
        runtime.RegisterObservableGauge(new("app.held"), () =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return [new(7)];
        });
        Task collection = Task.Run(listener.Collect);
        Task? stop = null;
        try
        {
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            stop = runtime.StopAsync();
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(collection.IsCompleted);
            Assert.AreSame(stop, runtime.StopAsync());
            release.TrySetResult();
            await Task.WhenAll(collection, stop).WaitAsync(MetricsTestSupport.Deadline);
            listener.Collect();
            Assert.AreEqual(1, Volatile.Read(ref calls));
            Assert.AreEqual(7d, (double)listener.Points("app.held").Single().Value);
        }
        finally
        {
            release.TrySetResult();
            await collection.WaitAsync(MetricsTestSupport.Deadline);
            await (stop ?? runtime.StopAsync()).WaitAsync(MetricsTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StopIncludesUserOwnedCountAndIndexerMaterializationBeforeTheSnapshotOwnerCanBeDisposed(bool holdIndexer)
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var entered = MetricsTestSupport.Signal();
        var release = MetricsTestSupport.Signal();
        int ownerDisposed = 0, calls = 0;
        void Hold()
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            if (Volatile.Read(ref ownerDisposed) != 0) { throw new ObjectDisposedException("Snapshot owner"); }
        }
        var observations = new ControlledObservations(
            () => { if (!holdIndexer) { Hold(); } return 1; },
            _ => { if (holdIndexer) { Hold(); } return new(9); });
        runtime.RegisterObservableGauge(new("app.materialized"), () =>
        { Interlocked.Increment(ref calls); return observations; });
        Task collection = Task.Run(listener.Collect);
        Task? stop = null;
        try
        {
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            stop = runtime.StopAsync();
            Assert.IsFalse(stop.IsCompleted, "The callback returned its list, but actual user-owned materialization is still active.");
            release.TrySetResult();
            await Task.WhenAll(collection, stop).WaitAsync(MetricsTestSupport.Deadline);
            Volatile.Write(ref ownerDisposed, 1);
            listener.Collect();
            Assert.AreEqual(1, Volatile.Read(ref calls));
            Assert.AreEqual(9d, (double)listener.Points("app.materialized").Single().Value);
        }
        finally
        {
            release.TrySetResult();
            await collection.WaitAsync(MetricsTestSupport.Deadline);
            await (stop ?? runtime.StopAsync()).WaitAsync(MetricsTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task HostLocalAttachmentAndBorrowedMetersRemainIsolatedAndRuntimeDisposalDoesNotDisposeTheMeter()
    {
        using var firstMeter = new Meter("CIS.Metrics");
        using var secondMeter = new Meter("CIS.Metrics");
        using var firstListener = new MetricsTestListener(firstMeter);
        using var secondListener = new MetricsTestListener(secondMeter);
        await using var first = new MetricsRuntime(firstMeter);
        await using var second = new MetricsRuntime(secondMeter);
        var owner = new MetricsAttachment();
        var otherOwner = new MetricsAttachment();
        Assert.IsTrue(owner.Attach(first));
        Assert.IsFalse(owner.Attach(first));
        Assert.IsTrue(otherOwner.Attach(second));
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.Attach(second));
        first.CreateCounter(new("app.host")).Add(1);
        second.CreateCounter(new("app.host")).Add(3);
        Assert.AreEqual(1L, (long)firstListener.Points("app.host").Single().Value);
        Assert.AreEqual(3L, (long)secondListener.Points("app.host").Single().Value);
        await first.DisposeAsync();
        firstMeter.CreateCounter<long>("app.borrowed").Add(5);
        Assert.AreEqual(5L, (long)firstListener.Points("app.borrowed").Single().Value);
        second.CreateCounter(new("app.host")).Add(4);
        CollectionAssert.AreEqual(new[] { 3L, 4L }, secondListener.Points("app.host").Select(point => (long)point.Value).ToArray());
    }

    [TestMethod]
    public async Task SettledRuntimeReleasesItsDiagnosticOwnerEvenWhenBorrowedMetricHandlesRemainAlive()
    {
        using var meter = new Meter("CIS.Metrics");
        (MetricsRuntime runtime, WeakReference diagnosticOwner) = WithDiagnosticOwner(meter);
        ICounter retained = runtime.CreateCounter(new("app.retained"));
        Assert.IsTrue(diagnosticOwner.IsAlive);
        await runtime.StopAsync().WaitAsync(MetricsTestSupport.Deadline);
        for (int attempt = 0; attempt < 10 && diagnosticOwner.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        Assert.IsFalse(diagnosticOwner.IsAlive, "Retaining a borrowed module handle must not retain the stopped host's diagnostic graph.");
        GC.KeepAlive(retained);
        GC.KeepAlive(runtime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (MetricsRuntime Runtime, WeakReference Owner) WithDiagnosticOwner(Meter meter)
    {
        var owner = new DiagnosticOwner();
        return (new MetricsRuntime(meter, diagnostic: owner.Record), new WeakReference(owner));
    }

    private sealed class DiagnosticOwner
    {
        internal void Record(string _) { }
    }

    private sealed class ControlledObservations(Func<int> count, Func<int, MetricObservation> read) : IReadOnlyList<MetricObservation>
    {
        public int Count => count();
        public MetricObservation this[int index] => read(index);
        public IEnumerator<MetricObservation> GetEnumerator()
        { for (int index = 0; index < Count; index++) { yield return this[index]; } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
