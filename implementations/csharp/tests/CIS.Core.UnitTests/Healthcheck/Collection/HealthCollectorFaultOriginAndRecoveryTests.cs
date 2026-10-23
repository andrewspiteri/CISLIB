using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorFaultOriginAndRecoveryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("missing-task")]
    [DataRow("evaluation")]
    [DataRow("unrequested-cancellation")]
    public async Task EvaluationFaultOriginIsDetachedAndAReleasedLeaseDoesNotPreventTheNextRealCollection(string fault)
    {
        var scope = new Scope { Fault = fault };
        Exception? primary = null;
        try
        {
            await scope.Runner.StartAsync().WaitAsync(Deadline);

            HealthCollectionAttempt failed = await scope.Registration.CollectAsync().WaitAsync(Deadline);

            Assert.AreEqual(HealthCollectionDisposition.Committed, failed.Disposition);
            Assert.IsTrue(failed.EvaluationFailed);
            Assert.IsNotNull(failed.Diagnostic);
            Assert.AreEqual("Healthcheck.CollectionEvaluation", failed.Diagnostic.Code);
            Assert.AreEqual(fault switch
            {
                "missing-task" => typeof(InvalidOperationException).FullName,
                "evaluation" => typeof(OwnerEvaluationException).FullName,
                _ => typeof(OperationCanceledException).FullName
            }, failed.Diagnostic.ExceptionType);
            Assert.IsFalse(scope.Owner.CancellationWasRequested,
                "An owner's unrelated cancellation exception is an evaluation failure, not lifetime retirement.");
            Assert.AreEqual(1, scope.Owner.Acquisitions);
            Assert.AreEqual(1, scope.Owner.Evaluations);
            Assert.AreEqual(1, scope.Owner.Cleanups);
            HealthMonitorSnapshot retained = scope.Snapshot();
            Assert.AreSame(failed.Diagnostic, retained.Diagnostic);
            Assert.AreSame(failed.Diagnostic, retained.Outcomes!.LastFailureDiagnostic);
            Assert.AreSame(failed.Diagnostic, retained.Collection!.LastError);
            Assert.AreEqual(1L, retained.Outcomes.Failed);
            Assert.AreEqual(0L, retained.Outcomes.Successful);
            Assert.IsFalse(retained.Collection.InProgress);

            scope.Fault = null;
            scope.Clock.Advance(TimeSpan.FromSeconds(1));
            HealthCollectionAttempt recovered = await scope.Registration.CollectAsync().WaitAsync(Deadline);

            Assert.AreEqual(HealthCollectionDisposition.Committed, recovered.Disposition);
            Assert.IsFalse(recovered.EvaluationFailed);
            Assert.IsNull(recovered.Diagnostic);
            Assert.AreEqual(2, scope.Owner.Acquisitions);
            Assert.AreEqual(2, scope.Owner.Evaluations);
            Assert.AreEqual(2, scope.Owner.Cleanups);
            HealthMonitorSnapshot current = scope.Snapshot();
            Assert.AreEqual(1L, current.Outcomes!.Failed);
            Assert.AreEqual(1L, current.Outcomes.Successful);
            Assert.AreEqual(MonitorState.Healthy, current.State);
            Assert.IsNull(current.Diagnostic);
            Assert.AreEqual(Epoch.AddSeconds(1), current.LastObservationAtUtc);
            Assert.AreSame(failed.Diagnostic, retained.Diagnostic);
            Assert.AreEqual(0L, retained.Outcomes.Successful);
            Assert.AreEqual(Epoch, retained.LastObservationAtUtc);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await scope.DrainAsync(primary); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LeaseCleanupKeepsItsOwnExceptionOriginWithoutReplacingTheAcceptedEvaluationOutcome(bool evaluationFails)
    {
        var scope = new Scope { Fault = evaluationFails ? "evaluation" : null, CleanupFails = true };
        Exception? primary = null;
        try
        {
            await scope.Runner.StartAsync().WaitAsync(Deadline);

            HealthCollectionAttempt attempt = await scope.Registration.CollectAsync().WaitAsync(Deadline);

            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.AreEqual(evaluationFails, attempt.EvaluationFailed);
            HealthMonitorSnapshot snapshot = scope.Snapshot();
            Assert.AreEqual(evaluationFails ? 0L : 1L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(evaluationFails ? 1L : 0L, snapshot.Outcomes.Failed);
            Assert.AreEqual("Healthcheck.LeaseCleanup", snapshot.Collection!.LastError!.Code);
            Assert.AreEqual(typeof(OwnerCleanupException).FullName, snapshot.Collection.LastError.ExceptionType);
            Assert.AreEqual(Epoch, snapshot.Collection.LastErrorAtUtc);
            Assert.AreEqual(Epoch, snapshot.Collection.LastCompletedAtUtc);
            Assert.IsFalse(snapshot.Collection.InProgress);
            if (evaluationFails)
            {
                Assert.AreEqual("Healthcheck.CollectionEvaluation", attempt.Diagnostic!.Code);
                Assert.AreEqual(typeof(OwnerEvaluationException).FullName, attempt.Diagnostic.ExceptionType);
                Assert.AreSame(attempt.Diagnostic, snapshot.Diagnostic);
                Assert.AreSame(attempt.Diagnostic, snapshot.Outcomes.LastFailureDiagnostic);
                Assert.AreNotSame(attempt.Diagnostic, snapshot.Collection.LastError);
            }
            else
            {
                Assert.IsNull(attempt.Diagnostic);
                Assert.IsNull(snapshot.Diagnostic);
                Assert.IsNull(snapshot.Outcomes.LastFailureDiagnostic);
            }
            Assert.AreEqual(1, scope.Owner.Acquisitions);
            Assert.AreEqual(1, scope.Owner.Evaluations);
            Assert.AreEqual(1, scope.Owner.Cleanups);

            scope.Fault = null;
            scope.CleanupFails = false;
            HealthCollectionAttempt next = await scope.Registration.CollectAsync().WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, next.Disposition);
            Assert.IsFalse(next.EvaluationFailed);
            Assert.IsNull(next.Diagnostic);
            Assert.AreEqual(2, scope.Owner.Acquisitions);
            Assert.AreEqual(2, scope.Owner.Evaluations);
            Assert.AreEqual(2, scope.Owner.Cleanups);
            Assert.AreEqual(evaluationFails ? 1L : 2L, scope.Snapshot().Outcomes!.Successful);
            Assert.AreEqual(evaluationFails ? 1L : 0L, scope.Snapshot().Outcomes!.Failed);
            Assert.AreEqual(typeof(OwnerCleanupException).FullName, snapshot.Collection.LastError.ExceptionType);
        }
        catch (Exception error) { primary = error; throw; }
        finally { await scope.DrainAsync(primary); }
    }

    private sealed class Scope
    {
        internal Scope()
        {
            Service = new(new(), new() { Name = "collection fault origin and recovery", InstanceId = Guid.NewGuid() }, Clock);
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            Runner = new(Service);
            Owner = new(this);
            Registration = Runner.Register(Monitor, Owner, new());
        }
        internal string? Fault { get; set; }
        internal bool CleanupFails { get; set; }
        internal ManualTimeProvider Clock { get; } = new(Epoch);
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal HealthCollectorRunner Runner { get; }
        internal Owner Owner { get; }
        internal IHealthCollectionRegistration Registration { get; }
        internal HealthMonitorSnapshot Snapshot() => Service.CaptureSnapshot().Monitors.Single();

        internal async Task DrainAsync(Exception? primary)
        {
            // Start all actual drains even when one cannot complete. The watchdog
            // remains a real timeout and never substitutes for owner settlement.
            Task runner = DrainRunnerAsync();
            Task registration = DrainRegistrationAsync();
            Task state = DrainStateAsync();
            Task drains = Task.WhenAll(runner, registration, state);
            try { await drains.WaitAsync(Deadline); }
            catch (Exception cleanup) when (primary is not null)
            { throw new AggregateException("The collection assertions and actual cleanup both failed.", primary, drains.Exception ?? cleanup); }
            catch (Exception cleanup) when (cleanup is not TimeoutException && drains.Exception is not null)
            { throw drains.Exception; }
            finally { GC.KeepAlive(Monitor); }

            async Task DrainRunnerAsync() => await Runner.DisposeAsync();
            async Task DrainRegistrationAsync() => await Registration.DisposeAsync();
            async Task DrainStateAsync() => await Service.DisposeAsync();
        }
    }

    private sealed class Owner(Scope scope) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal bool CancellationWasRequested { get; private set; }
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = new Lease(this, scope.Fault, scope.CleanupFails);
            return true;
        }

        private sealed class Lease(Owner owner, string? fault, bool cleanupFails) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                Interlocked.Increment(ref owner.evaluations);
                owner.CancellationWasRequested = lifetimeCancellation.IsCancellationRequested;
                return fault switch
                {
                    "missing-task" => null!,
                    "evaluation" => Task.FromException<HealthCollectionResult>(new OwnerEvaluationException()),
                    "unrequested-cancellation" => Task.FromException<HealthCollectionResult>(new OperationCanceledException()),
                    _ => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded))
                };
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.cleanups);
                return cleanupFails ? ValueTask.FromException(new OwnerCleanupException()) : ValueTask.CompletedTask;
            }
        }
    }

    private sealed class OwnerEvaluationException : Exception;
    private sealed class OwnerCleanupException : Exception;
}
