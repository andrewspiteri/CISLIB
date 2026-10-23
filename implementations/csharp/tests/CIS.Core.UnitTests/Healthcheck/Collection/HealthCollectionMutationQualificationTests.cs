using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionMutationQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CollectionIdentitiesAreUniqueAcrossDifferentActiveMonitorsAndReusableAfterRetirement()
    {
        await using var scope = new Scope();
        IConditionMonitor first = scope.Condition();
        IConditionMonitor second = scope.Condition();
        Guid registration = Guid.NewGuid();
        HealthCollectionTarget original = scope.Attach(first, registration);
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Attach(second, registration));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsNull(scope.Snapshot(second).Collection);
        await scope.State.WithdrawCollection(original).WaitAsync(TimeSpan.FromSeconds(10));
        HealthCollectionTarget replacement = scope.Attach(second, registration);
        Assert.AreEqual(registration, replacement.CollectionRegistrationId);
        Assert.AreEqual(second.MonitorId, replacement.MonitorId);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.State.TryReserve(original).Disposition);
        Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, scope.Reserve(replacement).Disposition);
    }

    [TestMethod]
    public async Task AttachingReservingPublishingAndRetiringEachAdvanceTheObservableRevision()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        HealthSnapshot initial = scope.Service.CaptureSnapshot();
        HealthCollectionTarget target = scope.Attach(monitor, required: false);
        HealthSnapshot attached = scope.Service.CaptureSnapshot();
        Assert.IsTrue(attached.Revision > initial.Revision);
        HealthCollectionReservation operation = scope.Reserve(target).Reservation!;
        HealthSnapshot reserved = scope.Service.CaptureSnapshot();
        Assert.IsTrue(reserved.Revision > attached.Revision);
        Assert.IsFalse(attached.Monitors.Single().Collection!.InProgress);
        Assert.IsTrue(reserved.Monitors.Single().Collection!.InProgress);
        Assert.IsTrue(scope.State.TryEnter(operation));
        HealthCollectionAttempt committed = scope.State.CommitResult(operation,
            HealthCollectionResult.Diagnostics([HealthMeasurement.Integer("published", 9, Epoch)]));
        HealthSnapshot published = scope.Service.CaptureSnapshot();
        Assert.AreEqual(HealthCollectionDisposition.Committed, committed.Disposition);
        Assert.IsFalse(committed.EvaluationFailed);
        Assert.IsTrue(published.Revision > reserved.Revision);
        Assert.HasCount(0, reserved.Monitors.Single().Measurements);
        Assert.AreEqual(9L, published.Monitors.Single().Measurements.Single().IntegerValue);
        scope.Complete(operation);
        HealthSnapshot settled = scope.Service.CaptureSnapshot();
        Assert.IsTrue(settled.Revision > published.Revision);
        await scope.State.WithdrawCollection(target).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(scope.Service.CaptureSnapshot().Revision > settled.Revision);
        Assert.IsTrue(published.Monitors.Single().Collection!.InProgress);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactRetainedMetricCapacityAllowsUpdatesAndOneNewKeyWithoutAnError(bool required)
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        for (int index = 0; index < 31; index++) { monitor.ReportMeasurement(HealthMeasurement.Integer($"key-{index:D2}", index, Epoch)); }
        HealthCollectionReservation operation = scope.Reserve(scope.Attach(monitor, required: required)).Reservation!;
        Assert.IsTrue(scope.State.TryEnter(operation));
        HealthMeasurement[] values = [HealthMeasurement.Integer("key-00", 101, Epoch.AddMinutes(1)), HealthMeasurement.Integer("last-key", 102, Epoch.AddMinutes(2))];

        HealthCollectionAttempt attempt = scope.State.CommitResult(operation, required
            ? HealthCollectionResult.Condition(true).WithMeasurements(values) : HealthCollectionResult.Diagnostics(values));

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
        HealthMonitorSnapshot snapshot = scope.Snapshot(monitor);
        Assert.HasCount(32, snapshot.Measurements);
        Assert.AreEqual(101L, snapshot.Measurements.Single(value => value.Key == "key-00").IntegerValue);
        Assert.AreEqual(102L, snapshot.Measurements.Single(value => value.Key == "last-key").IntegerValue);
        Assert.IsNull(snapshot.Collection!.LastError);
        Assert.AreEqual(required ? MonitorState.Unhealthy : MonitorState.Healthy, snapshot.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectedMetricBatchReportsEvaluationFailureOnlyWhenThereIsNoAcceptedRequiredHealthFact(bool required)
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        for (int index = 0; index < 32; index++) { monitor.ReportMeasurement(HealthMeasurement.Integer($"key-{index:D2}", index, Epoch)); }
        HealthCollectionReservation operation = scope.Reserve(scope.Attach(monitor, required: required)).Reservation!;
        Assert.IsTrue(scope.State.TryEnter(operation));
        HealthMeasurement[] values = [HealthMeasurement.Integer("key-00", 101, Epoch.AddMinutes(1)), HealthMeasurement.Integer("overflow", 102, Epoch.AddMinutes(2))];

        HealthCollectionAttempt attempt = scope.State.CommitResult(operation, required
            ? HealthCollectionResult.Condition(true).WithMeasurements(values) : HealthCollectionResult.Diagnostics(values));

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.AreEqual(!required, attempt.EvaluationFailed);
        Assert.AreEqual("Healthcheck.MeasurementCapacity", attempt.Diagnostic!.Code);
        HealthMonitorSnapshot snapshot = scope.Snapshot(monitor);
        Assert.HasCount(32, snapshot.Measurements);
        Assert.AreEqual(0L, snapshot.Measurements.Single(value => value.Key == "key-00").IntegerValue);
        Assert.IsFalse(snapshot.Measurements.Any(value => value.Key == "overflow"));
        Assert.AreSame(attempt.Diagnostic, snapshot.Collection!.LastError);
        Assert.AreEqual(required ? MonitorState.Unhealthy : MonitorState.Healthy, snapshot.State);
    }

    [TestMethod]
    public async Task EmptyOptionalEvaluationStillCommitsWithoutCreatingAHealthObservation()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        HealthCollectionReservation operation = scope.Reserve(scope.Attach(monitor, required: false)).Reservation!;
        Assert.IsTrue(scope.State.TryEnter(operation));

        HealthCollectionAttempt result = scope.State.CommitResult(operation, HealthCollectionResult.Diagnostics([]));

        Assert.AreEqual(HealthCollectionDisposition.Committed, result.Disposition);
        Assert.IsFalse(result.EvaluationFailed);
        Assert.IsNull(result.Diagnostic);
        Assert.AreEqual(MonitorState.Pending, scope.Snapshot(monitor).State);
        Assert.IsNull(scope.Snapshot(monitor).LastObservationAtUtc);
        Assert.AreEqual(0L, scope.Snapshot(monitor).Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot(monitor).Outcomes!.Failed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TheFirstAcceptedEvaluationRemainsExactlyOnceUntilActualCompletion(bool firstIsFailure)
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        HealthCollectionReservation operation = scope.Reserve(scope.Attach(monitor)).Reservation!;
        Assert.IsTrue(scope.State.TryEnter(operation));
        var firstDiagnostic = new HealthDiagnostic("FirstEvaluation", "actual first result");
        HealthCollectionAttempt first = firstIsFailure
            ? scope.State.CommitEvaluationFailure(operation, firstDiagnostic)
            : scope.State.CommitResult(operation, HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        HealthSnapshot retained = scope.Service.CaptureSnapshot();

        HealthCollectionAttempt second = scope.State.CommitResult(operation, HealthCollectionResult.Execution(HealthExecutionOutcome.Failed));
        HealthCollectionAttempt third = scope.State.CommitEvaluationFailure(operation, new HealthDiagnostic("LaterEvaluation"));

        Assert.AreEqual(first.Disposition, second.Disposition);
        Assert.AreEqual(first.Disposition, third.Disposition);
        Assert.AreEqual(first.EvaluationFailed, second.EvaluationFailed);
        Assert.AreEqual(first.EvaluationFailed, third.EvaluationFailed);
        Assert.AreSame(first.Diagnostic, second.Diagnostic);
        Assert.AreSame(first.Diagnostic, third.Diagnostic);
        Assert.AreEqual(retained.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(firstIsFailure ? 1L : 0L, scope.Snapshot(monitor).Outcomes!.Failed);
        Assert.AreEqual(firstIsFailure ? 0L : 1L, scope.Snapshot(monitor).Outcomes!.Successful);
        Assert.AreEqual(firstIsFailure ? MonitorState.Unhealthy : MonitorState.Healthy, scope.Snapshot(monitor).State);
        HealthCollectionAttempt completion = scope.State.Complete(operation, new(HealthCollectionCompletionReason.Settled, Epoch.AddSeconds(1)));
        Assert.AreEqual(first.EvaluationFailed, completion.EvaluationFailed);
        Assert.AreSame(first.Diagnostic, completion.Diagnostic);
    }

    [TestMethod]
    public async Task StaleRequiredEvaluationErrorRetainsItsFailureClassificationWithoutReplacingTheNewerObservation()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        HealthCollectionReservation operation = scope.Reserve(scope.Attach(monitor)).Reservation!;
        Assert.IsTrue(scope.State.TryEnter(operation));
        monitor.ReportSuccess();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        var reason = new HealthDiagnostic("ObsoleteEvaluation");

        HealthCollectionAttempt result = scope.State.CommitEvaluationFailure(operation, reason);

        Assert.AreEqual(HealthCollectionDisposition.Obsolete, result.Disposition);
        Assert.IsTrue(result.EvaluationFailed);
        Assert.AreSame(reason, result.Diagnostic);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(1L, scope.Snapshot(monitor).Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot(monitor).Outcomes!.Failed);
        Assert.IsNull(scope.Snapshot(monitor).Collection!.LastError);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvocationIsSingleEntryAndCompletionRejectsLateUnacceptedResults(bool entered)
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation operation = scope.Reserve(scope.Attach(monitor)).Reservation!;
        if (entered)
        {
            Assert.IsTrue(scope.State.TryEnter(operation));
            Assert.IsFalse(scope.State.TryEnter(operation));
        }
        else
        {
            HealthCollectionAttempt early = scope.State.CommitResult(operation, HealthCollectionResult.Condition(true));
            AssertRetired(early);
            AssertRetired(scope.State.CommitEvaluationFailure(operation, new HealthDiagnostic("BeforeEntry")));
        }
        HealthCollectionAttempt completion = scope.State.Complete(operation,
            new(entered ? HealthCollectionCompletionReason.Settled : HealthCollectionCompletionReason.RetiredBeforeInvocation, Epoch.AddSeconds(7)));
        Assert.AreEqual(entered ? HealthCollectionDisposition.Committed : HealthCollectionDisposition.Retired, completion.Disposition);
        Assert.IsFalse(completion.EvaluationFailed);
        Assert.IsNull(completion.Diagnostic);
        HealthSnapshot beforeLateFacts = scope.Service.CaptureSnapshot();
        Assert.IsFalse(scope.State.TryEnter(operation));
        AssertRetired(scope.State.CommitResult(operation, HealthCollectionResult.Condition(true)));
        AssertRetired(scope.State.CommitEvaluationFailure(operation, new HealthDiagnostic("AfterCompletion")));
        scope.State.Complete(operation, new(HealthCollectionCompletionReason.OwnerUnavailable, Epoch.AddYears(1)));
        Assert.AreEqual(beforeLateFacts.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(MonitorState.Healthy, scope.Snapshot(monitor).State);
        Assert.AreEqual(Epoch.AddSeconds(7), scope.Snapshot(monitor).Collection!.LastCompletedAtUtc);
    }

    [TestMethod]
    public async Task RealForeignTargetsAndReservationsCannotMutateAnotherService()
    {
        await using var local = new Scope();
        await using var foreign = new Scope();
        IConditionMonitor localMonitor = local.Condition();
        IConditionMonitor foreignMonitor = foreign.Condition();
        HealthCollectionTarget target = foreign.Attach(foreignMonitor);
        HealthCollectionReservation operation = foreign.Reserve(target).Reservation!;
        HealthSnapshot before = local.Service.CaptureSnapshot();

        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, local.State.TryReserve(target).Disposition);
        Assert.IsFalse(local.State.TryEnter(operation));
        AssertRetired(local.State.CommitResult(operation, HealthCollectionResult.Condition(true)));
        AssertRetired(local.State.CommitEvaluationFailure(operation, new HealthDiagnostic("Foreign")));
        AssertRetired(local.State.Complete(operation, new(HealthCollectionCompletionReason.OwnerUnavailable, Epoch)));
        Task foreignSettlement = local.State.WaitForSettlementAsync(operation);
        Assert.IsTrue(foreignSettlement.IsCompletedSuccessfully,
            "A foreign reservation has no settlement work in the receiving state.");
        await foreignSettlement;
        Task foreignWithdrawal = local.State.WithdrawCollection(target);
        Assert.IsTrue(foreignWithdrawal.IsCompletedSuccessfully,
            "A foreign target has no withdrawal work in the receiving state.");
        await foreignWithdrawal;

        Assert.AreEqual(before.Revision, local.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(MonitorState.Healthy, local.Snapshot(localMonitor).State);
        Assert.IsFalse(target.Retirement.IsCancellationRequested);
        Assert.IsTrue(foreign.State.TryEnter(operation));
        Assert.AreEqual(HealthCollectionDisposition.Committed,
            foreign.State.CommitResult(operation, HealthCollectionResult.Condition(true)).Disposition);
    }

    [TestMethod]
    public async Task InvalidStateInputsFailBeforeChangingARealAttachmentOrOperation()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), null!));
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation operation = scope.Reserve(target).Reservation!;
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.TryReserve(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.TryEnter(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.CommitResult(null!, HealthCollectionResult.Condition(true)));
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.CommitEvaluationFailure(operation, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.Complete(operation, null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scope.State.Complete(operation, new((HealthCollectionCompletionReason)999, Epoch)));
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.WithdrawCollection(null!));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsTrue(scope.State.TryEnter(operation));
        Assert.AreEqual(HealthCollectionDisposition.Committed, scope.State.CommitResult(operation, HealthCollectionResult.Condition(true)).Disposition);
    }

    private static void AssertRetired(HealthCollectionAttempt result)
    {
        Assert.AreEqual(HealthCollectionDisposition.Retired, result.Disposition);
        Assert.IsFalse(result.EvaluationFailed);
        Assert.IsNull(result.Diagnostic);
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<IHealthMonitor> monitors = [];
        private readonly List<HealthCollectionReservation> reservations = [];
        internal HealthcheckService Service { get; } = new(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "collection qualification", InstanceId = Guid.NewGuid() }, new ManualTimeProvider(Epoch));
        internal IHealthCollectionState State => ((IHealthStateAccess)Service).CollectionState;
        internal IConditionMonitor Condition()
        {
            IConditionMonitor monitor = Service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = false });
            monitors.Add(monitor);
            return monitor;
        }
        internal IConsecutiveFailureMonitor Failures()
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 });
            monitors.Add(monitor);
            return monitor;
        }
        internal HealthCollectionTarget Attach(IHealthMonitor monitor, Guid? registration = null, bool required = true) => State.RegisterCollection(monitor.MonitorId, registration ?? Guid.NewGuid(), new HealthCollectorOptions { IsRequired = required });
        internal HealthCollectionReservationResult Reserve(HealthCollectionTarget target)
        {
            HealthCollectionReservationResult result = State.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, result.Disposition);
            Assert.IsNotNull(result.Reservation);
            reservations.Add(result.Reservation);
            return result;
        }
        internal void Complete(HealthCollectionReservation operation) => State.Complete(operation, new(HealthCollectionCompletionReason.Settled, Epoch));
        internal HealthMonitorSnapshot Snapshot(IHealthMonitor monitor) => Service.CaptureSnapshot().Monitors.Single(value => value.MonitorId == monitor.MonitorId);
        public async ValueTask DisposeAsync()
        {
            foreach (HealthCollectionReservation operation in reservations) { Complete(operation); }
            await Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            GC.KeepAlive(monitors);
        }
    }
}
