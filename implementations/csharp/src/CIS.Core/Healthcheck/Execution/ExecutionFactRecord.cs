namespace CIS.Healthcheck.Internal;

/// <summary>One bounded detached execution payload; candidates are merged before the state transaction commits.</summary>
internal sealed class ExecutionFactRecord(long executionId)
{
    internal long ExecutionId { get; } = executionId;
    internal bool HasOutcome => outcome.HasValue;
    internal bool HasSuppression => suppressedAt.HasValue;

    private HealthExecutionOutcome? outcome;
    private HealthExecutionPhase? sourcePhase;
    private DateTimeOffset? eligibleAt;
    private DateTimeOffset? admittedAt;
    private DateTimeOffset? dispatchedAt;
    private DateTimeOffset? suppressedAt;
    private HealthExecutionPhase? suppressedPhase;
    private HealthExecutionSuppressionReason? suppressionReason;
    private bool? conditionResult;
    private bool? callbackResult;
    private HealthExecutionPhase? expectedCancellationPhase;
    private DateTimeOffset? conditionStartedAt;
    private DateTimeOffset? callbackStartedAt;
    private DateTimeOffset? workCompletedAt;
    private DateTimeOffset? handlerStartedAt;
    private DateTimeOffset? handlerCompletedAt;
    private DateTimeOffset? cleanupCompletedAt;
    private DateTimeOffset? settledAt;
    private double? queueDelay;
    private double? dispatchDelay;
    private double? conditionDuration;
    private double? callbackDuration;
    private double? handlerDuration;
    private double? cleanupDuration;
    private double? totalDuration;
    private HealthExecutionStepStatus? handlerStatus;
    private HealthExecutionStepStatus? cleanupStatus;
    private HealthDiagnostic? originalError;
    private HealthDiagnostic? handlerError;
    private HealthDiagnostic? cleanupError;
    private HealthDiagnostic? integrationError;
    private bool hasContext;

    internal ExecutionFactRecord Merge(HealthExecutionFact fact)
    {
        var next = (ExecutionFactRecord)MemberwiseClone();
        if (fact.Kind == HealthExecutionFactKind.Outcome)
        {
            next.outcome = MergeValue(outcome, fact.Outcome);
            next.sourcePhase = MergeValue(sourcePhase, fact.SourcePhase);
        }
        next.eligibleAt = MergeValue(eligibleAt, fact.EligibleAtUtc);
        next.admittedAt = MergeValue(admittedAt, fact.AdmittedAtUtc);
        next.dispatchedAt = MergeValue(dispatchedAt, fact.DispatchedAtUtc);
        next.suppressedAt = MergeValue(suppressedAt, fact.SuppressedAtUtc);
        next.suppressedPhase = MergeValue(suppressedPhase, fact.SuppressedPhase);
        next.suppressionReason = MergeValue(suppressionReason, fact.SuppressionReason);
        next.conditionResult = MergeValue(conditionResult, fact.ConditionResult);
        next.callbackResult = MergeValue(callbackResult, fact.CallbackResult);
        next.expectedCancellationPhase = MergeValue(expectedCancellationPhase, fact.ExpectedCancellationPhase);
        next.conditionStartedAt = MergeValue(conditionStartedAt, fact.ConditionStartedAtUtc);
        next.callbackStartedAt = MergeValue(callbackStartedAt, fact.CallbackStartedAtUtc);
        next.workCompletedAt = MergeValue(workCompletedAt, fact.WorkCompletedAtUtc);
        next.handlerStartedAt = MergeValue(handlerStartedAt, fact.ErrorHandlingStartedAtUtc);
        next.handlerCompletedAt = MergeValue(handlerCompletedAt, fact.ErrorHandlingCompletedAtUtc);
        next.cleanupCompletedAt = MergeValue(cleanupCompletedAt, fact.CleanupCompletedAtUtc);
        next.settledAt = MergeValue(settledAt, fact.SettledAtUtc);
        next.queueDelay = MergeValue(queueDelay, fact.QueueDelayMilliseconds);
        next.dispatchDelay = MergeValue(dispatchDelay, fact.DispatchDelayMilliseconds);
        next.conditionDuration = MergeValue(conditionDuration, fact.ConditionDurationMilliseconds);
        next.callbackDuration = MergeValue(callbackDuration, fact.CallbackDurationMilliseconds);
        next.handlerDuration = MergeValue(handlerDuration, fact.ErrorHandlingDurationMilliseconds);
        next.cleanupDuration = MergeValue(cleanupDuration, fact.CleanupDurationMilliseconds);
        next.totalDuration = MergeValue(totalDuration, fact.TotalDurationMilliseconds);
        next.handlerStatus = MergeStatus(handlerStatus, fact.ErrorHandlingStatus);
        next.cleanupStatus = MergeStatus(cleanupStatus, fact.CleanupStatus);
        next.originalError = MergeDiagnostic(originalError, fact.OriginalError);
        next.handlerError = MergeDiagnostic(handlerError, fact.ErrorHandlingError);
        next.cleanupError = MergeDiagnostic(cleanupError, fact.CleanupError);
        // Integration diagnostics are replaceable observations, never a new execution outcome.
        next.integrationError = fact.IntegrationError ?? integrationError;
        try
        {
            ExecutionFactValidation.ValidateContext(next.sourcePhase, next.outcome, next.callbackStartedAt, next.callbackDuration,
                next.handlerStartedAt, next.handlerCompletedAt, next.handlerDuration, next.handlerStatus, next.originalError,
                next.handlerError, next.cleanupError, next.cleanupCompletedAt, next.cleanupDuration, next.cleanupStatus, merged: true);
            ExecutionFactValidation.ValidateProducerContext(next.sourcePhase, next.outcome, next.conditionStartedAt,
                next.callbackStartedAt, next.conditionDuration, next.callbackDuration, next.suppressedAt,
                next.suppressedPhase, next.suppressionReason, next.conditionResult, next.callbackResult,
                next.expectedCancellationPhase, next.cleanupStatus, next.originalError, merged: true);
        }
        catch (ArgumentException)
        {
            if (hasContext) { throw ExecutionFactValidation.Contradiction(); }
            throw;
        }
        next.hasContext = true;
        return next;
    }

