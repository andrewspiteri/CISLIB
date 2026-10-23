using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthBindingRetirementBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task RetirementWithoutOutstandingWorkCompletesTheSharedBindingBarrier(bool owned, bool availability)
    {
        await using var service = Service();
        var options = new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid(), SuccessPublishesUsableData = true };
        IHealthMonitor? external = owned ? null : availability
            ? service.CreateAvailability(new() { IsCritical = false })
            : service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
        IHealthExecutionBinding binding = owned ? availability
            ? HealthExecutionBinder.Bind(service, new AvailabilityMonitorOptions { IsCritical = false }, options)
            : HealthExecutionBinder.Bind(service, new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 1 }, options)
            : HealthExecutionBinder.Bind(external!, options);
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = binding.RegistrationId, Kind = HealthExecutionFactKind.Retired, OccurredAtUtc = DateTimeOffset.UtcNow }));
            HealthSnapshot retired = service.CaptureSnapshot();
            Assert.IsTrue(retired.Revision > before.Revision);
            Assert.AreEqual(owned ? 0 : 1, retired.MonitorCounts.Active);

            Task first = binding.DisposeAsync().AsTask();
            Task second = binding.DisposeAsync().AsTask();

            Assert.AreSame(first, second);
            await Task.WhenAll(first, second).WaitAsync(Deadline);
            Assert.AreEqual(retired.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
            if (external is IAvailabilityMonitor published) { published.ReportPublished(); }
            if (external is IConsecutiveFailureMonitor failures) { failures.ReportSuccess(); }
            if (!owned)
            {
                HealthSnapshot usable = service.CaptureSnapshot();
                Assert.AreEqual(1, usable.MonitorCounts.Active);
                Assert.AreEqual(1, usable.Monitors.Count);
                var retained = usable.Monitors.Single();
                Assert.IsNotNull(retained.Outcomes);
                Assert.AreEqual(1L, retained.Outcomes.Successful);
            }
        }
        finally
        {
            try
            {
                // The body awaits the actual disposal task with its existing deadline.
                // Cleanup requests release and observes completed faults without
                // asserting or claiming completion of a pending private barrier.
                Task completion = binding.DisposeAsync().AsTask();
                if (completion.IsCompleted) { await completion; }
            }
            finally
            {
                if (external is not null) { await external.DisposeAsync().AsTask().WaitAsync(Deadline); }
                GC.KeepAlive(external);
            }
        }
    }

    [TestMethod]
    public async Task DisposingTheActuallyExecutingMonitorRejectsItsOwnWaitBeforeReturningATask()
    {
        await using var service = Service();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        Exception? observed = null;
        Task? incorrectlyReturnedBarrier = null;
        bool incompleteWhenReturned = false;
        var owner = new ObservingOwner(() =>
        {
            try
            {
                incorrectlyReturnedBarrier = monitor.DisposeAsync().AsTask();
                incompleteWhenReturned = !incorrectlyReturnedBarrier.IsCompleted;
            }
            catch (Exception error) { observed = error; }
        });
        await using var runner = new HealthCollectorRunner(service);
        await using IHealthCollectionRegistration registration = runner.Register(monitor, owner, new());
        await runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsNotNull(observed);
        Assert.AreEqual(typeof(InvalidOperationException), observed.GetType());
        Assert.IsNull(incorrectlyReturnedBarrier, "Own disposal must reject the self-containing wait synchronously after requesting withdrawal.");
        Assert.IsFalse(incompleteWhenReturned);
        Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.IsTrue(monitor.WaitForDisposalAsync().IsCompletedSuccessfully);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentWithdrawalAndFactDeliveryReleaseTheExternalBinding(bool outcome)
    {
        await using var service = Service();
        for (int round = 0; round < 16; round++)
        {
            IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
            IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, new() { RegistrationId = Guid.NewGuid() });
            using var start = new ManualResetEventSlim();
            DateTimeOffset at = DateTimeOffset.UtcNow;
            var fact = new HealthExecutionFact
            {
                RegistrationId = binding.RegistrationId,
                ExecutionId = 1,
                OccurredAtUtc = at,
                Kind = outcome ? HealthExecutionFactKind.Outcome : HealthExecutionFactKind.CallbackStarted,
                SourcePhase = outcome ? HealthExecutionPhase.Callback : null,
                Outcome = outcome ? HealthExecutionOutcome.Succeeded : null,
                WorkCompletedAtUtc = outcome ? at : null,
                CallbackStartedAtUtc = outcome ? null : at
            };
            try
            {
                Task<HealthFactDisposition> report = Task.Run(() => { start.Wait(); return binding.Report(fact); });
                Task withdrawal = Task.Run(() => { start.Wait(); monitor.Withdraw(); });
                start.Set();
                await Task.WhenAll(report, withdrawal).WaitAsync(Deadline);

                Assert.IsTrue(report.Result is HealthFactDisposition.Recorded or HealthFactDisposition.Retired);
                Task completion = binding.DisposeAsync().AsTask();
                await completion.WaitAsync(Deadline);
                HealthSnapshot after = service.CaptureSnapshot();
                Assert.AreEqual(0, after.MonitorCounts.Active);
                Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
                Assert.AreEqual(after.Revision, service.CaptureSnapshot().Revision);
            }
            finally
            {
                start.Set();
                try
                {
                    Task completion = binding.DisposeAsync().AsTask();
                    if (completion.IsCompleted) { await completion; }
                }
                finally { await monitor.DisposeAsync().AsTask().WaitAsync(Deadline); }
            }
        }
    }

    private static HealthcheckService Service() => new(new(), new HealthApplicationIdentity { Name = "retirement boundaries", InstanceId = Guid.NewGuid() });

    private sealed class ObservingOwner(Action evaluate) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        { Interlocked.Increment(ref acquisitions); lease = new Lease(this); return true; }
        private void Evaluate() { Interlocked.Increment(ref evaluations); evaluate(); }
        private sealed class Lease(ObservingOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                owner.Evaluate();
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
            public ValueTask DisposeAsync()
            { Interlocked.Increment(ref owner.cleanups); return ValueTask.CompletedTask; }
        }
    }
}
