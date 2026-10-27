using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRetirementAdmissionRaceQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task ActualAbandonedAttachmentRetirementCanCloseAdmissionBeforeANewRunnerHandleIsPublished()
    {
        var service = new HealthcheckService(new(),
            new HealthApplicationIdentity { Name = "retirement during collector admission", InstanceId = Guid.NewGuid() });
        var runner = new HealthCollectorRunner(service);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor destination = service.CreateConsecutiveFailures(new()
        {
            IsCritical = true,
            FailureThreshold = 1,
            Name = "retained destination"
        });
        var owner = new UninvokedOwner();
        AbandonedAttachment abandoned = CreateAbandonedAttachment(service, state);
        int retirementCallbacks = 0;
        bool stopPendingInsideObserver = false;
        Task? observerStop = null;
        IHealthCollectionRegistration? unexpected = null;
        Exception? primary = null;
        CancellationTokenRegistration observer = abandoned.Target.Retirement.Register(() =>
        {
            Interlocked.Increment(ref retirementCallbacks);
            // This is the real state-issued retirement signal, dispatched outside
            // its gate. Closing runner admission is allowed; the observer never
            // awaits the barrier containing the current registration call.
            observerStop = runner.StopAsync();
            stopPendingInsideObserver = !observerStop.IsCompleted;
        });
        try
        {
            CollectAbandonedHandle(abandoned.Handle);
            Assert.IsFalse(abandoned.Handle.IsAlive, "The real abandoned monitor handle must be collected before using its retirement as the admission interleaving.");
            Assert.IsFalse(abandoned.Target.Retirement.IsCancellationRequested);
            Assert.IsFalse(abandoned.MonitorDrain.IsCompleted);
            Assert.AreEqual(0, retirementCallbacks);

            Assert.ThrowsExactly<InvalidOperationException>(() =>
            {
                // State admission sweeps the actually lost handle, attaches the
                // destination and dispatches old retirement before returning its
                // new target. The observer stops this actual in-flight Register.
                unexpected = runner.Register(destination, owner, new());
            });

            Assert.AreEqual(1, retirementCallbacks);
            Assert.IsTrue(abandoned.Target.Retirement.IsCancellationRequested);
            Assert.IsTrue(abandoned.MonitorDrain.IsCompletedSuccessfully);
            Assert.IsTrue(stopPendingInsideObserver,
                "The actual registration call remains owned work while its state-return retirement observer runs.");
            Assert.IsNotNull(observerStop);
            await observerStop.WaitAsync(Deadline);
            Assert.AreEqual(0, owner.Acquisitions);
            HealthSnapshot snapshot = service.CaptureSnapshot();
            Assert.AreEqual(1, snapshot.MonitorCounts.Active);
            Assert.AreEqual(destination.MonitorId, snapshot.Monitors.Single().MonitorId);
            Assert.IsFalse(snapshot.Monitors.Single().Collection!.InProgress);
            Assert.IsFalse(snapshot.Application.IsStopping);
            Assert.AreEqual(0L, snapshot.Monitors.Single().Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Monitors.Single().Outcomes!.Failed);
            destination.ReportSuccess();
            Assert.AreEqual(MonitorState.Healthy, service.CaptureSnapshot().Monitors.Single().State);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try
            {
                // Start every actual drain independently. A blocked lifecycle
                // entry must not prevent another owner from beginning cleanup.
                await HealthCollectorTestCleanup.DrainAsync(Deadline, primary,
                    observerStop is null ? [] : [observerStop],
                    () => unexpected?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                    () => runner.DisposeAsync().AsTask(),
                    () => service.DisposeAsync().AsTask(),
                    () => { observer.Dispose(); return Task.CompletedTask; });
            }
            finally { GC.KeepAlive(destination); }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AbandonedAttachment CreateAbandonedAttachment(HealthcheckService service, IHealthCollectionState state)
    {
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new()
        {
            IsCritical = false,
            FailureThreshold = 1,
            Name = "actually abandoned"
        });
        HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
        return new(new WeakReference(monitor), target, monitor.WaitForDisposalAsync());
    }

    private static void CollectAbandonedHandle(WeakReference handle)
    {
        for (int attempt = 0; attempt < 10 && handle.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed record AbandonedAttachment(WeakReference Handle, HealthCollectionTarget Target, Task MonitorDrain);

    private sealed class UninvokedOwner : IHealthCollector
    {
        private int acquisitions;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = null;
            return false;
        }
    }
}