    internal bool SameContext(ExecutionFactRecord other) => outcome == other.outcome && sourcePhase == other.sourcePhase
        && eligibleAt == other.eligibleAt && admittedAt == other.admittedAt && conditionStartedAt == other.conditionStartedAt
        && dispatchedAt == other.dispatchedAt && suppressedAt == other.suppressedAt && suppressedPhase == other.suppressedPhase
        && suppressionReason == other.suppressionReason && conditionResult == other.conditionResult
        && callbackResult == other.callbackResult && expectedCancellationPhase == other.expectedCancellationPhase
        && callbackStartedAt == other.callbackStartedAt && workCompletedAt == other.workCompletedAt
        && handlerStartedAt == other.handlerStartedAt && handlerCompletedAt == other.handlerCompletedAt
        && cleanupCompletedAt == other.cleanupCompletedAt && settledAt == other.settledAt
        && queueDelay == other.queueDelay && dispatchDelay == other.dispatchDelay
        && conditionDuration == other.conditionDuration && callbackDuration == other.callbackDuration
        && handlerDuration == other.handlerDuration && cleanupDuration == other.cleanupDuration && totalDuration == other.totalDuration
        && handlerStatus == other.handlerStatus && cleanupStatus == other.cleanupStatus
        && SameDiagnostic(originalError, other.originalError) && SameDiagnostic(handlerError, other.handlerError)
        && SameDiagnostic(cleanupError, other.cleanupError) && SameDiagnostic(integrationError, other.integrationError);

    private static T? MergeValue<T>(T? previous, T? supplied) where T : struct
    {
        if (!supplied.HasValue) { return previous; }
        if (previous.HasValue && !EqualityComparer<T>.Default.Equals(previous.Value, supplied.Value))
        {
            throw ExecutionFactValidation.Contradiction();
        }
        return supplied;
    }

    private static HealthExecutionStepStatus? MergeStatus(HealthExecutionStepStatus? previous, HealthExecutionStepStatus? supplied)
    {
        if (!supplied.HasValue || supplied == previous) { return previous; }
        if (!previous.HasValue || previous == HealthExecutionStepStatus.NotStarted
            || previous == HealthExecutionStepStatus.Running && ExecutionFactValidation.IsTerminal(supplied))
        {
            return supplied;
        }
        throw ExecutionFactValidation.Contradiction();
    }

    private static HealthDiagnostic? MergeDiagnostic(HealthDiagnostic? previous, HealthDiagnostic? supplied)
    {
        if (supplied is null) { return previous; }
        if (previous is not null && !SameDiagnostic(previous, supplied)) { throw ExecutionFactValidation.Contradiction(); }
        return supplied;
    }

    private static bool SameDiagnostic(HealthDiagnostic? left, HealthDiagnostic? right) => ReferenceEquals(left, right)
        || left is not null && right is not null && left.Code == right.Code && left.Message == right.Message
            && left.ExceptionType == right.ExceptionType && left.IsTruncated == right.IsTruncated;
}
