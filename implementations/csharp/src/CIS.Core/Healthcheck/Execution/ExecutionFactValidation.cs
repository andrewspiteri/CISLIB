namespace CIS.Healthcheck.Internal;

internal static class ExecutionFactValidation
{
    internal static InvalidOperationException Contradiction() =>
        new("An execution fact contradicts retained immutable execution context.");

    internal static HealthExecutionFact Normalize(HealthExecutionFact fact, Guid registrationId)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.RegistrationId == Guid.Empty || fact.RegistrationId != registrationId || !Enum.IsDefined(fact.Kind)
            || fact.ExecutionId is <= 0 || (fact.SourcePhase.HasValue && !Enum.IsDefined(fact.SourcePhase.Value))
            || (fact.Outcome.HasValue && !Enum.IsDefined(fact.Outcome.Value))
            || (fact.ErrorHandlingStatus.HasValue && !Enum.IsDefined(fact.ErrorHandlingStatus.Value))
            || (fact.CleanupStatus.HasValue && !Enum.IsDefined(fact.CleanupStatus.Value)))
        {
            throw Invalid();
        }
        bool registrationFact = fact.Kind is HealthExecutionFactKind.Registered or HealthExecutionFactKind.Retired;
        if (registrationFact ? fact.ExecutionId.HasValue
            : fact.Kind != HealthExecutionFactKind.IntegrationFault && !fact.ExecutionId.HasValue)
        {
            throw Invalid();
        }
        ValidateDuration(fact.QueueDelayMilliseconds);
        ValidateDuration(fact.DispatchDelayMilliseconds);
        ValidateDuration(fact.ConditionDurationMilliseconds);
        ValidateDuration(fact.CallbackDurationMilliseconds);
        ValidateDuration(fact.ErrorHandlingDurationMilliseconds);
        ValidateDuration(fact.CleanupDurationMilliseconds);
        ValidateDuration(fact.TotalDurationMilliseconds);

        if ((fact.Kind != HealthExecutionFactKind.Outcome && fact.Outcome.HasValue)
            || (fact.Kind is not (HealthExecutionFactKind.Outcome or HealthExecutionFactKind.IntegrationFault) && fact.SourcePhase.HasValue)
            || (fact.Kind != HealthExecutionFactKind.Settled && (fact.SettledAtUtc.HasValue || fact.TotalDurationMilliseconds.HasValue)))
        {
            throw Invalid();
        }
        if (registrationFact || (fact.Kind == HealthExecutionFactKind.IntegrationFault && !fact.ExecutionId.HasValue))
        {
            if (HasExecutionContext(fact) || (fact.Kind == HealthExecutionFactKind.Registered && fact.IntegrationError is not null))
            {
                throw Invalid();
            }
        }

        switch (fact.Kind)
        {
            case HealthExecutionFactKind.Eligible:
                RequireEventTime(fact.EligibleAtUtc, fact.OccurredAtUtc);
                break;
            case HealthExecutionFactKind.Admitted:
                RequireEventTime(fact.AdmittedAtUtc, fact.OccurredAtUtc);
                break;
            case HealthExecutionFactKind.Dispatched:
                RequireEventTime(fact.DispatchedAtUtc, fact.OccurredAtUtc);
                break;
            case HealthExecutionFactKind.Suppressed:
                RequireEventTime(fact.SuppressedAtUtc, fact.OccurredAtUtc);
                if (!fact.SuppressedPhase.HasValue || !fact.SuppressionReason.HasValue) { throw Invalid(); }
                break;
            case HealthExecutionFactKind.ConditionStarted:
                RequireEventTime(fact.ConditionStartedAtUtc, fact.OccurredAtUtc);
                break;
            case HealthExecutionFactKind.CallbackStarted:
                RequireEventTime(fact.CallbackStartedAtUtc, fact.OccurredAtUtc);
                break;
            case HealthExecutionFactKind.Outcome:
                RequireEventTime(fact.WorkCompletedAtUtc, fact.OccurredAtUtc);
                if (!fact.Outcome.HasValue || fact.SourcePhase is not (HealthExecutionPhase.Condition or HealthExecutionPhase.Callback)
                    || (fact.SourcePhase == HealthExecutionPhase.Condition && fact.Outcome == HealthExecutionOutcome.Succeeded)
                    || (fact.SourcePhase == HealthExecutionPhase.Callback && fact.Outcome == HealthExecutionOutcome.Skipped))
                {
                    throw Invalid();
                }
                break;
            case HealthExecutionFactKind.ErrorHandlingStarted:
                RequireEventTime(fact.ErrorHandlingStartedAtUtc, fact.OccurredAtUtc);
                if (fact.ErrorHandlingStatus != HealthExecutionStepStatus.Running) { throw Invalid(); }
                break;
            case HealthExecutionFactKind.ErrorHandlingCompleted:
                RequireEventTime(fact.ErrorHandlingCompletedAtUtc, fact.OccurredAtUtc);
                if (!IsTerminal(fact.ErrorHandlingStatus)) { throw Invalid(); }
                break;
            case HealthExecutionFactKind.CleanupCompleted:
                RequireEventTime(fact.CleanupCompletedAtUtc, fact.OccurredAtUtc);
                if (!IsTerminal(fact.CleanupStatus)) { throw Invalid(); }
                break;
            case HealthExecutionFactKind.Settled:
                RequireEventTime(fact.SettledAtUtc, fact.OccurredAtUtc);
                if (!IsTerminal(fact.ErrorHandlingStatus) || !IsTerminal(fact.CleanupStatus)) { throw Invalid(); }
                break;
            case HealthExecutionFactKind.IntegrationFault:
                if (fact.IntegrationError is null) { throw Invalid(); }
                break;
        }
        ValidateContext(fact.SourcePhase, fact.Outcome, fact.CallbackStartedAtUtc, fact.CallbackDurationMilliseconds,
            fact.ErrorHandlingStartedAtUtc, fact.ErrorHandlingCompletedAtUtc, fact.ErrorHandlingDurationMilliseconds,
            fact.ErrorHandlingStatus, fact.OriginalError, fact.ErrorHandlingError, fact.CleanupError,
            fact.CleanupCompletedAtUtc, fact.CleanupDurationMilliseconds, fact.CleanupStatus);
        ValidateProducerContext(fact.SourcePhase, fact.Outcome, fact.ConditionStartedAtUtc, fact.CallbackStartedAtUtc,
            fact.ConditionDurationMilliseconds, fact.CallbackDurationMilliseconds, fact.SuppressedAtUtc,
            fact.SuppressedPhase, fact.SuppressionReason, fact.ConditionResult, fact.CallbackResult,
            fact.ExpectedCancellationPhase, fact.CleanupStatus, fact.OriginalError, merged: false);

        return new HealthExecutionFact
        {
            RegistrationId = fact.RegistrationId,
            Kind = fact.Kind,
            OccurredAtUtc = fact.OccurredAtUtc.ToUniversalTime(),
            ExecutionId = fact.ExecutionId,
            SourcePhase = fact.SourcePhase,
            Outcome = fact.Outcome,
            EligibleAtUtc = fact.EligibleAtUtc?.ToUniversalTime(),
            AdmittedAtUtc = fact.AdmittedAtUtc?.ToUniversalTime(),
            DispatchedAtUtc = fact.DispatchedAtUtc?.ToUniversalTime(),
            SuppressedAtUtc = fact.SuppressedAtUtc?.ToUniversalTime(),
            SuppressedPhase = fact.SuppressedPhase,
            SuppressionReason = fact.SuppressionReason,
            ConditionResult = fact.ConditionResult,
            CallbackResult = fact.CallbackResult,
            ExpectedCancellationPhase = fact.ExpectedCancellationPhase,
            ConditionStartedAtUtc = fact.ConditionStartedAtUtc?.ToUniversalTime(),
            CallbackStartedAtUtc = fact.CallbackStartedAtUtc?.ToUniversalTime(),
            WorkCompletedAtUtc = fact.WorkCompletedAtUtc?.ToUniversalTime(),
            ErrorHandlingStartedAtUtc = fact.ErrorHandlingStartedAtUtc?.ToUniversalTime(),
            ErrorHandlingCompletedAtUtc = fact.ErrorHandlingCompletedAtUtc?.ToUniversalTime(),
            CleanupCompletedAtUtc = fact.CleanupCompletedAtUtc?.ToUniversalTime(),
            SettledAtUtc = fact.SettledAtUtc?.ToUniversalTime(),
            QueueDelayMilliseconds = fact.QueueDelayMilliseconds,
            DispatchDelayMilliseconds = fact.DispatchDelayMilliseconds,
            ConditionDurationMilliseconds = fact.ConditionDurationMilliseconds,
            CallbackDurationMilliseconds = fact.CallbackDurationMilliseconds,
            ErrorHandlingDurationMilliseconds = fact.ErrorHandlingDurationMilliseconds,
            CleanupDurationMilliseconds = fact.CleanupDurationMilliseconds,
            TotalDurationMilliseconds = fact.TotalDurationMilliseconds,
            ErrorHandlingStatus = fact.ErrorHandlingStatus,
            CleanupStatus = fact.CleanupStatus,
            OriginalError = fact.OriginalError,
            ErrorHandlingError = fact.ErrorHandlingError,
            CleanupError = fact.CleanupError,
            IntegrationError = fact.IntegrationError
        };
    }

    internal static bool IsTerminal(HealthExecutionStepStatus? status) => status is
        HealthExecutionStepStatus.Succeeded or HealthExecutionStepStatus.Failed or HealthExecutionStepStatus.ExpectedCancellation
        or HealthExecutionStepStatus.NotApplicable or HealthExecutionStepStatus.NotInvoked;

    internal static void ValidateProducerContext(HealthExecutionPhase? sourcePhase, HealthExecutionOutcome? outcome,
        DateTimeOffset? conditionStarted, DateTimeOffset? callbackStarted, double? conditionDuration, double? callbackDuration,
        DateTimeOffset? suppressedAt, HealthExecutionPhase? suppressedPhase, HealthExecutionSuppressionReason? suppressionReason,
        bool? conditionResult, bool? callbackResult, HealthExecutionPhase? expectedCancellationPhase,
        HealthExecutionStepStatus? cleanupStatus, HealthDiagnostic? originalError, bool merged)
    {
        if (suppressedAt.HasValue != suppressedPhase.HasValue || suppressedAt.HasValue != suppressionReason.HasValue
            || (suppressedPhase.HasValue && suppressedPhase is not (HealthExecutionPhase.Condition or HealthExecutionPhase.Callback))
            || (suppressionReason.HasValue && !Enum.IsDefined(suppressionReason.Value))
            || (expectedCancellationPhase.HasValue && expectedCancellationPhase is not (HealthExecutionPhase.Condition or HealthExecutionPhase.Callback)))
        { throw Invalid(); }

        bool conditionRan = conditionStarted.HasValue || conditionDuration.HasValue || conditionResult.HasValue
            || expectedCancellationPhase == HealthExecutionPhase.Condition || sourcePhase == HealthExecutionPhase.Condition && outcome.HasValue;
        bool callbackRan = callbackStarted.HasValue || callbackDuration.HasValue || callbackResult.HasValue
            || expectedCancellationPhase == HealthExecutionPhase.Callback || sourcePhase == HealthExecutionPhase.Callback && outcome.HasValue;
        if (conditionResult == false && (callbackRan || suppressedPhase == HealthExecutionPhase.Callback)
            || expectedCancellationPhase == HealthExecutionPhase.Condition && (conditionResult.HasValue || callbackRan)
            || expectedCancellationPhase == HealthExecutionPhase.Callback && callbackResult.HasValue
            || suppressedPhase == HealthExecutionPhase.Condition && (conditionRan || callbackRan)
            || suppressedPhase == HealthExecutionPhase.Callback && callbackRan
            || outcome.HasValue && expectedCancellationPhase.HasValue && sourcePhase != expectedCancellationPhase
            || outcome == HealthExecutionOutcome.ExpectedCancellation && (sourcePhase == HealthExecutionPhase.Condition && conditionResult.HasValue
                || sourcePhase == HealthExecutionPhase.Callback && callbackResult.HasValue)
            || originalError is not null && (sourcePhase == HealthExecutionPhase.Condition && conditionResult.HasValue
                || sourcePhase == HealthExecutionPhase.Callback && callbackResult.HasValue)
            || callbackResult == false && outcome == HealthExecutionOutcome.Succeeded)
        { throw Invalid(); }

        if (!merged || !outcome.HasValue) { return; }
        bool cleanupFailed = cleanupStatus == HealthExecutionStepStatus.Failed;
        if (suppressedAt.HasValue && (outcome != HealthExecutionOutcome.Failed || sourcePhase != HealthExecutionPhase.Condition
                || conditionResult != true || !cleanupFailed)
            || cleanupFailed && outcome != HealthExecutionOutcome.Failed
                && (conditionResult.HasValue || callbackResult.HasValue || expectedCancellationPhase.HasValue)
            || sourcePhase == HealthExecutionPhase.Condition && conditionResult == true && outcome != HealthExecutionOutcome.Failed
            || outcome == HealthExecutionOutcome.Failed && !cleanupFailed && (expectedCancellationPhase.HasValue
                || sourcePhase == HealthExecutionPhase.Condition && conditionResult.HasValue
                || sourcePhase == HealthExecutionPhase.Callback && callbackResult == true)
            || conditionResult == false && sourcePhase == HealthExecutionPhase.Condition && outcome != HealthExecutionOutcome.Skipped && !cleanupFailed)
        { throw Invalid(); }
    }

    internal static void ValidateContext(HealthExecutionPhase? sourcePhase, HealthExecutionOutcome? outcome,
        DateTimeOffset? callbackStarted, double? callbackDuration, DateTimeOffset? handlerStarted,
        DateTimeOffset? handlerCompleted, double? handlerDuration, HealthExecutionStepStatus? handlerStatus,
        HealthDiagnostic? original, HealthDiagnostic? handlerError, HealthDiagnostic? cleanupError,
        DateTimeOffset? cleanupCompleted, double? cleanupDuration, HealthExecutionStepStatus? cleanupStatus,
        bool merged = false)
    {
        if (merged)
        {
            handlerStatus ??= HealthExecutionStepStatus.NotStarted;
            cleanupStatus ??= HealthExecutionStepStatus.NotStarted;
        }
        if ((outcome.HasValue && sourcePhase == HealthExecutionPhase.Condition && (callbackStarted.HasValue || callbackDuration.HasValue))
            || ((outcome is HealthExecutionOutcome.Succeeded or HealthExecutionOutcome.Skipped) && original is not null)
            || ((handlerStatus is HealthExecutionStepStatus.NotApplicable or HealthExecutionStepStatus.NotInvoked)
                && (handlerStarted.HasValue || handlerDuration.HasValue))
            || ((cleanupStatus is HealthExecutionStepStatus.NotApplicable or HealthExecutionStepStatus.NotInvoked) && cleanupDuration.HasValue)
            || (handlerStatus == HealthExecutionStepStatus.NotStarted && (handlerStarted.HasValue || handlerCompleted.HasValue || handlerDuration.HasValue))
            || (handlerStatus == HealthExecutionStepStatus.Running && handlerCompleted.HasValue)
            || ((cleanupStatus is HealthExecutionStepStatus.NotStarted or HealthExecutionStepStatus.Running) && cleanupCompleted.HasValue)
            || (handlerError is not null && (handlerStatus is HealthExecutionStepStatus.NotStarted or HealthExecutionStepStatus.Running
                or HealthExecutionStepStatus.Succeeded or HealthExecutionStepStatus.NotApplicable))
            || (cleanupError is not null && (cleanupStatus is HealthExecutionStepStatus.NotStarted or HealthExecutionStepStatus.Running
                or HealthExecutionStepStatus.Succeeded or HealthExecutionStepStatus.NotApplicable)))
        {
            throw Invalid();
        }
    }

    private static bool HasExecutionContext(HealthExecutionFact fact) => fact.Outcome.HasValue
        || fact.EligibleAtUtc.HasValue || fact.AdmittedAtUtc.HasValue || fact.ConditionStartedAtUtc.HasValue
        || fact.DispatchedAtUtc.HasValue || fact.DispatchDelayMilliseconds.HasValue || fact.SuppressedAtUtc.HasValue
        || fact.SuppressedPhase.HasValue || fact.SuppressionReason.HasValue || fact.ConditionResult.HasValue
        || fact.CallbackResult.HasValue || fact.ExpectedCancellationPhase.HasValue
        || fact.CallbackStartedAtUtc.HasValue || fact.WorkCompletedAtUtc.HasValue || fact.ErrorHandlingStartedAtUtc.HasValue
        || fact.ErrorHandlingCompletedAtUtc.HasValue || fact.CleanupCompletedAtUtc.HasValue || fact.SettledAtUtc.HasValue
        || fact.QueueDelayMilliseconds.HasValue || fact.ConditionDurationMilliseconds.HasValue || fact.CallbackDurationMilliseconds.HasValue
        || fact.ErrorHandlingDurationMilliseconds.HasValue || fact.CleanupDurationMilliseconds.HasValue || fact.TotalDurationMilliseconds.HasValue
        || fact.ErrorHandlingStatus.HasValue || fact.CleanupStatus.HasValue || fact.OriginalError is not null
        || fact.ErrorHandlingError is not null || fact.CleanupError is not null;

    private static void RequireEventTime(DateTimeOffset? supplied, DateTimeOffset occurred)
    {
        if (!supplied.HasValue || supplied.Value != occurred) { throw Invalid(); }
    }

    private static void ValidateDuration(double? duration)
    {
        if (duration.HasValue && (!double.IsFinite(duration.Value) || duration.Value < 0)) { throw Invalid(); }
    }

    private static ArgumentException Invalid() => new("Supply a valid detached execution fact for this binding.", "fact");
}
