using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionFactValidationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("empty-registration")]
    [DataRow("foreign-registration")]
    [DataRow("undefined-kind")]
    [DataRow("missing-execution")]
    [DataRow("zero-execution")]
    [DataRow("negative-execution")]
    [DataRow("registered-execution")]
    [DataRow("retired-execution")]
    [DataRow("undefined-outcome")]
    [DataRow("undefined-phase")]
    [DataRow("undefined-handler")]
    [DataRow("undefined-cleanup")]
    public async Task InvalidIdentitiesAndEnumValuesAreRejectedWithoutAnyObservation(string scenario)
    {
        await using var scope = new FactScope();
        var input = ValidOutcome(scope.Id);
        switch (scenario)
        {
            case "empty-registration": input.RegistrationId = Guid.Empty; break;
            case "foreign-registration": input.RegistrationId = Guid.NewGuid(); break;
            case "undefined-kind": input.Kind = (HealthExecutionFactKind)int.MaxValue; break;
            case "missing-execution": input.ExecutionId = null; break;
            case "zero-execution": input.ExecutionId = 0; break;
            case "negative-execution": input.ExecutionId = long.MinValue; break;
            case "registered-execution": input = new(scope.Id, HealthExecutionFactKind.Registered) { ExecutionId = 1 }; break;
            case "retired-execution": input = new(scope.Id, HealthExecutionFactKind.Retired) { ExecutionId = 1 }; break;
            case "undefined-outcome": input.Outcome = (HealthExecutionOutcome)int.MaxValue; break;
            case "undefined-phase": input.SourcePhase = (HealthExecutionPhase)int.MaxValue; break;
            case "undefined-handler": input.ErrorHandlingStatus = (HealthExecutionStepStatus)int.MaxValue; break;
            case "undefined-cleanup": input.CleanupStatus = (HealthExecutionStepStatus)int.MaxValue; break;
            default: throw new AssertFailedException("Unknown identity/enum case.");
        }
        AssertRejectedWithoutMutation(scope, input.Build());
    }

    [TestMethod]
    public async Task NullFactIsRejectedWithoutMutatingTheBinding()
    {
        await using var scope = new FactScope();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.Binding.Report(null!));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(ValidOutcome(scope.Id).Build()));
    }

    [TestMethod]
    [DataRow(HealthExecutionFactKind.Eligible)]
    [DataRow(HealthExecutionFactKind.Admitted)]
    [DataRow(HealthExecutionFactKind.ConditionStarted)]
    [DataRow(HealthExecutionFactKind.CallbackStarted)]
    [DataRow(HealthExecutionFactKind.Outcome)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingStarted)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingCompleted)]
    [DataRow(HealthExecutionFactKind.CleanupCompleted)]
    [DataRow(HealthExecutionFactKind.Settled)]
    public async Task EveryExecutionKindRequiresItsOwnActualEventTimestamp(HealthExecutionFactKind kind)
    {
        await using var scope = new FactScope();
        var missing = ValidFact(scope.Id, kind);
        SetEventTime(missing, null);
        AssertRejectedWithoutMutation(scope, missing.Build());
    }

    [TestMethod]
    [DataRow(HealthExecutionFactKind.Eligible)]
    [DataRow(HealthExecutionFactKind.Admitted)]
    [DataRow(HealthExecutionFactKind.ConditionStarted)]
    [DataRow(HealthExecutionFactKind.CallbackStarted)]
    [DataRow(HealthExecutionFactKind.Outcome)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingStarted)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingCompleted)]
    [DataRow(HealthExecutionFactKind.CleanupCompleted)]
    [DataRow(HealthExecutionFactKind.Settled)]
    public async Task EventTimestampMustEqualOccurredAtByInstant(HealthExecutionFactKind kind)
    {
        await using var scope = new FactScope();
        var mismatched = ValidFact(scope.Id, kind);
        SetEventTime(mismatched, Epoch.AddTicks(1));
        AssertRejectedWithoutMutation(scope, mismatched.Build());
    }

    [TestMethod]
    [DataRow(HealthExecutionFactKind.Eligible)]
    [DataRow(HealthExecutionFactKind.Admitted)]
    [DataRow(HealthExecutionFactKind.ConditionStarted)]
    [DataRow(HealthExecutionFactKind.CallbackStarted)]
    [DataRow(HealthExecutionFactKind.Outcome)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingStarted)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingCompleted)]
    [DataRow(HealthExecutionFactKind.CleanupCompleted)]
    [DataRow(HealthExecutionFactKind.Settled)]
    public async Task MatchingEventTimestampsWithDifferentOffsetsAreNormalized(HealthExecutionFactKind kind)
    {
        await using var scope = new FactScope();
        var input = ValidFact(scope.Id, kind);
        input.OccurredAtUtc = Epoch.ToOffset(TimeSpan.FromHours(3));
        SetEventTime(input, Epoch.ToOffset(TimeSpan.FromHours(-5)));

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(input.Build()));

        HealthExecutionSnapshot execution = scope.Monitor.Outcomes!.Execution!;
        DateTimeOffset? actual = kind switch
        {
            HealthExecutionFactKind.Eligible => execution.EligibleAtUtc,
            HealthExecutionFactKind.Admitted => execution.AdmittedAtUtc,
            HealthExecutionFactKind.ConditionStarted => execution.ConditionStartedAtUtc,
            HealthExecutionFactKind.CallbackStarted => execution.CallbackStartedAtUtc,
            HealthExecutionFactKind.Outcome => execution.WorkCompletedAtUtc,
            HealthExecutionFactKind.ErrorHandlingStarted => execution.ErrorHandlingStartedAtUtc,
            HealthExecutionFactKind.ErrorHandlingCompleted => execution.ErrorHandlingCompletedAtUtc,
            HealthExecutionFactKind.CleanupCompleted => execution.CleanupCompletedAtUtc,
            HealthExecutionFactKind.Settled => execution.SettledAtUtc,
            _ => throw new AssertFailedException("Unknown event kind.")
        };
        Assert.AreEqual(Epoch, actual);
        Assert.AreEqual(TimeSpan.Zero, actual!.Value.Offset);
        Assert.AreEqual(kind == HealthExecutionFactKind.Outcome ? 1L : 0L, scope.Monitor.Outcomes!.Failed);
    }

    [TestMethod]
    [DataRow("missing-outcome")]
    [DataRow("missing-source")]
    [DataRow("wrong-source")]
    [DataRow("condition-success")]
    [DataRow("callback-skip")]
    [DataRow("premature-settlement-time")]
    [DataRow("premature-total-duration")]
    [DataRow("success-original-error")]
    [DataRow("skip-original-error")]
    [DataRow("condition-callback-time")]
    [DataRow("condition-callback-duration")]
    public async Task OutcomeMustDescribeExactlyItsActualWorkResult(string scenario)
    {
        await using var scope = new FactScope();
        var input = ValidOutcome(scope.Id);
        switch (scenario)
        {
            case "missing-outcome": input.Outcome = null; break;
            case "missing-source": input.SourcePhase = null; break;
            case "wrong-source": input.SourcePhase = HealthExecutionPhase.Cleanup; break;
            case "condition-success": input.SourcePhase = HealthExecutionPhase.Condition; input.Outcome = HealthExecutionOutcome.Succeeded; break;
            case "callback-skip": input.Outcome = HealthExecutionOutcome.Skipped; break;
            case "premature-settlement-time": input.SettledAtUtc = Epoch; break;
            case "premature-total-duration": input.TotalDurationMilliseconds = 0; break;
            case "success-original-error": input.Outcome = HealthExecutionOutcome.Succeeded; input.OriginalError = new("Unexpected"); break;
            case "skip-original-error": input.Outcome = HealthExecutionOutcome.Skipped; input.SourcePhase = HealthExecutionPhase.Condition; input.OriginalError = new("Unexpected"); break;
            case "condition-callback-time": input.SourcePhase = HealthExecutionPhase.Condition; input.CallbackStartedAtUtc = Epoch; break;
            case "condition-callback-duration": input.SourcePhase = HealthExecutionPhase.Condition; input.CallbackDurationMilliseconds = 1; break;
            default: throw new AssertFailedException("Unknown outcome case.");
        }
        AssertRejectedWithoutMutation(scope, input.Build());
    }

    [TestMethod]
    [DataRow(HealthExecutionPhase.Condition, HealthExecutionOutcome.Skipped)]
    [DataRow(HealthExecutionPhase.Condition, HealthExecutionOutcome.Failed)]
    [DataRow(HealthExecutionPhase.Condition, HealthExecutionOutcome.ExpectedCancellation)]
    [DataRow(HealthExecutionPhase.Callback, HealthExecutionOutcome.Succeeded)]
    [DataRow(HealthExecutionPhase.Callback, HealthExecutionOutcome.Failed)]
    [DataRow(HealthExecutionPhase.Callback, HealthExecutionOutcome.ExpectedCancellation)]
    public async Task EveryPermittedPhaseOutcomePairIsAcceptedWithoutEarlierNotifications(
        HealthExecutionPhase phase, HealthExecutionOutcome outcome)
    {
        await using var scope = new FactScope();
        var input = ValidOutcome(scope.Id);
        input.SourcePhase = phase;
        input.Outcome = outcome;

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(input.Build()));

        HealthOutcomeSnapshot observed = scope.Monitor.Outcomes!;
        Assert.AreEqual(outcome, observed.LastOutcome);
        Assert.AreEqual(outcome == HealthExecutionOutcome.Succeeded ? 1L : 0L, observed.Successful);
        Assert.AreEqual(outcome == HealthExecutionOutcome.Failed ? 1L : 0L, observed.Failed);
        Assert.AreEqual(outcome == HealthExecutionOutcome.Skipped ? 1L : 0L, observed.Skipped);
        long actualCanceledCount = observed.ExpectedCanceled;
        Assert.AreEqual(outcome == HealthExecutionOutcome.ExpectedCancellation ? 1L : 0L, actualCanceledCount);
    }

    [TestMethod]
    [DataRow(HealthExecutionFactKind.Registered)]
    [DataRow(HealthExecutionFactKind.Eligible)]
    [DataRow(HealthExecutionFactKind.Admitted)]
    [DataRow(HealthExecutionFactKind.ConditionStarted)]
    [DataRow(HealthExecutionFactKind.CallbackStarted)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingStarted)]
    [DataRow(HealthExecutionFactKind.ErrorHandlingCompleted)]
    [DataRow(HealthExecutionFactKind.CleanupCompleted)]
    [DataRow(HealthExecutionFactKind.Settled)]
    [DataRow(HealthExecutionFactKind.Retired)]
    [DataRow(HealthExecutionFactKind.IntegrationFault)]
    public async Task NonOutcomeFactsCannotCountOrCarryAnOutcome(HealthExecutionFactKind kind)
    {
        await using var scope = new FactScope();
        var input = ValidFact(scope.Id, kind);
        input.Outcome = HealthExecutionOutcome.Failed;
        AssertRejectedWithoutMutation(scope, input.Build());
    }

    [TestMethod]
    [DataRow("registered-time")]
    [DataRow("registered-duration")]
    [DataRow("registered-diagnostic")]
    [DataRow("registered-status")]
    [DataRow("retired-time")]
    [DataRow("retired-original-error")]
    [DataRow("retired-status")]
    [DataRow("registration-fault-time")]
    [DataRow("registration-fault-status")]
    [DataRow("registration-fault-original-error")]
    [DataRow("integration-fault-without-error")]
    [DataRow("eligible-with-source")]
    [DataRow("cleanup-with-source")]
    [DataRow("eligible-settlement-time")]
    [DataRow("cleanup-total-duration")]
    public async Task RegistrationAndNonSettlementFactsOmitForbiddenContext(string scenario)
    {
        await using var scope = new FactScope();
        Inputs input;
        switch (scenario)
        {
            case "registered-time": input = new(scope.Id, HealthExecutionFactKind.Registered) { EligibleAtUtc = Epoch }; break;
            case "registered-duration": input = new(scope.Id, HealthExecutionFactKind.Registered) { QueueDelayMilliseconds = 0 }; break;
            case "registered-diagnostic": input = new(scope.Id, HealthExecutionFactKind.Registered) { IntegrationError = new("Unexpected") }; break;
            case "registered-status": input = new(scope.Id, HealthExecutionFactKind.Registered) { CleanupStatus = HealthExecutionStepStatus.NotStarted }; break;
            case "retired-time": input = new(scope.Id, HealthExecutionFactKind.Retired) { WorkCompletedAtUtc = Epoch }; break;
            case "retired-original-error": input = new(scope.Id, HealthExecutionFactKind.Retired) { OriginalError = new("Unexpected") }; break;
            case "retired-status": input = new(scope.Id, HealthExecutionFactKind.Retired) { ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable }; break;
            case "registration-fault-time": input = new(scope.Id, HealthExecutionFactKind.IntegrationFault) { IntegrationError = new("Integration"), EligibleAtUtc = Epoch }; break;
            case "registration-fault-status": input = new(scope.Id, HealthExecutionFactKind.IntegrationFault) { IntegrationError = new("Integration"), CleanupStatus = HealthExecutionStepStatus.NotInvoked }; break;
            case "registration-fault-original-error": input = new(scope.Id, HealthExecutionFactKind.IntegrationFault) { IntegrationError = new("Integration"), OriginalError = new("Unexpected") }; break;
            case "integration-fault-without-error": input = new(scope.Id, HealthExecutionFactKind.IntegrationFault); break;
            case "eligible-with-source": input = ValidFact(scope.Id, HealthExecutionFactKind.Eligible); input.SourcePhase = HealthExecutionPhase.Condition; break;
            case "cleanup-with-source": input = ValidFact(scope.Id, HealthExecutionFactKind.CleanupCompleted); input.SourcePhase = HealthExecutionPhase.Cleanup; break;
            case "eligible-settlement-time": input = ValidFact(scope.Id, HealthExecutionFactKind.Eligible); input.SettledAtUtc = Epoch; break;
            case "cleanup-total-duration": input = ValidFact(scope.Id, HealthExecutionFactKind.CleanupCompleted); input.TotalDurationMilliseconds = 0; break;
            default: throw new AssertFailedException("Unknown forbidden-context case.");
        }
        AssertRejectedWithoutMutation(scope, input.Build());
    }

    [TestMethod]
    [DataRow("queue", -1d)]
    [DataRow("queue", double.NaN)]
    [DataRow("queue", double.PositiveInfinity)]
    [DataRow("condition", -1d)]
    [DataRow("condition", double.NaN)]
    [DataRow("condition", double.PositiveInfinity)]
    [DataRow("callback", -1d)]
    [DataRow("callback", double.NaN)]
    [DataRow("callback", double.PositiveInfinity)]
    [DataRow("handler", -1d)]
    [DataRow("handler", double.NaN)]
    [DataRow("handler", double.PositiveInfinity)]
    [DataRow("cleanup", -1d)]
    [DataRow("cleanup", double.NaN)]
    [DataRow("cleanup", double.PositiveInfinity)]
    [DataRow("total", -1d)]
    [DataRow("total", double.NaN)]
    [DataRow("total", double.NegativeInfinity)]
    public async Task EveryMeasuredDurationMustBeFiniteAndNonnegative(string field, double duration)
    {
        await using var scope = new FactScope();
        var input = ValidFact(scope.Id, HealthExecutionFactKind.Settled);
        switch (field)
        {
            case "queue": input.QueueDelayMilliseconds = duration; break;
            case "condition": input.ConditionDurationMilliseconds = duration; break;
            case "callback": input.CallbackDurationMilliseconds = duration; break;
            case "handler": input.ErrorHandlingDurationMilliseconds = duration; break;
            case "cleanup": input.CleanupDurationMilliseconds = duration; break;
            case "total": input.TotalDurationMilliseconds = duration; break;
            default: throw new AssertFailedException("Unknown duration case.");
        }
        AssertRejectedWithoutMutation(scope, input.Build());
    }

    [TestMethod]
    [DataRow("handler-start-missing")]
    [DataRow("handler-start-terminal")]
    [DataRow("handler-end-missing")]
    [DataRow("handler-end-notstarted")]
    [DataRow("handler-end-running")]
    [DataRow("cleanup-missing")]
    [DataRow("cleanup-running")]
    [DataRow("settled-handler-missing")]
    [DataRow("settled-handler-running")]
    [DataRow("settled-cleanup-missing")]
    [DataRow("settled-cleanup-notstarted")]
    public async Task SecondaryLifecycleKindsRequireTheirSpecifiedStatuses(string scenario)
    {
        await using var scope = new FactScope();
        Inputs input;
        switch (scenario)
        {
            case "handler-start-missing": input = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingStarted); input.ErrorHandlingStatus = null; break;
            case "handler-start-terminal": input = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingStarted); input.ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded; break;
            case "handler-end-missing": input = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingCompleted); input.ErrorHandlingStatus = null; break;
            case "handler-end-notstarted": input = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingCompleted); input.ErrorHandlingStatus = HealthExecutionStepStatus.NotStarted; break;
            case "handler-end-running": input = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingCompleted); input.ErrorHandlingStatus = HealthExecutionStepStatus.Running; break;
            case "cleanup-missing": input = ValidFact(scope.Id, HealthExecutionFactKind.CleanupCompleted); input.CleanupStatus = null; break;
            case "cleanup-running": input = ValidFact(scope.Id, HealthExecutionFactKind.CleanupCompleted); input.CleanupStatus = HealthExecutionStepStatus.Running; break;
            case "settled-handler-missing": input = ValidFact(scope.Id, HealthExecutionFactKind.Settled); input.ErrorHandlingStatus = null; break;
            case "settled-handler-running": input = ValidFact(scope.Id, HealthExecutionFactKind.Settled); input.ErrorHandlingStatus = HealthExecutionStepStatus.Running; break;
            case "settled-cleanup-missing": input = ValidFact(scope.Id, HealthExecutionFactKind.Settled); input.CleanupStatus = null; break;
            case "settled-cleanup-notstarted": input = ValidFact(scope.Id, HealthExecutionFactKind.Settled); input.CleanupStatus = HealthExecutionStepStatus.NotStarted; break;
            default: throw new AssertFailedException("Unknown secondary-status case.");
        }
        AssertRejectedWithoutMutation(scope, input.Build());
    }

    [TestMethod]
    [DataRow(HealthExecutionStepStatus.Succeeded)]
    [DataRow(HealthExecutionStepStatus.Failed)]
    [DataRow(HealthExecutionStepStatus.ExpectedCancellation)]
    [DataRow(HealthExecutionStepStatus.NotApplicable)]
    [DataRow(HealthExecutionStepStatus.NotInvoked)]
    public async Task SecondaryCompletionAcceptsEveryTerminalStatusWithoutCreatingAnOutcome(HealthExecutionStepStatus status)
    {
        await using var scope = new FactScope();
        var handler = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingCompleted);
        handler.ErrorHandlingStatus = status;
        var cleanup = ValidFact(scope.Id, HealthExecutionFactKind.CleanupCompleted);
        cleanup.CleanupStatus = status;
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(handler.Build()));
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(cleanup.Build()));
        Assert.AreEqual(status, scope.Monitor.Outcomes!.Execution!.ErrorHandlingStatus);
        Assert.AreEqual(status, scope.Monitor.Outcomes.Execution.CleanupStatus);
        Assert.AreEqual(0L, scope.Monitor.Outcomes.Failed);
        Assert.AreEqual(0L, scope.Monitor.Outcomes.Successful);
        Assert.AreEqual(MonitorState.Pending, scope.Monitor.State);
        Assert.IsNull(scope.Monitor.LastObservationAtUtc);
    }

    [TestMethod]
    public async Task BackwardWallClockTimesDoNotReplaceMeasuredDurationsOrRejectRealFacts()
    {
        await using var scope = new FactScope();
        var start = ValidFact(scope.Id, HealthExecutionFactKind.CallbackStarted);
        start.OccurredAtUtc = Epoch.AddHours(1);
        start.CallbackStartedAtUtc = start.OccurredAtUtc;
        scope.Binding.Report(start.Build());
        var outcome = ValidOutcome(scope.Id);
        outcome.CallbackDurationMilliseconds = 3.25;
        scope.Binding.Report(outcome.Build());
        var settled = ValidFact(scope.Id, HealthExecutionFactKind.Settled);
        settled.OccurredAtUtc = Epoch.AddHours(-1);
        settled.SettledAtUtc = settled.OccurredAtUtc;
        settled.TotalDurationMilliseconds = 7.5;

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(settled.Build()));

        HealthExecutionSnapshot execution = scope.Monitor.Outcomes!.Execution!;
        Assert.AreEqual(Epoch.AddHours(1), execution.CallbackStartedAtUtc);
        Assert.AreEqual(Epoch, execution.WorkCompletedAtUtc);
        Assert.AreEqual(Epoch.AddHours(-1), execution.SettledAtUtc);
        Assert.AreEqual(3.25, execution.CallbackDurationMilliseconds);
        Assert.AreEqual(7.5, execution.TotalDurationMilliseconds);
        Assert.AreEqual(1L, scope.Monitor.Outcomes.Failed);
    }

    [TestMethod]
    public async Task SecondaryErrorsAndSettlementNeverReplaceTheOriginalFailureOutcome()
    {
        await using var scope = new FactScope();
        var outcome = ValidOutcome(scope.Id);
        outcome.OriginalError = new("Original", "work failed");
        scope.Binding.Report(outcome.Build());
        DateTimeOffset? observation = scope.Monitor.LastObservationAtUtc;
        var handler = ValidFact(scope.Id, HealthExecutionFactKind.ErrorHandlingCompleted);
        handler.ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded;
        scope.Binding.Report(handler.Build());
        var cleanup = ValidFact(scope.Id, HealthExecutionFactKind.CleanupCompleted);
        cleanup.CleanupStatus = HealthExecutionStepStatus.Failed;
        cleanup.CleanupError = new("Cleanup", "cleanup failed");
        scope.Binding.Report(cleanup.Build());
        var fault = new Inputs(scope.Id, HealthExecutionFactKind.IntegrationFault)
        {
            ExecutionId = 1,
            SourcePhase = HealthExecutionPhase.Cleanup,
            IntegrationError = new("Reporting", "integration observer failed")
        };
        scope.Binding.Report(fault.Build());
        var settled = ValidFact(scope.Id, HealthExecutionFactKind.Settled);
        settled.CleanupStatus = HealthExecutionStepStatus.Failed;
        settled.TotalDurationMilliseconds = 10;
        scope.Binding.Report(settled.Build());

        HealthMonitorSnapshot monitor = scope.Monitor;
        Assert.AreEqual(MonitorState.Unhealthy, monitor.State);
        Assert.AreEqual(observation, monitor.LastObservationAtUtc);
        Assert.AreEqual(1L, monitor.Outcomes!.Failed);
        Assert.AreEqual(0L, monitor.Outcomes.Successful);
        Assert.AreEqual(HealthExecutionOutcome.Failed, monitor.Outcomes.LastOutcome);
        Assert.AreEqual("Original", monitor.Outcomes.LastFailureDiagnostic!.Code);
        HealthExecutionSnapshot execution = monitor.Outcomes.Execution!;
        Assert.AreEqual(HealthExecutionPhase.Settled, execution.Phase);
        Assert.AreEqual("Original", execution.OriginalError!.Code);
        Assert.AreEqual("Cleanup", execution.CleanupError!.Code);
        Assert.AreEqual("Reporting", execution.IntegrationError!.Code);
        Assert.AreEqual(HealthExecutionStepStatus.Succeeded, execution.ErrorHandlingStatus);
        Assert.AreEqual(HealthExecutionStepStatus.Failed, execution.CleanupStatus);
    }

    [TestMethod]
    public async Task RegistrationScopedIntegrationFaultDoesNotInventAnExecutionOrHealthFailure()
    {
        await using var scope = new FactScope();
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(new Inputs(scope.Id, HealthExecutionFactKind.IntegrationFault)
        {
            IntegrationError = new("ScheduleFault"),
            SourcePhase = HealthExecutionPhase.Registered
        }.Build()));
        Assert.AreEqual(MonitorState.Pending, scope.Monitor.State);
        Assert.IsNull(scope.Monitor.Outcomes!.Execution);
        Assert.IsNull(scope.Monitor.LastObservationAtUtc);
        Assert.AreEqual(0L, scope.Monitor.Outcomes.Failed);
    }

    [TestMethod]
    [DataRow(HealthExecutionPhase.Settled, false)]
    [DataRow(HealthExecutionPhase.Retired, false)]
    [DataRow(HealthExecutionPhase.Settled, true)]
    [DataRow(HealthExecutionPhase.Retired, true)]
    public async Task IntegrationFaultOriginCannotInventLifecycleProgressAndRoundTripsThroughPipeReports(
        HealthExecutionPhase origin, bool executionAlreadyStarted)
    {
        await using var scope = new FactScope();
        if (executionAlreadyStarted)
        {
            scope.Binding.Report(ValidFact(scope.Id, HealthExecutionFactKind.CallbackStarted).Build());
        }
        var fault = new Inputs(scope.Id, HealthExecutionFactKind.IntegrationFault)
        {
            ExecutionId = 1,
            SourcePhase = origin,
            IntegrationError = new("Observer", "Diagnostic origin is not a settlement notification.")
        };

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(fault.Build()));

        HealthExecutionSnapshot execution = scope.Monitor.Outcomes!.Execution!;
        Assert.AreEqual(executionAlreadyStarted ? HealthExecutionPhase.Callback : HealthExecutionPhase.Registered, execution.Phase);
        Assert.IsNull(execution.SettledAtUtc);
        Assert.IsNull(execution.TotalDurationMilliseconds);
        Assert.AreEqual("Observer", execution.IntegrationError!.Code);
        Assert.AreEqual(0L, scope.Monitor.Outcomes.Failed);
        var options = new HealthcheckOptions();
        HealthReportBuffer report = HealthReportFormatter.FormatPipeResponse(scope.Service.CaptureSnapshot(), HealthReportView.Details, options);
        Assert.IsFalse(report.IsError);
        Assert.IsTrue(report.SelectedHealthy);
        HealthParsedResponse parsed = HealthReportProtocol.ParseResponse(report.Utf8Bytes, HealthReportView.Details, options.MaxReportBytes);
        Assert.IsFalse(parsed.IsError);
        Assert.IsTrue(parsed.SelectedHealthy);
    }

    [TestMethod]
    [DataRow("timestamp")]
    [DataRow("duration")]
    [DataRow("terminal-handler")]
    [DataRow("terminal-cleanup")]
    [DataRow("original-error")]
    public async Task RetainedImmutableContextCannotBeContradicted(string field)
    {
        await using var scope = new FactScope();
        var first = ValidOutcome(scope.Id);
        first.CallbackStartedAtUtc = Epoch.AddTicks(-2);
        first.CallbackDurationMilliseconds = 2;
        first.OriginalError = new("Original");
        first.ErrorHandlingStatus = HealthExecutionStepStatus.Failed;
        first.CleanupStatus = HealthExecutionStepStatus.Succeeded;
        scope.Binding.Report(first.Build());
        var contradiction = ValidOutcome(scope.Id);
        contradiction.CallbackStartedAtUtc = first.CallbackStartedAtUtc;
        contradiction.CallbackDurationMilliseconds = first.CallbackDurationMilliseconds;
        contradiction.OriginalError = new("Original");
        contradiction.ErrorHandlingStatus = first.ErrorHandlingStatus;
        contradiction.CleanupStatus = first.CleanupStatus;
        switch (field)
        {
            case "timestamp": contradiction.CallbackStartedAtUtc = Epoch.AddTicks(-1); break;
            case "duration": contradiction.CallbackDurationMilliseconds = 3; break;
            case "terminal-handler": contradiction.ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded; break;
            case "terminal-cleanup": contradiction.CleanupStatus = HealthExecutionStepStatus.Failed; break;
            case "original-error": contradiction.OriginalError = new("Changed"); break;
            default: throw new AssertFailedException("Unknown immutable-context case.");
        }
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Binding.Report(contradiction.Build()));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(1L, scope.Monitor.Outcomes!.Failed);
    }

    [TestMethod]
    public async Task LaterFactsFillAbsentContextWithoutRegressingExecutionPhaseOrCountingOutcomes()
    {
        await using var scope = new FactScope();
        scope.Binding.Report(ValidOutcome(scope.Id).Build());
        scope.Binding.Report(ValidFact(scope.Id, HealthExecutionFactKind.Settled).Build());
        var lateStart = ValidFact(scope.Id, HealthExecutionFactKind.CallbackStarted);
        lateStart.OccurredAtUtc = Epoch.AddSeconds(-1);
        lateStart.CallbackStartedAtUtc = lateStart.OccurredAtUtc;
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.Binding.Report(lateStart.Build()));
        HealthExecutionSnapshot execution = scope.Monitor.Outcomes!.Execution!;
        Assert.AreEqual(HealthExecutionPhase.Settled, execution.Phase);
        Assert.AreEqual(Epoch.AddSeconds(-1), execution.CallbackStartedAtUtc);
        Assert.AreEqual(Epoch, execution.SettledAtUtc);
        Assert.AreEqual(1L, scope.Monitor.Outcomes.Failed);
    }

    [TestMethod]
    [DataRow("handler")]
    [DataRow("cleanup")]
    public async Task CompletedSecondaryStepsCannotReopen(string step)
    {
        await using var scope = new FactScope();
        var outcome = ValidOutcome(scope.Id);
        if (step == "handler") { outcome.ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded; }
        else { outcome.CleanupStatus = HealthExecutionStepStatus.Succeeded; }
        scope.Binding.Report(outcome.Build());
        var reopening = new Inputs(scope.Id, HealthExecutionFactKind.IntegrationFault)
        {
            ExecutionId = 1,
            IntegrationError = new("Observer")
        };
        if (step == "handler") { reopening.ErrorHandlingStatus = HealthExecutionStepStatus.Running; }
        else { reopening.CleanupStatus = HealthExecutionStepStatus.Running; }
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.ThrowsExactly<InvalidOperationException>(() => scope.Binding.Report(reopening.Build()));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
    }

    [TestMethod]
    public async Task DuplicateOutcomeMayEnrichAbsentContextWithoutIncrementingPolicyAgain()
    {
        await using var scope = new FactScope();
        scope.Binding.Report(ValidOutcome(scope.Id).Build());
        var enriched = ValidOutcome(scope.Id);
        enriched.CallbackDurationMilliseconds = 3.5;
        enriched.ErrorHandlingStatus = HealthExecutionStepStatus.Failed;
        enriched.ErrorHandlingError = new("Handler");
        enriched.CleanupStatus = HealthExecutionStepStatus.NotInvoked;
        enriched.CleanupError = new("OwnerUnavailable");

        Assert.AreEqual(HealthFactDisposition.Duplicate, scope.Binding.Report(enriched.Build()));

        HealthExecutionSnapshot execution = scope.Monitor.Outcomes!.Execution!;
        Assert.AreEqual(3.5, execution.CallbackDurationMilliseconds);
        Assert.AreEqual("Handler", execution.ErrorHandlingError!.Code);
        Assert.AreEqual(HealthExecutionStepStatus.NotInvoked, execution.CleanupStatus);
        Assert.AreEqual("OwnerUnavailable", execution.CleanupError!.Code);
        Assert.AreEqual(1L, scope.Monitor.Outcomes.Failed);
        Assert.AreEqual(0L, scope.Monitor.Outcomes.Successful);
    }

    private static void AssertRejectedWithoutMutation(FactScope scope, HealthExecutionFact fact)
    {
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.Throws<ArgumentException>(() => scope.Binding.Report(fact));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(MonitorState.Pending, scope.Monitor.State);
        Assert.AreEqual(0L, scope.Monitor.Outcomes!.Failed);
        Assert.IsNull(scope.Monitor.Outcomes.Execution);
        Assert.IsNull(scope.Monitor.LastObservationAtUtc);
    }

    private static Inputs ValidOutcome(Guid id) => new(id, HealthExecutionFactKind.Outcome)
    {
        ExecutionId = 1,
        Outcome = HealthExecutionOutcome.Failed,
        SourcePhase = HealthExecutionPhase.Callback,
        WorkCompletedAtUtc = Epoch
    };

    private static Inputs ValidFact(Guid id, HealthExecutionFactKind kind)
    {
        Inputs input = kind == HealthExecutionFactKind.Outcome ? ValidOutcome(id) : new(id, kind);
        if (kind is not (HealthExecutionFactKind.Registered or HealthExecutionFactKind.Retired)) { input.ExecutionId = 1; }
        switch (kind)
        {
            case HealthExecutionFactKind.Eligible: input.EligibleAtUtc = Epoch; break;
            case HealthExecutionFactKind.Admitted: input.AdmittedAtUtc = Epoch; break;
            case HealthExecutionFactKind.ConditionStarted: input.ConditionStartedAtUtc = Epoch; break;
            case HealthExecutionFactKind.CallbackStarted: input.CallbackStartedAtUtc = Epoch; break;
            case HealthExecutionFactKind.ErrorHandlingStarted:
                input.ErrorHandlingStartedAtUtc = Epoch;
                input.ErrorHandlingStatus = HealthExecutionStepStatus.Running;
                break;
            case HealthExecutionFactKind.ErrorHandlingCompleted:
                input.ErrorHandlingCompletedAtUtc = Epoch;
                input.ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded;
                break;
            case HealthExecutionFactKind.CleanupCompleted:
                input.CleanupCompletedAtUtc = Epoch;
                input.CleanupStatus = HealthExecutionStepStatus.Succeeded;
                break;
            case HealthExecutionFactKind.Settled:
                input.SettledAtUtc = Epoch;
                input.ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded;
                input.CleanupStatus = HealthExecutionStepStatus.Succeeded;
                break;
            case HealthExecutionFactKind.IntegrationFault: input.IntegrationError = new("Integration"); break;
        }
        return input;
    }

    private static void SetEventTime(Inputs input, DateTimeOffset? time)
    {
        switch (input.Kind)
        {
            case HealthExecutionFactKind.Eligible: input.EligibleAtUtc = time; break;
            case HealthExecutionFactKind.Admitted: input.AdmittedAtUtc = time; break;
            case HealthExecutionFactKind.ConditionStarted: input.ConditionStartedAtUtc = time; break;
            case HealthExecutionFactKind.CallbackStarted: input.CallbackStartedAtUtc = time; break;
            case HealthExecutionFactKind.Outcome: input.WorkCompletedAtUtc = time; break;
            case HealthExecutionFactKind.ErrorHandlingStarted: input.ErrorHandlingStartedAtUtc = time; break;
            case HealthExecutionFactKind.ErrorHandlingCompleted: input.ErrorHandlingCompletedAtUtc = time; break;
            case HealthExecutionFactKind.CleanupCompleted: input.CleanupCompletedAtUtc = time; break;
            case HealthExecutionFactKind.Settled: input.SettledAtUtc = time; break;
            default: throw new AssertFailedException("Unknown event timestamp kind.");
        }
    }

    private sealed class FactScope : IAsyncDisposable
    {
        internal FactScope()
        {
            Id = Guid.NewGuid();
            Service = new HealthcheckService(new HealthcheckOptions(),
                new HealthApplicationIdentity { Name = "execution-fact-tests", InstanceId = Guid.NewGuid() },
                new ManualTimeProvider(Epoch));
            Binding = HealthExecutionBinder.Bind(Service,
                new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 },
                new HealthExecutionBindingOptions { RegistrationId = Id });
        }

        internal Guid Id { get; }
        internal HealthcheckService Service { get; }
        internal IHealthExecutionBinding Binding { get; }
        internal HealthMonitorSnapshot Monitor => Service.CaptureSnapshot().Monitors.Single();

        public async ValueTask DisposeAsync()
        {
            await Binding.DisposeAsync();
            await Service.DisposeAsync();
        }
    }

    // Named mutable test input keeps invalid combinations visible at each call site; the public fact remains immutable.
    private sealed class Inputs(Guid registrationId, HealthExecutionFactKind kind)
    {
        internal Guid RegistrationId { get; set; } = registrationId;
        internal HealthExecutionFactKind Kind { get; set; } = kind;
        internal DateTimeOffset OccurredAtUtc { get; set; } = Epoch;
        internal long? ExecutionId { get; set; }
        internal HealthExecutionPhase? SourcePhase { get; set; }
        internal HealthExecutionOutcome? Outcome { get; set; }
        internal DateTimeOffset? EligibleAtUtc { get; set; }
        internal DateTimeOffset? AdmittedAtUtc { get; set; }
        internal DateTimeOffset? ConditionStartedAtUtc { get; set; }
        internal DateTimeOffset? CallbackStartedAtUtc { get; set; }
        internal DateTimeOffset? WorkCompletedAtUtc { get; set; }
        internal DateTimeOffset? ErrorHandlingStartedAtUtc { get; set; }
        internal DateTimeOffset? ErrorHandlingCompletedAtUtc { get; set; }
        internal DateTimeOffset? CleanupCompletedAtUtc { get; set; }
        internal DateTimeOffset? SettledAtUtc { get; set; }
        internal double? QueueDelayMilliseconds { get; set; }
        internal double? ConditionDurationMilliseconds { get; set; }
        internal double? CallbackDurationMilliseconds { get; set; }
        internal double? ErrorHandlingDurationMilliseconds { get; set; }
        internal double? CleanupDurationMilliseconds { get; set; }
        internal double? TotalDurationMilliseconds { get; set; }
        internal HealthExecutionStepStatus? ErrorHandlingStatus { get; set; }
        internal HealthExecutionStepStatus? CleanupStatus { get; set; }
        internal HealthDiagnostic? OriginalError { get; set; }
        internal HealthDiagnostic? ErrorHandlingError { get; set; }
        internal HealthDiagnostic? CleanupError { get; set; }
        internal HealthDiagnostic? IntegrationError { get; set; }

        internal HealthExecutionFact Build() => new()
        {
            RegistrationId = RegistrationId,
            Kind = Kind,
            OccurredAtUtc = OccurredAtUtc,
            ExecutionId = ExecutionId,
            SourcePhase = SourcePhase,
            Outcome = Outcome,
            EligibleAtUtc = EligibleAtUtc,
            AdmittedAtUtc = AdmittedAtUtc,
            ConditionStartedAtUtc = ConditionStartedAtUtc,
            CallbackStartedAtUtc = CallbackStartedAtUtc,
            WorkCompletedAtUtc = WorkCompletedAtUtc,
            ErrorHandlingStartedAtUtc = ErrorHandlingStartedAtUtc,
            ErrorHandlingCompletedAtUtc = ErrorHandlingCompletedAtUtc,
            CleanupCompletedAtUtc = CleanupCompletedAtUtc,
            SettledAtUtc = SettledAtUtc,
            QueueDelayMilliseconds = QueueDelayMilliseconds,
            ConditionDurationMilliseconds = ConditionDurationMilliseconds,
            CallbackDurationMilliseconds = CallbackDurationMilliseconds,
            ErrorHandlingDurationMilliseconds = ErrorHandlingDurationMilliseconds,
            CleanupDurationMilliseconds = CleanupDurationMilliseconds,
            TotalDurationMilliseconds = TotalDurationMilliseconds,
            ErrorHandlingStatus = ErrorHandlingStatus,
            CleanupStatus = CleanupStatus,
            OriginalError = OriginalError,
            ErrorHandlingError = ErrorHandlingError,
            CleanupError = CleanupError,
            IntegrationError = IntegrationError
        };
    }
}
