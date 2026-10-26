using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionSettlementMilestoneTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow("settled")]
    [DataRow("before-invocation")]
    [DataRow("acquisition-fault")]
    [DataRow("cleanup-fault")]
    public void CompletionWithoutOutstandingCallbacksSignalsThePreviouslyCapturedOperationBarrier(string mode)
    {
        using var scope = new Scope();
        HealthCollectionTarget target = scope.Attach();
        HealthCollectionReservation operation = scope.Reserve(target);
        Task settlement = scope.State.WaitForSettlementAsync(operation);
        Assert.IsFalse(settlement.IsCompleted);
        Assert.IsTrue(scope.Snapshot().Collection!.InProgress);
        Assert.AreSame(settlement, scope.State.WaitForSettlementAsync(operation));
        bool evaluated = mode is "settled" or "cleanup-fault";
        if (evaluated)
        {
            Assert.IsTrue(scope.State.TryEnter(operation));
            scope.State.CommitResult(operation, HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        }
        HealthDiagnostic? acquisitionError = null;
        if (mode == "acquisition-fault")
        {
            ArgumentException? acquisitionRejection = null;
            try { acquisitionError = new HealthDiagnostic("Owner.Acquisition", "A bounded acquisition fault."); }
            catch (ArgumentException error) { acquisitionRejection = error; }
            Assert.IsNull(acquisitionRejection, "Valid acquisition diagnostic text must be accepted.");
            Assert.IsNotNull(acquisitionError);
        }
        HealthDiagnostic? cleanupError = null;
        if (mode == "cleanup-fault")
        {
            ArgumentException? cleanupRejection = null;
            try { cleanupError = new HealthDiagnostic("Owner.Cleanup", "A bounded cleanup fault."); }
            catch (ArgumentException error) { cleanupRejection = error; }
            Assert.IsNull(cleanupRejection, "Valid cleanup diagnostic text must be accepted.");
            Assert.IsNotNull(cleanupError);
        }
        DateTimeOffset completedAt = Epoch.AddSeconds(7);

        HealthCollectionAttempt attempt = scope.State.Complete(operation, new(
            mode == "before-invocation" ? HealthCollectionCompletionReason.RetiredBeforeInvocation
                : mode == "acquisition-fault" ? HealthCollectionCompletionReason.LeaseAcquisitionFault
                : HealthCollectionCompletionReason.Settled,
            completedAt, acquisitionError, cleanupError));

        // Capture before Complete: resolving the removed operation afterwards would
        // return an unrelated already-completed task and hide a forgotten signal.
        Assert.IsTrue(settlement.IsCompletedSuccessfully, "A completed wrapper with no outstanding callbacks must release its existing settlement waiter.");
        Assert.AreEqual(mode == "before-invocation" ? HealthCollectionDisposition.Retired : HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        HealthMonitorSnapshot snapshot = scope.Snapshot();
        Assert.IsFalse(snapshot.Collection!.InProgress);
        Assert.AreEqual(completedAt, snapshot.Collection.LastCompletedAtUtc);
        Assert.AreEqual(evaluated ? 1L : 0L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        Assert.AreEqual(evaluated ? MonitorState.Healthy : MonitorState.Pending, snapshot.State);
        Assert.AreEqual((acquisitionError ?? cleanupError)?.Code, snapshot.Collection.LastError?.Code);
        Assert.AreEqual(acquisitionError is not null || cleanupError is not null ? (DateTimeOffset?)completedAt : null, snapshot.Collection.LastErrorAtUtc);
        Assert.IsFalse(target.Retirement.IsCancellationRequested);
        Assert.IsFalse(operation.LifetimeCancellation.IsCancellationRequested);

        HealthCollectionReservation next = scope.Reserve(target);
        Assert.AreNotEqual(operation.OperationId, next.OperationId);
        Assert.IsTrue(scope.State.TryEnter(next));
        Assert.AreEqual(HealthCollectionDisposition.Committed,
            scope.State.CommitResult(next, HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded)).Disposition);
        Task nextSettlement = scope.State.WaitForSettlementAsync(next);
        scope.State.Complete(next, new(HealthCollectionCompletionReason.Settled, completedAt.AddSeconds(1)));
        Assert.IsTrue(nextSettlement.IsCompletedSuccessfully);
        Assert.AreEqual(evaluated ? 2L : 1L, scope.Snapshot().Outcomes!.Successful);
        Assert.IsFalse(scope.Snapshot().Collection!.InProgress);
    }

    [TestMethod]
    [DataRow("collection")]
    [DataRow("monitor")]
    [DataRow("service")]
    public void WithdrawalRetainsTheRealOperationUntilCompleteThenSignalsAllApplicableCapturedBarriers(string lifetime)
    {
        using var scope = new Scope();
        HealthCollectionTarget target = scope.Attach();
        HealthCollectionReservation operation = scope.Reserve(target);
        Assert.IsTrue(scope.State.TryEnter(operation));
        Task settlement = scope.State.WaitForSettlementAsync(operation);
        Task monitorDrain = scope.Monitor.WaitForDisposalAsync();
        Task serviceDrain = scope.Service.WaitForDisposalAsync();

        Task attachmentDrain = scope.Withdraw(target, lifetime);

        Assert.IsTrue(target.Retirement.IsCancellationRequested);
        Assert.IsTrue(operation.LifetimeCancellation.IsCancellationRequested);
        Assert.IsFalse(settlement.IsCompleted);
        Assert.IsFalse(attachmentDrain.IsCompleted);
        Assert.IsFalse(monitorDrain.IsCompleted);
        Assert.IsFalse(serviceDrain.IsCompleted);
        Assert.AreSame(attachmentDrain, scope.State.WithdrawCollection(target));
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(target).Disposition);

        HealthCollectionAttempt result = scope.State.Complete(operation,
            new(HealthCollectionCompletionReason.Settled, Epoch.AddSeconds(2)));

        Assert.AreEqual(HealthCollectionDisposition.Retired, result.Disposition);
        Assert.IsFalse(result.EvaluationFailed);
        Assert.IsTrue(settlement.IsCompletedSuccessfully);
        Assert.IsTrue(attachmentDrain.IsCompletedSuccessfully);
        Assert.AreEqual(lifetime != "collection", monitorDrain.IsCompletedSuccessfully);
        Assert.AreEqual(lifetime == "service", serviceDrain.IsCompletedSuccessfully);
        if (lifetime == "service")
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => scope.Service.CaptureSnapshot());
        }
        else { Assert.AreEqual(lifetime == "collection" ? 1 : 0, scope.Service.CaptureSnapshot().MonitorCounts.Active); }
        Assert.AreEqual(HealthCollectionDisposition.Retired,
            scope.State.CommitResult(operation, HealthCollectionResult.Execution(HealthExecutionOutcome.Failed)).Disposition);
        if (lifetime == "collection")
        {
            Assert.IsFalse(scope.Snapshot().Collection!.InProgress);
            Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
            Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
            scope.Monitor.ReportSuccess();
            Assert.AreEqual(MonitorState.Healthy, scope.Snapshot().State);
        }
    }

    [TestMethod]
    [DataRow("collection")]
    [DataRow("monitor")]
    [DataRow("service")]
    public void IdleAttachmentWithdrawalSettlesSynchronouslyWithoutInventingAnOperation(string lifetime)
    {
        using var scope = new Scope();
        HealthCollectionTarget target = scope.Attach();
        Task monitorDrain = scope.Monitor.WaitForDisposalAsync();
        Task serviceDrain = scope.Service.WaitForDisposalAsync();
        Assert.IsFalse(scope.Snapshot().Collection!.InProgress);

        Task attachmentDrain = scope.Withdraw(target, lifetime);

        Assert.IsTrue(target.Retirement.IsCancellationRequested);
        Assert.IsTrue(attachmentDrain.IsCompletedSuccessfully);
        Assert.AreEqual(lifetime != "collection", monitorDrain.IsCompletedSuccessfully);
        Assert.AreEqual(lifetime == "service", serviceDrain.IsCompletedSuccessfully);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(target).Disposition);
        if (lifetime == "collection")
        {
            Assert.IsFalse(scope.Snapshot().Collection!.InProgress);
            Assert.IsNull(scope.Snapshot().Collection!.LastCompletedAtUtc);
            Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
            Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
            scope.Monitor.ReportSuccess();
            Assert.AreEqual(1L, scope.Snapshot().Outcomes!.Successful);
        }
        else if (lifetime == "service")
        {
            Assert.ThrowsExactly<ObjectDisposedException>(() => scope.Service.CaptureSnapshot());
        }
        else { Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active); }
    }

    [TestMethod]
    public async Task CompletionPublishesOnceWhileActualCancellationStillHoldsTheCapturedSettlementBarrier()
    {
        using var scope = new Scope();
        HealthCollectionTarget target = scope.Attach();
        HealthCollectionReservation operation = scope.Reserve(target);
        Assert.IsTrue(scope.State.TryEnter(operation));
        scope.State.CommitResult(operation, HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        Task settlement = scope.State.WaitForSettlementAsync(operation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        bool callbackReleased = false;
        using CancellationTokenRegistration callback = operation.LifetimeCancellation.Register(() =>
        {
            entered.TrySetResult();
            callbackReleased = release.Wait(Deadline);
        });
        Task stopping = Task.Run(scope.Service.BeginStopping);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            HealthSnapshot beforeCompletion = scope.Service.CaptureSnapshot();
            DateTimeOffset firstAt = Epoch.AddSeconds(4);

            HealthCollectionAttempt first = scope.State.Complete(operation,
                new(HealthCollectionCompletionReason.Settled, firstAt));

            HealthSnapshot completed = scope.Service.CaptureSnapshot();
            Assert.IsTrue(completed.Revision > beforeCompletion.Revision, "Publishing the actual completion timestamp is an accepted state change even while cancellation remains unsettled.");
            Assert.AreEqual(firstAt, completed.Monitors.Single().Collection!.LastCompletedAtUtc);
            Assert.IsTrue(completed.Monitors.Single().Collection!.InProgress);
            Assert.IsFalse(settlement.IsCompleted);
            Assert.IsFalse(stopping.IsCompleted);
            Assert.AreEqual(1L, completed.Monitors.Single().Outcomes!.Successful);
            Assert.AreEqual(0L, completed.Monitors.Single().Outcomes!.Failed);

            HealthDiagnostic? lateDiagnostic = null;
            ArgumentException? lateRejection = null;
            try { lateDiagnostic = new HealthDiagnostic("Late.Completion"); }
            catch (ArgumentException error) { lateRejection = error; }
            Assert.IsNull(lateRejection, "A valid late-completion diagnostic code must be accepted.");
            Assert.IsNotNull(lateDiagnostic);
            HealthCollectionAttempt duplicate = scope.State.Complete(operation,
                new(HealthCollectionCompletionReason.LeaseAcquisitionFault, Epoch.AddDays(1), lateDiagnostic));

            Assert.AreEqual(first.Disposition, duplicate.Disposition);
            Assert.AreEqual(first.EvaluationFailed, duplicate.EvaluationFailed);
            Assert.AreEqual(first.Diagnostic, duplicate.Diagnostic);
            HealthSnapshot afterDuplicate = scope.Service.CaptureSnapshot();
            Assert.AreEqual(completed.Revision, afterDuplicate.Revision);
            Assert.AreEqual(firstAt, afterDuplicate.Monitors.Single().Collection!.LastCompletedAtUtc);
            Assert.IsNull(afterDuplicate.Monitors.Single().Collection!.LastError);
            Assert.IsFalse(settlement.IsCompleted);
        }
        finally
        {
            release.Set();
            await stopping.WaitAsync(Deadline);
        }

        Assert.IsTrue(callbackReleased);
        Assert.IsTrue(settlement.IsCompletedSuccessfully);
        Assert.IsFalse(scope.Snapshot().Collection!.InProgress);
        Assert.AreEqual(1L, scope.Snapshot().Outcomes!.Successful);
    }

    private sealed class Scope : IDisposable
    {
        private readonly List<HealthCollectionReservation> reservations = [];
        internal HealthcheckService Service { get; } = new(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "collection settlement milestones", InstanceId = Guid.NewGuid() },
            new ManualTimeProvider(Epoch));
        internal Scope()
        {
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal IHealthCollectionState State => ((IHealthStateAccess)Service).CollectionState;
        internal HealthCollectionTarget Attach() => State.RegisterCollection(Monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
        internal HealthCollectionReservation Reserve(HealthCollectionTarget target)
        {
            HealthCollectionReservationResult result = State.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, result.Disposition);
            Assert.IsNotNull(result.Reservation);
            reservations.Add(result.Reservation);
            return result.Reservation;
        }
        internal Task Withdraw(HealthCollectionTarget target, string lifetime)
        {
            if (lifetime == "monitor") { Monitor.Withdraw(); }
            else if (lifetime == "service") { _ = Service.DisposeAsync().AsTask(); }
            return State.WithdrawCollection(target);
        }
        internal HealthMonitorSnapshot Snapshot() => Service.CaptureSnapshot().Monitors.Single();
        public void Dispose()
        {
            // These direct receiving-seam cases own no runner, timer, lease or worker.
            // Retire managed bookkeeping without awaiting a broken completion signal;
            // the sole real cancellation callback is released and joined by its test.
            foreach (HealthCollectionReservation operation in reservations)
            {
                State.Complete(operation, new(HealthCollectionCompletionReason.Settled, Epoch));
            }
            Task disposal = Service.DisposeAsync().AsTask();
            if (disposal.IsCompleted) { disposal.GetAwaiter().GetResult(); }
            GC.KeepAlive(Monitor);
        }
    }
}
