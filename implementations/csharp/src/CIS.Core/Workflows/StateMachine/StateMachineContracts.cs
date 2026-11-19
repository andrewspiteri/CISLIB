namespace CIS.Workflows;

/// <summary>Creates immutable definitions bound to an application-owned reference context.</summary>
public interface IStateMachineFactory
{
    /// <summary>Creates a builder without activating handlers or allocating scopes.</summary>
    StateMachineBuilder<TState, TTrigger, TContext> CreateBuilder<TState, TTrigger, TContext>(
        string name, TContext context, StateMachineOptions? options = null)
        where TState : struct, Enum where TTrigger : struct, Enum where TContext : class;
}

/// <summary>A reusable, exclusive, in-memory state machine with result-based operational completion.</summary>
public interface IStateMachine<TState, TTrigger> where TState : struct, Enum where TTrigger : struct, Enum
{
    /// <summary>Gets the committed state, never a speculative target.</summary>
    TState CurrentState
    {
        get;
    }
    /// <summary>Gets whether an admitted attempt still owns work or cleanup.</summary>
    bool IsExecuting
    {
        get;
    }
    /// <summary>Gets the immutable trigger list for one committed-state observation.</summary>
    IReadOnlyCollection<TTrigger> PermittedTriggers
    {
        get;
    }
    /// <summary>Gets the last completed admitted attempt; Busy/pre-cancelled calls do not replace it.</summary>
    StateTransitionResult<TState, TTrigger>? LastTransition
    {
        get;
    }
    /// <summary>Checks mapping membership only, without admission, validation work or I/O.</summary>
    bool CanTrigger(TTrigger trigger);
    /// <summary>Returns one immutable operational result after actual work and owned cleanup settle.</summary>
    Task<StateTransitionResult<TState, TTrigger>> Trigger(TTrigger trigger, CancellationToken cancellationToken = default);
}

/// <summary>One freshly activated attempt's application work and optional compensation.</summary>
public interface IStateTransitionHandler<TState, TTrigger, TContext>
    where TState : struct, Enum where TTrigger : struct, Enum where TContext : class
{
    /// <summary>Performs selected transition work using the bound context and execution token.</summary>
    Task<TransitionActionResult> Execute(TContext context, TransitionInfo<TState, TTrigger> transition,
        CancellationToken cancellationToken);
    /// <summary>Compensates this entered attempt with its original failure and independent cleanup token.</summary>
    Task Error(TContext context, TransitionInfo<TState, TTrigger> transition, StateTransitionFailure failure,
        CancellationToken cancellationToken);
}

/// <summary>Mutable input options copied when the definition is built.</summary>
public sealed class StateMachineOptions
{
    /// <summary>Gets or sets the independent cooperative compensation budget; infinite is supported.</summary>
    public TimeSpan ErrorHandlingTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets automatic events without changing execution or metrics.</summary>
    public bool LogCalls { get; set; } = true;
    /// <summary>Gets or sets Information promotion for successful routine events; false keeps them Debug.</summary>
    public bool LogRoutineAsInformation
    {
        get; set;
    }
}

/// <summary>An immutable selected source, intended target and trigger.</summary>
public sealed record TransitionInfo<TState, TTrigger>(TState SourceState, TState TargetState, TTrigger Trigger)
    where TState : struct, Enum where TTrigger : struct, Enum;

/// <summary>A guarded immutable expected handler outcome.</summary>
public sealed class TransitionActionResult
{
    private static readonly TransitionActionResult succeeded = new(true, null, null);

    private TransitionActionResult(bool success, string? code, string? message)
    {
        Success = success;
        ErrorCode = code;
        ErrorMessage = message;
    }
    /// <summary>Gets whether selected work succeeded.</summary>
    public bool Success
    {
        get;
    }
    /// <summary>Gets the stable business failure code, absent on success.</summary>
    public string? ErrorCode
    {
        get;
    }
    /// <summary>Gets the optional safe application explanation.</summary>
    public string? ErrorMessage
    {
        get;
    }
    /// <summary>Returns success without failure details.</summary>
    public static TransitionActionResult Succeeded()
    {
        return succeeded;
    }
    /// <summary>Returns an expected unsuccessful result with a nonempty stable code.</summary>
    public static TransitionActionResult Failed(string code, string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new(false, code, message);
    }
}

/// <summary>The finite outcomes of a public trigger call.</summary>
public enum StateTransitionOutcome
{
    /// <summary>Selected work and cleanup succeeded and the target was committed.</summary>
    Succeeded,
    /// <summary>The trigger is undefined or has no mapping from the observed source.</summary>
    Rejected,
    /// <summary>Another attempt already owns this machine.</summary>
    Busy,
    /// <summary>An operational phase failed and the source was retained.</summary>
    Failed,
    /// <summary>Execution cancellation was observed before its cutoff; the task still completes normally.</summary>
    Cancelled
}

/// <summary>The originating operation of a transition diagnostic.</summary>
public enum StateTransitionPhase
{
    /// <summary>Immediate call admission or pre-cancellation.</summary>
    Admission,
    /// <summary>Enum/mapping validation after instance admission.</summary>
    Validation,
    /// <summary>Scope acquisition, activation or preparation checkpoint.</summary>
    Construction,
    /// <summary>Forward work or its final cancellation checkpoint.</summary>
    Execute,
    /// <summary>Application compensation.</summary>
    Error,
    /// <summary>Owned handler, scope or cleanup-budget disposal.</summary>
    Disposal
}

