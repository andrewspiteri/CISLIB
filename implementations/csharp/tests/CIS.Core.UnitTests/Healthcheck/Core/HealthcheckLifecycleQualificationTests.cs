using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.UnitTests.Healthcheck.HealthBindingRetirementBoundaryTests;

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
    public Task WithdrawingMonitorReleasesBindingRegistrationBeforeAnyOldBindingCall(bool dispose)
    {
        RetirementObservation observed = WithdrawAndReplaceBindingThenReleaseActualOwners(dispose);
        ReviewRetiredBinding(observed);
        return observed.First!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetirementObservation WithdrawAndReplaceBindingThenReleaseActualOwners(bool dispose)
    {
        HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor? first = null, replacement = null;
        IHealthExecutionBinding? old = null, current = null;
        Task? oldCompletion = null, oldCleanupCompletion = null, currentCleanupCompletion = null;
        BindingPhase? oldPhase = null, currentPhase = null;
        Exception? primary = null;
        var holds = new List<Exception>();
        bool bodyReturned = false, sourceReleaseJoined = false, missingSignalPhase = false;
        try
        {
            first = Failures(service);
            Guid registration = Guid.NewGuid();
            var options = new HealthExecutionBindingOptions { RegistrationId = registration };
            old = HealthExecutionBinder.Bind(first, options);

            if (dispose) { Assert.IsTrue(first.DisposeAsync().IsCompletedSuccessfully); }
            else { first.Withdraw(); }
            Assert.IsTrue(first.WaitForDisposalAsync().IsCompletedSuccessfully);

            // No old binding call is allowed before the actual replacement bind.
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
            oldCompletion = old.DisposeAsync().AsTask();
            oldPhase = ReadActualRetirement(old, first, service, owned: false, expectedWithdrawn: true);
            JoinLifecycleOperation(oldPhase.StateRelease);
            JoinLifecycleOperation(oldPhase.EntryDisposal);
            JoinLifecycleOperation(oldPhase.MonitorDisposal);
            sourceReleaseJoined = true;
            if (oldCompletion.IsCompleted) { JoinLifecycleOperation(oldCompletion); }
            else
            {
                if (!oldPhase.Retired || !oldPhase.ReleaseStarted || oldPhase.ActualSettlement is not null)
                { throw new InvalidOperationException("The old withdrawn binding has no proved missing-release phase; actual state work or observer may remain active."); }
                missingSignalPhase = true;
            }
            Task currentCompletion = current.DisposeAsync().AsTask();
            JoinLifecycleOperation(currentCompletion);
            currentPhase = ReadActualRetirement(current, replacement, service, owned: false, expectedWithdrawn: false);
            JoinLifecycleOperation(currentPhase.StateRelease);
            replacement.ReportSuccess();
            HealthSnapshot usable = service.CaptureSnapshot();
            Assert.AreEqual(1, usable.MonitorCounts.Active);
            Assert.AreEqual(1, usable.Monitors.Count);
            var retained = usable.Monitors.Single();
            Assert.IsNotNull(retained.Outcomes);
            Assert.AreEqual(1L, retained.Outcomes.Successful);
            Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
            bodyReturned = true;
        }
        catch (Exception error) { primary = error; }
        finally
        {
            // Preserve both original real observers and their actual Task.WhenAll.
            // The captured values are the actual SUT tasks, not observer-task aliases.
            Task oldObserver = ObserveCompletedBindingRelease(old, actual => oldCleanupCompletion = actual);
            Task currentObserver = ObserveCompletedBindingRelease(current, actual => currentCleanupCompletion = actual);
            Task actualObservers = Task.WhenAll(oldObserver, currentObserver);
            try { JoinLifecycleOperation(actualObservers); }
            catch (Exception error) { holds.Add(actualObservers.Exception ?? error); }
            if (oldCleanupCompletion is not null)
            {
                bool provedPrivatePromise = primary is null && bodyReturned && sourceReleaseJoined && missingSignalPhase
                    && ReferenceEquals(oldCleanupCompletion, oldCompletion) && oldPhase is { Retired: true, ReleaseStarted: true, ActualSettlement: null };
                if (!oldCleanupCompletion.IsCompleted && !provedPrivatePromise)
                {
                    try { JoinLifecycleOperation(oldCleanupCompletion); }
                    catch (Exception error) { holds.Add(error); }
                }
            }
            if (currentCleanupCompletion is not null && !currentCleanupCompletion.IsCompleted)
            {
                try { JoinLifecycleOperation(currentCleanupCompletion); }
                catch (Exception error) { holds.Add(error); }
            }
            try { JoinLifecycleOperation(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            try { JoinLifecycleOperation(service.WaitForDisposalAsync()); }
            catch (Exception error) { holds.Add(error); }
            foreach (BindingPhase? phase in new[] { oldPhase, currentPhase })
            {
                if (phase is null) { continue; }
                try { JoinLifecycleOperation(phase.EntryDisposal); }
                catch (Exception error) { holds.Add(error); }
                try { JoinLifecycleOperation(phase.MonitorDisposal); }
                catch (Exception error) { holds.Add(error); }
            }
            if (first is not null)
            {
                try { JoinLifecycleOperation(first.WaitForDisposalAsync()); }
                catch (Exception error) { holds.Add(error); }
            }
            if (replacement is not null)
            {
                try { JoinLifecycleOperation(replacement.WaitForDisposalAsync()); }
                catch (Exception error) { holds.Add(error); }
            }
            GC.KeepAlive(first);
            GC.KeepAlive(replacement);
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            throw new AggregateException("The replacement body or actual binding/monitor/state cleanup did not settle successfully.", holds);
        }
        if (primary is not null) { ExceptionDispatchInfo.Capture(primary).Throw(); }
        return new(null, oldCompletion, oldCleanupCompletion, missingSignalPhase,
            oldPhase!.Graph.Concat(currentPhase!.Graph).ToArray());
    }

    private static void JoinLifecycleOperation(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static async Task ObserveCompletedBindingRelease(IHealthExecutionBinding? binding, Action<Task>? capture = null)
    {
        if (binding is null) { return; }
        Task completion = binding.DisposeAsync().AsTask();
        capture?.Invoke(completion);
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
        Task<HealthCollectionAttempt>? operation = null;
        Task? monitorWait = null, monitorDisposalTask = null, serviceWait = null, serviceDisposalTask = null;
        Exception? primary = null;
        try
        {
            await runner.StartAsync().WaitAsync(Deadline);
            collection = registration.CollectAsync();
            await Task.WhenAny(owner.Entered.Task, owner.CleanupEntered.Task, collection).WaitAsync(Deadline);
            Assert.IsTrue(owner.Entered.Task.IsCompletedSuccessfully, "The actual collection must invoke its callback before cleanup or settlement.");
            Assert.IsFalse(collection.IsCompleted, "The actual collection must remain pending while its evaluation gate is held.");
            operation = registration.CollectAsync();
            monitorWait = monitor.WaitForDisposalAsync();
            ValueTask monitorDisposal = monitor.DisposeAsync();
            monitorDisposalTask = monitorDisposal.AsTask();
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

            serviceWait = service.WaitForDisposalAsync();
            ValueTask serviceDisposal = service.DisposeAsync();
            serviceDisposalTask = serviceDisposal.AsTask();
            Assert.IsFalse(serviceDisposal.IsCompleted);
            Assert.IsFalse(serviceWait.IsCompleted);
            owner.ReleaseCleanup.TrySetResult();

            await Task.WhenAll(operation, monitorDisposalTask, serviceDisposalTask).WaitAsync(Deadline);
            Assert.IsTrue(monitorWait.IsCompletedSuccessfully);
            Assert.IsTrue(serviceWait.IsCompletedSuccessfully);
            Assert.AreEqual(1, owner.CleanupCount);
            Assert.IsTrue(monitor.DisposeAsync().IsCompletedSuccessfully);
            Assert.IsTrue(service.DisposeAsync().IsCompletedSuccessfully);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.ReleaseWork.TrySetResult();
            owner.ReleaseCleanup.TrySetResult();
            Task[] cleanup = [DrainRegistrationAsync(), DrainRunnerAsync(), DrainServiceAsync()];
            cleanup = [.. cleanup, .. new[] { operation, monitorWait, monitorDisposalTask, serviceWait, serviceDisposalTask }.OfType<Task>()];
            Task drains = Task.WhenAll(cleanup);
            List<Exception> errors = primary is null ? [] : [primary];
            try { await drains.WaitAsync(Deadline); }
            catch (Exception error) { RecordTaskError(drains, error); }
            foreach (Task actual in cleanup)
            {
                if (!actual.IsCompleted) { continue; }
                try { await actual; }
                catch (Exception error) { RecordTaskError(actual, error); }
            }
            if (collection is { IsCompleted: true })
            {
                try { await collection; }
                catch (Exception error) { RecordTaskError(collection, error); }
            }
            GC.KeepAlive(monitor);
            GC.KeepAlive(registration);
            if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
            if (errors.Count > 1) { throw new AggregateException("The actual canceled collection, original caller or resource cleanup failed.", errors); }

            void RecordTaskError(Task actual, Exception error)
            {
                if (error is TimeoutException) { Add(error); }
                if (actual.Exception is { } aggregate) { foreach (Exception inner in aggregate.InnerExceptions) { Add(inner); } }
                else { Add(error); }
            }
            void Add(Exception error) { if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); } }
            async Task DrainRegistrationAsync() => await registration.DisposeAsync();
            async Task DrainRunnerAsync() => await runner.DisposeAsync();
            async Task DrainServiceAsync() => await service.DisposeAsync();
        }
        Assert.IsTrue(collection!.IsCompleted, "The original queued caller must complete after actual canceled work, lease cleanup and resource drains have settled.");
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await collection!).Disposition);
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
