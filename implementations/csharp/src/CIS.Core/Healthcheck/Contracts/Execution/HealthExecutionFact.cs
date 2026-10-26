namespace CIS.Healthcheck;

/// <summary>A detached, named-init scheduling fact validated by its receiving binding.</summary>
/// <remarks>Execution-scoped facts require a positive execution ID. Times are UTC and elapsed durations are finite nonnegative monotonic milliseconds.</remarks>
public sealed class HealthExecutionFact
{
    /// <summary>Gets the required nonempty correlated registration identity.</summary>
    public required Guid RegistrationId { get; init; }

    /// <summary>Gets the required fact discriminator.</summary>
    public required HealthExecutionFactKind Kind { get; init; }

    /// <summary>Gets the required actual UTC time of the reported event.</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Gets the positive execution identity, or null for a registration-lifetime fact.</summary>
    public long? ExecutionId { get; init; }

    /// <summary>Gets the phase that produced an outcome or integration diagnostic when applicable.</summary>
    public HealthExecutionPhase? SourcePhase { get; init; }

    /// <summary>Gets the single outcome required by an Outcome fact; settlement never carries another outcome.</summary>
    public HealthExecutionOutcome? Outcome { get; init; }

    /// <summary>Gets the observed UTC eligibility time when known.</summary>
    public DateTimeOffset? EligibleAtUtc { get; init; }

    /// <summary>Gets the observed UTC admission time when known.</summary>
    public DateTimeOffset? AdmittedAtUtc { get; init; }

    /// <summary>Gets the observed UTC condition-start time when known.</summary>
    public DateTimeOffset? ConditionStartedAtUtc { get; init; }

    /// <summary>Gets the observed UTC callback-start time when known.</summary>
    public DateTimeOffset? CallbackStartedAtUtc { get; init; }

    /// <summary>Gets the actual UTC end of condition or callback work; an Outcome fact requires this value.</summary>
    public DateTimeOffset? WorkCompletedAtUtc { get; init; }

    /// <summary>Gets the observed UTC error-handler start time when known.</summary>
    public DateTimeOffset? ErrorHandlingStartedAtUtc { get; init; }

    /// <summary>Gets the observed UTC error-handler completion time when known.</summary>
    public DateTimeOffset? ErrorHandlingCompletedAtUtc { get; init; }

    /// <summary>Gets the observed UTC cleanup completion time when known.</summary>
    public DateTimeOffset? CleanupCompletedAtUtc { get; init; }

    /// <summary>Gets actual UTC settlement time; an Outcome fact must not invent future settlement.</summary>
    public DateTimeOffset? SettledAtUtc { get; init; }

    /// <summary>Gets monotonic milliseconds from eligibility to admission when measured.</summary>
    public double? QueueDelayMilliseconds { get; init; }

    /// <summary>Gets monotonic milliseconds spent evaluating the condition when measured.</summary>
    public double? ConditionDurationMilliseconds { get; init; }

    /// <summary>Gets monotonic milliseconds spent in callback work when measured.</summary>
    public double? CallbackDurationMilliseconds { get; init; }

    /// <summary>Gets monotonic milliseconds spent handling the original error when measured.</summary>
    public double? ErrorHandlingDurationMilliseconds { get; init; }

    /// <summary>Gets monotonic milliseconds spent in cleanup when measured.</summary>
    public double? CleanupDurationMilliseconds { get; init; }

    /// <summary>Gets actual monotonic milliseconds through settlement when measured.</summary>
    public double? TotalDurationMilliseconds { get; init; }

    /// <summary>Gets the observed secondary error-handler status when supplied.</summary>
    public HealthExecutionStepStatus? ErrorHandlingStatus { get; init; }

    /// <summary>Gets the observed cleanup status when supplied.</summary>
    public HealthExecutionStepStatus? CleanupStatus { get; init; }

    /// <summary>Gets the original detached failure diagnostic; handler success never erases it.</summary>
    public HealthDiagnostic? OriginalError { get; init; }

    /// <summary>Gets the detached secondary error-handler diagnostic when already observed.</summary>
    public HealthDiagnostic? ErrorHandlingError { get; init; }

    /// <summary>Gets the detached cleanup diagnostic when already observed.</summary>
    public HealthDiagnostic? CleanupError { get; init; }

    /// <summary>Gets a detached integration diagnostic without replacing the actual execution outcome.</summary>
    /// <remarks>Without an execution ID, IntegrationFault and Retired publish the monitor's separate registration diagnostic.</remarks>
    public HealthDiagnostic? IntegrationError { get; init; }
}
