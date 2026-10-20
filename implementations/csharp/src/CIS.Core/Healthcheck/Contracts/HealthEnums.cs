namespace CIS.Healthcheck;

/// <summary>The aggregate health of the active critical monitors.</summary>
public enum HealthStatus
{
    /// <summary>No active critical monitor is unhealthy.</summary>
    Healthy = 0,
    /// <summary>At least one active critical monitor is unhealthy.</summary>
    Unhealthy = 1
}

/// <summary>The evaluated state of one monitor.</summary>
public enum MonitorState
{
    /// <summary>The policy has not received the observation needed for evaluation.</summary>
    Pending = 0,
    /// <summary>The policy currently permits normal health.</summary>
    Healthy = 1,
    /// <summary>The policy currently reports an unhealthy condition.</summary>
    Unhealthy = 2
}

/// <summary>The fixed evaluation policy selected when a monitor is registered.</summary>
public enum HealthPolicyKind
{
    /// <summary>An explicitly flagged or cleared condition.</summary>
    Condition = 0,
    /// <summary>A streak of failed outcomes evaluated against an explicit threshold.</summary>
    ConsecutiveFailures = 1,
    /// <summary>A finite numeric observation evaluated against an explicit comparison.</summary>
    NumericThreshold = 2,
    /// <summary>Availability established by publishing usable data and cleared by invalidation.</summary>
    FirstSuccessAvailability = 3
}

/// <summary>The comparison that makes a numeric threshold monitor unhealthy.</summary>
public enum NumericComparison
{
    /// <summary>The observed value is strictly greater than the threshold.</summary>
    GreaterThan = 0,
    /// <summary>The observed value is greater than or equal to the threshold.</summary>
    GreaterThanOrEqual = 1,
    /// <summary>The observed value is strictly less than the threshold.</summary>
    LessThan = 2,
    /// <summary>The observed value is less than or equal to the threshold.</summary>
    LessThanOrEqual = 3
}

/// <summary>An explicitly reported execution outcome, independent of later settlement.</summary>
public enum HealthExecutionOutcome
{
    /// <summary>The operation succeeded.</summary>
    Succeeded = 0,
    /// <summary>The operation failed.</summary>
    Failed = 1,
    /// <summary>The operation was intentionally skipped.</summary>
    Skipped = 2,
    /// <summary>The operation observed expected retirement or shutdown cancellation.</summary>
    ExpectedCancellation = 3
}

/// <summary>The one scalar representation carried by a diagnostic measurement.</summary>
public enum HealthMeasurementValueKind
{
    /// <summary>No scalar value is available.</summary>
    Unavailable = 0,
    /// <summary>A bounded Unicode string.</summary>
    String = 1,
    /// <summary>A Boolean value.</summary>
    Boolean = 2,
    /// <summary>A signed 64-bit integer.</summary>
    Integer = 3,
    /// <summary>A finite double-precision number.</summary>
    Number = 4
}

/// <summary>The selected health report representation.</summary>
public enum HealthReportView
{
    /// <summary>A compact report selected by aggregate health.</summary>
    Health = 0,
    /// <summary>A compact report selected by application readiness.</summary>
    Readiness = 1,
    /// <summary>A complete report containing ordered monitor details.</summary>
    Details = 2
}
