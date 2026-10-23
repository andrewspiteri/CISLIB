using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionRetirementOverlapBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task MonitorDisposalRetainsAnEarlierIdleCollectionRetirementUntilItsActualObserverReturns()
    {
        var service = new HealthcheckService(new(),
            new HealthApplicationIdentity { Name = "overlapping idle collection retirement", InstanceId = Guid.NewGuid() });
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConditionMonitor? monitor = null;
        Task? retirement = null;
        CancellationTokenRegistration observer = default;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int enteredCount = 0, returnedCount = 0;
        bool assertionsCompleted = false;
        try
        {
            monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
            HealthCollectionTarget target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new());
            Task monitorDrain = monitor.WaitForDisposalAsync();
            observer = target.Retirement.Register(() =>
            {
                Interlocked.Increment(ref enteredCount);
                entered.TrySetResult();
                release.Wait();
                Interlocked.Increment(ref returnedCount);
            });

            // Only collection admission closes on this worker. The external
            // monitor remains live while its actual retirement observer runs.
            retirement = Task.Run(() => state.WithdrawCollection(target));
            await entered.Task.WaitAsync(Deadline);
            Assert.AreEqual(1, Volatile.Read(ref enteredCount));
            Assert.AreEqual(0, Volatile.Read(ref returnedCount));
            Assert.IsTrue(target.Retirement.IsCancellationRequested);
            Assert.IsFalse(retirement.IsCompleted);
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);

            Task monitorDisposal = monitor.DisposeAsync().AsTask();

            Assert.AreSame(monitorDrain, monitorDisposal);
            Assert.IsFalse(monitorDrain.IsCompleted,
                "Disposing the monitor must retain the earlier collection retirement observer even though no evaluation operation exists.");
            Assert.IsFalse(retirement.IsCompleted);
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Task serviceDisposal = service.DisposeAsync().AsTask();
            Assert.IsFalse(serviceDisposal.IsCompleted);

            release.Set();
            await retirement.WaitAsync(Deadline);

            Assert.IsTrue(release.IsSet);
            Assert.AreEqual(1, Volatile.Read(ref returnedCount));
            Assert.IsTrue(monitorDrain.IsCompletedSuccessfully,
                "The shared monitor drain must complete after its sole real retirement observer returns.");
            Assert.IsTrue(serviceDisposal.IsCompletedSuccessfully);
            assertionsCompleted = true;
        }
        finally
        {
            release.Set();
            try
            {
                if (retirement is not null) { await retirement.WaitAsync(Deadline); }
            }
            finally
            {
                observer.Dispose();
                try
                {
                    if (monitor is not null)
                    {
                        Task monitorDrain = monitor.DisposeAsync().AsTask();
                        // The callback above is released and joined. A failed
                        // completion assertion can leave only the tested managed
                        // monitor signal; never hide it behind another dead wait.
                        if (assertionsCompleted || monitorDrain.IsCompleted)
                        { await monitorDrain.WaitAsync(Deadline); }
                    }
                }
                finally { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
                GC.KeepAlive(monitor);
            }
        }
    }
}
