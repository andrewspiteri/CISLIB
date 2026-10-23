using System.Runtime.CompilerServices;
using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRunnerLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Epoch = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task ConstructionRegistrationAndSnapshotCaptureNeverAcquireOwnerDependencies()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { Interval = TimeSpan.FromSeconds(1) });

        scope.Clock.Advance(TimeSpan.FromDays(1));
        HealthSnapshot first = scope.Service.CaptureSnapshot();
        HealthSnapshot second = scope.Service.CaptureSnapshot();

        Assert.AreEqual(monitor.MonitorId, registration.MonitorId);
        Assert.AreEqual(0, owner.Acquisitions);
        Assert.AreEqual(0, owner.Callbacks);
        Assert.AreEqual(first.Revision, second.Revision);
        Assert.AreEqual(MonitorState.Healthy, first.Monitors.Single().State);
        Assert.IsFalse(first.Monitors.Single().Collection!.InProgress);
    }

    [TestMethod]
    public async Task InvalidForeignAndDuplicateAttachmentsAreRejectedWithoutOwnerInvocation()
    {
        await using var scope = new Scope();
        await using var otherService = new HealthcheckService(new HealthcheckOptions());
        IConditionMonitor monitor = scope.Condition();
        IConditionMonitor foreign = otherService.CreateCondition(new ConditionMonitorOptions
        { IsCritical = true, InitiallyFlagged = false });
        Owner owner = scope.Owner(HealthCollectionResult.Condition(false));

        Assert.Throws<ArgumentNullException>(() => scope.Runner.Register(null!, owner, new HealthCollectorOptions()));
        Assert.Throws<ArgumentNullException>(() => scope.Runner.Register(monitor, null!, new HealthCollectorOptions()));
        Assert.Throws<ArgumentNullException>(() => scope.Runner.Register(monitor, owner, null!));
        Assert.Throws<ArgumentException>(() => scope.Runner.Register(foreign, owner, new HealthCollectorOptions()));
        Assert.Throws<ArgumentException>(() => scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { Interval = TimeSpan.Zero }));
        IHealthCollectionRegistration first = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        Assert.Throws<InvalidOperationException>(() => scope.Runner.Register(monitor, owner, new HealthCollectorOptions()));

        Assert.AreEqual(0, owner.Acquisitions);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Committed, (await first.CollectAsync().WaitAsync(Deadline)).Disposition);
        Assert.AreEqual(1, owner.Callbacks);
    }

    [TestMethod]
    public async Task AServiceAcceptsOnlyOneCollectorRunnerForItsLifetime()
    {
        await using var scope = new Scope();
        Assert.Throws<InvalidOperationException>(() => new HealthCollectorRunner(scope.Service));
        await scope.Runner.DisposeAsync().AsTask().WaitAsync(Deadline);
        Assert.Throws<InvalidOperationException>(() => new HealthCollectorRunner(scope.Service));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task ManualEvaluationUsesItsRealOwnerLeaseAndTheSelectedPolicy(int policy)
    {
        await using var scope = new Scope();
        IHealthMonitor monitor = scope.Policy(policy);
        Owner owner = scope.Owner(HealthyResult(policy));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromSeconds(3));

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        Assert.AreEqual(scope.Clock.GetUtcNow(), scope.Only().LastObservationAtUtc);
        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Callbacks);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, owner.ActiveLeases);
        Assert.IsFalse(scope.Only().Collection!.InProgress);
        Assert.AreEqual(scope.Clock.GetUtcNow(), scope.Only().Collection!.LastCompletedAtUtc);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 0)]
    [DataRow(2, 0)]
    [DataRow(3, 0)]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(3, 1)]
    [DataRow(0, 2)]
    [DataRow(1, 2)]
    [DataRow(2, 2)]
    [DataRow(3, 2)]
    public async Task RequiredExceptionMalformedResultAndUnexpectedCancellationApplyOnePolicyFailure(int policy, int fault)
    {
        await using var scope = new Scope();
        IHealthMonitor monitor = scope.Policy(policy);
        Owner owner = scope.Owner(HealthyResult(policy));
        owner.Evaluate = _ => fault switch
        {
            0 => Task.FromException<HealthCollectionResult>(new InvalidOperationException("owner evaluation failed")),
            1 => Task.FromResult(HealthCollectionResult.Diagnostics([])),
            _ => Task.FromException<HealthCollectionResult>(new OperationCanceledException("unexpected cancellation"))
        };
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsTrue(attempt.EvaluationFailed);
        Assert.IsNotNull(attempt.Diagnostic);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
        Assert.IsNotNull(scope.Only().Collection!.LastError);
        if (policy is 1 or 3)
        {
            Assert.AreEqual(1L, scope.Only().Outcomes!.Failed);
            Assert.AreEqual(1L, scope.Only().Outcomes!.ConsecutiveFailures);
            Assert.AreEqual(0L, scope.Only().Outcomes!.Successful);
        }
        Assert.AreEqual(1, owner.Cleanups);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task NullRequiredResultBecomesOneEvaluationFailureAndCanRecoverOnTheNextAttempt()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.Evaluate = _ => Task.FromResult<HealthCollectionResult>(null!);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        Assert.IsTrue((await registration.CollectAsync().WaitAsync(Deadline)).EvaluationFailed);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Failed);
        owner.Evaluate = _ => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        Assert.IsFalse((await registration.CollectAsync().WaitAsync(Deadline)).EvaluationFailed);

        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Failed);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Only().Outcomes!.ConsecutiveFailures);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task OptionalEvaluationErrorsPreserveHealthAndTheLastMeasurementValueAndTime(int fault)
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        monitor.ReportMeasurement(HealthMeasurement.Integer("count", 7, Epoch.AddSeconds(-3)));
        HealthMonitorSnapshot before = scope.Only();
        Owner owner = scope.Owner(HealthCollectionResult.Diagnostics([]));
        owner.Evaluate = _ => fault switch
        {
            0 => Task.FromException<HealthCollectionResult>(new InvalidOperationException("optional read failed")),
            1 => Task.FromResult(HealthCollectionResult.Condition(true)),
            2 => Task.FromException<HealthCollectionResult>(new OperationCanceledException("unexpected cancellation")),
            _ => Task.FromResult<HealthCollectionResult>(null!)
        };
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { IsRequired = false });
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromMinutes(2));

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsTrue(attempt.EvaluationFailed);
        HealthMonitorSnapshot after = scope.Only();
        Assert.AreEqual(before.State, after.State);
        Assert.AreEqual(before.LastObservationAtUtc, after.LastObservationAtUtc);
        Assert.AreEqual(7L, after.Measurements.Single().IntegerValue);
        Assert.AreEqual(Epoch.AddSeconds(-3), after.Measurements.Single().ObservedAtUtc);
        Assert.IsNotNull(after.Collection!.LastError);
        Assert.AreEqual(scope.Clock.GetUtcNow(), after.Collection.LastErrorAtUtc);
    }

    [TestMethod]
    public async Task OptionalMeasurementsNeverSupplyAnImplicitHealthObservation()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        DateTimeOffset observed = Epoch.AddHours(-1);
        Owner owner = scope.Owner(HealthCollectionResult.Diagnostics([HealthMeasurement.Integer("count", 9, observed)]));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { IsRequired = false });
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Pending, scope.Only().State);
        Assert.IsNull(scope.Only().LastObservationAtUtc);
        Assert.AreEqual(0L, scope.Only().Outcomes!.Successful);
        Assert.HasCount(1, scope.Only().Measurements, "The optional collection must publish its diagnostic measurement while leaving policy state pending.");
        Assert.AreEqual(observed, scope.Only().Measurements.Single().ObservedAtUtc);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task FailedRequiredNumericReadRetainsTheLastGoodValueAndRecoversFromARealCollection()
    {
        await using var scope = new Scope();
        var monitor = (INumericThresholdMonitor)scope.Policy(2);
        monitor.ReportValue(4);
        DateTimeOffset? valueTime = scope.Only().Numeric!.LastValueAtUtc;
        Owner owner = scope.Owner(HealthCollectionResult.Number(3));
        owner.Evaluate = _ => Task.FromException<HealthCollectionResult>(new InvalidOperationException("numeric read failed"));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromSeconds(1));

        await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
        Assert.AreEqual(4d, scope.Only().Numeric!.LastValue);
        Assert.AreEqual(valueTime, scope.Only().Numeric!.LastValueAtUtc);
        owner.Evaluate = _ => Task.FromResult(HealthCollectionResult.Number(3));
        await registration.CollectAsync().WaitAsync(Deadline);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        Assert.AreEqual(3d, scope.Only().Numeric!.LastValue);
    }

    [TestMethod]
    public async Task AvailabilityRefreshFailurePreservesDataPublishedByAnEarlierCollection()
    {
        await using var scope = new Scope();
        var monitor = (IAvailabilityMonitor)scope.Policy(3);
        Owner owner = scope.Owner(HealthCollectionResult.AvailabilityPublished());
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await registration.CollectAsync().WaitAsync(Deadline);
        owner.Evaluate = _ => Task.FromResult(HealthCollectionResult.AvailabilityRefreshFailed(new HealthDiagnostic("RefreshFailed")));

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Successful);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Failed);
        monitor.Invalidate();
        await registration.CollectAsync().WaitAsync(Deadline);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ANewerDirectObservationDefeatsBothStaleResultsAndStaleEvaluationErrors(int observation)
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(observation == 0), blockCallback: true);
        if (observation == 2)
        { owner.Evaluate = _ => Task.FromException<HealthCollectionResult>(new InvalidOperationException("stale error")); }
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        var reason = new HealthDiagnostic("DirectWinner");
        monitor.Flag(reason);
        DateTimeOffset? accepted = scope.Only().LastObservationAtUtc;

        owner.ReleaseCallback();
        HealthCollectionAttempt attempt = await pending.WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Obsolete, attempt.Disposition);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
        Assert.AreSame(reason, scope.Only().Diagnostic);
        Assert.AreEqual(accepted, scope.Only().LastObservationAtUtc);
        Assert.IsNull(scope.Only().Collection!.LastError);
    }

    [TestMethod]
    public async Task AStaleMetricDoesNotDiscardTheFreshHealthResultOrOtherMetricKeys()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        monitor.ReportMeasurement(HealthMeasurement.Integer("shared", 1, Epoch));
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true).WithMeasurements(
        [HealthMeasurement.Integer("shared", 2, Epoch), HealthMeasurement.Integer("other", 3, Epoch)]), blockCallback: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);
        monitor.ReportMeasurement(HealthMeasurement.Integer("shared", 99, Epoch.AddSeconds(1)));
        owner.ReleaseCallback();

        HealthCollectionAttempt attempt = await pending.WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
        Assert.AreEqual(99L, scope.Only().Measurements.Single(value => value.Key == "shared").IntegerValue);
        Assert.AreEqual(3L, scope.Only().Measurements.Single(value => value.Key == "other").IntegerValue);
    }

    [TestMethod]
    public async Task OwnerRefusalWithdrawsOnlyItsMonitorWithoutInventingAnEvaluationFailure()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        IConditionMonitor survivor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        await owner.BeginOwnerDisposal().WaitAsync(Deadline);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.AreEqual(HealthCollectionDisposition.OwnerUnavailable, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(survivor.MonitorId, scope.Only().MonitorId);
        Assert.AreEqual(0, owner.Callbacks);
        Assert.AreEqual(0, owner.Cleanups);
        await monitor.WaitForDisposalAsync().WaitAsync(Deadline);
        Assert.Throws<ObjectDisposedException>(() => monitor.ReportSuccess());
        GC.KeepAlive(survivor);
    }

    [TestMethod]
    public async Task LeaseAcquisitionExceptionIsAnIntegrationDiagnosticAndLeavesPolicyCountersUntouched()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.AcquisitionFault = new InvalidOperationException("lease acquisition failed");
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Pending, scope.Only().State);
        Assert.AreEqual(0L, scope.Only().Outcomes!.Failed);
        Assert.AreEqual(0L, scope.Only().Outcomes!.Successful);
        Assert.IsNotNull(scope.Only().Collection!.LastError);
        Assert.AreEqual(0, owner.Callbacks);
        Assert.AreEqual(0, owner.ActiveLeases);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task MalformedLeaseAcquisitionIsAnIntegrationFaultAndEveryAcquiredLeaseIsReleased(int protocol)
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.AcquisitionProtocol = protocol;
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Pending, scope.Only().State);
        Assert.AreEqual(0L, scope.Only().Outcomes!.Failed);
        Assert.IsNotNull(scope.Only().Collection!.LastError);
        Assert.AreEqual(0, owner.Callbacks);
        Assert.AreEqual(protocol == 1 ? 0 : 1, owner.Cleanups);
        Assert.AreEqual(0, owner.ActiveLeases);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task LeaseCleanupFaultCannotTurnOneSuccessfulResultIntoASecondFailedOutcome()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.CleanupFault = new InvalidOperationException("lease cleanup failed");
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Only().Outcomes!.Failed);
        Assert.IsNotNull(scope.Only().Collection!.LastError);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, owner.ActiveLeases);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    public async Task WithdrawalFromLeaseAcquisitionSuppressesInvocationButStillAwaitsAcquiredLeaseCleanup()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true), blockCleanup: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        owner.AfterAcquisition = registration.Withdraw;
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CleanupEntered, pending);
        Task disposal = registration.WaitForDisposalAsync();
        Assert.AreEqual(0, owner.Callbacks);
        Assert.AreEqual(1, owner.ActiveLeases);
        Assert.IsFalse(pending.IsCompleted);
        Assert.IsFalse(disposal.IsCompleted);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);

        owner.ReleaseCleanup();
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await pending.WaitAsync(Deadline)).Disposition);
        await disposal.WaitAsync(Deadline);
        Assert.AreEqual(0, owner.ActiveLeases);
        monitor.Flag();
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
    }

    [TestMethod]
    public async Task RegistrationWithdrawalCancelsButDoesNotDisposeDependenciesBeforeCallbackAndCleanupSettle()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true), blockCallback: true, blockCleanup: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);
        Task dependencyDisposal = owner.BeginOwnerDisposal();

        registration.Withdraw();
        await owner.LifetimeCanceled.WaitAsync(Deadline);
        Task registrationDisposal = registration.WaitForDisposalAsync();
        Assert.IsFalse(dependencyDisposal.IsCompleted);
        Assert.IsFalse(registrationDisposal.IsCompleted);
        Assert.IsFalse(pending.IsCompleted);
        monitor.Flag(new HealthDiagnostic("FinalOwnerReport"));
        owner.ReleaseCallback();
        await WaitForOwnedGateAsync(owner.CleanupEntered, pending);
        Assert.IsFalse(dependencyDisposal.IsCompleted);
        Assert.IsFalse(registrationDisposal.IsCompleted);
        Assert.IsFalse(pending.IsCompleted);

        owner.ReleaseCleanup();
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await pending.WaitAsync(Deadline)).Disposition);
        await Task.WhenAll(dependencyDisposal, registrationDisposal).WaitAsync(Deadline);
        Assert.IsTrue(owner.DependenciesDisposed);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
        Assert.AreEqual("FinalOwnerReport", scope.Only().Diagnostic!.Code);
    }

    [TestMethod]
    public async Task MonitorWithdrawalRemovesItsContributionImmediatelyAndDrainsTheActualLease()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition(initiallyFlagged: true);
        Owner owner = scope.Owner(HealthCollectionResult.Condition(false), blockCallback: true, blockCleanup: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);

        monitor.Withdraw();
        await owner.LifetimeCanceled.WaitAsync(Deadline);
        Task disposal = monitor.WaitForDisposalAsync();
        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(HealthStatus.Healthy, scope.Service.CaptureSnapshot().HealthStatus);
        Assert.IsFalse(disposal.IsCompleted);
        owner.ReleaseCallback();
        await WaitForOwnedGateAsync(owner.CleanupEntered, pending);
        Assert.IsFalse(disposal.IsCompleted);
        owner.ReleaseCleanup();

        Assert.AreEqual(HealthCollectionDisposition.Retired, (await pending.WaitAsync(Deadline)).Disposition);
        await disposal.WaitAsync(Deadline);
        Assert.AreEqual(0, owner.ActiveLeases);
    }

    [TestMethod]
    public async Task AnIdleMonitorWithdrawalSettlesItsManualRegistrationWithoutAdvancingTheClock()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        Task registrationDisposal = registration.WaitForDisposalAsync();
        Assert.IsFalse(registrationDisposal.IsCompleted);

        monitor.Withdraw();

        await registrationDisposal.WaitAsync(Deadline);
        Assert.AreEqual(Epoch, scope.Clock.GetUtcNow());
        Assert.AreEqual(0, owner.Acquisitions);
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await registration.CollectAsync().WaitAsync(Deadline)).Disposition);
        await monitor.WaitForDisposalAsync().WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task AQueuedMonitorWithdrawalReleasesItsPendingManualCallerWhileAnotherLeaseStillOccupiesCapacity()
    {
        await using var scope = new Scope(concurrency: 1);
        IConditionMonitor blockerMonitor = scope.Condition();
        IConditionMonitor queuedMonitor = scope.Condition();
        Owner blocker = scope.Owner(HealthCollectionResult.Condition(false), blockCallback: true);
        Owner queuedOwner = scope.Owner(HealthCollectionResult.Condition(true));
        IHealthCollectionRegistration first = scope.Runner.Register(blockerMonitor, blocker, new HealthCollectorOptions());
        IHealthCollectionRegistration queued = scope.Runner.Register(queuedMonitor, queuedOwner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> firstAttempt = first.CollectAsync();
        await WaitForOwnedGateAsync(blocker.CallbackEntered, firstAttempt, blocker.CleanupEntered);
        Task<HealthCollectionAttempt> pending = queued.CollectAsync();
        Assert.IsFalse(pending.IsCompleted);

        queuedMonitor.Withdraw();

        Assert.AreEqual(HealthCollectionDisposition.Retired, (await pending.WaitAsync(Deadline)).Disposition);
        await queued.WaitForDisposalAsync().WaitAsync(Deadline);
        Assert.AreEqual(0, queuedOwner.Acquisitions);
        Assert.AreEqual(1, blocker.ActiveLeases);
        Assert.IsFalse(firstAttempt.IsCompleted);
        blocker.ReleaseCallback();
        await firstAttempt.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task WithdrawingAPeriodicMonitorDrainsItsRegistrationAndPreventsFurtherAcquisition()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true), blockCallback: true, blockCleanup: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { Interval = TimeSpan.FromSeconds(1) });
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        registration.ActivatePeriodic();
        await WaitForOwnedGateAsync(owner.CallbackEntered, null, owner.CleanupEntered);

        monitor.Withdraw();
        Task registrationDisposal = registration.WaitForDisposalAsync();
        Assert.IsFalse(registrationDisposal.IsCompleted);
        owner.ReleaseCallback();
        await owner.CleanupEntered.WaitAsync(Deadline);
        Assert.IsFalse(registrationDisposal.IsCompleted);
        owner.ReleaseCleanup();
        await registrationDisposal.WaitAsync(Deadline);
        scope.Clock.Advance(TimeSpan.FromDays(1));
        Assert.AreEqual(HealthCollectionDisposition.Retired, (await registration.CollectAsync().WaitAsync(Deadline)).Disposition);
        await scope.Runner.StopAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, owner.Acquisitions);
        Assert.AreEqual(1, owner.Callbacks);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(0, owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AClockFailureAfterActualLeaseCleanupCannotStrandCollectionOrDisposal(bool failTimestamp)
    {
        var clock = new OneShotFaultClock();
        await using var scope = new Scope(timeProvider: clock);
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true));
        owner.AfterCleanup = () => clock.Arm(failTimestamp);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(1, clock.Faults);
        Assert.AreEqual(0, owner.ActiveLeases);
        Assert.AreEqual(1, owner.Cleanups);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
        await scope.Runner.StopAsync().WaitAsync(Deadline);
        await registration.WaitForDisposalAsync().WaitAsync(Deadline);
        await scope.Service.DisposeAsync().AsTask().WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task ServiceDisposalIncludesWorkOfAMonitorWithdrawnBeforeServiceDisposalStarted()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true), blockCallback: true, blockCleanup: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);
        monitor.Withdraw();

        Task disposal = scope.Service.DisposeAsync().AsTask();
        Assert.IsFalse(disposal.IsCompleted);
        Assert.IsTrue(scope.Service.CaptureSnapshot().Application.IsStopping);
        owner.ReleaseCallback();
        await WaitForOwnedGateAsync(owner.CleanupEntered, pending);
        Assert.IsFalse(disposal.IsCompleted);
        owner.ReleaseCleanup();

        await Task.WhenAll(disposal, pending).WaitAsync(Deadline);
        Assert.AreEqual(0, owner.ActiveLeases);
        Assert.Throws<ObjectDisposedException>(() => scope.Service.CaptureSnapshot());
    }

    [TestMethod]
    public async Task RunnerStopCancellationIsNeutralAndKeepsTheExternalMonitorAvailableForFinalReports()
    {
        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        monitor.ReportFailure();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded), blockCallback: true, blockCleanup: true);
        owner.Evaluate = token => Task.FromException<HealthCollectionResult>(new OperationCanceledException(token));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);

        Task stop = scope.Runner.StopAsync();
        await owner.LifetimeCanceled.WaitAsync(Deadline);
        Assert.IsFalse(stop.IsCompleted);
        Assert.IsFalse(pending.IsCompleted);
        owner.ReleaseCallback();
        await WaitForOwnedGateAsync(owner.CleanupEntered, pending);
        Assert.IsFalse(stop.IsCompleted);
        owner.ReleaseCleanup();
        await Task.WhenAll(stop, pending).WaitAsync(Deadline);

        Assert.IsFalse((await pending).EvaluationFailed);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Failed);
        Assert.AreEqual(0L, scope.Only().Outcomes!.Successful);
        Assert.AreEqual(1L, scope.Only().Outcomes!.ConsecutiveFailures);
        monitor.ReportSuccess();
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
    }

    [TestMethod]
    public async Task ServiceStoppingSignalsLifetimeCancellationWithoutRequiringRunnerStopFirst()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true), blockCallback: true);
        owner.Evaluate = token => Task.FromException<HealthCollectionResult>(new OperationCanceledException(token));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);

        scope.Service.BeginStopping();
        await owner.LifetimeCanceled.WaitAsync(Deadline);
        Assert.IsFalse(pending.IsCompleted);
        owner.ReleaseCallback();
        HealthCollectionAttempt attempt = await pending.WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        monitor.Flag();
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
    }

    [TestMethod]
    public async Task TheIdleRunnerAndServiceDoNotRetainARegistrationCollectorOrOwnerGraph()
    {
        await using var scope = new Scope();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        WeakReference[] references = RegisterAbandonedOwner(scope.Service, scope.Runner);

        CollectUntilDead(references);

        Assert.IsTrue(references.All(reference => !reference.IsAlive), "An idle registry must retain only detached facts and weak registration references.");
        Assert.AreEqual(0, scope.Service.CaptureSnapshot().MonitorCounts.Active);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public async Task ACollectorCannotAwaitADisposalBarrierContainingItsOwnLease(int barrier)
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(false));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        owner.Evaluate = async _ =>
        {
            switch (barrier)
            {
                case 0:
                    registration.Withdraw();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => registration.WaitForDisposalAsync()).WaitAsync(Deadline);
                    break;
                case 1:
                    monitor.Withdraw();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.WaitForDisposalAsync()).WaitAsync(Deadline);
                    break;
                case 2:
                    scope.Service.BeginStopping();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.WaitForDisposalAsync()).WaitAsync(Deadline);
                    break;
                case 3:
                    await Assert.ThrowsAsync<InvalidOperationException>(() => registration.DisposeAsync().AsTask()).WaitAsync(Deadline);
                    break;
                case 4:
                    await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.DisposeAsync().AsTask()).WaitAsync(Deadline);
                    break;
                case 5:
                    await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.DisposeAsync().AsTask()).WaitAsync(Deadline);
                    break;
                default:
                    await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Runner.DisposeAsync().AsTask()).WaitAsync(Deadline);
                    break;
            }
            return HealthCollectionResult.Condition(false);
        };
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        if (barrier != 2) { Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition); }
        registration.Withdraw();
        await registration.WaitForDisposalAsync().WaitAsync(Deadline);
        Assert.AreEqual(1, owner.Cleanups);
    }

    [TestMethod]
    public async Task ACollectorCannotAwaitItsOwnRunnerStopBarrier()
    {
        await using var scope = new Scope();
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(false));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        owner.Evaluate = async _ =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Runner.StopAsync()).WaitAsync(Deadline);
            return HealthCollectionResult.Condition(false);
        };
        await scope.Runner.StartAsync().WaitAsync(Deadline);

        await registration.CollectAsync().WaitAsync(Deadline);
        await scope.Runner.StopAsync().WaitAsync(Deadline);

        Assert.AreEqual(1, owner.Cleanups);
        GC.KeepAlive(monitor);
    }

    private static async Task WaitForOwnedGateAsync(AsyncSignal entered, Task<HealthCollectionAttempt>? actualCaller,
        AsyncSignal? cleanupEntered = null)
    {
        // The receiving test holds the owner's callback or cleanup gate closed.
        // Lease cleanup without the expected callback is also observable progress
        // when cleanup itself is gated and the actual caller remains pending.
        // Returning the collection result before releasing that gate violates
        // the actual-operation barrier even if the owner was never entered.
        using var entryWait = new CancellationTokenSource();
        Task entry = entered.WaitAsync(Deadline, entryWait.Token);
        try
        {
            Task cleanup = cleanupEntered?.WaitAsync(Deadline, entryWait.Token) ?? entry;
            await Task.WhenAny(entry, actualCaller ?? entry, cleanup).WaitAsync(Deadline);
            if (cleanupEntered?.IsSet == true)
            {
                Assert.IsTrue(entered.IsSet,
                    "The owner cannot begin lease cleanup before its expected callback was invoked.");
            }
            if (actualCaller is not null)
            {
                Assert.IsFalse(actualCaller.IsCompleted,
                    "The actual collection settled before its owned callback or cleanup gate was released.");
            }
            await entry;
        }
        finally { entryWait.Cancel(); }
    }

    private static HealthCollectionResult HealthyResult(int policy) => policy switch
    {
        0 => HealthCollectionResult.Condition(false),
        1 => HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded),
        2 => HealthCollectionResult.Number(4),
        _ => HealthCollectionResult.AvailabilityPublished()
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RegisterAbandonedOwner(HealthcheckService service, HealthCollectorRunner runner)
    {
        IConditionMonitor monitor = service.CreateCondition(new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = true });
        var owner = new Owner(HealthCollectionResult.Condition(false), false, false);
        IHealthCollectionRegistration registration = runner.Register(monitor, owner, new HealthCollectorOptions());
        return [new WeakReference(monitor), new WeakReference(owner), new WeakReference(owner.Dependencies), new WeakReference(registration)];
    }

    private static void CollectUntilDead(WeakReference[] references)
    {
        for (int attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<Owner> owners = [];
        private readonly List<IHealthMonitor> monitors = [];
        internal ManualTimeProvider Clock { get; } = new(Epoch);
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }

        internal Scope(int concurrency = 4, TimeProvider? timeProvider = null)
        {
            Service = new HealthcheckService(new HealthcheckOptions { MaxConcurrentCollections = concurrency }, timeProvider: timeProvider ?? Clock);
            Runner = new HealthCollectorRunner(Service);
        }

        internal IConditionMonitor Condition(bool initiallyFlagged = false) => Retain(Service.CreateCondition(
            new ConditionMonitorOptions { IsCritical = true, InitiallyFlagged = initiallyFlagged }));

        internal IConsecutiveFailureMonitor Failures() => Retain(Service.CreateConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 }));

        internal IHealthMonitor Policy(int policy) => policy switch
        {
            0 => Condition(initiallyFlagged: true),
            1 => Failures(),
            2 => Retain(Service.CreateNumericThreshold(new NumericThresholdMonitorOptions
            { IsCritical = true, Comparison = NumericComparison.GreaterThan, Threshold = 5, Unit = "items" })),
            _ => Retain(Service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true }))
        };

        private T Retain<T>(T monitor) where T : IHealthMonitor
        {
            monitors.Add(monitor);
            return monitor;
        }

        internal Owner Owner(HealthCollectionResult result, bool blockCallback = false, bool blockCleanup = false)
        {
            var owner = new Owner(result, blockCallback, blockCleanup);
            owners.Add(owner);
            return owner;
        }

        internal HealthMonitorSnapshot Only() => Service.CaptureSnapshot().Monitors.Single();

        public async ValueTask DisposeAsync()
        {
            // Release both barriers even if an assertion failed before the callback/cleanup reached it.
            foreach (Owner owner in owners) { owner.ReleaseCallback(); owner.ReleaseCleanup(); }
            // Both actual drains start after every owner gate has been released.
            // Capture synchronous failures independently so neither can prevent
            // the other disposal from being attempted; retain the same watchdog.
            Task runner = DrainRunnerAsync();
            Task service = DrainServiceAsync();
            await Task.WhenAll(runner, service).WaitAsync(Deadline);

            async Task DrainRunnerAsync() => await Runner.DisposeAsync();
            async Task DrainServiceAsync() => await Service.DisposeAsync();
        }
    }

    // This fixture implements an owner lifetime protocol, rather than merely retaining a
    // dependency reference. Closing admission cannot dispose dependencies until all acquired
    // leases have completed their asynchronous cleanup.
    private sealed class Owner : IHealthCollector
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource callbackRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource cleanupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource ownerDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int acquisitions, callbacks, cleanups, activeLeases;
        private bool accepting = true, dependenciesDisposed;

        internal Owner(HealthCollectionResult result, bool blockCallback, bool blockCleanup)
        {
            Evaluate = _ => Task.FromResult(result);
            if (!blockCallback) { callbackRelease.TrySetResult(); }
            if (!blockCleanup) { cleanupRelease.TrySetResult(); }
        }

        internal object Dependencies { get; } = new();
        internal AsyncSignal CallbackEntered { get; } = new();
        internal AsyncSignal CleanupEntered { get; } = new();
        internal AsyncSignal LifetimeCanceled { get; } = new();
        internal Func<CancellationToken, Task<HealthCollectionResult>> Evaluate { get; set; }
        internal Action? AfterAcquisition { get; set; }
        internal Action? AfterCleanup { get; set; }
        internal Exception? AcquisitionFault { get; set; }
        internal int AcquisitionProtocol { get; set; }
        internal Exception? CleanupFault { get; set; }
        internal int Acquisitions { get { lock (gate) { return acquisitions; } } }
        internal int Callbacks { get { lock (gate) { return callbacks; } } }
        internal int Cleanups { get { lock (gate) { return cleanups; } } }
        internal int ActiveLeases { get { lock (gate) { return activeLeases; } } }
        internal bool DependenciesDisposed { get { lock (gate) { return dependenciesDisposed; } } }

        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            lock (gate)
            {
                acquisitions++;
                if (AcquisitionFault is not null) { throw AcquisitionFault; }
                if (!accepting) { lease = null; return false; }
                if (AcquisitionProtocol == 1) { lease = null; return true; }
                activeLeases++;
                lease = new Lease(this);
            }
            if (AcquisitionProtocol == 3) { throw new InvalidOperationException("Acquisition assigned a lease and then threw."); }
            AfterAcquisition?.Invoke();
            return AcquisitionProtocol != 2;
        }

        internal Task BeginOwnerDisposal()
        {
            bool complete;
            lock (gate)
            {
                accepting = false;
                complete = activeLeases == 0;
                if (complete) { dependenciesDisposed = true; }
            }
            if (complete) { ownerDisposed.TrySetResult(); }
            return ownerDisposed.Task;
        }

        internal void ReleaseCallback() => callbackRelease.TrySetResult();
        internal void ReleaseCleanup() => cleanupRelease.TrySetResult();

        private sealed class Lease(Owner owner) : IHealthCollectionLease
        {
            private int disposed;

            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                lock (owner.gate)
                {
                    Assert.IsFalse(owner.dependenciesDisposed, "The acquired lease must protect the actual owner dependencies.");
                    owner.callbacks++;
                }
                using CancellationTokenRegistration cancellation = lifetimeCancellation.Register(owner.LifetimeCanceled.Set);
                owner.CallbackEntered.Set();
                // Only the receiving test releases this gate. Its entry/result
                // watchdogs remain bounded, and Scope.DisposeAsync releases it
                // even when an assertion fails before owner entry.
                await owner.callbackRelease.Task;
                lock (owner.gate) { Assert.IsFalse(owner.dependenciesDisposed); }
                return await owner.Evaluate(lifetimeCancellation);
            }

            public async ValueTask DisposeAsync()
            {
                Assert.AreEqual(0, Interlocked.Exchange(ref disposed, 1), "A lease must be disposed exactly once.");
                owner.CleanupEntered.Set();
                await owner.cleanupRelease.Task;
                bool complete;
                lock (owner.gate)
                {
                    Assert.IsFalse(owner.dependenciesDisposed, "Dependencies cannot be disposed during lease cleanup.");
                    owner.cleanups++;
                    owner.activeLeases--;
                    complete = !owner.accepting && owner.activeLeases == 0;
                    if (complete) { owner.dependenciesDisposed = true; }
                }
                if (complete) { owner.ownerDisposed.TrySetResult(); }
                owner.AfterCleanup?.Invoke();
                if (owner.CleanupFault is not null) { throw owner.CleanupFault; }
            }
        }
    }

    private sealed class OneShotFaultClock : TimeProvider
    {
        private readonly ManualTimeProvider inner = new(Epoch);
        private int nextUtc, nextTimestamp, faults, samplingThread;
        internal int Faults => Volatile.Read(ref faults);
        public override long TimestampFrequency => inner.TimestampFrequency;

        internal void Arm(bool timestamp)
        {
            // The lease returns synchronously after this hook. Target its finalizer's next
            // sample rather than an unrelated dispatcher sample on another pool thread.
            Volatile.Write(ref samplingThread, Environment.CurrentManagedThreadId);
            if (timestamp) { Volatile.Write(ref nextTimestamp, 1); }
            else { Volatile.Write(ref nextUtc, 1); }
        }

        public override DateTimeOffset GetUtcNow()
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref samplingThread) && Interlocked.Exchange(ref nextUtc, 0) != 0)
            {
                Interlocked.Increment(ref faults);
                throw new InvalidOperationException("One completion UTC sample failed.");
            }
            return inner.GetUtcNow();
        }

        public override long GetTimestamp()
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref samplingThread) && Interlocked.Exchange(ref nextTimestamp, 0) != 0)
            {
                Interlocked.Increment(ref faults);
                throw new InvalidOperationException("One completion monotonic sample failed.");
            }
            return inner.GetTimestamp();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }
}
