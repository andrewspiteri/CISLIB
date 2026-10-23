using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckLifecycleQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public void NoWorkServiceDisposalCompletesEveryExistingWaitBeforeReturning(int monitorCount)
    {
        HealthcheckService service = CreateService();
        IConditionMonitor[] monitors = Enumerable.Range(0, monitorCount).Select(_ =>
            service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true })).ToArray();
        Task serviceWait = service.WaitForDisposalAsync();
        Task[] monitorWaits = monitors.Select(monitor => monitor.WaitForDisposalAsync()).ToArray();
        Assert.IsFalse(serviceWait.IsCompleted);
        Assert.IsTrue(monitorWaits.All(wait => !wait.IsCompleted));

        ValueTask disposal = service.DisposeAsync();

        // No admitted work or native resources exist. Observe the required milestone
        // directly; awaiting a missing completion would hide the defect in cleanup.
        Assert.IsTrue(disposal.IsCompletedSuccessfully);
        Assert.IsTrue(serviceWait.IsCompletedSuccessfully);
        Assert.IsTrue(monitorWaits.All(wait => wait.IsCompletedSuccessfully));
        Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
        Assert.IsTrue(service.WaitForDisposalAsync().IsCompletedSuccessfully);
        service.BeginStopping();
        Assert.ThrowsExactly<ObjectDisposedException>(() => service.CaptureSnapshot());
        foreach (IConditionMonitor monitor in monitors)
        {
            Assert.IsTrue(monitor.DisposeAsync().IsCompletedSuccessfully);
            Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.Clear());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NoWorkMonitorWithdrawalCompletesItsExistingWaitAndIsIdempotent(bool dispose)
    {
        HealthcheckService service = CreateService();
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions
        { IsCritical = true, InitiallyFlagged = true, Name = "withdrawn" });
        Guid id = monitor.MonitorId;
        Task wait = monitor.WaitForDisposalAsync();
        Assert.IsFalse(wait.IsCompleted);

        if (dispose) { Assert.IsTrue(monitor.DisposeAsync().IsCompletedSuccessfully); }
        else { monitor.Withdraw(); }

        Assert.IsTrue(wait.IsCompletedSuccessfully);
        HealthSnapshot after = service.CaptureSnapshot();
        Assert.AreEqual(0, after.MonitorCounts.Active);
        Assert.AreEqual(HealthStatus.Healthy, after.HealthStatus);
        monitor.Withdraw();
        Assert.IsTrue(monitor.DisposeAsync().IsCompletedSuccessfully);
        Assert.IsTrue(monitor.WaitForDisposalAsync().IsCompletedSuccessfully);
        Assert.AreEqual(after.Revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(id, monitor.MonitorId);
        Assert.AreEqual("withdrawn", monitor.Name);
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.Flag());
        Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StoppingOrDisposedServiceRejectsItsFirstRunnerClaimAndAllNewAdmission(bool disposed)
    {
        HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = Failures(service);
        if (disposed) { Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully); }
        else { service.BeginStopping(); }

        // This service has never claimed a runner: a second-runner guard cannot
        // accidentally mask the terminal service admission check.
        Action claim = () => _ = new HealthCollectorRunner(service);
        Action register = () => _ = Failures(service);
        Action bind = () => _ = HealthExecutionBinder.Bind(monitor,
            new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid() });
        if (disposed)
        {
            Assert.ThrowsExactly<ObjectDisposedException>(claim);
            Assert.ThrowsExactly<ObjectDisposedException>(register);
            Assert.ThrowsExactly<ObjectDisposedException>(bind);
        }
        else
        {
            Assert.ThrowsExactly<InvalidOperationException>(claim);
            Assert.ThrowsExactly<InvalidOperationException>(register);
            Assert.ThrowsExactly<InvalidOperationException>(bind);
            Assert.IsTrue(service.CaptureSnapshot().Application.IsStopping);
        }
        Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WithdrawingMonitorReleasesBindingRegistrationBeforeAnyOldBindingCall(bool dispose)
    {
        HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor? first = null;
        IConsecutiveFailureMonitor? replacement = null;
        IHealthExecutionBinding? old = null;
        IHealthExecutionBinding? current = null;
        try
        {
            first = Failures(service);
            Guid registration = Guid.NewGuid();
            var options = new HealthExecutionBindingOptions { RegistrationId = registration };
            old = HealthExecutionBinder.Bind(first, options);

            if (dispose) { Assert.IsTrue(first.DisposeAsync().IsCompletedSuccessfully); }
            else { first.Withdraw(); }
            Assert.IsTrue(first.WaitForDisposalAsync().IsCompletedSuccessfully);

            // Do not call the old binding first: its lazy observation/release could
            // repair a missing eager retirement and mask stale registration retention.
            replacement = Failures(service);
            current = HealthExecutionBinder.Bind(replacement, options);
            Assert.AreEqual(registration, current.RegistrationId);
            Assert.AreNotEqual(old.MonitorId, current.MonitorId);
            HealthSnapshot replacementBound = service.CaptureSnapshot();
            Assert.AreEqual(1, replacementBound.MonitorCounts.Active);
            Assert.AreEqual(1, replacementBound.Monitors.Count);
            Assert.AreEqual(replacement.MonitorId, replacementBound.Monitors.Single().MonitorId);
            Assert.AreEqual(HealthFactDisposition.Retired, old.Report(new HealthExecutionFact
            {
                RegistrationId = registration,
                Kind = HealthExecutionFactKind.Registered,
                OccurredAtUtc = DateTimeOffset.UtcNow
            }));
            await old.DisposeAsync().AsTask().WaitAsync(Deadline);
            await current.DisposeAsync().AsTask().WaitAsync(Deadline);
            replacement.ReportSuccess();
            HealthSnapshot usable = service.CaptureSnapshot();
            Assert.AreEqual(1, usable.MonitorCounts.Active);
            Assert.AreEqual(1, usable.Monitors.Count);
            var retained = usable.Monitors.Single();
            Assert.IsNotNull(retained.Outcomes);
            Assert.AreEqual(1L, retained.Outcomes.Successful);
            Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
        }
        finally
        {
            try
            {
                // Both release requests start even if either implementation throws
                // synchronously; each async observer captures that fault in its Task.
                await Task.WhenAll(ObserveCompletedBindingRelease(old), ObserveCompletedBindingRelease(current));
            }
            finally
            {
                await service.DisposeAsync().AsTask().WaitAsync(Deadline);
                GC.KeepAlive(first);
                GC.KeepAlive(replacement);
            }
        }
    }

    private static async Task ObserveCompletedBindingRelease(IHealthExecutionBinding? binding)
    {
        if (binding is null) { return; }
        Task completion = binding.DisposeAsync().AsTask();
        // Pure cleanup does not assert or claim completion of a pending private
        // wrapper task. The actual service lifetime is always joined separately.
        if (completion.IsCompleted) { await completion; }
    }

    [TestMethod]
    public async Task MonitorAndServiceDisposalWaitForActualCanceledCollectionAndLeaseCleanup()
    {
        HealthcheckService service = CreateService();
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = Failures(service);
        var owner = new GatedOwner();
        IHealthCollectionRegistration registration = runner.Register(monitor, owner, new HealthCollectorOptions());
        Task<HealthCollectionAttempt>? collection = null;
        try
        {
            await runner.StartAsync().WaitAsync(Deadline);
            collection = registration.CollectAsync();
            await Task.WhenAny(owner.Entered.Task, owner.CleanupEntered.Task, collection).WaitAsync(Deadline);
            Assert.IsTrue(owner.Entered.Task.IsCompletedSuccessfully, "The actual collection must invoke its callback before cleanup or settlement.");
            Assert.IsFalse(collection.IsCompleted, "The actual collection must remain pending while its evaluation gate is held.");
            Task monitorWait = monitor.WaitForDisposalAsync();
            ValueTask monitorDisposal = monitor.DisposeAsync();
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.IsFalse(monitorDisposal.IsCompleted);
            Assert.IsFalse(monitorWait.IsCompleted);
            Assert.IsTrue(owner.CancellationObserved.Task.IsCompletedSuccessfully);
            await owner.CancellationObserved.Task.WaitAsync(Deadline);
            Assert.IsFalse(collection.IsCompleted);
            Assert.IsFalse(owner.CleanupEntered.Task.IsCompleted);
            Assert.AreEqual(0, owner.CleanupCount);
            Assert.IsFalse(monitorDisposal.IsCompleted);
            owner.ReleaseWork.TrySetResult();
            await owner.CleanupEntered.Task.WaitAsync(Deadline);
            Assert.IsFalse(collection.IsCompleted);
            Assert.IsFalse(monitorWait.IsCompleted);
            Assert.AreEqual(0, owner.CleanupCount);

            Task serviceWait = service.WaitForDisposalAsync();
            ValueTask serviceDisposal = service.DisposeAsync();
            Assert.IsFalse(serviceDisposal.IsCompleted);
            Assert.IsFalse(serviceWait.IsCompleted);
            owner.ReleaseCleanup.TrySetResult();

            await Task.WhenAll(collection, monitorDisposal.AsTask(), serviceDisposal.AsTask()).WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, collection.Result.Disposition);
            Assert.IsTrue(monitorWait.IsCompletedSuccessfully);
            Assert.IsTrue(serviceWait.IsCompletedSuccessfully);
            Assert.AreEqual(1, owner.CleanupCount);
            Assert.IsTrue(monitor.DisposeAsync().IsCompletedSuccessfully);
            Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
        }
        finally
        {
            owner.ReleaseWork.TrySetResult();
            owner.ReleaseCleanup.TrySetResult();
            Task[] cleanup = [runner.DisposeAsync().AsTask(), service.DisposeAsync().AsTask()];
            if (collection is not null) { cleanup = [.. cleanup, collection]; }
            await Task.WhenAll(cleanup).WaitAsync(Deadline);
            GC.KeepAlive(monitor);
            GC.KeepAlive(registration);
        }
    }

    private static HealthcheckService CreateService() => new(new HealthcheckOptions(), new HealthApplicationIdentity
    {
        Name = "lifecycle-qualification",
        InstanceId = Guid.NewGuid()
    });

    private static IConsecutiveFailureMonitor Failures(HealthcheckService service) => service.CreateConsecutiveFailures(
        new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 });

    private sealed class GatedOwner : IHealthCollector
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWork { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int cleanupCount;
        internal int CleanupCount => Volatile.Read(ref cleanupCount);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            lease = new Lease(this);
            return true;
        }
        private sealed class Lease(GatedOwner owner) : IHealthCollectionLease
        {
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                using CancellationTokenRegistration cancellation = lifetimeCancellation.Register(() => owner.CancellationObserved.TrySetResult());
                owner.Entered.TrySetResult();
                await owner.ReleaseWork.Task;
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }
            public async ValueTask DisposeAsync()
            {
                owner.CleanupEntered.TrySetResult();
                await owner.ReleaseCleanup.Task;
                Interlocked.Increment(ref owner.cleanupCount);
            }
        }
    }
}
