using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorExecutionBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow("monitor-dispose")]
    [DataRow("monitor-wait")]
    [DataRow("registration-dispose")]
    [DataRow("registration-wait")]
    [DataRow("foreign-service-dispose")]
    [DataRow("foreign-service-wait")]
    [DataRow("foreign-runner-stop")]
    public async Task ACollectorCanAwaitBarriersThatDoNotContainItsOwnOperation(string barrier)
    {
        await using var local = new Scope();
        await using var foreign = new Scope();
        IConsecutiveFailureMonitor actor = local.Monitor();
        IConsecutiveFailureMonitor otherMonitor = local.Monitor();
        var idleOwner = new Owner();
        IHealthCollectionRegistration otherRegistration = local.Register(otherMonitor, idleOwner);
        int barrierCompleted = 0;
        var owner = new Owner
        {
            Evaluate = async _ =>
            {
                switch (barrier)
                {
                    case "monitor-dispose": await otherMonitor.DisposeAsync(); break;
                    case "monitor-wait": otherMonitor.Withdraw(); await otherMonitor.WaitForDisposalAsync(); break;
                    case "registration-dispose": await otherRegistration.DisposeAsync(); break;
                    case "registration-wait": otherRegistration.Withdraw(); await otherRegistration.WaitForDisposalAsync(); break;
                    case "foreign-service-dispose": await foreign.Service.DisposeAsync(); break;
                    case "foreign-service-wait":
                        await foreign.Service.DisposeAsync();
                        await foreign.Service.WaitForDisposalAsync();
                        break;
                    default: await foreign.Runner.StopAsync(); break;
                }
                Interlocked.Increment(ref barrierCompleted);
                return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
            }
        };
        IHealthCollectionRegistration registration = local.Register(actor, owner);
        await local.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, barrierCompleted);
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, idleOwner.Acquisitions);
        HealthMonitorSnapshot snapshot = local.Snapshot(actor);
        Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.IsNull(snapshot.Collection!.LastError);
    }

    [TestMethod]
    [DataRow("acquisition", "monitor")]
    [DataRow("acquisition", "registration")]
    [DataRow("acquisition", "service")]
    [DataRow("cleanup", "monitor")]
    [DataRow("cleanup", "registration")]
    [DataRow("cleanup", "service")]
    public async Task ActualAcquisitionAndLeaseCleanupCannotWaitForTheirOwnLifetime(string phase, string barrier)
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        IHealthCollectionRegistration? registration = null;
        Exception? observed = null;
        Task? incorrectlyAdmittedWait = null;
        int attempts = 0;
        void TryOwnWait()
        {
            Interlocked.Increment(ref attempts);
            try
            {
                incorrectlyAdmittedWait = barrier switch
                {
                    "monitor" => monitor.WaitForDisposalAsync(),
                    "registration" => registration!.WaitForDisposalAsync(),
                    _ => scope.Service.WaitForDisposalAsync()
                };
            }
            catch (Exception error) { observed = error; }
        }
        var owner = new Owner
        {
            DuringAcquisition = phase == "acquisition" ? TryOwnWait : null,
            Cleanup = () =>
            {
                if (phase == "cleanup") { TryOwnWait(); }
                return ValueTask.CompletedTask;
            }
        };
        registration = scope.Register(monitor, owner);
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, attempts);
        Assert.IsNotNull(observed);
        Assert.AreEqual(typeof(InvalidOperationException), observed.GetType());
        Assert.IsNull(incorrectlyAdmittedWait, "A self-containing wait must be rejected before an incomplete task is handed to its owner.");
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Evaluations);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);
    }

    [TestMethod]
    [DataRow("registration")]
    [DataRow("monitor")]
    [DataRow("runner")]
    [DataRow("service")]
    public async Task AChildContextCapturedByTheOwnerCanDisposeAfterTheActualOperationSettles(string barrier)
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Monitor();
        IHealthCollectionRegistration? registration = null;
        var childEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? child = null;
        int completed = 0;
        var owner = new Owner
        {
            Evaluate = _ =>
            {
                child = Task.Run(async () =>
                {
                    childEntered.TrySetResult();
                    await releaseChild.Task;
                    switch (barrier)
                    {
                        case "registration": await registration!.DisposeAsync(); break;
                        case "monitor": await monitor.DisposeAsync(); break;
                        case "runner": await scope.Runner.StopAsync(); break;
                        default: await scope.Service.DisposeAsync(); break;
                    }
                    Interlocked.Increment(ref completed);
                });
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
        };
        registration = scope.Register(monitor, owner);
        try
        {
            await scope.Runner.StartAsync().WaitAsync(Deadline);
            HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);
            await childEntered.Task.WaitAsync(Deadline);
            Assert.IsNotNull(child);
            Assert.IsFalse(child.IsCompleted);
            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);

            releaseChild.TrySetResult();
            await child.WaitAsync(Deadline);

            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
        }
        finally
        {
            releaseChild.TrySetResult();
            if (child is not null) { await child.WaitAsync(Deadline); }
        }
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IHealthMonitor> monitors = [];
        private readonly List<IHealthCollectionRegistration> registrations = [];
        internal Scope()
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 22, 18, 0, 0, TimeSpan.Zero));
            Service = new HealthcheckService(new(), new() { Name = "collector execution boundaries", InstanceId = Guid.NewGuid() }, clock);
            Runner = new HealthCollectorRunner(Service);
        }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal IConsecutiveFailureMonitor Monitor()
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            monitors.Add(monitor);
            return monitor;
        }
        internal IHealthCollectionRegistration Register(IHealthMonitor monitor, IHealthCollector owner)
        {
            IHealthCollectionRegistration registration = Runner.Register(monitor, owner, new());
            registrations.Add(registration);
            return registration;
        }
        internal HealthMonitorSnapshot Snapshot(IHealthMonitor monitor) => Service.CaptureSnapshot().Monitors.Single(item => item.MonitorId == monitor.MonitorId);
        public async ValueTask DisposeAsync()
        {
            foreach (IHealthCollectionRegistration registration in registrations) { registration.Withdraw(); }
            await Runner.DisposeAsync().AsTask().WaitAsync(Deadline);
            await Service.DisposeAsync().AsTask().WaitAsync(Deadline);
            GC.KeepAlive(monitors);
        }
    }

    private sealed class Owner : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal Action? DuringAcquisition { get; init; }
        internal Func<CancellationToken, Task<HealthCollectionResult>>? Evaluate { get; init; }
        internal Func<ValueTask>? Cleanup { get; init; }
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            DuringAcquisition?.Invoke();
            lease = new Lease(this);
            return true;
        }
        private sealed class Lease(Owner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.evaluations);
                return owner.Evaluate?.Invoke(lifetimeCancellation)
                    ?? Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.cleanups);
                return owner.Cleanup?.Invoke() ?? ValueTask.CompletedTask;
            }
        }
    }
}
