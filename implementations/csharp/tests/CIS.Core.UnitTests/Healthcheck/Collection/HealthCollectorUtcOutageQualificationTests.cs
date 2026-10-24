using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorUtcOutageQualificationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AnActualUtcProviderOutageCannotStrandTheLeaseOrInventAPolicyOutcome()
    {
        // These resource-free public prerequisites use representative exceptions.
        // Actual unavailable UTC reads below still allocate their own new errors.
        HealthDiagnostic? evaluationProjection = null;
        ArgumentException? evaluationRejection = null;
        try { evaluationProjection = HealthDiagnostic.FromException(new UtcUnavailableException(), "Healthcheck.CollectionEvaluation"); }
        catch (ArgumentException error) { evaluationRejection = error; }
        Assert.IsNull(evaluationRejection, "The valid UTC outage error must support the evaluation diagnostic before dispatch.");
        Assert.IsNotNull(evaluationProjection);
        Assert.AreEqual("Healthcheck.CollectionEvaluation", evaluationProjection.Code);
        Assert.AreEqual(typeof(UtcUnavailableException).FullName, evaluationProjection.ExceptionType);

        HealthDiagnostic? integrationProjection = null;
        ArgumentException? integrationRejection = null;
        try { integrationProjection = HealthDiagnostic.FromException(new UtcUnavailableException(), "Healthcheck.CollectionIntegration"); }
        catch (ArgumentException error) { integrationRejection = error; }
        Assert.IsNull(integrationRejection, "The valid UTC outage error must support the integration diagnostic before dispatch.");
        Assert.IsNotNull(integrationProjection);
        Assert.AreEqual("Healthcheck.CollectionIntegration", integrationProjection.Code);
        Assert.AreEqual(typeof(UtcUnavailableException).FullName, integrationProjection.ExceptionType);

        var clock = new OutageClock();
        var service = new HealthcheckService(new() { MaxConcurrentCollections = 1 },
            new HealthApplicationIdentity { Name = "collection UTC provider outage", InstanceId = Guid.NewGuid() }, clock);
        var runner = new HealthCollectorRunner(service);
        var owner = new ResourceOwner(clock);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new()
        {
            IsCritical = true,
            FailureThreshold = 1
        });
        try
        {
            IHealthCollectionRegistration registration = runner.Register(monitor, owner, new());
            HealthSnapshot before = service.CaptureSnapshot();
            await runner.StartAsync().WaitAsync(Deadline);

            // The owner returns a successful real evaluation. The provider outage
            // begins inside that callback and remains in force for both state
            // publication paths and the completion UTC request. Monotonic time,
            // timers, the service's state and its ownership handles are untouched.
            HealthCollectionAttempt completed;
            try { completed = await registration.CollectAsync().WaitAsync(Deadline); }
            finally { clock.Restore(); }

            Assert.AreEqual(HealthCollectionDisposition.Committed, completed.Disposition);
            Assert.IsFalse(completed.EvaluationFailed);
            Assert.IsNull(completed.Diagnostic);
            Assert.IsTrue(clock.UtcFailures >= 2);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.IsFalse(owner.LastResource!.CanWrite, "The actually acquired resource must have been disposed before its collection task completes.");

            HealthMonitorSnapshot observed = service.CaptureSnapshot().Monitors.Single();
            Assert.AreEqual(MonitorState.Pending, observed.State);
            Assert.IsNull(observed.LastObservationAtUtc);
            Assert.AreEqual(0L, observed.Outcomes!.Successful);
            Assert.AreEqual(0L, observed.Outcomes.Failed);
            Assert.IsFalse(observed.Collection!.InProgress);
            Assert.IsNotNull(observed.Collection.LastCompletedAtUtc);
            Assert.AreEqual(observed.Collection.LastCompletedAtUtc, observed.Collection.LastErrorAtUtc);
            Assert.IsNotNull(observed.Collection.LastError);
            Assert.AreEqual("Healthcheck.CollectionIntegration", observed.Collection.LastError.Code,
                "An unavailable state-publication dependency must retain its integration category instead of being replaced by a secondary completion-clock error.");
            Assert.AreEqual(typeof(UtcUnavailableException).FullName, observed.Collection.LastError.ExceptionType);
            Assert.IsNull(before.Monitors.Single().Collection!.LastError);
            Assert.AreEqual(0L, before.Monitors.Single().Outcomes!.Successful);

            // A subsequent real acquisition also demonstrates that the sole
            // runner slot and state operation were released, without waiting on
            // a canceled caller or treating a timeout as successful settlement.
            HealthCollectionAttempt recovered = await registration.CollectAsync().WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Committed, recovered.Disposition);
            Assert.IsFalse(recovered.EvaluationFailed);
            Assert.AreEqual(2, owner.Acquisitions);
            Assert.AreEqual(2, owner.Evaluations);
            Assert.AreEqual(2, owner.Cleanups);
            Assert.IsFalse(owner.LastResource!.CanWrite);
            HealthMonitorSnapshot afterRecovery = service.CaptureSnapshot().Monitors.Single();
            Assert.AreEqual(MonitorState.Healthy, afterRecovery.State);
            Assert.AreEqual(1L, afterRecovery.Outcomes!.Successful);
            Assert.AreEqual(0L, afterRecovery.Outcomes.Failed);
            Assert.IsFalse(afterRecovery.Collection!.InProgress);
            Assert.AreEqual("Healthcheck.CollectionIntegration", afterRecovery.Collection.LastError!.Code);
        }
        finally
        {
            clock.Restore();
            try { await runner.DisposeAsync().AsTask().WaitAsync(Deadline); }
            finally
            {
                try { await service.DisposeAsync().AsTask().WaitAsync(Deadline); }
                finally { GC.KeepAlive(monitor); }
            }
        }
    }

    private sealed class OutageClock : TimeProvider
    {
        private int unavailable, failures;
        internal int UtcFailures => Volatile.Read(ref failures);
        internal void BeginOutage() => Volatile.Write(ref unavailable, 1);
        internal void Restore() => Volatile.Write(ref unavailable, 0);
        public override DateTimeOffset GetUtcNow()
        {
            if (Volatile.Read(ref unavailable) != 0)
            {
                Interlocked.Increment(ref failures);
                throw new UtcUnavailableException();
            }
            return Epoch;
        }
    }

    private sealed class ResourceOwner(OutageClock clock) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal MemoryStream? LastResource { get; private set; }
        private void BeginOutage() => clock.BeginOutage();
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            int ordinal = Interlocked.Increment(ref acquisitions);
            var resource = new MemoryStream();
            LastResource = resource;
            lease = new Lease(this, resource, ordinal);
            return true;
        }
        private sealed class Lease(ResourceOwner owner, MemoryStream resource, int ordinal) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                lifetimeCancellation.ThrowIfCancellationRequested();
                resource.WriteByte(0x45);
                Interlocked.Increment(ref owner.evaluations);
                if (ordinal == 1) { owner.BeginOutage(); }
                return Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            }
            public ValueTask DisposeAsync()
            {
                resource.Dispose();
                Interlocked.Increment(ref owner.cleanups);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class UtcUnavailableException : Exception;
}
