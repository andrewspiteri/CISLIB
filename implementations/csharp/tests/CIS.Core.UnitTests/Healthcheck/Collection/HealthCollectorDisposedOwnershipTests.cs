using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorDisposedOwnershipTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DisposedRunnerCanBeCollectedWhileItsServiceAndMonitorRemainUsable(bool collectOnce)
    {
        var service = new HealthcheckService(new(), new() { Name = "disposed runner ownership", InstanceId = Guid.NewGuid() });
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        Exception? primary = null;
        try
        {
            RunnerObservation observed = CreateDisposeAndForgetRunner(service, monitor, collectOnce);

            Assert.AreEqual(collectOnce ? 1 : 0, observed.Acquisitions);
            Assert.AreEqual(collectOnce ? 1 : 0, observed.Evaluations);
            Assert.AreEqual(collectOnce ? 1 : 0, observed.Cleanups);
            Assert.AreEqual(0, observed.ActiveLeases);
            Assert.IsFalse(service.CaptureSnapshot().Application.IsStopping);
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);

            CollectUntilDead(observed.Runner);

            Assert.IsFalse(observed.Runner.IsAlive,
                "Terminal runner disposal must release its service-stopping subscription rather than let the still-live service root the disposed runner.");
            monitor.ReportSuccess();
            HealthSnapshot snapshot = service.CaptureSnapshot();
            Assert.IsFalse(snapshot.Application.IsStopping);
            Assert.AreEqual(1, snapshot.MonitorCounts.Active);
            Assert.AreEqual((collectOnce ? 1L : 0L) + 1L, snapshot.Monitors.Single().Outcomes!.Successful);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Task cleanup = DrainService(service);
            try { cleanup.WaitAsync(Deadline).GetAwaiter().GetResult(); }
            catch (Exception error) when (primary is not null)
            { throw new AggregateException("The ownership assertion and actual service cleanup both failed.", primary, cleanup.Exception ?? error); }
            finally { GC.KeepAlive(monitor); GC.KeepAlive(service); }
        }
    }

    // This helper is deliberately synchronous and cannot be inlined. After it
    // returns, no fixture async state machine, returned Task or owner handle
    // can keep the runner alive. Every real operation and both drains finished.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RunnerObservation CreateDisposeAndForgetRunner(HealthcheckService service,
        IConsecutiveFailureMonitor monitor, bool collectOnce)
    {
        var runner = new HealthCollectorRunner(service);
        var owner = new CountingOwner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Exception? primary = null;
        try
        {
            if (collectOnce)
            {
                registration = runner.Register(monitor, owner, new());
                runner.StartAsync().WaitAsync(Deadline).GetAwaiter().GetResult();
                caller = registration.CollectAsync();
                Task.WhenAny(owner.AcquisitionEntered.Task, caller).WaitAsync(Deadline).GetAwaiter().GetResult();
                if (caller.IsCompleted)
                {
                    caller.GetAwaiter().GetResult();
                    Assert.Fail("The queued caller cannot settle before its actual acquisition is released.");
                }
                owner.AcquisitionEntered.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                Task<HealthCollectionAttempt> operation = registration.CollectAsync();
                owner.ReleaseAcquisition();
                operation.WaitAsync(Deadline).GetAwaiter().GetResult();
            }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.ReleaseAcquisition();
            // Initiate every actual drain before observing one; synchronous
            // disposal faults cannot suppress the other owner's cleanup.
            Task registrationDrain = DrainRegistration(registration);
            Task runnerDrain = DrainRunner(runner);
            Task all = Task.WhenAll(registrationDrain, runnerDrain);
            List<Exception> errors = [];
            if (primary is not null) { errors.Add(primary); }
            try { all.WaitAsync(Deadline).GetAwaiter().GetResult(); }
            catch (Exception error)
            {
                IEnumerable<Exception> actualErrors = all.Exception is { } aggregate ? aggregate.InnerExceptions : [error];
                foreach (Exception actual in actualErrors)
                {
                    if (!errors.Any(existing => ReferenceEquals(existing, actual))) { errors.Add(actual); }
                }
            }
            finally { GC.KeepAlive(monitor); GC.KeepAlive(service); }
            foreach (Task drain in new[] { registrationDrain, runnerDrain })
            {
                if (!drain.IsCompleted) { continue; }
                try { drain.GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    IEnumerable<Exception> actualErrors = drain.Exception is { } aggregate ? aggregate.InnerExceptions : [error];
                    foreach (Exception actual in actualErrors)
                    {
                        if (!errors.Any(existing => ReferenceEquals(existing, actual))) { errors.Add(actual); }
                    }
                }
            }
            if (caller is not null && (caller.IsFaulted || caller.IsCanceled))
            {
                try { caller.GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); }
                }
            }
            if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
            if (errors.Count > 1) { throw new AggregateException("The actual collection, original caller or runner cleanup failed.", errors); }
        }
        if (caller is not null)
        {
            if (caller.IsFaulted || caller.IsCanceled) { caller.GetAwaiter().GetResult(); }
            Assert.IsTrue(caller.IsCompleted, "The original queued caller must complete after its actual operation and runner drains have settled.");
            HealthCollectionAttempt attempt = caller.GetAwaiter().GetResult();
            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
        }
        return new(new WeakReference(runner), owner.Acquisitions, owner.Evaluations, owner.Cleanups, owner.ActiveLeases);
    }

    private static Task DrainRegistration(IHealthCollectionRegistration? registration)
    {
        try { return registration?.DisposeAsync().AsTask() ?? Task.CompletedTask; }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static Task DrainRunner(HealthCollectorRunner runner)
    {
        try { return runner.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static Task DrainService(HealthcheckService service)
    {
        try { return service.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static void CollectUntilDead(WeakReference reference)
    {
        for (int attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed record RunnerObservation(WeakReference Runner, int Acquisitions, int Evaluations, int Cleanups, int ActiveLeases);

    private sealed class CountingOwner : IHealthCollector
    {
        private readonly TaskCompletionSource releaseAcquisition = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AcquisitionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Acquisitions;
        internal int Evaluations;
        internal int Cleanups;
        internal int ActiveLeases;
        internal void ReleaseAcquisition() => releaseAcquisition.TrySetResult();

        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            AcquisitionEntered.TrySetResult();
            releaseAcquisition.Task.GetAwaiter().GetResult();
            Interlocked.Increment(ref Acquisitions);
            Interlocked.Increment(ref ActiveLeases);
            lease = new Lease(this);
            return true;
        }

        private sealed class Lease(CountingOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.Evaluations);
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.Cleanups);
                Interlocked.Decrement(ref owner.ActiveLeases);
                return ValueTask.CompletedTask;
            }
        }
    }
}
