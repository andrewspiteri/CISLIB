namespace CIS.Healthcheck;

/// <summary>The disposition of one detached execution fact.</summary>
public enum HealthFactDisposition
{
    /// <summary>The fact was recorded.</summary>
    Recorded = 0,
    /// <summary>An identical retained terminal fact was already recorded.</summary>
    Duplicate = 1,
    /// <summary>The fact is older than the bounded retained history.</summary>
    Obsolete = 2,
    /// <summary>The binding or associated monitor has retired.</summary>
    Retired = 3
}

/// <summary>Identifies the detached lifecycle or outcome fact being reported.</summary>
public enum HealthExecutionFactKind
{
    /// <summary>The scheduling registration was established.</summary>
    Registered = 0,
    /// <summary>The execution became eligible.</summary>
    Eligible = 1,
    /// <summary>The execution was admitted.</summary>
    Admitted = 2,
    /// <summary>Condition evaluation began.</summary>
    ConditionStarted = 3,
    /// <summary>Callback work began.</summary>
    CallbackStarted = 4,
    /// <summary>The execution produced one policy outcome.</summary>
    Outcome = 5,
    /// <summary>Error handling began.</summary>
    ErrorHandlingStarted = 6,
    /// <summary>Error handling completed.</summary>
    ErrorHandlingCompleted = 7,
    /// <summary>Execution cleanup completed.</summary>
    CleanupCompleted = 8,
    /// <summary>All actual execution work and cleanup settled.</summary>
    Settled = 9,
    /// <summary>The scheduling registration retired.</summary>
    Retired = 10,
    /// <summary>A bounded integration diagnostic was observed without changing the actual callback outcome.</summary>
    IntegrationFault = 11
}

/// <summary>A detached execution phase, independent of health policy interpretation.</summary>
public enum HealthExecutionPhase
{
    /// <summary>The registration exists; no execution has become eligible.</summary>
    Registered = 0,
    /// <summary>The execution is eligible for admission.</summary>
    Eligible = 1,
    /// <summary>The execution has been admitted.</summary>
    Admitted = 2,
    /// <summary>The condition is being evaluated or produced the outcome.</summary>
    Condition = 3,
    /// <summary>The callback is running or produced the outcome.</summary>
    Callback = 4,
    /// <summary>Error handling is running or completed.</summary>
    ErrorHandling = 5,
    /// <summary>Execution cleanup is running or completed.</summary>
    Cleanup = 6,
    /// <summary>The actual execution has settled.</summary>
    Settled = 7,
    /// <summary>The registration has retired.</summary>
    Retired = 8
}

/// <summary>The observed status of secondary error handling or cleanup.</summary>
public enum HealthExecutionStepStatus
{
    /// <summary>No start of this step has been observed.</summary>
    NotStarted = 0,
    /// <summary>The step started and has not completed.</summary>
    Running = 1,
    /// <summary>The step completed successfully.</summary>
    Succeeded = 2,
    /// <summary>The step failed.</summary>
    Failed = 3,
    /// <summary>The step completed with expected lifetime cancellation.</summary>
    ExpectedCancellation = 4,
    /// <summary>The execution does not require this step.</summary>
    NotApplicable = 5,
    /// <summary>The step was not invoked because its target or owner was unavailable; a detached diagnostic may explain why.</summary>
    NotInvoked = 6
}
