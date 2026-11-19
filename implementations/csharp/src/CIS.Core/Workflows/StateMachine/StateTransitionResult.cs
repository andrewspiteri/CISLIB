namespace CIS.Workflows;

/// <summary>A guarded immutable public-call snapshot, also constructible by consumer mocks.</summary>
public sealed class StateTransitionResult<TState, TTrigger> where TState : struct, Enum where TTrigger : struct, Enum
{
    private StateTransitionResult(string name, Guid id, TTrigger trigger, TState source, TState? target,
        TState current, StateTransitionOutcome outcome, StateTransitionFailure? error, TimeSpan? duration,
        StateTransitionHandlerReport? handler, IReadOnlyList<StateTransitionFailure> handling,
        IReadOnlyList<StateTransitionFailure> lifecycle, bool expired)
    {
        MachineName = name;
        AttemptId = id;
        Trigger = trigger;
        SourceState = source;
        TargetState = target;
        CurrentState = current;
        Outcome = outcome;
        Error = error;
        Duration = duration;
        HandlerReport = handler;
        ErrorHandlingFailures = handling;
        LifecycleFailures = lifecycle;
        CleanupBudgetExpired = expired;
    }
    /// <summary>Constructs a valid terminal result without bypassing state/outcome invariants.</summary>
    public static StateTransitionResult<TState, TTrigger> Create(string machineName, Guid attemptId,
        TTrigger trigger, TState sourceState, TState? targetState, TState currentState,
        StateTransitionOutcome outcome, StateTransitionFailure? error = null, TimeSpan? duration = null,
        StateTransitionHandlerReport? handlerReport = null,
        IReadOnlyList<StateTransitionFailure>? errorHandlingFailures = null,
        IReadOnlyList<StateTransitionFailure>? lifecycleFailures = null, bool cleanupBudgetExpired = false)
    {
        PipelineContractRules.ValidateName(machineName, nameof(machineName));
        if (attemptId == Guid.Empty)
        {
            throw new ArgumentException("A result requires a nonempty call identifier.", nameof(attemptId));
        }
        if (!Enum.IsDefined(sourceState) || !Enum.IsDefined(currentState)
            || targetState is { } target && !Enum.IsDefined(target) || !Enum.IsDefined(outcome))
        {
            throw new ArgumentException("Result state and outcome values must be declared.");
        }
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
        IReadOnlyList<StateTransitionFailure> handling = Copy(errorHandlingFailures);
        IReadOnlyList<StateTransitionFailure> lifecycle = Copy(lifecycleFailures);
        bool success = outcome == StateTransitionOutcome.Succeeded;
        if (success != (error is null))
        {
            throw new ArgumentException("Only a successful result omits its primary cause.");
        }
        if (success)
        {
            if (!targetState.HasValue || !EqualityComparer<TState>.Default.Equals(currentState, targetState.Value)
                || !Enum.IsDefined(trigger) || handling.Count != 0 || lifecycle.Count != 0 || cleanupBudgetExpired
                || handlerReport is not null && (handlerReport.ExecutionStatus != PipelineStepExecutionStatus.Succeeded
                    || handlerReport.ErrorStatus != PipelineStepErrorStatus.NotCalled))
            {
                throw new ArgumentException("Success requires a committed target and no failed cleanup.");
            }
        }
        else
        {
            if (!EqualityComparer<TState>.Default.Equals(sourceState, currentState))
            {
                throw new ArgumentException("An unsuccessful attempt must retain its source state.");
            }
            if (outcome is StateTransitionOutcome.Busy or StateTransitionOutcome.Rejected)
            {
                if (targetState.HasValue || handlerReport is not null || handling.Count != 0 || lifecycle.Count != 0
                    || cleanupBudgetExpired || error!.Kind != StateTransitionFailureKind.Rejection)
                {
                    throw new ArgumentException("A rejected admission/mapping result cannot report selected work.");
                }
            }
            else if (outcome == StateTransitionOutcome.Cancelled && error!.Kind != StateTransitionFailureKind.Cancellation
                || outcome == StateTransitionOutcome.Failed && error!.Kind is StateTransitionFailureKind.Rejection
                    or StateTransitionFailureKind.Cancellation or StateTransitionFailureKind.CleanupTimeout)
            {
                throw new ArgumentException("The primary classification must agree with the result outcome.");
            }
        }
        return new(machineName, attemptId, trigger, sourceState, targetState, currentState, outcome, error,
            duration, handlerReport, handling, lifecycle, cleanupBudgetExpired);
    }

    private static IReadOnlyList<StateTransitionFailure> Copy(IReadOnlyList<StateTransitionFailure>? source)
    {
        int count = source?.Count ?? 0;
        if (count < 0)
        {
            throw new ArgumentException("A diagnostic collection cannot have a negative count.");
        }
        var copy = new StateTransitionFailure[count];
        for (int index = 0; index < count; index++)
        {
            copy[index] = source![index] ?? throw new ArgumentException("A diagnostic collection cannot contain null.");
        }
        return Array.AsReadOnly(copy);
    }

    /// <summary>Gets the stable machine alias.</summary>
    public string MachineName
    {
        get;
    }
    /// <summary>Gets this public call's identity, including Busy/pre-cancelled calls.</summary>
    public Guid AttemptId
    {
        get;
    }
    /// <summary>Gets whether the target committed successfully.</summary>
    public bool Success => Outcome == StateTransitionOutcome.Succeeded;
    /// <summary>Gets the finite operational outcome.</summary>
    public StateTransitionOutcome Outcome
    {
        get;
    }
    /// <summary>Gets the supplied trigger, including an undefined value for diagnostic inspection.</summary>
    public TTrigger Trigger
    {
        get;
    }
    /// <summary>Gets the call's committed source observation.</summary>
    public TState SourceState
    {
        get;
    }
    /// <summary>Gets the intended target when a mapping was selected.</summary>
    public TState? TargetState
    {
        get;
    }
    /// <summary>Gets the committed state for this result.</summary>
    public TState CurrentState
    {
        get;
    }
    /// <summary>Gets whether this successful attempt committed a different state.</summary>
    public bool StateChanged => Success && !EqualityComparer<TState>.Default.Equals(SourceState, CurrentState);
    /// <summary>Gets the primary portable code.</summary>
    public string? ErrorCode => Error?.Code;
    /// <summary>Gets the primary safe explanation.</summary>
    public string? ErrorMessage => Error?.Message;
    /// <summary>Gets the original cause, independent of secondary failures.</summary>
    public StateTransitionFailure? Error
    {
        get;
    }
    /// <summary>Gets measured elapsed time, or null when timing infrastructure failed.</summary>
    public TimeSpan? Duration
    {
        get;
    }
    /// <summary>Gets terminal handler facts without retaining the handler or its services.</summary>
    public StateTransitionHandlerReport? HandlerReport
    {
        get;
    }
    /// <summary>Gets detached secondary compensation failures.</summary>
    public IReadOnlyList<StateTransitionFailure> ErrorHandlingFailures
    {
        get;
    }
    /// <summary>Gets detached construction/disposal diagnostics.</summary>
    public IReadOnlyList<StateTransitionFailure> LifecycleFailures
    {
        get;
    }
    /// <summary>Gets whether the independent compensation budget requested cancellation.</summary>
    public bool CleanupBudgetExpired
    {
        get;
    }
}
