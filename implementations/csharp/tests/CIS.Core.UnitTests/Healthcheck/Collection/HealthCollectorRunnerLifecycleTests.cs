using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
    public void ManualEvaluationUsesItsRealOwnerLeaseAndTheSelectedPolicy(int policy)
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Manual, policy, 0));
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
    public void RequiredExceptionMalformedResultAndUnexpectedCancellationApplyOnePolicyFailure(int policy, int fault)
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Required, policy, fault));
    }

    [TestMethod]
    public void NullRequiredResultBecomesOneEvaluationFailureAndCanRecoverOnTheNextAttempt()
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Null, 0, 0));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void OptionalEvaluationErrorsPreserveHealthAndTheLastMeasurementValueAndTime(int fault)
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Optional, 0, fault));
    }

    [TestMethod]
    public void OptionalMeasurementsNeverSupplyAnImplicitHealthObservation()
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Measurements, 0, 0));
    }

    [TestMethod]
    public void FailedRequiredNumericReadRetainsTheLastGoodValueAndRecoversFromARealCollection()
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Numeric, 0, 0));
    }

    [TestMethod]
    public void AvailabilityRefreshFailurePreservesDataPublishedByAnEarlierCollection()
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Availability, 0, 0));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void ANewerDirectObservationDefeatsBothStaleResultsAndStaleEvaluationErrors(int observation)
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.Newer, 0, observation));
    }

    [TestMethod]
    public void AStaleMetricDoesNotDiscardTheFreshHealthResultOrOtherMetricKeys()
    {
        AssertLifecycleOwnership(RunAndReleaseScenario(LifecycleScenario.StaleMetric, 0, 0));
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
        var acquisitionFailure = new InvalidOperationException("lease acquisition failed");
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(acquisitionFailure, "Healthcheck.LeaseAcquisition"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid acquisition exception must produce its detached integration diagnostic.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.AcquisitionFault = acquisitionFailure;
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
        Exception? protocolFailure = protocol == 3 ? new InvalidOperationException("Acquisition assigned a lease and then threw.") : null;
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try
        {
            projected = protocolFailure is null
                ? new HealthDiagnostic("Healthcheck.LeaseProtocol", "The owner returned an inconsistent acquisition result and lease.")
                : HealthDiagnostic.FromException(protocolFailure, "Healthcheck.LeaseAcquisition");
        }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid malformed-acquisition diagnostic inputs must be accepted before dispatch.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.AcquisitionProtocol = protocol;
        owner.AcquisitionProtocolFailure = protocolFailure;
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
        var cleanupFailure = new InvalidOperationException("lease cleanup failed");
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(cleanupFailure, "Healthcheck.LeaseCleanup"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid lease cleanup exception must produce its detached diagnostic before dispatch.");
        Assert.IsNotNull(projected);

        await using var scope = new Scope();
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.CleanupFault = cleanupFailure;
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
        HealthDiagnostic? reason = null;
        ArgumentException? diagnosticRejection = null;
        try { reason = new HealthDiagnostic("FinalOwnerReport"); }
        catch (ArgumentException error) { diagnosticRejection = error; }
        Assert.IsNull(diagnosticRejection, "The valid final owner report diagnostic must be accepted at the actual withdrawal phase.");
        Assert.IsNotNull(reason);
        monitor.Flag(reason);
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
        await using var scope = new Scope(observePeriodicDispatch: true);
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true), blockCallback: true, blockCleanup: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { Interval = TimeSpan.FromSeconds(1) });
        Task<TimeSpan> startupChange = scope.DispatchClock!.NextChangeAsync();
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await startupChange;
        Assert.AreEqual(0, owner.Acquisitions, "Actual startup must leave the unactivated periodic owner inert.");
        Task<TimeSpan> activatedChange = scope.DispatchClock.NextChangeAsync();
        registration.ActivatePeriodic();
        await activatedChange;
        Assert.AreEqual(1, owner.Acquisitions,
            "Actual activation must acquire its due owner before the next idle timer request.");
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
        var completionFailure = new InvalidOperationException(failTimestamp
            ? "One completion monotonic sample failed." : "One completion UTC sample failed.");
        HealthDiagnostic? projected = null;
        ArgumentException? rejection = null;
        try { projected = HealthDiagnostic.FromException(completionFailure, "Healthcheck.CollectionClock"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The actual valid completion-clock exception must produce its detached diagnostic before dispatch.");
        Assert.IsNotNull(projected);

        var clock = new OneShotFaultClock { CompletionFailure = completionFailure };
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

    private static async Task ManualEvaluationUsesItsRealOwnerLeaseAndTheSelectedPolicy(Scope scope, int policy)
    {
        IHealthMonitor monitor = scope.Policy(policy);
        Owner owner = scope.Owner(HealthyResult(policy));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
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

    private static async Task RequiredExceptionMalformedResultAndUnexpectedCancellationApplyOnePolicyFailure(Scope scope, int policy, int fault)
    {
        IHealthMonitor monitor = scope.Policy(policy);
        Owner owner = scope.Owner(HealthyResult(policy));
        owner.Evaluate = _ => fault switch
        {
            0 => Task.FromException<HealthCollectionResult>(owner.ProducerFailure(ProducerSite.Required, new InvalidOperationException("owner evaluation failed"))),
            1 => Task.FromResult(HealthCollectionResult.Diagnostics([])),
            _ => Task.FromException<HealthCollectionResult>(owner.ProducerFailure(ProducerSite.Required, new OperationCanceledException("unexpected cancellation")))
        };
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
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

    private static async Task NullRequiredResultBecomesOneEvaluationFailureAndCanRecoverOnTheNextAttempt(Scope scope)
    {
        IConsecutiveFailureMonitor monitor = scope.Failures();
        Owner owner = scope.Owner(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        owner.Evaluate = _ => Task.FromResult<HealthCollectionResult>(null!);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
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

    private static async Task OptionalEvaluationErrorsPreserveHealthAndTheLastMeasurementValueAndTime(Scope scope, int fault)
    {
        IConditionMonitor monitor = scope.Condition();
        monitor.ReportMeasurement(HealthMeasurement.Integer("count", 7, Epoch.AddSeconds(-3)));
        HealthMonitorSnapshot before = scope.Only();
        Owner owner = scope.Owner(HealthCollectionResult.Diagnostics([]));
        owner.Evaluate = _ => fault switch
        {
            0 => Task.FromException<HealthCollectionResult>(owner.ProducerFailure(ProducerSite.Optional, new InvalidOperationException("optional read failed"))),
            1 => Task.FromResult(HealthCollectionResult.Condition(true)),
            2 => Task.FromException<HealthCollectionResult>(owner.ProducerFailure(ProducerSite.Optional, new OperationCanceledException("unexpected cancellation"))),
            _ => Task.FromResult<HealthCollectionResult>(null!)
        };
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { IsRequired = false });
        scope.RetainRegistration(registration);
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

    private static async Task OptionalMeasurementsNeverSupplyAnImplicitHealthObservation(Scope scope)
    {
        IConsecutiveFailureMonitor monitor = scope.Failures();
        DateTimeOffset observed = Epoch.AddHours(-1);
        Owner owner = scope.Owner(HealthCollectionResult.Diagnostics([HealthMeasurement.Integer("count", 9, observed)]));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner,
            new HealthCollectorOptions { IsRequired = false });
        scope.RetainRegistration(registration);
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

    private static async Task FailedRequiredNumericReadRetainsTheLastGoodValueAndRecoversFromARealCollection(Scope scope)
    {
        var monitor = (INumericThresholdMonitor)scope.Policy(2);
        monitor.ReportValue(4);
        DateTimeOffset? valueTime = scope.Only().Numeric!.LastValueAtUtc;
        Owner owner = scope.Owner(HealthCollectionResult.Number(3));
        owner.Evaluate = _ => Task.FromException<HealthCollectionResult>(owner.ProducerFailure(ProducerSite.Numeric, new InvalidOperationException("numeric read failed")));
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
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

    private static async Task AvailabilityRefreshFailurePreservesDataPublishedByAnEarlierCollection(Scope scope)
    {
        var monitor = (IAvailabilityMonitor)scope.Policy(3);
        Owner owner = scope.Owner(HealthCollectionResult.AvailabilityPublished());
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        await registration.CollectAsync().WaitAsync(Deadline);
        HealthDiagnostic? refreshFailure = null;
        ArgumentException? rejection = null;
        try { refreshFailure = new HealthDiagnostic("RefreshFailed"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The valid immutable refresh-failure diagnostic must be accepted after the first actual collection.");
        Assert.IsNotNull(refreshFailure);
        owner.Evaluate = _ => Task.FromResult(HealthCollectionResult.AvailabilityRefreshFailed(refreshFailure));

        HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.AreEqual(MonitorState.Healthy, scope.Only().State);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Successful);
        Assert.AreEqual(1L, scope.Only().Outcomes!.Failed);
        monitor.Invalidate();
        await registration.CollectAsync().WaitAsync(Deadline);
        Assert.AreEqual(MonitorState.Unhealthy, scope.Only().State);
    }

    private static async Task ANewerDirectObservationDefeatsBothStaleResultsAndStaleEvaluationErrors(Scope scope, int observation)
    {
        IConditionMonitor monitor = scope.Condition();
        Owner owner = scope.Owner(HealthCollectionResult.Condition(observation == 0), blockCallback: true);
        if (observation == 2)
        { owner.Evaluate = _ => Task.FromException<HealthCollectionResult>(owner.ProducerFailure(ProducerSite.Stale, new InvalidOperationException("stale error"))); }
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
        await scope.Runner.StartAsync().WaitAsync(Deadline);
        Task<HealthCollectionAttempt> pending = registration.CollectAsync();
        await WaitForOwnedGateAsync(owner.CallbackEntered, pending, owner.CleanupEntered);
        scope.Clock.Advance(TimeSpan.FromSeconds(1));
        HealthDiagnostic? reason = null;
        ArgumentException? rejection = null;
        try { reason = new HealthDiagnostic("DirectWinner"); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "The valid direct-observation diagnostic must be accepted at the original gated observation phase.");
        Assert.IsNotNull(reason);
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

    private static async Task AStaleMetricDoesNotDiscardTheFreshHealthResultOrOtherMetricKeys(Scope scope)
    {
        IConditionMonitor monitor = scope.Condition();
        monitor.ReportMeasurement(HealthMeasurement.Integer("shared", 1, Epoch));
        Owner owner = scope.Owner(HealthCollectionResult.Condition(true).WithMeasurements(
        [HealthMeasurement.Integer("shared", 2, Epoch), HealthMeasurement.Integer("other", 3, Epoch)]), blockCallback: true);
        IHealthCollectionRegistration registration = scope.Runner.Register(monitor, owner, new HealthCollectorOptions());
        scope.RetainRegistration(registration);
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

    private enum LifecycleScenario { Manual, Required, Null, Optional, Measurements, Numeric, Availability, Newer, StaleMetric }
    private enum ProducerSite { None, Required, Optional, Numeric, Stale }

    private static void AssertLifecycleOwnership(LifecycleObservation observed)
    {
        if (observed.Graph.Length != 0)
        {
            var watch = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && watch.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && watch.Elapsed < Deadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            { throw new InvalidOperationException("Actual Lifecycle owner/runner/manual-clock graph remained reachable; pending work remains a hold."); }
            if (observed.Owners.Any(facts => !facts.ActualWorkReturned) || observed.Timers.Any(timer => Volatile.Read(ref timer.CallbackEntries) != Volatile.Read(ref timer.CallbackReturns)))
            { throw new InvalidOperationException("Actual owner or manual timer work changed during graph observation; its completion remains a hold."); }
            foreach (WeakReference<Task> reference in observed.Observers)
            { if (reference.TryGetTarget(out Task? actual)) { ObserveLifecycle(actual); } }
            Exception? unknown = PreserveUnknownLifecycleFailures(observed.Primary?.SourceException, observed.Owners, observed.Timers);
            if (unknown is not null) { ExceptionDispatchInfo.Capture(unknown).Throw(); }
            TerminalFailure[] failures = observed.Completion.Failures();
            if (failures.Any(failure => failure.Error.GetType() != typeof(InvalidOperationException)))
            { throw new AggregateException("Actual Lifecycle terminal calls had an unknown runtime failure or cancellation.", failures.Select(failure => failure.Error)); }
            Assert.HasCount(1, observed.Timers, "The actual Start must create its private manual timer.");
            foreach (TimerFacts timer in observed.Timers)
            {
                Assert.IsTrue(Volatile.Read(ref timer.DisposalRequests) > 0, "Each actual manual timer requires a real disposal invocation.");
                Assert.IsTrue(Volatile.Read(ref timer.DisposalCompletions) > 0, "Each actual manual timer disposal must return.");
            }
            Assert.AreEqual(0, failures.Length, "Valid Lifecycle terminal calls must not reject shutdown.");
            foreach (string name in observed.RequiredCompletions)
            { Assert.IsTrue(observed.Completion.Succeeded(name), "Actual terminal API must publish successful completion after real ownership is released: " + name); }
        }
        Exception? preserved = PreserveUnknownLifecycleFailures(observed.Primary?.SourceException, observed.Owners, observed.Timers);
        if (preserved is not null) { ExceptionDispatchInfo.Capture(preserved).Throw(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LifecycleObservation RunAndReleaseScenario(LifecycleScenario scenario, int policy, int fault, Action<object>? initializeOwner = null)
    {
        ProducerSite site = scenario switch
        {
            LifecycleScenario.Required when fault is 0 or 2 => ProducerSite.Required,
            LifecycleScenario.Optional when fault is 0 or 2 => ProducerSite.Optional,
            LifecycleScenario.Numeric => ProducerSite.Numeric,
            LifecycleScenario.Newer when fault == 2 => ProducerSite.Stale,
            _ => ProducerSite.None
        };
        var scope = new Scope(trackOwnership: true, expectedProducerSite: site, expectedProducerCount: site == ProducerSite.None ? 0 : 1);
        scope.InitializeOwner = initializeOwner;
        Exception? primary = null;
        try
        {
            Task body = scenario switch
            {
                LifecycleScenario.Manual => ManualEvaluationUsesItsRealOwnerLeaseAndTheSelectedPolicy(scope, policy),
                LifecycleScenario.Required => RequiredExceptionMalformedResultAndUnexpectedCancellationApplyOnePolicyFailure(scope, policy, fault),
                LifecycleScenario.Null => NullRequiredResultBecomesOneEvaluationFailureAndCanRecoverOnTheNextAttempt(scope),
                LifecycleScenario.Optional => OptionalEvaluationErrorsPreserveHealthAndTheLastMeasurementValueAndTime(scope, fault),
                LifecycleScenario.Measurements => OptionalMeasurementsNeverSupplyAnImplicitHealthObservation(scope),
                LifecycleScenario.Numeric => FailedRequiredNumericReadRetainsTheLastGoodValueAndRecoversFromARealCollection(scope),
                LifecycleScenario.Availability => AvailabilityRefreshFailurePreservesDataPublishedByAnEarlierCollection(scope),
                LifecycleScenario.Newer => ANewerDirectObservationDefeatsBothStaleResultsAndStaleEvaluationErrors(scope, fault),
                LifecycleScenario.StaleMetric => AStaleMetricDoesNotDiscardTheFreshHealthResultOrOtherMetricKeys(scope),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            ObserveLifecycle(body);
        }
        catch (Exception error) { primary = error; }
        foreach (Owner owner in scope.TrackedOwners) { owner.ReleaseCallback(); owner.ReleaseCleanup(); }

        // Retained handles cannot be GC-retired before actual BeginStop. Later
        // Dispose calls cannot repair a missed Retire after entries are Closed.
        var actualCalls = new List<(string Name, Task Task)>
        {
            ("runner-stop", BeginLifecycle(() => scope.Runner.StopAsync())),
            ("runner-dispose", BeginLifecycle(() => scope.Runner.DisposeAsync().AsTask()))
        };
        int index = 0;
        foreach (IHealthCollectionRegistration registration in scope.TrackedRegistrations)
        { actualCalls.Add(("registration-" + index++, BeginLifecycle(() => registration.DisposeAsync().AsTask()))); }
        Task[] ownerDisposals = scope.TrackedOwners.Select(owner => BeginLifecycle(owner.BeginOwnerDisposal)).ToArray();
        Task service = BeginLifecycle(() => scope.Service.DisposeAsync().AsTask());
        var completion = new TerminalCompletion(); WeakReference[] graph = []; WeakReference<Task>[] observers = [];
        OwnerFacts[] owners = scope.TrackedOwners.Select(owner => owner.Ownership!).ToArray();
        TimerFacts[] timers = scope.OwnershipClock!.Timers.Select(timer => timer.Facts).ToArray();
        try
        {
            // Neither a known producer failure nor GC supplies a state drain,
            // dependency lifetime completion or actual callback/lease return.
            ObserveLifecycle(Task.WhenAll(ownerDisposals.Append(service)));
            bool candidate = primary is null && owners.Length == 1 && owners.All(facts => facts.ActualWorkReturned)
                && scope.TrackedOwners.All(owner => owner.ActiveLeases == 0 && owner.DependenciesDisposed && owner.ReleasedGates)
                && scope.TrackedRegistrations.Count == 1 && timers.Length == 1
                && timers.All(timer => Volatile.Read(ref timer.CallbackEntries) == Volatile.Read(ref timer.CallbackReturns));
            if (candidate)
            {
                observers = actualCalls.Select(item => ObserveLifecycleCompletion(item.Task, completion, item.Name)).ToArray();
                graph = scope.WeakGraph();
            }
            else { ObserveLifecycle(Task.WhenAll(actualCalls.Select(item => item.Task).Append(service).Concat(ownerDisposals))); }
        }
        catch (Exception cleanup)
        { primary = primary is null ? cleanup : new AggregateException("The original Lifecycle body and actual cleanup both failed.", primary, cleanup); }
        primary = PreserveUnknownLifecycleFailures(primary, owners, timers);
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), graph, completion, observers, owners, timers,
            actualCalls.Select(item => item.Name).ToArray());
    }

    private static Exception? PreserveUnknownLifecycleFailures(Exception? primary, OwnerFacts[] owners, TimerFacts[] timers)
    {
        Exception[] unknown = owners.SelectMany(facts => facts.UnknownErrors()).Concat(timers.SelectMany(timer => timer.UnknownErrors()))
            .Select(failure => failure.Error.SourceException).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        bool unknownCount = owners.Any(facts => facts.HasUnknownFaults);
        if (unknown.Length == 0 && !unknownCount) { return primary; }
        var failures = new List<Exception>();
        if (primary is not null) { failures.Add(primary); }
        failures.AddRange(unknown.Where(error => !ReferenceEquals(error, primary)));
        if (unknown.Length == 0)
        { failures.Add(new InvalidOperationException("Actual Lifecycle owner recorded an unknown acquisition/producer/callback/cleanup fault without an exception object.")); }
        return new AggregateException("Actual Lifecycle unknown owner or manual callback errors remain runtime holds after actual cleanup.", failures);
    }

    private static Task BeginLifecycle(Func<Task> begin)
    { try { return begin(); } catch (Exception error) { return Task.FromException(error); } }
    private static void ObserveLifecycle(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObserveLifecycleCompletion(Task actual, TerminalCompletion completion, string name)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((TerminalCompletion Completion, string Name))state!;
            if (completed.Exception is { } failure)
            { foreach (Exception error in failure.Flatten().InnerExceptions) { observed.Completion.Add(observed.Name, error); } }
            else if (completed.IsCanceled) { observed.Completion.Add(observed.Name, new TaskCanceledException("Actual Lifecycle terminal API was canceled.")); }
            else { observed.Completion.Success(observed.Name); }
        }, (completion, name), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record LifecycleObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph, TerminalCompletion Completion,
        WeakReference<Task>[] Observers, OwnerFacts[] Owners, TimerFacts[] Timers, string[] RequiredCompletions);
    private sealed record TerminalFailure(string Name, Exception Error);
    private sealed class TerminalCompletion
    {
        private readonly object gate = new();
        private readonly List<TerminalFailure> failures = [];
        private readonly HashSet<string> successes = new(StringComparer.Ordinal);
        internal void Add(string name, Exception error) { lock (gate) { failures.Add(new(name, error)); } }
        internal void Success(string name) { lock (gate) { successes.Add(name); } }
        internal bool Succeeded(string name) { lock (gate) { return successes.Contains(name); } }
        internal TerminalFailure[] Failures() { lock (gate) { return [.. failures]; } }
    }

    private sealed record UnknownFailure(string Phase, ProducerSite Site, ExceptionDispatchInfo Error);
    private sealed class OwnerFacts(ProducerSite expectedSite, int expectedCount)
    {
        private readonly object errorGate = new();
        private readonly List<UnknownFailure> unknownErrors = [];
        internal void RecordUnknown(string phase, Exception error)
        { lock (errorGate) { unknownErrors.Add(new(phase, ExpectedSite, ExceptionDispatchInfo.Capture(error))); } }
        internal UnknownFailure[] UnknownErrors() { lock (errorGate) { return [.. unknownErrors]; } }
        internal bool HasUnknownFaults => Volatile.Read(ref UnknownAcquisitionFaults) != 0 || Volatile.Read(ref UnknownCallbackFaults) != 0
            || Volatile.Read(ref UnknownProducerFaults) != 0 || Volatile.Read(ref UnknownCleanupFaults) != 0;
        internal ProducerSite ExpectedSite { get; } = expectedSite;
        private int ExpectedCount { get; } = expectedCount;
        internal int AcquisitionEntries, AcquisitionReturns, AcquiredLeases, UnknownAcquisitionFaults;
        internal int CallbackEntries, CallbackReturns, CancellationRegistrationDisposals, UnknownCallbackFaults;
        internal int ProducerEntries, ProducerTaskReturns, ProducerSuccesses, ProducerSettlements, UnknownProducerFaults;
        internal int ExpectedProducerRecords, PendingProducerRecords, MatchedProducerFailures;
        internal int CleanupEntries, CleanupReturns, CleanupCompletions, UnknownCleanupFaults;
        internal bool ActualWorkReturned => Volatile.Read(ref AcquisitionEntries) > 0
            && Volatile.Read(ref AcquisitionEntries) == Volatile.Read(ref AcquisitionReturns)
            && Volatile.Read(ref AcquisitionEntries) == Volatile.Read(ref AcquiredLeases)
            && Volatile.Read(ref UnknownAcquisitionFaults) == 0 && Volatile.Read(ref UnknownCallbackFaults) == 0
            && Volatile.Read(ref UnknownProducerFaults) == 0 && Volatile.Read(ref UnknownCleanupFaults) == 0
            && Volatile.Read(ref CallbackEntries) == Volatile.Read(ref AcquiredLeases)
            && Volatile.Read(ref CallbackEntries) == Volatile.Read(ref CallbackReturns)
            && Volatile.Read(ref CallbackEntries) == Volatile.Read(ref CancellationRegistrationDisposals)
            && Volatile.Read(ref CallbackEntries) == Volatile.Read(ref ProducerEntries)
            && Volatile.Read(ref ProducerEntries) == Volatile.Read(ref ProducerTaskReturns)
            && Volatile.Read(ref ProducerEntries) == Volatile.Read(ref ProducerSettlements)
            && Volatile.Read(ref ProducerSuccesses) + Volatile.Read(ref MatchedProducerFailures) == Volatile.Read(ref ProducerSettlements)
            && Volatile.Read(ref ExpectedProducerRecords) == ExpectedCount && Volatile.Read(ref MatchedProducerFailures) == ExpectedCount
            && Volatile.Read(ref PendingProducerRecords) == 0
            && Volatile.Read(ref CleanupEntries) == Volatile.Read(ref AcquiredLeases)
            && Volatile.Read(ref CleanupEntries) == Volatile.Read(ref CleanupReturns)
            && Volatile.Read(ref CleanupEntries) == Volatile.Read(ref CleanupCompletions);
    }

    private sealed class TimerFacts
    {
        internal int CallbackEntries, CallbackReturns, DisposalRequests, DisposalCompletions;
        private readonly object errorGate = new();
        private readonly List<UnknownFailure> unknownErrors = [];
        internal void RecordUnknown(Exception error)
        { lock (errorGate) { unknownErrors.Add(new("manual-timer-callback", ProducerSite.None, ExceptionDispatchInfo.Capture(error))); } }
        internal UnknownFailure[] UnknownErrors() { lock (errorGate) { return [.. unknownErrors]; } }
    }
    private sealed class TrackingClock(ManualTimeProvider inner) : TimeProvider
    {
        internal List<TrackedTimer> Timers { get; } = [];
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TrackedTimer(callback, state);
            timer.Initialize(inner.CreateTimer(static item => ((TrackedTimer)item!).Invoke(), timer, dueTime, period));
            Timers.Add(timer);
            return timer;
        }
        // The scenario's original Clock.Advance calls the same private provider
        // synchronously. Its real timer callback entries/returns are observed.
        internal WeakReference[] WeakGraph() => [new(this), new(inner), .. Timers.SelectMany(timer => timer.WeakGraph())];
    }
    private sealed class TrackedTimer(TimerCallback callback, object? state) : ITimer
    {
        private ITimer? actual;
        internal TimerFacts Facts { get; } = new();
        internal void Initialize(ITimer created) => actual = created;
        internal void Invoke()
        {
            Interlocked.Increment(ref Facts.CallbackEntries);
            try { callback(state); }
            catch (Exception error) { Facts.RecordUnknown(error); throw; }
            finally { Interlocked.Increment(ref Facts.CallbackReturns); }
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) => actual!.Change(dueTime, period);
        public void Dispose()
        {
            Interlocked.Increment(ref Facts.DisposalRequests); actual!.Dispose();
            Interlocked.Increment(ref Facts.DisposalCompletions);
        }
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Facts.DisposalRequests); await actual!.DisposeAsync().ConfigureAwait(false);
            Interlocked.Increment(ref Facts.DisposalCompletions);
        }
        internal WeakReference[] WeakGraph() => [new(this), new(actual!)];
    }

    private sealed class PeriodicDispatchClock(ManualTimeProvider inner) : TimeProvider
    {
        private TaskCompletionSource<TimeSpan>? nextChange;
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override long GetTimestamp() => inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        internal Task<TimeSpan> NextChangeAsync()
        {
            var next = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref nextChange, next, null) is not null)
            { throw new InvalidOperationException("The previous actual timer Change observation is still armed."); }
            return next.Task.WaitAsync(Deadline);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new ObservedTimer(inner.CreateTimer(callback, state, dueTime, period), this);
        private sealed class ObservedTimer(ITimer timer, PeriodicDispatchClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = timer.Change(dueTime, period);
                if (changed) { Interlocked.Exchange(ref clock.nextChange, null)?.TrySetResult(dueTime); }
                return changed;
            }
            public void Dispose() => timer.Dispose();
            public ValueTask DisposeAsync() => timer.DisposeAsync();
        }
    }

    private sealed class Scope : IAsyncDisposable
    {
        private readonly List<Owner> owners = [];
        private readonly List<IHealthMonitor> monitors = [];
        internal ManualTimeProvider Clock { get; } = new(Epoch);
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal PeriodicDispatchClock? DispatchClock { get; }

        internal TrackingClock? OwnershipClock { get; }
        internal ProducerSite ExpectedProducerSite { get; }
        internal int ExpectedProducerCount { get; }
        internal Action<object>? InitializeOwner { get; set; }
        private readonly List<IHealthCollectionRegistration> registrations = [];
        internal void RetainRegistration(IHealthCollectionRegistration registration) => registrations.Add(registration);

        internal Scope(int concurrency = 4, TimeProvider? timeProvider = null, bool trackOwnership = false, ProducerSite expectedProducerSite = ProducerSite.None, int expectedProducerCount = 0, bool observePeriodicDispatch = false)
        {
            if (trackOwnership)
            {
                if (timeProvider is not null) { throw new InvalidOperationException("Selected lifecycle ownership requires its private manual clock."); }
                OwnershipClock = new TrackingClock(Clock);
            }
            ExpectedProducerSite = expectedProducerSite;
            ExpectedProducerCount = expectedProducerCount;
            if (observePeriodicDispatch)
            {
                if (timeProvider is not null || trackOwnership) { throw new InvalidOperationException("Periodic dispatch observation requires the original private manual clock."); }
                DispatchClock = new(Clock);
            }
            Service = new HealthcheckService(new HealthcheckOptions { MaxConcurrentCollections = concurrency }, timeProvider: timeProvider ?? (TimeProvider?)OwnershipClock ?? (TimeProvider?)DispatchClock ?? Clock);
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
            if (OwnershipClock is not null) { owner.Ownership = new OwnerFacts(ExpectedProducerSite, ExpectedProducerCount); }
            owners.Add(owner);
            InitializeOwner?.Invoke(owner);
            return owner;
        }

        internal HealthMonitorSnapshot Only() => Service.CaptureSnapshot().Monitors.Single();
        internal IReadOnlyList<Owner> TrackedOwners => owners;
        internal IReadOnlyList<IHealthCollectionRegistration> TrackedRegistrations => registrations;

        internal WeakReference[] WeakGraph() => [new(this), new(Service), new(Runner), new(Clock),
            .. monitors.Select(monitor => new WeakReference(monitor)), .. registrations.Select(registration => new WeakReference(registration)),
            .. owners.SelectMany(owner => new[] { new WeakReference(owner), new WeakReference(owner.Dependencies) }), .. OwnershipClock!.WeakGraph()];

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
        internal Exception? AcquisitionProtocolFailure { get; set; }
        internal Exception? CleanupFault { get; set; }
        internal int Acquisitions { get { lock (gate) { return acquisitions; } } }
        internal int Callbacks { get { lock (gate) { return callbacks; } } }
        internal int Cleanups { get { lock (gate) { return cleanups; } } }
        internal int ActiveLeases { get { lock (gate) { return activeLeases; } } }
        internal bool DependenciesDisposed { get { lock (gate) { return dependenciesDisposed; } } }

        internal OwnerFacts? Ownership { get; set; }
        private readonly Dictionary<Exception, ProducerSite> expectedProducerFailures = new(ReferenceEqualityComparer.Instance);

        internal T ProducerFailure<T>(ProducerSite site, T actualCreatedError) where T : Exception
        {
            OwnerFacts facts = Ownership ?? throw new InvalidOperationException("Producer provenance is limited to selected ownership scenarios.");
            lock (gate)
            {
                expectedProducerFailures.Add(actualCreatedError, site);
                Interlocked.Increment(ref facts.ExpectedProducerRecords);
                Interlocked.Increment(ref facts.PendingProducerRecords);
                if (site != facts.ExpectedSite || site == ProducerSite.None)
                { Interlocked.Increment(ref facts.UnknownProducerFaults); facts.RecordUnknown("producer-registration", actualCreatedError); }
            }
            return actualCreatedError;
        }

        private bool MatchProducerFailure(Exception actualError)
        {
            OwnerFacts facts = Ownership!;
            lock (gate)
            {
                if (!expectedProducerFailures.Remove(actualError, out ProducerSite site) || site != facts.ExpectedSite || site == ProducerSite.None) { return false; }
                Interlocked.Decrement(ref facts.PendingProducerRecords);
                Interlocked.Increment(ref facts.MatchedProducerFailures);
                return true;
            }
        }

        internal bool ReleasedGates => callbackRelease.Task.IsCompletedSuccessfully && cleanupRelease.Task.IsCompletedSuccessfully;

        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            if (Ownership is not { } facts) { return TryAcquireOriginal(out lease); }
            Interlocked.Increment(ref facts.AcquisitionEntries);
            try
            {
                bool acquired = TryAcquireOriginal(out lease);
                if (acquired && lease is not null) { Interlocked.Increment(ref facts.AcquiredLeases); }
                else { Interlocked.Increment(ref facts.UnknownAcquisitionFaults); }
                Interlocked.Increment(ref facts.AcquisitionReturns);
                return acquired;
            }
            catch (Exception error) { Interlocked.Increment(ref facts.UnknownAcquisitionFaults); facts.RecordUnknown("owner-acquisition", error); throw; }
        }

        private bool TryAcquireOriginal(out IHealthCollectionLease? lease)
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
            if (AcquisitionProtocol == 3) { throw AcquisitionProtocolFailure ?? new InvalidOperationException("Acquisition assigned a lease and then threw."); }
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

            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) =>
                owner.Ownership is null ? CollectOriginalAsync(lifetimeCancellation) : CollectTrackedAsync(lifetimeCancellation);

            private async Task<HealthCollectionResult> CollectTrackedAsync(CancellationToken lifetimeCancellation)
            {
                OwnerFacts facts = owner.Ownership!;
                Interlocked.Increment(ref facts.CallbackEntries);
                Exception? matchedProducerFailure = null;
                try
                {
                    lock (owner.gate)
                    {
                        Assert.IsFalse(owner.dependenciesDisposed, "The acquired lease must protect the actual owner dependencies.");
                        owner.callbacks++;
                    }
                    CancellationTokenRegistration cancellation = lifetimeCancellation.Register(owner.LifetimeCanceled.Set);
                    try
                    {
                        owner.CallbackEntered.Set();
                        await owner.callbackRelease.Task;
                        lock (owner.gate) { Assert.IsFalse(owner.dependenciesDisposed); }
                        Interlocked.Increment(ref facts.ProducerEntries);
                        Task<HealthCollectionResult> actual;
                        try { actual = owner.Evaluate(lifetimeCancellation); }
                        catch (Exception error) { facts.RecordUnknown("producer-invocation", error); throw; }
                        Interlocked.Increment(ref facts.ProducerTaskReturns);
                        try
                        {
                            HealthCollectionResult result = await actual;
                            Interlocked.Increment(ref facts.ProducerSuccesses);
                            return result;
                        }
                        catch (Exception error)
                        {
                            // This exact object was registered at its original
                            // fixture producer site. Same type/message is insufficient.
                            if (actual.IsFaulted && owner.MatchProducerFailure(error)) { matchedProducerFailure = error; }
                            else { Interlocked.Increment(ref facts.UnknownProducerFaults); facts.RecordUnknown("producer-task", error); }
                            throw;
                        }
                        finally { Interlocked.Increment(ref facts.ProducerSettlements); }
                    }
                    finally
                    {
                        cancellation.Dispose();
                        Interlocked.Increment(ref facts.CancellationRegistrationDisposals);
                    }
                }
                catch (Exception error)
                {
                    if (!ReferenceEquals(error, matchedProducerFailure))
                    { Interlocked.Increment(ref facts.UnknownCallbackFaults); facts.RecordUnknown("owner-callback", error); }
                    throw;
                }
                finally { Interlocked.Increment(ref facts.CallbackReturns); }
            }

            private async Task<HealthCollectionResult> CollectOriginalAsync(CancellationToken lifetimeCancellation)
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

            public ValueTask DisposeAsync()
            {
                if (owner.Ownership is not { } facts) { return DisposeOriginalAsync(); }
                Interlocked.Increment(ref facts.CleanupEntries);
                ValueTask actual;
                try { actual = DisposeOriginalAsync(); Interlocked.Increment(ref facts.CleanupReturns); }
                catch (Exception error) { Interlocked.Increment(ref facts.UnknownCleanupFaults); facts.RecordUnknown("cleanup-invocation", error); throw; }
                return ObserveCleanupAsync(actual, facts);
            }

            private static async ValueTask ObserveCleanupAsync(ValueTask actual, OwnerFacts facts)
            {
                try { await actual.ConfigureAwait(false); Interlocked.Increment(ref facts.CleanupCompletions); }
                catch (Exception error) { Interlocked.Increment(ref facts.UnknownCleanupFaults); facts.RecordUnknown("cleanup-task", error); throw; }
            }

            private async ValueTask DisposeOriginalAsync()
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
        internal Exception? CompletionFailure { get; init; }
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
                throw CompletionFailure ?? new InvalidOperationException("One completion UTC sample failed.");
            }
            return inner.GetUtcNow();
        }

        public override long GetTimestamp()
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref samplingThread) && Interlocked.Exchange(ref nextTimestamp, 0) != 0)
            {
                Interlocked.Increment(ref faults);
                throw CompletionFailure ?? new InvalidOperationException("One completion monotonic sample failed.");
            }
            return inner.GetTimestamp();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }
}
