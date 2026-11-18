namespace CIS.Workflows;

/// <summary>One freshly activated ordered participant in a pipeline.</summary>
/// <typeparam name="TContext">The shared application-owned context type.</typeparam>
public interface IPipelineStep<TContext> where TContext : class
{
    /// <summary>Performs this registration's forward work once.</summary>
    Task<PipelineStepResult> Execute(TContext context, CancellationToken cancellationToken);

    /// <summary>Compensates entered work using the original failure and an independent cleanup token.</summary>
    Task Error(TContext context, PipelineFailure failure, CancellationToken cancellationToken);
}

/// <summary>Creates single-execution definitions using the host's existing dependencies.</summary>
public interface IPipelineFactory
{
    /// <summary>Creates a builder; construction and scope acquisition are deferred until execution.</summary>
    PipelineBuilder<TContext> CreateBuilder<TContext>(string name, TContext context, PipelineOptions? options = null)
        where TContext : class;
}

/// <summary>One inspectable, single-use pipeline execution.</summary>
/// <typeparam name="TContext">The shared application-owned context type.</typeparam>
public interface IPipeline<TContext> where TContext : class
{
    /// <summary>Gets the stable definition name.</summary>
    string Name { get; }
    /// <summary>Gets the identifier of this built execution instance.</summary>
    Guid ExecutionId { get; }
    /// <summary>Gets its current overall lifecycle state.</summary>
    PipelineStatus Status { get; }
    /// <summary>Gets whether execution and owned cleanup finished successfully.</summary>
    bool Success { get; }
    /// <summary>Gets the original application-owned context without cloning.</summary>
    TContext Context { get; }
    /// <summary>Gets the original unsuccessful outcome, independently of secondary failures.</summary>
    PipelineFailure? Error { get; }
    /// <summary>Gets the failed forward registration, or null for other primary failures.</summary>
    PipelineStepReport? FailedStep { get; }
    /// <summary>Gets a detached ordered snapshot of all registrations.</summary>
    IReadOnlyList<PipelineStepReport> Steps { get; }
    /// <summary>Gets a detached snapshot of registrations whose forward methods were entered.</summary>
    IReadOnlyList<PipelineStepReport> ExecutedSteps { get; }
    /// <summary>Gets secondary handler failures in actual unwind order.</summary>
    IReadOnlyList<PipelineFailure> ErrorHandlingFailures { get; }
    /// <summary>Gets construction/disposal diagnostics without replacing the original cause.</summary>
    IReadOnlyList<PipelineFailure> LifecycleFailures { get; }
    /// <summary>Gets final elapsed time after owned work and cleanup finish.</summary>
    TimeSpan? Duration { get; }
    /// <summary>Gets whether the independent unwind budget requested cancellation.</summary>
    bool CleanupBudgetExpired { get; }
    /// <summary>Claims and awaits this one run; rejected repeated calls do not alter its report.</summary>
    Task Execute(CancellationToken cancellationToken = default);
}

/// <summary>Build-time per-definition settings, copied by Build.</summary>
public sealed class PipelineOptions
{
    /// <summary>Gets or sets the cooperative whole-unwind budget; infinite disables its timer.</summary>
    public TimeSpan ErrorHandlingTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets automatic lifecycle logging without changing execution or metrics.</summary>
    public bool LogCalls { get; set; } = true;
}

/// <summary>An immutable expected result from forward step work.</summary>
public sealed class PipelineStepResult
{
    private static readonly PipelineStepResult successful = new(true, null, null);

