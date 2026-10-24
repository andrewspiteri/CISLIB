using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckStateSeamTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task StateAccessUsesStableServiceIdentitiesForEveryLocalHandle()
    {
        await using var scope = new StateScope();
        IConditionMonitor first = scope.Condition();
        IConsecutiveFailureMonitor second = scope.Failures();
        var firstAccess = (IHealthStateAccess)first;
        var secondAccess = (IHealthStateAccess)second;

        Assert.AreSame(scope.Collections, firstAccess.CollectionState);
        Assert.AreSame(scope.Collections, secondAccess.CollectionState);
        Assert.AreSame(scope.Executions, firstAccess.ExecutionState);
        Assert.AreSame(scope.Executions, secondAccess.ExecutionState);
        Assert.AreSame(scope.Collections, ((IHealthStateAccess)scope.Service).CollectionState);
        Assert.AreSame(scope.Executions, ((IHealthStateAccess)scope.Service).ExecutionState);
    }

    [TestMethod]
    public async Task RejectedCollectionOptionsLeaveTheMonitorAvailableForAValidAttachment()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        Guid registrationId = Guid.NewGuid();
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.Throws<ArgumentException>(() => scope.Collections.RegisterCollection(
            monitor.MonitorId, registrationId, new HealthCollectorOptions { Interval = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => scope.Collections.RegisterCollection(
            monitor.MonitorId, Guid.Empty, new HealthCollectorOptions()));
        Assert.Throws<ArgumentException>(() => scope.Collections.RegisterCollection(
            Guid.NewGuid(), registrationId, new HealthCollectorOptions()));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        HealthCollectionTarget target = scope.Collections.RegisterCollection(
            monitor.MonitorId, registrationId, new HealthCollectorOptions { Interval = TimeSpan.FromSeconds(3), IsRequired = false });
        Assert.AreEqual(monitor.MonitorId, target.MonitorId);
        Assert.AreEqual(registrationId, target.CollectionRegistrationId);
        Assert.AreEqual(TimeSpan.FromSeconds(3), target.Interval);
        Assert.IsFalse(target.IsRequired);
        Assert.IsFalse(Only(scope).Collection!.InProgress);
    }

    [TestMethod]
    public async Task DuplicateAttachmentCannotReplaceItsFrozenRequirednessOrCurrentOperation()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation reservation = scope.Reserve(target);
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Collections.RegisterCollection(
            monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions { IsRequired = false }));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        HealthCollectionReservationResult joined = scope.Collections.TryReserve(target);
        Assert.AreEqual(HealthCollectionReservationDisposition.Existing, joined.Disposition);
        Assert.AreEqual(reservation.OperationId, joined.Reservation!.OperationId);
        Assert.IsTrue(target.IsRequired);
    }

    [TestMethod]
    public async Task ReservationJoinsOneOperationAndLifecycleChangesDoNotInvalidateItsResult()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation first = scope.Reserve(target);
        HealthCollectionReservationResult joined = scope.Collections.TryReserve(target);

        Assert.AreEqual(HealthCollectionReservationDisposition.Existing, joined.Disposition);
        Assert.AreEqual(first.OperationId, joined.Reservation!.OperationId);
        Assert.AreEqual(first.HealthObservationRevision, joined.Reservation.HealthObservationRevision);
        Assert.IsTrue(Only(scope).Collection!.InProgress);
        Assert.IsTrue(scope.Collections.TryEnter(first));

        scope.Clock.Advance(TimeSpan.FromSeconds(2));
        HealthCollectionAttempt attempt = scope.Collections.CommitResult(first, HealthCollectionResult.Condition(true));
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Unhealthy, Only(scope).State);
        scope.Finish(first);

        HealthMonitorSnapshot completed = Only(scope);
        Assert.IsFalse(completed.Collection!.InProgress);
        Assert.AreEqual(scope.Clock.GetUtcNow(), completed.Collection.LastCompletedAtUtc);
        HealthSnapshot settled = scope.Service.CaptureSnapshot();
        scope.Finish(first);
        Assert.AreEqual(settled.Revision, scope.Service.CaptureSnapshot().Revision);

        HealthCollectionReservation second = scope.Reserve(target);
        Assert.AreNotEqual(first.OperationId, second.OperationId);
        Assert.IsTrue(second.HealthObservationRevision > first.HealthObservationRevision);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NewerDirectObservationDefeatsStaleCollectionSuccessAndFailure(bool staleFlag)
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        var directReason = AcceptedDiagnostic("Direct", "The newer direct observation wins.");
        monitor.Flag(directReason);
        HealthMonitorSnapshot direct = Only(scope);
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        HealthCollectionAttempt attempt = scope.Collections.CommitResult(
            reservation, HealthCollectionResult.Condition(staleFlag, AcceptedDiagnostic("Old")));

        Assert.AreEqual(HealthCollectionDisposition.Obsolete, attempt.Disposition);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(direct.State, Only(scope).State);
        Assert.AreSame(directReason, Only(scope).Diagnostic);
        Assert.AreEqual(direct.LastObservationAtUtc, Only(scope).LastObservationAtUtc);
    }

    [TestMethod]
    public async Task RepeatingAnUnchangedDirectConditionStillInvalidatesAnOlderCollection()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        HealthSnapshot beforeDirect = scope.Service.CaptureSnapshot();

        monitor.Clear();

        HealthSnapshot afterDirect = scope.Service.CaptureSnapshot();
        Assert.IsTrue(afterDirect.Revision > beforeDirect.Revision);
        Assert.AreEqual(HealthCollectionDisposition.Obsolete,
            scope.Collections.CommitResult(reservation, HealthCollectionResult.Condition(true)).Disposition);
        Assert.AreEqual(afterDirect.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(MonitorState.Healthy, Only(scope).State);
    }

    [TestMethod]
    public async Task HealthAndMeasurementFreshnessAreIndependentAndComparedPerOrdinalKey()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        monitor.ReportMeasurement(HealthMeasurement.Integer("shared", 0, Epoch));
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        DateTimeOffset directTime = Epoch.AddHours(1);
        monitor.ReportMeasurement(HealthMeasurement.Integer("shared", 99, directTime));

        HealthCollectionAttempt attempt = scope.Collections.CommitResult(reservation,
            HealthCollectionResult.Condition(true).WithMeasurements(
            [
                HealthMeasurement.Integer("shared", 1, Epoch.AddSeconds(1)),
                HealthMeasurement.Integer("Shared", 2, Epoch.AddSeconds(2)),
                HealthMeasurement.Integer("new", 3, Epoch.AddSeconds(3))
            ]));

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        HealthMonitorSnapshot captured = Only(scope);
        Assert.AreEqual(MonitorState.Unhealthy, captured.State);
        Assert.AreEqual(99L, Measurement(captured, "shared").IntegerValue);
        Assert.AreEqual(directTime, Measurement(captured, "shared").ObservedAtUtc);
        Assert.AreEqual(2L, Measurement(captured, "Shared").IntegerValue);
        Assert.AreEqual(3L, Measurement(captured, "new").IntegerValue);
    }

    [TestMethod]
    public async Task StaleHealthResultCanStillPublishUncontestedMeasurementKeys()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        monitor.Flag(AcceptedDiagnostic("Direct"));
        DateTimeOffset healthTime = Only(scope).LastObservationAtUtc!.Value;

        scope.Collections.CommitResult(reservation, HealthCollectionResult.Condition(false).WithMeasurements(
            [HealthMeasurement.Integer("independent", 17, Epoch.AddMinutes(-1))]));

        HealthMonitorSnapshot captured = Only(scope);
        Assert.AreEqual(MonitorState.Unhealthy, captured.State);
        Assert.AreEqual("Direct", captured.Diagnostic!.Code);
        Assert.AreEqual(healthTime, captured.LastObservationAtUtc);
        Assert.AreEqual(17L, Measurement(captured, "independent").IntegerValue);
        Assert.AreEqual(Epoch.AddMinutes(-1), Measurement(captured, "independent").ObservedAtUtc);
    }

    [TestMethod]
    public async Task OptionalDiagnosticsDoNotCreateAHealthObservationOrAnExecutionOutcome()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor, required: false));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));

        HealthCollectionAttempt attempt = scope.Collections.CommitResult(reservation,
            HealthCollectionResult.Diagnostics([HealthMeasurement.Boolean("reachable", false, Epoch)]));

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        HealthMonitorSnapshot captured = Only(scope);
        Assert.AreEqual(MonitorState.Pending, captured.State);
        Assert.IsNull(captured.LastObservationAtUtc);
        Assert.AreEqual(0L, captured.Outcomes!.Failed);
        Assert.AreEqual(0L, captured.Outcomes.Successful);
        Assert.IsFalse(Measurement(captured, "reachable").BooleanValue!.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MeasurementCapacityRejectsTheWholeBatchWhilePreservingAValidRequiredHealthResult(bool required)
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        for (int index = 0; index < 32; index++)
        {
            monitor.ReportMeasurement(HealthMeasurement.Integer($"key-{index:D2}", index, Epoch));
        }
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor, required));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        HealthMeasurement[] measurements =
        [
            HealthMeasurement.Integer("key-00", 999, Epoch.AddMinutes(1)),
            HealthMeasurement.Integer("new-key", 123, Epoch.AddMinutes(1))
        ];

        HealthCollectionResult result = required
            ? HealthCollectionResult.Condition(true).WithMeasurements(measurements)
            : HealthCollectionResult.Diagnostics(measurements);
        ArgumentException? rejection = null;
        try { _ = scope.Collections.CommitResult(reservation, result); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A valid capacity-overflow batch must return its detached diagnostic without propagating an argument error.");

        HealthMonitorSnapshot after = Only(scope);
        Assert.AreEqual(required ? MonitorState.Unhealthy : MonitorState.Healthy, after.State);
        Assert.HasCount(32, after.Measurements);
        Assert.AreEqual(0L, Measurement(after, "key-00").IntegerValue);
        Assert.AreEqual(Epoch, Measurement(after, "key-00").ObservedAtUtc);
        Assert.IsFalse(after.Measurements.Any(measurement => measurement.Key == "new-key"));
        Assert.IsNotNull(after.Collection!.LastError);
        Assert.IsNull(after.Outcomes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OptionalErrorPreservesPolicyAndPreviousMeasurementWithoutRefreshingEither(bool mismatchedResult)
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        monitor.ReportSuccess();
        monitor.ReportMeasurement(HealthMeasurement.Integer("count", 42, Epoch.AddDays(-1)));
        HealthMonitorSnapshot before = Only(scope);
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor, required: false));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        scope.Clock.Advance(TimeSpan.FromMinutes(1));

        HealthCollectionAttempt attempt;
        if (mismatchedResult)
        {
            HealthCollectionResult result = HealthCollectionResult.Execution(HealthExecutionOutcome.Failed);
            HealthCollectionAttempt? accepted = null;
            ArgumentException? rejection = null;
            try { accepted = scope.Collections.CommitResult(reservation, result); }
            catch (ArgumentException error) { rejection = error; }
            Assert.IsNull(rejection, "An incompatible optional result must return one evaluation failure without propagating an argument error.");
            Assert.IsNotNull(accepted);
            attempt = accepted;
        }
        else { attempt = scope.Collections.CommitEvaluationFailure(reservation, AcceptedDiagnostic("OptionalReadFailed")); }

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsTrue(attempt.EvaluationFailed);
        HealthMonitorSnapshot after = Only(scope);
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(before.LastObservationAtUtc, after.LastObservationAtUtc);
        Assert.AreEqual(before.Outcomes!.Successful, after.Outcomes!.Successful);
        Assert.AreEqual(0L, after.Outcomes.Failed);
        Assert.AreEqual(42L, Measurement(after, "count").IntegerValue);
        Assert.AreEqual(Epoch.AddDays(-1), Measurement(after, "count").ObservedAtUtc);
        Assert.IsNotNull(after.Collection!.LastError);
        Assert.AreEqual(scope.Clock.GetUtcNow(), after.Collection.LastErrorAtUtc);
    }

    [TestMethod]
    public async Task RequiredConditionEvaluationErrorFlagsAndAValidEvaluationRecovers()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionTarget target = scope.Attach(monitor);
        var diagnostic = AcceptedDiagnostic("RequiredReadFailed");
        HealthCollectionReservation failed = scope.Reserve(target);
        Assert.IsTrue(scope.Collections.TryEnter(failed));

        HealthCollectionAttempt attempt = scope.Collections.CommitEvaluationFailure(failed, diagnostic);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsTrue(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Unhealthy, Only(scope).State);
        Assert.AreSame(diagnostic, Only(scope).Diagnostic);
        Assert.IsNull(Only(scope).Outcomes);
        scope.Finish(failed);
        HealthCollectionReservation recovered = scope.Reserve(target);
        Assert.IsTrue(scope.Collections.TryEnter(recovered));
        scope.Collections.CommitResult(recovered, HealthCollectionResult.Condition(false));
        Assert.AreEqual(MonitorState.Healthy, Only(scope).State);
        Assert.IsNull(Only(scope).Diagnostic);
    }

    [TestMethod]
    public async Task RequiredNumericErrorRetainsLastFiniteValueAndItsOriginalObservationTime()
    {
        await using var scope = new StateScope();
        INumericThresholdMonitor monitor = scope.Own(scope.Service.CreateNumericThreshold(new NumericThresholdMonitorOptions
        {
            IsCritical = true,
            Comparison = NumericComparison.GreaterThan,
            Threshold = 10,
            Unit = "ms"
        }));
        monitor.ReportValue(5);
        DateTimeOffset original = Only(scope).Numeric!.LastValueAtUtc!.Value;
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation failed = scope.Reserve(target);
        Assert.IsTrue(scope.Collections.TryEnter(failed));
        scope.Clock.Advance(TimeSpan.FromMinutes(1));

        scope.Collections.CommitEvaluationFailure(failed, AcceptedDiagnostic("RequiredReadFailed"));

        Assert.AreEqual(MonitorState.Unhealthy, Only(scope).State);
        Assert.AreEqual(5d, Only(scope).Numeric!.LastValue);
        Assert.AreEqual(original, Only(scope).Numeric!.LastValueAtUtc);
        scope.Finish(failed);
        HealthCollectionReservation recovered = scope.Reserve(target);
        Assert.IsTrue(scope.Collections.TryEnter(recovered));
        scope.Collections.CommitResult(recovered, HealthCollectionResult.Number(7));
        Assert.AreEqual(MonitorState.Healthy, Only(scope).State);
        Assert.AreEqual(7d, Only(scope).Numeric!.LastValue);
        Assert.AreEqual(scope.Clock.GetUtcNow(), Only(scope).Numeric!.LastValueAtUtc);
    }

    [TestMethod]
    public async Task RequiredFailureEvaluationsCountOnceEachUnderTheDeclaredThreshold()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 2);
        HealthCollectionTarget target = scope.Attach(monitor);
        for (int failure = 1; failure <= 2; failure++)
        {
            HealthCollectionReservation reservation = scope.Reserve(target);
            Assert.IsTrue(scope.Collections.TryEnter(reservation));
            scope.Collections.CommitEvaluationFailure(reservation, AcceptedDiagnostic("RequiredReadFailed"));
            scope.Finish(reservation);
            HealthMonitorSnapshot captured = Only(scope);
            Assert.AreEqual((long)failure, captured.Outcomes!.Failed);
            Assert.AreEqual((long)failure, captured.Outcomes.ConsecutiveFailures);
            Assert.AreEqual(failure == 2 ? MonitorState.Unhealthy : MonitorState.Healthy, captured.State);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RequiredAvailabilityErrorPreservesOnlyAnAlreadyEstablishedPublication(bool published)
    {
        await using var scope = new StateScope();
        IAvailabilityMonitor monitor = scope.Own(scope.Service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true }));
        if (published) { monitor.ReportPublished(); }
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));

        HealthCollectionAttempt attempt = scope.Collections.CommitEvaluationFailure(reservation, AcceptedDiagnostic("ReadFailed"));

        Assert.IsTrue(attempt.EvaluationFailed);
        HealthMonitorSnapshot captured = Only(scope);
        Assert.AreEqual(published ? MonitorState.Healthy : MonitorState.Unhealthy, captured.State);
        Assert.AreEqual(published ? 1L : 0L, captured.Outcomes!.Successful);
        Assert.AreEqual(1L, captured.Outcomes.Failed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NullAndMismatchedRequiredResultsBecomeOneEvaluationFailure(bool nullResult)
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));

        HealthCollectionResult? result = nullResult ? null : HealthCollectionResult.Number(12);
        HealthCollectionAttempt? attempt = null;
        ArgumentException? rejection = null;
        try { attempt = scope.Collections.CommitResult(reservation, result!); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "A null or incompatible required result must return one evaluation failure without propagating an argument error.");
        Assert.IsNotNull(attempt);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsTrue(attempt.EvaluationFailed);
        Assert.IsNotNull(attempt.Diagnostic);
        Assert.AreEqual(MonitorState.Unhealthy, Only(scope).State);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Failed);
        Assert.AreEqual(0L, Only(scope).Outcomes!.Successful);
    }

    [TestMethod]
    public async Task StaleRequiredEvaluationErrorCannotReplaceANewerDirectSuccess()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        monitor.ReportSuccess();
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        HealthCollectionAttempt attempt = scope.Collections.CommitEvaluationFailure(reservation, AcceptedDiagnostic("OldError"));

        Assert.AreEqual(HealthCollectionDisposition.Obsolete, attempt.Disposition);
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(MonitorState.Healthy, Only(scope).State);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Successful);
        Assert.AreEqual(0L, Only(scope).Outcomes!.Failed);
        Assert.IsNull(Only(scope).Diagnostic);
        Assert.IsNull(Only(scope).Collection!.LastError);
    }

    [TestMethod]
    public async Task WithdrawingACollectionCancelsOutsideTheGateAndWaitsForActualComplete()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation reservation = scope.Reserve(target);
        bool callbackObservedUnlockedState = false;
        using CancellationTokenRegistration cancellation = reservation.LifetimeCancellation.Register(() =>
        {
            callbackObservedUnlockedState = Task.Run(scope.Service.CaptureSnapshot).Wait(TimeSpan.FromSeconds(5));
        });

        Task settlement = scope.Collections.WithdrawCollection(target);

        Assert.IsTrue(callbackObservedUnlockedState, "Cancellation callbacks must be able to read state from another thread.");
        Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
        Assert.IsFalse(settlement.IsCompleted);
        Assert.IsFalse(scope.Collections.TryEnter(reservation));
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.Collections.TryReserve(target).Disposition);
        Assert.AreEqual(HealthCollectionDisposition.Retired,
            scope.Collections.CommitResult(reservation, HealthCollectionResult.Condition(true)).Disposition);
        monitor.Flag();
        Assert.AreEqual(MonitorState.Unhealthy, Only(scope).State);
        scope.Finish(reservation, HealthCollectionCompletionReason.RetiredBeforeInvocation);
        await settlement.WaitAsync(TimeSpan.FromSeconds(5));
        await scope.Collections.WithdrawCollection(target).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(Only(scope).Collection!.InProgress);
    }

    [TestMethod]
    public async Task MonitorWithdrawalHidesItsStateButCannotSettleAReservedWrapperEarly()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));

        monitor.Withdraw();
        Task disposal = monitor.WaitForDisposalAsync();

        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        Assert.IsFalse(disposal.IsCompleted);
        Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
        Assert.IsFalse(scope.Collections.TryEnter(reservation));
        Assert.AreEqual(HealthCollectionDisposition.Retired,
            scope.Collections.CommitEvaluationFailure(reservation, AcceptedDiagnostic("Late")).Disposition);
        Assert.ThrowsExactly<ObjectDisposedException>(() => monitor.Clear());
        scope.Finish(reservation, HealthCollectionCompletionReason.RetiredBeforeInvocation);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
    }

    [TestMethod]
    public async Task ReentrantCompleteCannotReleaseDrainUntilItsCancellationCallbackActuallyReturns()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        var completedInsideCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCancellation = new ManualResetEventSlim();
        using CancellationTokenRegistration cancellation = reservation.LifetimeCancellation.Register(() =>
        {
            scope.Finish(reservation);
            completedInsideCancellation.TrySetResult();
            releaseCancellation.Wait();
        });
        Task withdrawal = Task.Run(monitor.Withdraw);
        Task? serviceDrain = null;
        try
        {
            await completedInsideCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(monitor.WaitForDisposalAsync().IsCompleted,
                "Completing the wrapper from cancellation must retain protection through the running cancellation handler.");
            serviceDrain = scope.Service.DisposeAsync().AsTask();
            Assert.IsFalse(serviceDrain.IsCompleted);
            Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
            Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
        }
        finally
        {
            releaseCancellation.Set();
            await withdrawal.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.IsNotNull(serviceDrain);
        await Task.WhenAll(serviceDrain, monitor.WaitForDisposalAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        await scope.Service.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ThrowingCancellationHandlerRecordsItsActualErrorTimeWithoutAHealthFailureOrStrandedDrain()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        using CancellationTokenRegistration cancellation = reservation.LifetimeCancellation.Register(
            () => throw new InvalidOperationException("A cancellation observer failed."));
        scope.Clock.Advance(TimeSpan.FromHours(2));

        ArgumentException? rejection = null;
        try { scope.Service.BeginStopping(); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual cancellation observer failure must become a detached integration diagnostic without propagating an argument error.");

        HealthMonitorSnapshot after = Only(scope);
        Assert.AreEqual(MonitorState.Pending, after.State);
        Assert.AreEqual(0L, after.Outcomes!.Failed);
        Assert.IsNull(after.LastObservationAtUtc);
        Assert.IsNotNull(after.Collection!.LastError);
        Assert.AreEqual(scope.Clock.GetUtcNow(), after.Collection.LastErrorAtUtc);
        Assert.AreNotEqual(after.RegisteredAtUtc, after.Collection.LastErrorAtUtc);
        scope.Finish(reservation);
        await scope.Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task StoppingSuppressesReservedInvocationWithoutInventingAFailedEvaluation()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation reservation = scope.Reserve(target);

        scope.Service.BeginStopping();

        Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
        Assert.IsFalse(scope.Collections.TryEnter(reservation));
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.Collections.TryReserve(target).Disposition);
        scope.Finish(reservation, HealthCollectionCompletionReason.RetiredBeforeInvocation);
        Assert.AreEqual(0L, Only(scope).Outcomes!.Failed);
        Assert.AreEqual(MonitorState.Pending, Only(scope).State);
        monitor.ReportSuccess();
        Assert.AreEqual(1L, Only(scope).Outcomes!.Successful);
        Assert.IsFalse(scope.Service.CaptureSnapshot().Ready);
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Collections.RegisterCollection(
            monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions()));
    }

    [TestMethod]
    public async Task ServiceDisposalAndCanceledWaitsRetainTheActualWrapperBarrier()
    {
        await using var scope = new StateScope();
        IConditionMonitor monitor = scope.Condition();
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        Task firstDisposal = scope.Service.DisposeAsync().AsTask();
        Task secondDisposal = scope.Service.DisposeAsync().AsTask();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.Service.WaitForDisposalAsync(canceled.Token)).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<OperationCanceledException>(() => monitor.WaitForDisposalAsync(canceled.Token)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(firstDisposal.IsCompleted);
        Assert.IsFalse(secondDisposal.IsCompleted);
        Assert.IsFalse(monitor.WaitForDisposalAsync().IsCompleted);
        HealthSnapshot during = scope.Service.CaptureSnapshot();
        Assert.IsTrue(during.Application.IsStopping);
        Assert.AreEqual(0, during.MonitorCounts.Active);
        scope.Finish(reservation);

        await Task.WhenAll(firstDisposal, secondDisposal, monitor.WaitForDisposalAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        await scope.Service.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.ThrowsExactly<ObjectDisposedException>(() => scope.Service.CaptureSnapshot());
    }

    [TestMethod]
    public async Task OwnerUnavailableRemovesTheMonitorWithoutManufacturingAPolicyFailure()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        monitor.ReportSuccess();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        HealthCollectionTarget target = scope.Attach(monitor);
        HealthCollectionReservation reservation = scope.Reserve(target);

        HealthCollectionAttempt attempt = scope.Finish(reservation, HealthCollectionCompletionReason.OwnerUnavailable);

        Assert.AreEqual(HealthCollectionDisposition.OwnerUnavailable, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(0L, before.Monitors[0].Outcomes!.Failed);
        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, scope.Collections.TryReserve(target).Disposition);
        await monitor.WaitForDisposalAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task LeaseAcquisitionFaultRecordsAnIntegrationErrorWithoutAnAdmittedOutcome()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        var error = AcceptedDiagnostic("AcquireFailed");

        HealthCollectionAttempt attempt = scope.Finish(reservation,
            HealthCollectionCompletionReason.LeaseAcquisitionFault, acquisitionError: error);

        Assert.IsFalse(attempt.EvaluationFailed);
        HealthMonitorSnapshot captured = Only(scope);
        Assert.AreEqual(MonitorState.Pending, captured.State);
        Assert.AreEqual(0L, captured.Outcomes!.Failed);
        Assert.AreEqual(0L, captured.Outcomes.Successful);
        Assert.AreSame(error, captured.Collection!.LastError);
        Assert.IsFalse(captured.Collection.InProgress);
        Assert.IsNull(captured.LastObservationAtUtc);
    }

    [TestMethod]
    public async Task ActualCleanupFaultDoesNotCountTheSuccessfulEvaluationAgain()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        HealthCollectionReservation reservation = scope.Reserve(scope.Attach(monitor));
        Assert.IsTrue(scope.Collections.TryEnter(reservation));
        scope.Collections.CommitResult(reservation, HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        var cleanup = AcceptedDiagnostic("CleanupFailed");

        scope.Finish(reservation, cleanupError: cleanup);

        HealthMonitorSnapshot captured = Only(scope);
        Assert.AreEqual(MonitorState.Healthy, captured.State);
        Assert.AreEqual(1L, captured.Outcomes!.Successful);
        Assert.AreEqual(0L, captured.Outcomes.Failed);
        Assert.AreSame(cleanup, captured.Collection!.LastError);
        Assert.IsFalse(captured.Collection.InProgress);
    }

    [TestMethod]
    public async Task InvalidCreatedBindingRollsBackBeforeConsumingCapacityOrRegistrationIdentity()
    {
        await using var scope = new StateScope(maxMonitors: 1);
        Guid registrationId = Guid.NewGuid();
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.Throws<ArgumentException>(() => scope.Executions.Bind(BindingRequest.ForAvailability(
            new AvailabilityMonitorOptions { IsCritical = true }, BindingOptions(registrationId))));
        Assert.Throws<ArgumentException>(() => scope.Executions.Bind(BindingRequest.ForConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 0 }, BindingOptions(registrationId))));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForAvailability(
            new AvailabilityMonitorOptions { IsCritical = true }, BindingOptions(registrationId, publishes: true)));
        Assert.AreEqual(HealthBindingOwnership.CreatedMonitor, target.Ownership);
        Assert.AreEqual(registrationId, target.RegistrationId);
        Assert.AreEqual(1, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        await scope.Executions.ReleaseBinding(target);
    }

    [TestMethod]
    public async Task CapacityFailureDoesNotReserveAnExecutionRegistrationId()
    {
        await using var scope = new StateScope(maxMonitors: 1);
        IConditionMonitor existing = scope.Condition();
        Guid registrationId = Guid.NewGuid();
        BindingRequest request = BindingRequest.ForConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 3 }, BindingOptions(registrationId));
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<HealthcheckCapacityException>(() => scope.Executions.Bind(request));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        existing.Withdraw();
        BindingTarget target = scope.Executions.Bind(request);
        Assert.AreEqual(registrationId, target.RegistrationId);
        Assert.AreEqual(1, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        await scope.Executions.ReleaseBinding(target);
    }

    [TestMethod]
    public async Task DuplicateActiveRegistrationIdCannotCreateAnAdditionalMonitor()
    {
        await using var scope = new StateScope(maxMonitors: 2);
        Guid registrationId = Guid.NewGuid();
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 2 }, BindingOptions(registrationId)));
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Executions.Bind(BindingRequest.ForAvailability(
            new AvailabilityMonitorOptions { IsCritical = false }, BindingOptions(registrationId, publishes: true))));

        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.AreEqual(before.Revision, after.Revision);
        Assert.AreEqual(1, after.MonitorCounts.Active);
        Assert.AreEqual(target.MonitorId, after.Monitors[0].MonitorId);
        IConditionMonitor remainingCapacity = scope.Condition();
        Assert.AreEqual(2, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        await scope.Executions.ReleaseBinding(target);
        GC.KeepAlive(remainingCapacity);
    }

    [TestMethod]
    public async Task UnsupportedAndForeignExternalMonitorsRejectWithoutLeavingABinding()
    {
        await using var scope = new StateScope();
        IConditionMonitor condition = scope.Condition();
        IConsecutiveFailureMonitor suitable = scope.Failures();
        Guid registrationId = Guid.NewGuid();
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.Throws<ArgumentException>(() => scope.Executions.Bind(BindingRequest.ForMonitor(
            condition.MonitorId, BindingOptions(registrationId))));
        Assert.Throws<ArgumentException>(() => scope.Executions.Bind(BindingRequest.ForMonitor(
            Guid.NewGuid(), BindingOptions(registrationId))));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForMonitor(suitable.MonitorId, BindingOptions(registrationId)));
        Assert.AreEqual(HealthBindingOwnership.ExternalMonitor, target.Ownership);
        await scope.Executions.ReleaseBinding(target);
        Assert.AreEqual(2, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        GC.KeepAlive(condition);
    }

    [TestMethod]
    public async Task ReleasingAnExternalBindingLeavesItsMonitorAndCountersCallerOwned()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));
        Assert.AreEqual(HealthFactDisposition.Recorded,
            scope.Executions.ApplyOutcome(target, Outcome(target, 1, HealthExecutionOutcome.Failed)));

        await scope.Executions.ReleaseBinding(target);
        await scope.Executions.ReleaseBinding(target);

        Assert.AreEqual(1, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(HealthFactDisposition.Retired,
            scope.Executions.ApplyOutcome(target, Outcome(target, 2, HealthExecutionOutcome.Failed)));
        monitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, Only(scope).State);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Successful);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Failed);
    }

    [TestMethod]
    public async Task ReleasingACreatedBindingWithdrawsImmediatelyAndSharesActualCollectionSettlement()
    {
        await using var scope = new StateScope();
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 }, BindingOptions(Guid.NewGuid())));
        HealthCollectionTarget collection = scope.Collections.RegisterCollection(target.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
        HealthCollectionReservation reservation = scope.Reserve(collection);
        Assert.IsTrue(scope.Collections.TryEnter(reservation));

        Task first = scope.Executions.ReleaseBinding(target);
        Task second = scope.Executions.ReleaseBinding(target);

        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
        Assert.AreEqual(HealthFactDisposition.Retired,
            scope.Executions.ApplyOutcome(target, Outcome(target, 1, HealthExecutionOutcome.Failed)));
        scope.Finish(reservation);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task OutcomeCommitsPolicyCountersAndDetachedExecutionFactsAsOneVersion()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));
        DateTimeOffset reportedTime = Epoch.ToOffset(TimeSpan.FromHours(2));
        var original = AcceptedDiagnostic("CallbackFailed", "A real detached failure reason.");
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        var fact = new HealthExecutionFact
        {
            RegistrationId = target.RegistrationId,
            Kind = HealthExecutionFactKind.Outcome,
            ExecutionId = 7,
            OccurredAtUtc = reportedTime,
            WorkCompletedAtUtc = reportedTime,
            SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Failed,
            CallbackDurationMilliseconds = 12.5,
            OriginalError = original
        };

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Executions.ApplyOutcome(target, fact));

        HealthSnapshot after = scope.Service.CaptureSnapshot();
        Assert.IsTrue(after.Revision > before.Revision);
        Assert.AreEqual(HealthStatus.Unhealthy, after.HealthStatus);
        Assert.AreEqual(1, after.MonitorCounts.CriticalFailing);
        HealthMonitorSnapshot captured = after.Monitors[0];
        Assert.AreEqual(MonitorState.Unhealthy, captured.State);
        Assert.AreEqual(Epoch, captured.LastObservationAtUtc);
        Assert.AreEqual(1L, captured.Outcomes!.Failed);
        Assert.AreEqual(1L, captured.Outcomes.ConsecutiveFailures);
        Assert.AreSame(original, captured.Outcomes.LastFailureDiagnostic);
        HealthExecutionSnapshot execution = captured.Outcomes.Execution!;
        Assert.AreEqual(target.RegistrationId, execution.RegistrationId);
        Assert.AreEqual(7L, execution.ExecutionId);
        Assert.AreEqual(HealthExecutionPhase.Callback, execution.Phase);
        Assert.AreEqual(Epoch, execution.WorkCompletedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, execution.WorkCompletedAtUtc!.Value.Offset);
        Assert.AreEqual(12.5, execution.CallbackDurationMilliseconds);
        Assert.AreSame(original, execution.OriginalError);
        Assert.IsNull(execution.SettledAtUtc);
        await scope.Executions.ReleaseBinding(target);
    }

    [TestMethod]
    public async Task LifecycleSettlementAddsActualFactsWithoutASecondOutcomeOrWallClockInference()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));
        Assert.AreEqual(HealthFactDisposition.Recorded,
            scope.Executions.ApplyOutcome(target, Outcome(target, 1, HealthExecutionOutcome.Failed)));
        HealthMonitorSnapshot before = Only(scope);
        var cleanupError = AcceptedDiagnostic("CleanupFailed");
        DateTimeOffset settlementTime = Epoch.AddMinutes(-5);
        var settled = new HealthExecutionFact
        {
            RegistrationId = target.RegistrationId,
            Kind = HealthExecutionFactKind.Settled,
            ExecutionId = 1,
            OccurredAtUtc = settlementTime,
            SettledAtUtc = settlementTime,
            ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded,
            CleanupStatus = HealthExecutionStepStatus.Failed,
            CleanupError = cleanupError,
            TotalDurationMilliseconds = 25
        };

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Executions.UpdateLifecycle(target, settled));

        HealthMonitorSnapshot after = Only(scope);
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(before.LastObservationAtUtc, after.LastObservationAtUtc);
        Assert.AreEqual(1L, after.Outcomes!.Failed);
        Assert.AreEqual(0L, after.Outcomes.Successful);
        HealthExecutionSnapshot execution = after.Outcomes.Execution!;
        Assert.AreEqual(HealthExecutionPhase.Settled, execution.Phase);
        Assert.AreEqual(settlementTime, execution.SettledAtUtc);
        Assert.AreEqual(25d, execution.TotalDurationMilliseconds);
        Assert.AreEqual(HealthExecutionStepStatus.Succeeded, execution.ErrorHandlingStatus);
        Assert.AreEqual(HealthExecutionStepStatus.Failed, execution.CleanupStatus);
        Assert.AreSame(cleanupError, execution.CleanupError);
        Assert.AreEqual(Epoch, execution.WorkCompletedAtUtc);
        await scope.Executions.ReleaseBinding(target);
    }

    [TestMethod]
    public async Task PreviousExecutionSettlementDoesNotReplaceTheCurrentExecutionOrItsOutcome()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));
        scope.Executions.ApplyOutcome(target, Outcome(target, 1, HealthExecutionOutcome.Failed));
        scope.Executions.ApplyOutcome(target, Outcome(target, 2, HealthExecutionOutcome.Succeeded));
        HealthMonitorSnapshot current = Only(scope);
        var lateSettlement = new HealthExecutionFact
        {
            RegistrationId = target.RegistrationId,
            Kind = HealthExecutionFactKind.Settled,
            ExecutionId = 1,
            OccurredAtUtc = Epoch.AddSeconds(10),
            SettledAtUtc = Epoch.AddSeconds(10),
            ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable,
            CleanupStatus = HealthExecutionStepStatus.Failed,
            CleanupError = AcceptedDiagnostic("PreviousCleanup"),
            TotalDurationMilliseconds = 19
        };

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Executions.UpdateLifecycle(target, lateSettlement));

        HealthMonitorSnapshot after = Only(scope);
        Assert.AreEqual(MonitorState.Healthy, after.State);
        Assert.AreEqual(current.LastObservationAtUtc, after.LastObservationAtUtc);
        Assert.AreEqual(1L, after.Outcomes!.Successful);
        Assert.AreEqual(1L, after.Outcomes.Failed);
        Assert.AreEqual(0L, after.Outcomes.ConsecutiveFailures);
        Assert.AreEqual(HealthExecutionOutcome.Succeeded, after.Outcomes.LastOutcome);
        Assert.AreEqual(2L, after.Outcomes.Execution!.ExecutionId);
        Assert.AreEqual(HealthExecutionPhase.Callback, after.Outcomes.Execution.Phase);
        Assert.IsNull(after.Outcomes.Execution.SettledAtUtc);
        Assert.IsNull(after.Outcomes.Execution.CleanupError);
        Assert.IsNull(after.Outcomes.Execution.TotalDurationMilliseconds);
        await scope.Executions.ReleaseBinding(target);
    }

    [TestMethod]
    public async Task DistinctExecutionRegistrationsShareAnExternalPolicyWithoutSharingTheirIdentityOrRetirement()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures(threshold: 1);
        BindingTarget first = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));
        BindingTarget second = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));

        Assert.AreEqual(HealthFactDisposition.Recorded,
            scope.Executions.ApplyOutcome(first, Outcome(first, 1, HealthExecutionOutcome.Failed)));
        Assert.AreEqual(HealthFactDisposition.Recorded,
            scope.Executions.ApplyOutcome(second, Outcome(second, 1, HealthExecutionOutcome.Succeeded)));
        Assert.AreEqual(1, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Failed);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Successful);
        Assert.AreEqual(second.RegistrationId, Only(scope).Outcomes!.Execution!.RegistrationId);

        await scope.Executions.ReleaseBinding(first);

        Assert.AreEqual(HealthFactDisposition.Retired,
            scope.Executions.ApplyOutcome(first, Outcome(first, 2, HealthExecutionOutcome.Failed)));
        Assert.AreEqual(HealthFactDisposition.Recorded,
            scope.Executions.ApplyOutcome(second, Outcome(second, 2, HealthExecutionOutcome.Failed)));
        Assert.AreEqual(2L, Only(scope).Outcomes!.Failed);
        Assert.AreEqual(1L, Only(scope).Outcomes!.Successful);
        Assert.AreEqual(MonitorState.Unhealthy, Only(scope).State);
        monitor.Withdraw();
        Assert.AreEqual(HealthFactDisposition.Retired,
            scope.Executions.UpdateLifecycle(second, new HealthExecutionFact
            {
                RegistrationId = second.RegistrationId,
                Kind = HealthExecutionFactKind.Registered,
                OccurredAtUtc = Epoch
            }));
        await scope.Executions.ReleaseBinding(second);
        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
    }

    [TestMethod]
    public async Task BeginStoppingRejectsNewBindingsWhileExistingTargetsCanFinishTheirHandoff()
    {
        await using var scope = new StateScope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        BindingTarget target = scope.Executions.Bind(BindingRequest.ForMonitor(monitor.MonitorId, BindingOptions(Guid.NewGuid())));

        scope.Service.BeginStopping();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Executions.Bind(BindingRequest.ForConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 }, BindingOptions(Guid.NewGuid()))));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(HealthFactDisposition.Recorded,
            scope.Executions.ApplyOutcome(target, Outcome(target, 1, HealthExecutionOutcome.Succeeded)));

        Assert.AreEqual(1L, Only(scope).Outcomes!.Successful);
        Assert.IsFalse(scope.Service.CaptureSnapshot().Ready);
        await scope.Executions.ReleaseBinding(target);
    }

    private static HealthMonitorSnapshot Only(StateScope scope)
    {
        HealthSnapshot snapshot = scope.Service.CaptureSnapshot();
        Assert.HasCount(1, snapshot.Monitors);
        return snapshot.Monitors[0];
    }

    private static HealthMeasurement Measurement(HealthMonitorSnapshot monitor, string key) =>
        monitor.Measurements.Single(measurement => string.Equals(measurement.Key, key, StringComparison.Ordinal));

    private static HealthExecutionBindingOptions BindingOptions(Guid registrationId, bool publishes = false) => new()
    {
        RegistrationId = registrationId,
        SuccessPublishesUsableData = publishes
    };

    private static HealthExecutionFact Outcome(BindingTarget target, long executionId, HealthExecutionOutcome outcome) => new()
    {
        RegistrationId = target.RegistrationId,
        Kind = HealthExecutionFactKind.Outcome,
        ExecutionId = executionId,
        OccurredAtUtc = Epoch,
        WorkCompletedAtUtc = Epoch,
        SourcePhase = HealthExecutionPhase.Callback,
        Outcome = outcome
    };

    /// <summary>Acts only as the caller of real state seams; there is no background callback or simulated coordinator.</summary>
    private sealed class StateScope : IAsyncDisposable
    {
        private readonly List<HealthCollectionReservation> reservations = [];
        private readonly HashSet<Guid> finished = [];
        private readonly List<IHealthMonitor> monitors = [];

        internal StateScope(int maxMonitors = 32)
        {
            Clock = new ManualTimeProvider(Epoch);
            Service = new HealthcheckService(new HealthcheckOptions { MaxMonitors = maxMonitors }, new HealthApplicationIdentity
            {
                Name = "State seam tests",
                InstanceId = Guid.Parse("7dce64ee-2277-497b-83f8-1952f4dc9580")
            }, Clock);
            var access = (IHealthStateAccess)Service;
            Collections = access.CollectionState;
            Executions = access.ExecutionState;
        }

        internal HealthcheckService Service { get; }
        internal ManualTimeProvider Clock { get; }
        internal IHealthCollectionState Collections { get; }
        internal IHealthExecutionState Executions { get; }

        internal IConditionMonitor Condition() => Own(Service.CreateCondition(new ConditionMonitorOptions
        {
            IsCritical = true,
            InitiallyFlagged = false
        }));

        internal IConsecutiveFailureMonitor Failures(int threshold = 3) => Own(Service.CreateConsecutiveFailures(new ConsecutiveFailureMonitorOptions
        {
            IsCritical = true,
            FailureThreshold = threshold
        }));

        internal T Own<T>(T monitor) where T : IHealthMonitor
        {
            monitors.Add(monitor);
            return monitor;
        }

        internal HealthCollectionTarget Attach(IHealthMonitor monitor, bool required = true) =>
            Collections.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions { IsRequired = required });

        internal HealthCollectionReservation Reserve(HealthCollectionTarget target)
        {
            HealthCollectionReservationResult result = Collections.TryReserve(target);
            Assert.AreEqual(HealthCollectionReservationDisposition.Reserved, result.Disposition);
            Assert.IsNotNull(result.Reservation);
            reservations.Add(result.Reservation);
            return result.Reservation;
        }

        internal HealthCollectionAttempt Finish(
            HealthCollectionReservation reservation,
            HealthCollectionCompletionReason reason = HealthCollectionCompletionReason.Settled,
            HealthDiagnostic? acquisitionError = null,
            HealthDiagnostic? cleanupError = null)
        {
            HealthCollectionAttempt attempt = Collections.Complete(reservation,
                new HealthCollectionCompletion(reason, Clock.GetUtcNow(), acquisitionError, cleanupError));
            finished.Add(reservation.OperationId);
            return attempt;
        }

        public async ValueTask DisposeAsync()
        {
            // A failed assertion must not strand its no-callback unit operation and hang test cleanup.
            foreach (HealthCollectionReservation reservation in reservations)
            {
                if (!finished.Contains(reservation.OperationId)) { Finish(reservation); }
            }
            await Service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private static HealthDiagnostic AcceptedDiagnostic(string code, string? message = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce the actual detached reason.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }
}
