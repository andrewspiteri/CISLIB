using CIS.Healthcheck;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionHeartbeatFactTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AdapterPolicyValidationReusesHealthcheckRulesWithoutBindingOrChangingState()
    {
        await using var service = Service();
        var revision = service.CaptureSnapshot().Revision;
        HealthExecutionBinder.ValidatePolicy(new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 3 }, false);
        HealthExecutionBinder.ValidatePolicy(new AvailabilityMonitorOptions { IsCritical = true }, true);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HealthExecutionBinder.ValidatePolicy(
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 0 }, false));
        Assert.ThrowsExactly<ArgumentException>(() => HealthExecutionBinder.ValidatePolicy(
            new AvailabilityMonitorOptions { IsCritical = true }, false));
        Assert.ThrowsExactly<ArgumentException>(() => HealthExecutionBinder.ValidatePolicy(
            new AvailabilityMonitorOptions { IsCritical = true, Name = new string('x', 129) }, true));
        Assert.AreEqual(revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
    }

    [TestMethod]
    public async Task DispatchRetainsCapacityWaitWithoutCreatingAPolicyOutcome()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Admitted,
            OccurredAtUtc = Epoch, AdmittedAtUtc = Epoch, EligibleAtUtc = Epoch.AddMilliseconds(-3), QueueDelayMilliseconds = 3
        });
        DateTimeOffset dispatched = Epoch.AddMilliseconds(17).ToOffset(TimeSpan.FromHours(2));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Dispatched,
            OccurredAtUtc = dispatched, DispatchedAtUtc = dispatched, DispatchDelayMilliseconds = 17
        }));

        HealthOutcomeSnapshot outcomes = Only(service).Outcomes!;
        HealthExecutionSnapshot execution = outcomes.Execution!;
        Assert.AreEqual(HealthExecutionPhase.Admitted, execution.Phase);
        Assert.AreEqual(Epoch.AddMilliseconds(17), execution.DispatchedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, execution.DispatchedAtUtc!.Value.Offset);
        Assert.AreEqual(3d, execution.QueueDelayMilliseconds);
        Assert.AreEqual(17d, execution.DispatchDelayMilliseconds);
        Assert.AreEqual(0L, outcomes.Successful + outcomes.Failed + outcomes.Skipped + outcomes.ExpectedCanceled);
    }

    [TestMethod]
    public async Task SuppressionIsDeduplicatedNeutralAndDistinctFromActualSettlement()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        HealthExecutionFact suppressed = Suppression(binding, 1);
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(suppressed));
        HealthSnapshot beforeRepeat = service.CaptureSnapshot();
        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(suppressed));
        Assert.AreEqual(beforeRepeat.Revision, service.CaptureSnapshot().Revision);
        Assert.IsNull(Only(service).Outcomes!.Execution!.SettledAtUtc);

        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Settled,
            OccurredAtUtc = Epoch.AddMilliseconds(5), SettledAtUtc = Epoch.AddMilliseconds(5),
            TotalDurationMilliseconds = 5, CleanupStatus = HealthExecutionStepStatus.Succeeded,
            ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable
        }));
        HealthOutcomeSnapshot outcomes = Only(service).Outcomes!;
        Assert.AreEqual(HealthExecutionPhase.Settled, outcomes.Execution!.Phase);
        Assert.AreEqual(HealthExecutionPhase.Callback, outcomes.Execution.SuppressedPhase);
        Assert.AreEqual(HealthExecutionSuppressionReason.ApplicationStopping, outcomes.Execution.SuppressionReason);
        Assert.AreEqual(0L, outcomes.Successful + outcomes.Failed + outcomes.Skipped + outcomes.ExpectedCanceled);
    }

    [TestMethod]
    public async Task RetainedSuppressionContradictionsLeaveTheAcceptedFactUnchanged()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(Suppression(binding, 1));
        var revision = service.CaptureSnapshot().Revision;
        Assert.ThrowsExactly<InvalidOperationException>(() => binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Suppressed,
            OccurredAtUtc = Epoch, SuppressedAtUtc = Epoch, SuppressedPhase = HealthExecutionPhase.Callback,
            SuppressionReason = HealthExecutionSuppressionReason.TargetUnavailable
        }));
        Assert.AreEqual(revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(HealthExecutionSuppressionReason.ApplicationStopping, Only(service).Outcomes!.Execution!.SuppressionReason);
    }

    [TestMethod]
    public async Task StartedTrueConditionCanRetainSuppressedCallbackAndOneCleanupFailureOutcome()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.ConditionStarted,
            OccurredAtUtc = Epoch.AddMilliseconds(-1), ConditionStartedAtUtc = Epoch.AddMilliseconds(-1)
        });
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Suppressed,
            OccurredAtUtc = Epoch, SuppressedAtUtc = Epoch, SuppressedPhase = HealthExecutionPhase.Callback,
            SuppressionReason = HealthExecutionSuppressionReason.ApplicationStopping, ConditionResult = true
        });
        var cleanup = new HealthDiagnostic("Heartbeat.OwnerRelease", "Cleanup failed after the condition returned.");
        HealthExecutionFact final = new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = HealthExecutionPhase.Condition,
            Outcome = HealthExecutionOutcome.Failed, ConditionResult = true,
            CleanupStatus = HealthExecutionStepStatus.Failed, CleanupError = cleanup,
            CleanupCompletedAtUtc = Epoch.AddMilliseconds(9), ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable
        };
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(final));
        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(final));
        HealthOutcomeSnapshot outcomes = Only(service).Outcomes!;
        Assert.AreEqual(1L, outcomes.Failed);
        Assert.AreEqual(0L, outcomes.Skipped);
        Assert.IsTrue(outcomes.Execution!.ConditionResult);
        Assert.IsNull(outcomes.Execution.CallbackResult);
        Assert.AreSame(cleanup, outcomes.Execution.CleanupError);
        Assert.IsNull(outcomes.Execution.OriginalError);
        Assert.AreEqual(Epoch, outcomes.Execution.WorkCompletedAtUtc);
        Assert.AreEqual(Epoch.AddMilliseconds(9), outcomes.Execution.CleanupCompletedAtUtc);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CleanupFailureRetainsTheActualCallbackBoolean(bool callbackResult)
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Failed, ConditionResult = true, CallbackResult = callbackResult,
            CleanupStatus = HealthExecutionStepStatus.Failed, CleanupError = new("Heartbeat.Cleanup")
        });
        HealthOutcomeSnapshot outcomes = Only(service).Outcomes!;
        Assert.AreEqual(1L, outcomes.Failed);
        Assert.AreEqual(callbackResult, outcomes.Execution!.CallbackResult);
        Assert.IsTrue(outcomes.Execution.ConditionResult);
        Assert.IsNull(outcomes.Execution.OriginalError);
    }

    [TestMethod]
    public async Task CleanupFailureRetainsAFalseConditionInsteadOfCountingASkip()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = HealthExecutionPhase.Condition,
            Outcome = HealthExecutionOutcome.Failed, ConditionResult = false,
            CleanupStatus = HealthExecutionStepStatus.Failed, CleanupError = new("Heartbeat.Cleanup")
        });
        Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
        Assert.AreEqual(0L, Only(service).Outcomes!.Skipped);
        Assert.IsFalse(Only(service).Outcomes!.Execution!.ConditionResult);
    }

    [TestMethod]
    [DataRow(HealthExecutionPhase.Condition)]
    [DataRow(HealthExecutionPhase.Callback)]
    public async Task CleanupFailureRetainsPreviouslyAcknowledgedCancellation(HealthExecutionPhase phase)
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = phase,
            Outcome = HealthExecutionOutcome.Failed, ExpectedCancellationPhase = phase,
            CleanupStatus = HealthExecutionStepStatus.Failed, CleanupError = new("Heartbeat.Cleanup")
        });
        Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
        Assert.AreEqual(0L, Only(service).Outcomes!.ExpectedCanceled);
        HealthExecutionPhase? expectedPhase = phase;
        HealthExecutionPhase? actualPhase = Only(service).Outcomes!.Execution!.ExpectedCancellationPhase;
        Assert.AreEqual(expectedPhase, actualPhase);
    }

    [TestMethod]
    public async Task CleanupFaultWhileWithdrawingUnstartedWorkCreatesNoPolicyOutcome()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(Suppression(binding, 1));
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.IntegrationFault,
            OccurredAtUtc = Epoch, IntegrationError = new("Heartbeat.Cleanup"), CleanupStatus = HealthExecutionStepStatus.Failed,
            CleanupError = new("Heartbeat.Cleanup")
        });
        HealthOutcomeSnapshot outcomes = Only(service).Outcomes!;
        Assert.AreEqual(0L, outcomes.Successful + outcomes.Failed + outcomes.Skipped + outcomes.ExpectedCanceled);
        Assert.IsNotNull(outcomes.Execution!.CleanupError);
    }

    [TestMethod]
    [DataRow("dispatch-time")]
    [DataRow("negative-delay")]
    [DataRow("nan-delay")]
    [DataRow("infinite-delay")]
    [DataRow("suppression-reason")]
    [DataRow("suppression-phase")]
    [DataRow("started-suppression")]
    [DataRow("false-condition-callback")]
    [DataRow("boolean-cancellation")]
    [DataRow("false-success")]
    [DataRow("true-failure-without-cleanup")]
    [DataRow("true-cleanup-success")]
    [DataRow("true-condition-skip")]
    [DataRow("registration-context")]
    public async Task InvalidAddedContextIsRejectedWithoutChangingState(string scenario)
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        var revision = service.CaptureSnapshot().Revision;
        Assert.ThrowsExactly<ArgumentException>(() => binding.Report(InvalidFact(binding, scenario)));
        Assert.AreEqual(revision, service.CaptureSnapshot().Revision);
        Assert.IsNull(Only(service).Outcomes!.Execution);
    }

    [TestMethod]
    public async Task BooleanContradictionAcrossFactsCannotReplaceTheRecordedResult()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = Epoch, WorkCompletedAtUtc = Epoch, SourcePhase = HealthExecutionPhase.Callback,
            Outcome = HealthExecutionOutcome.Failed, CallbackResult = false
        });
        var revision = service.CaptureSnapshot().Revision;
        Assert.ThrowsExactly<InvalidOperationException>(() => binding.Report(new()
        {
            RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.IntegrationFault,
            OccurredAtUtc = Epoch, IntegrationError = new("Heartbeat.Reporting"), CallbackResult = true
        }));
        Assert.AreEqual(revision, service.CaptureSnapshot().Revision);
        Assert.IsFalse(Only(service).Outcomes!.Execution!.CallbackResult);
    }

    [TestMethod]
    public async Task SuppressionUsesExistingBoundedHistoryWithoutCountingAnOutcome()
    {
        await using var service = Service();
        await using IHealthExecutionBinding binding = Bind(service);
        binding.Report(Suppression(binding, 1));
        binding.Report(Suppression(binding, 2));
        binding.Report(Suppression(binding, 3));
        Assert.AreEqual(HealthFactDisposition.Obsolete, binding.Report(Suppression(binding, 1)));
        Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Suppression(binding, 2)));
        Assert.AreEqual(3L, Only(service).Outcomes!.Execution!.ExecutionId);
        Assert.AreEqual(0L, Only(service).Outcomes!.Failed);
    }

    private static HealthExecutionFact InvalidFact(IHealthExecutionBinding binding, string scenario) => new()
    {
        RegistrationId = binding.RegistrationId,
        ExecutionId = scenario == "registration-context" ? null : 1,
        Kind = scenario switch
        {
            "dispatch-time" or "negative-delay" or "nan-delay" or "infinite-delay" => HealthExecutionFactKind.Dispatched,
            "suppression-reason" or "suppression-phase" or "started-suppression" => HealthExecutionFactKind.Suppressed,
            "registration-context" => HealthExecutionFactKind.Registered,
            _ => HealthExecutionFactKind.Outcome
        },
        OccurredAtUtc = Epoch,
        DispatchedAtUtc = scenario.Contains("delay", StringComparison.Ordinal) ? Epoch : null,
        DispatchDelayMilliseconds = scenario switch { "negative-delay" => -1, "nan-delay" => double.NaN, "infinite-delay" => double.PositiveInfinity, _ => null },
        SuppressedAtUtc = scenario is "suppression-reason" or "suppression-phase" or "started-suppression" ? Epoch : null,
        SuppressedPhase = scenario is "suppression-reason" or "started-suppression" ? HealthExecutionPhase.Callback
            : scenario == "suppression-phase" ? HealthExecutionPhase.Settled : null,
        SuppressionReason = scenario is "suppression-phase" or "started-suppression" ? HealthExecutionSuppressionReason.ApplicationStopping
            : scenario == "suppression-reason" ? (HealthExecutionSuppressionReason)42 : null,
        CallbackStartedAtUtc = scenario == "started-suppression" ? Epoch : null,
        ConditionResult = scenario == "false-condition-callback" ? false : scenario == "true-condition-skip" ? true : null,
        CallbackResult = scenario switch { "false-condition-callback" or "boolean-cancellation" or "true-failure-without-cleanup" or "registration-context" or "true-cleanup-success" => true, "false-success" => false, _ => null },
        CleanupStatus = scenario == "true-cleanup-success" ? HealthExecutionStepStatus.Failed : null,
        ExpectedCancellationPhase = scenario == "boolean-cancellation" ? HealthExecutionPhase.Callback : null,
        SourcePhase = scenario is "false-condition-callback" or "boolean-cancellation" or "false-success" or "true-failure-without-cleanup" or "true-cleanup-success" ? HealthExecutionPhase.Callback
            : scenario == "true-condition-skip" ? HealthExecutionPhase.Condition : null,
        Outcome = scenario == "boolean-cancellation" ? HealthExecutionOutcome.ExpectedCancellation
            : scenario == "true-failure-without-cleanup" ? HealthExecutionOutcome.Failed
            : scenario is "false-condition-callback" or "false-success" or "true-cleanup-success" ? HealthExecutionOutcome.Succeeded
            : scenario == "true-condition-skip" ? HealthExecutionOutcome.Skipped : null,
        WorkCompletedAtUtc = scenario is "false-condition-callback" or "boolean-cancellation" or "false-success" or "true-failure-without-cleanup" or "true-cleanup-success" or "true-condition-skip" ? Epoch : null
    };

    private static HealthExecutionFact Suppression(IHealthExecutionBinding binding, long id) => new()
    {
        RegistrationId = binding.RegistrationId, ExecutionId = id, Kind = HealthExecutionFactKind.Suppressed,
        OccurredAtUtc = Epoch, SuppressedAtUtc = Epoch, SuppressedPhase = HealthExecutionPhase.Callback,
        SuppressionReason = HealthExecutionSuppressionReason.ApplicationStopping
    };

    private static HealthcheckService Service() => new(new HealthcheckOptions(), timeProvider: new ManualTimeProvider(Epoch));
    private static IHealthExecutionBinding Bind(HealthcheckService service) => HealthExecutionBinder.Bind(service,
        new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 2 }, new() { RegistrationId = Guid.NewGuid() });
    private static HealthMonitorSnapshot Only(HealthcheckService service) => service.CaptureSnapshot().Monitors.Single();
}