    private PipelineStepResult(bool success, string? errorCode, string? errorMessage)
    {
        Success = success;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Gets whether forward work succeeded.</summary>
    public bool Success { get; }
    /// <summary>Gets the stable business failure code, if unsuccessful.</summary>
    public string? ErrorCode { get; }
    /// <summary>Gets the optional application-supplied safe failure explanation.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Returns a successful result without failure details.</summary>
    public static PipelineStepResult Succeeded()
    {
        return successful;
    }

    /// <summary>Returns an expected failure with a nonempty stable code.</summary>
    public static PipelineStepResult Failed(string code, string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new(false, code, message);
    }
}

/// <summary>The overall execution lifecycle, terminal only after owned cleanup.</summary>
public enum PipelineStatus
{
    /// <summary>No accepted invocation has started.</summary>
    NotStarted,
    /// <summary>Activation, forward work or successful-path cleanup is active.</summary>
    Running,
    /// <summary>Reverse handlers or unsuccessful-path cleanup is active.</summary>
    Unwinding,
    /// <summary>Forward work and cleanup succeeded.</summary>
    Succeeded,
    /// <summary>A reported failure, programming fault or primary cleanup fault ended the run.</summary>
    Failed,
    /// <summary>Execution cancellation was observed before forward success committed.</summary>
    Cancelled
}

/// <summary>The actual lifecycle of a registration's forward method.</summary>
public enum PipelineStepExecutionStatus
{
    /// <summary>The method was not entered.</summary>
    NotStarted,
    /// <summary>The method is still active.</summary>
    Running,
    /// <summary>The method returned success.</summary>
    Succeeded,
    /// <summary>The method returned failure or faulted.</summary>
    Failed,
    /// <summary>The method ended with identified execution cancellation.</summary>
    Cancelled
}

/// <summary>The actual lifecycle of a registration's reverse handler.</summary>
public enum PipelineStepErrorStatus
{
    /// <summary>The handler was not entered.</summary>
    NotCalled,
    /// <summary>The handler is still active.</summary>
    Running,
    /// <summary>The handler finished successfully.</summary>
    Succeeded,
    /// <summary>The handler faulted.</summary>
    Failed,
    /// <summary>The handler ended with identified cleanup-budget cancellation.</summary>
    Cancelled
}

/// <summary>The originating operation of a failure.</summary>
public enum PipelinePhase
{
    /// <summary>Scope acquisition, activation or a preparation checkpoint.</summary>
    Construction,
    /// <summary>Forward invocation or a forward checkpoint.</summary>
    Execute,
    /// <summary>Application compensation.</summary>
    Error,
    /// <summary>Owned step or scope disposal.</summary>
    Disposal
}

/// <summary>A finite classification independent of native exception types.</summary>
public enum PipelineFailureKind
{
    /// <summary>An expected unsuccessful step result.</summary>
    ReportedFailure,
    /// <summary>An unexpected exception.</summary>
    Exception,
    /// <summary>Identified execution cancellation.</summary>
    Cancellation,
    /// <summary>A null task/result or another violated step contract.</summary>
    ContractViolation,
    /// <summary>A handler ended with identified cleanup-budget cancellation.</summary>
    CleanupTimeout
}

/// <summary>An immutable original or secondary cause; registration fields are absent for scope/checkpoint failures.</summary>
/// <param name="Code">The stable failure code.</param>
/// <param name="Message">The safe explanation.</param>
/// <param name="Phase">The originating phase.</param>
/// <param name="Kind">The finite failure classification.</param>
/// <param name="StepPosition">The zero-based registration position when applicable.</param>
/// <param name="StepName">The registration's display name when applicable.</param>
/// <param name="Exception">The original native exception, if any.</param>
public sealed record PipelineFailure(string Code, string Message, PipelinePhase Phase, PipelineFailureKind Kind,
    int? StepPosition = null, string? StepName = null, Exception? Exception = null);

/// <summary>A detached immutable snapshot of one configured registration.</summary>
/// <param name="Position">The zero-based registration position.</param>
/// <param name="Name">The stable display name.</param>
/// <param name="ExecutionStatus">The actual forward status.</param>
/// <param name="ErrorStatus">The actual handler status.</param>
/// <param name="ExecutionError">The forward failure if any.</param>
/// <param name="ErrorHandlingError">The handler failure if any.</param>
/// <param name="ExecutionDuration">Elapsed forward time, absent when unentered.</param>
/// <param name="ErrorDuration">Elapsed handler time, absent when unentered.</param>
public sealed record PipelineStepReport(int Position, string Name,
    PipelineStepExecutionStatus ExecutionStatus, PipelineStepErrorStatus ErrorStatus,
    PipelineFailure? ExecutionError, PipelineFailure? ErrorHandlingError,
    TimeSpan? ExecutionDuration, TimeSpan? ErrorDuration);

/// <summary>Preserves a failed primary cancellation exception without representing caller cancellation.</summary>
public sealed class PipelineExecutionException : Exception
{
    /// <summary>Wraps the original cancellation exception of a failed pipeline.</summary>
    public PipelineExecutionException(OperationCanceledException innerException)
        : base("A pipeline phase faulted with an unrelated cancellation exception.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