/// <summary>A portable finite classification, independent of native exception types.</summary>
public enum StateTransitionFailureKind
{
    /// <summary>Busy or invalid/unavailable trigger.</summary>
    Rejection,
    /// <summary>An expected unsuccessful handler result.</summary>
    ReportedFailure,
    /// <summary>An unexpected application-boundary exception.</summary>
    Exception,
    /// <summary>A null task/result or another violated handler contract.</summary>
    ContractViolation,
    /// <summary>Identified requested execution cancellation.</summary>
    Cancellation,
    /// <summary>Identified cancellation of compensation by its independent budget.</summary>
    CleanupTimeout
}

/// <summary>A guarded immutable primary or secondary cause; native exception identity is diagnostic only.</summary>
public sealed class StateTransitionFailure
{
    /// <summary>Constructs a portable cause with a stable code and safe explanation.</summary>
    public StateTransitionFailure(string code, string message, StateTransitionPhase phase,
        StateTransitionFailureKind kind, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(phase) || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }
        Code = code;
        Message = message;
        Phase = phase;
        Kind = kind;
        Exception = exception;
    }
    /// <summary>Gets the stable failure code.</summary>
    public string Code
    {
        get;
    }
    /// <summary>Gets the safe explanation.</summary>
    public string Message
    {
        get;
    }
    /// <summary>Gets the originating phase.</summary>
    public StateTransitionPhase Phase
    {
        get;
    }
    /// <summary>Gets the finite failure classification.</summary>
    public StateTransitionFailureKind Kind
    {
        get;
    }
    /// <summary>Gets the original C# exception when present.</summary>
    public Exception? Exception
    {
        get;
    }
}

/// <summary>A detached terminal snapshot of the selected handler's two phases.</summary>
public sealed class StateTransitionHandlerReport
{
    /// <summary>Constructs terminal phase facts without retaining the handler or its dependencies.</summary>
    public StateTransitionHandlerReport(string name, PipelineStepExecutionStatus executionStatus,
        PipelineStepErrorStatus errorStatus, StateTransitionFailure? executionError = null,
        StateTransitionFailure? errorHandlingError = null, TimeSpan? executionDuration = null, TimeSpan? errorDuration = null)
    {
        PipelineContractRules.ValidateName(name, nameof(name));
        if (!Enum.IsDefined(executionStatus) || executionStatus == PipelineStepExecutionStatus.Running
            || !Enum.IsDefined(errorStatus) || errorStatus == PipelineStepErrorStatus.Running)
        {
            throw new ArgumentException("A terminal handler report requires terminal or unentered phase statuses.");
        }
        if (executionDuration < TimeSpan.Zero || errorDuration < TimeSpan.Zero
            || executionStatus == PipelineStepExecutionStatus.NotStarted && executionDuration.HasValue
            || errorStatus == PipelineStepErrorStatus.NotCalled && errorDuration.HasValue)
        {
            throw new ArgumentException("A duration must be nonnegative and belong to an entered method.");
        }
        bool forwardFailed = executionStatus is PipelineStepExecutionStatus.Failed or PipelineStepExecutionStatus.Cancelled;
        bool errorFailed = errorStatus is PipelineStepErrorStatus.Failed or PipelineStepErrorStatus.Cancelled;
        if (executionStatus == PipelineStepExecutionStatus.NotStarted && errorStatus != PipelineStepErrorStatus.NotCalled)
        {
            throw new ArgumentException("An unentered handler cannot have compensation work.");
        }
        if (forwardFailed != (executionError is not null) || errorFailed != (errorHandlingError is not null))
        {
            throw new ArgumentException("Phase failure details must agree with phase statuses.");
        }
        Name = name;
        ExecutionStatus = executionStatus;
        ErrorStatus = errorStatus;
        ExecutionError = executionError;
        ErrorHandlingError = errorHandlingError;
        ExecutionDuration = executionDuration;
        ErrorDuration = errorDuration;
    }
    /// <summary>Gets the stable handler display name.</summary>
    public string Name
    {
        get;
    }
    /// <summary>Gets the actual forward outcome.</summary>
    public PipelineStepExecutionStatus ExecutionStatus
    {
        get;
    }
    /// <summary>Gets the actual compensation outcome.</summary>
    public PipelineStepErrorStatus ErrorStatus
    {
        get;
    }
    /// <summary>Gets the original forward cause when the method failed or cancelled.</summary>
    public StateTransitionFailure? ExecutionError
    {
        get;
    }
    /// <summary>Gets the secondary compensation cause.</summary>
    public StateTransitionFailure? ErrorHandlingError
    {
        get;
    }
    /// <summary>Gets known forward elapsed time, absent for unentered/unavailable measurements.</summary>
    public TimeSpan? ExecutionDuration
    {
        get;
    }
    /// <summary>Gets known compensation elapsed time, absent for unentered/unavailable measurements.</summary>
    public TimeSpan? ErrorDuration
    {
        get;
    }
}
