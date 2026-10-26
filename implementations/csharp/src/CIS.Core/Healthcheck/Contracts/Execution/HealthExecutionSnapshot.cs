namespace CIS.Healthcheck;

/// <summary>A detached bounded execution record captured from actual accepted facts.</summary>
public sealed class HealthExecutionSnapshot
{
    internal HealthExecutionSnapshot(HealthExecutionSnapshotData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        RegistrationId = data.RegistrationId;
        ExecutionId = data.ExecutionId;
        Phase = data.Phase;
        EligibleAtUtc = data.EligibleAtUtc?.ToUniversalTime();
        AdmittedAtUtc = data.AdmittedAtUtc?.ToUniversalTime();
        ConditionStartedAtUtc = data.ConditionStartedAtUtc?.ToUniversalTime();
        CallbackStartedAtUtc = data.CallbackStartedAtUtc?.ToUniversalTime();
        WorkCompletedAtUtc = data.WorkCompletedAtUtc?.ToUniversalTime();
        ErrorHandlingStartedAtUtc = data.ErrorHandlingStartedAtUtc?.ToUniversalTime();
        ErrorHandlingCompletedAtUtc = data.ErrorHandlingCompletedAtUtc?.ToUniversalTime();
        CleanupCompletedAtUtc = data.CleanupCompletedAtUtc?.ToUniversalTime();
        SettledAtUtc = data.SettledAtUtc?.ToUniversalTime();
        QueueDelayMilliseconds = data.QueueDelayMilliseconds;
        ConditionDurationMilliseconds = data.ConditionDurationMilliseconds;
        CallbackDurationMilliseconds = data.CallbackDurationMilliseconds;
        ErrorHandlingDurationMilliseconds = data.ErrorHandlingDurationMilliseconds;
        CleanupDurationMilliseconds = data.CleanupDurationMilliseconds;
        TotalDurationMilliseconds = data.TotalDurationMilliseconds;
        ErrorHandlingStatus = data.ErrorHandlingStatus;
        CleanupStatus = data.CleanupStatus;
        OriginalError = data.OriginalError;
        ErrorHandlingError = data.ErrorHandlingError;
        CleanupError = data.CleanupError;
        IntegrationError = data.IntegrationError;
    }

    /// <summary>Gets the correlated scheduling registration identity.</summary>
    public Guid RegistrationId { get; }
    /// <summary>Gets the positive execution identity represented by this record.</summary>
    public long ExecutionId { get; }
    /// <summary>Gets the most recently accepted lifecycle phase.</summary>
    public HealthExecutionPhase Phase { get; }
    /// <summary>Gets actual UTC eligibility time when known.</summary>
    public DateTimeOffset? EligibleAtUtc { get; }
    /// <summary>Gets actual UTC admission time when known.</summary>
    public DateTimeOffset? AdmittedAtUtc { get; }
    /// <summary>Gets actual UTC condition-start time when known.</summary>
    public DateTimeOffset? ConditionStartedAtUtc { get; }
    /// <summary>Gets actual UTC callback-start time when known.</summary>
    public DateTimeOffset? CallbackStartedAtUtc { get; }
    /// <summary>Gets actual UTC end of condition or callback work when known.</summary>
    public DateTimeOffset? WorkCompletedAtUtc { get; }
    /// <summary>Gets actual UTC error-handler start time when known.</summary>
    public DateTimeOffset? ErrorHandlingStartedAtUtc { get; }
    /// <summary>Gets actual UTC error-handler completion time when known.</summary>
    public DateTimeOffset? ErrorHandlingCompletedAtUtc { get; }
    /// <summary>Gets actual UTC cleanup completion time when known.</summary>
    public DateTimeOffset? CleanupCompletedAtUtc { get; }
    /// <summary>Gets actual UTC settlement time when known.</summary>
    public DateTimeOffset? SettledAtUtc { get; }
    /// <summary>Gets measured monotonic milliseconds from eligibility to admission.</summary>
    public double? QueueDelayMilliseconds { get; }
    /// <summary>Gets measured monotonic condition duration in milliseconds.</summary>
    public double? ConditionDurationMilliseconds { get; }
    /// <summary>Gets measured monotonic callback duration in milliseconds.</summary>
    public double? CallbackDurationMilliseconds { get; }
    /// <summary>Gets measured monotonic secondary error-handler duration in milliseconds.</summary>
    public double? ErrorHandlingDurationMilliseconds { get; }
    /// <summary>Gets measured monotonic cleanup duration in milliseconds.</summary>
    public double? CleanupDurationMilliseconds { get; }
    /// <summary>Gets measured monotonic duration through actual settlement in milliseconds.</summary>
    public double? TotalDurationMilliseconds { get; }
    /// <summary>Gets the observed secondary error-handler status.</summary>
    public HealthExecutionStepStatus ErrorHandlingStatus { get; }
    /// <summary>Gets the observed cleanup status.</summary>
    public HealthExecutionStepStatus CleanupStatus { get; }
    /// <summary>Gets the detached original failure diagnostic.</summary>
    public HealthDiagnostic? OriginalError { get; }
    /// <summary>Gets the detached secondary error-handler diagnostic.</summary>
    public HealthDiagnostic? ErrorHandlingError { get; }
    /// <summary>Gets the detached cleanup diagnostic.</summary>
    public HealthDiagnostic? CleanupError { get; }
    /// <summary>Gets a detached reporting/integration diagnostic.</summary>
    public HealthDiagnostic? IntegrationError { get; }
}

/// <summary>Named internal construction input for one accumulated detached execution record.</summary>
internal sealed class HealthExecutionSnapshotData
{
    internal required Guid RegistrationId { get; init; }
    internal required long ExecutionId { get; init; }
    internal required HealthExecutionPhase Phase { get; init; }
    internal DateTimeOffset? EligibleAtUtc { get; init; }
    internal DateTimeOffset? AdmittedAtUtc { get; init; }
    internal DateTimeOffset? ConditionStartedAtUtc { get; init; }
    internal DateTimeOffset? CallbackStartedAtUtc { get; init; }
    internal DateTimeOffset? WorkCompletedAtUtc { get; init; }
    internal DateTimeOffset? ErrorHandlingStartedAtUtc { get; init; }
    internal DateTimeOffset? ErrorHandlingCompletedAtUtc { get; init; }
    internal DateTimeOffset? CleanupCompletedAtUtc { get; init; }
    internal DateTimeOffset? SettledAtUtc { get; init; }
    internal double? QueueDelayMilliseconds { get; init; }
    internal double? ConditionDurationMilliseconds { get; init; }
    internal double? CallbackDurationMilliseconds { get; init; }
    internal double? ErrorHandlingDurationMilliseconds { get; init; }
    internal double? CleanupDurationMilliseconds { get; init; }
    internal double? TotalDurationMilliseconds { get; init; }
    internal HealthExecutionStepStatus ErrorHandlingStatus { get; init; }
    internal HealthExecutionStepStatus CleanupStatus { get; init; }
    internal HealthDiagnostic? OriginalError { get; init; }
    internal HealthDiagnostic? ErrorHandlingError { get; init; }
    internal HealthDiagnostic? CleanupError { get; init; }
    internal HealthDiagnostic? IntegrationError { get; init; }
}
