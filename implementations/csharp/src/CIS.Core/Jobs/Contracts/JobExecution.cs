using CIS.Coordination;
using CIS.Transactions;

namespace CIS.Jobs;

/// <summary>The result of one finite continuous iteration, never an instruction to replay uncertain work.</summary>
public enum ContinuousIterationOutcome
{
    /// <summary>Another finite unit is available, after yielding and new admission.</summary>
    MoreWork,
    /// <summary>No current work; retain ownership while awaiting the configured idle delay.</summary>
    Idle,
    /// <summary>The duty faults locally after this invocation settles.</summary>
    Failed,
    /// <summary>The application cannot establish the business outcome; use its existing recovery contract.</summary>
    Indeterminate
}

/// <summary>A finite iteration fact with optional original RM-02 completion evidence.</summary>
public sealed class ContinuousIterationResult
{
    /// <summary>Copies a result without replacing actual persistence facts with cancellation or ownership state.</summary>
    public ContinuousIterationResult(ContinuousIterationOutcome outcome, string? reasonCode = null,
        TransactionCompletion? completion = null)
    {
        if (!Enum.IsDefined(outcome)
            || completion?.Outcome == TransactionOutcome.Unknown && outcome != ContinuousIterationOutcome.Indeterminate)
        {
            throw new ArgumentException("An unknown business commit remains an indeterminate iteration.");
        }

        Outcome = outcome;
        ReasonCode = reasonCode is null ? null : JobText.Text(reasonCode, 128, nameof(reasonCode));
        Completion = completion;
    }

    /// <summary>Gets MoreWork, Idle, Failed or Indeterminate.</summary>
    public ContinuousIterationOutcome Outcome
    {
        get;
    }
    /// <summary>Gets an optional bounded safe diagnostic code.</summary>
    public string? ReasonCode
    {
        get;
    }
    /// <summary>Gets actual original business completion, if the handler used RM-02.</summary>
    public TransactionCompletion? Completion
    {
        get;
    }
}

/// <summary>Precise scheduled execution outcomes, separate from ownership and transport acknowledgement.</summary>
public enum ScheduledJobOutcome
{
    /// <summary>The required business completion was established.</summary>
    Completed,
    /// <summary>The original established result was recovered without repeating the protected effect.</summary>
    PreviouslyCompleted,
    /// <summary>Another current owner holds this stable job/resource.</summary>
    Busy,
    /// <summary>Local enablement or capacity prevented callback admission; preserve the durable occurrence.</summary>
    Deferred,
    /// <summary>Ownership was lost before the required completion was established.</summary>
    OwnershipLost,
    /// <summary>Cancellation was observed without claiming that business rollback occurred.</summary>
    Cancelled,
    /// <summary>A known failed invocation settled.</summary>
    Failed,
    /// <summary>The required business outcome cannot be established.</summary>
    Indeterminate,
    /// <summary>The registered target, mode, payload or admission is unsupported.</summary>
    Unsupported,
    /// <summary>The established caller lacks required permission.</summary>
    Unauthorized
}

/// <summary>One execution result returned to the authoritative scheduling source.</summary>
public sealed class ScheduledJobResult
{
    /// <summary>Preserves original RM-02 evidence; staged/uncommitted work cannot be reported Completed.</summary>
    public ScheduledJobResult(ScheduledJobOutcome outcome, string? reasonCode = null,
        TransactionCompletion? completion = null)
    {
        if (!Enum.IsDefined(outcome)
            || completion?.Outcome == TransactionOutcome.Unknown && outcome != ScheduledJobOutcome.Indeterminate
            || outcome is ScheduledJobOutcome.Completed or ScheduledJobOutcome.PreviouslyCompleted
                && completion is not null && completion.Outcome != TransactionOutcome.Committed)
        {
            throw new ArgumentException("Scheduled completion must preserve the actual business commit outcome.");
        }

        Outcome = outcome;
        ReasonCode = reasonCode is null ? null : JobText.Text(reasonCode, 128, nameof(reasonCode));
        Completion = completion;
    }

    /// <summary>Gets the established execution fact, without acknowledging a provider-owned occurrence.</summary>
    public ScheduledJobOutcome Outcome
    {
        get;
    }
    /// <summary>Gets an optional bounded safe diagnostic code.</summary>
    public string? ReasonCode
    {
        get;
    }
    /// <summary>Gets original business persistence evidence, if applicable.</summary>
    public TransactionCompletion? Completion
    {
        get;
    }
}

/// <summary>Explicit per-invocation data; the runtime owns the actual callback and services lifetime.</summary>
public sealed class JobExecutionContext
{
    /// <summary>Copies references to the exact definition, lease, independently owned services and immutable settings.</summary>
    public JobExecutionContext(JobDefinition definition, string resourceId, ICoordinationLease lease,
        IServiceProvider services, JobExecutionSettings settings, AdmittedJobRun? admittedRun = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ResourceId = JobText.Text(resourceId, 512, nameof(resourceId));
        CoordinationKey key = lease.Grant.Acquisition.Key;
        if (!definition.SupportedModes.Contains(settings.Mode)
            || key.ExecutionScope != definition.ExecutionScope || key.DutyId != definition.JobId || key.ResourceId != ResourceId
            || (settings.Mode == JobExecutionMode.Scheduled) != (admittedRun is not null)
            || admittedRun is not null && (admittedRun.ExecutionScope != definition.ExecutionScope
                || admittedRun.JobId != definition.JobId || admittedRun.ResourceId != ResourceId
                || !ReferenceEquals(admittedRun.Settings, settings)))
        {
            throw new ArgumentException("Use the exact registered target, grant and admitted execution snapshot.");
        }

        Definition = definition;
        Lease = lease;
        Services = services;
        Settings = settings;
        AdmittedRun = admittedRun;
    }

    /// <summary>Gets the exact registered definition.</summary>
    public JobDefinition Definition
    {
        get;
    }
    /// <summary>Gets the stable resource/partition, independent of occurrence and generation.</summary>
    public string ResourceId
    {
        get;
    }
    /// <summary>Gets managed ownership; protected effects still require the original-transaction guard.</summary>
    public ICoordinationLease Lease
    {
        get;
    }
    /// <summary>Gets independently scoped services, retained until the actual callback and its cleanup settle.</summary>
    public IServiceProvider Services
    {
        get;
    }
    /// <summary>Gets the immutable settings admitted for this callback.</summary>
    public JobExecutionSettings Settings
    {
        get;
    }
    /// <summary>Gets the scheduler-owned occurrence for Scheduled mode only.</summary>
    public AdmittedJobRun? AdmittedRun
    {
        get;
    }
}

/// <summary>One finite continuous iteration; the runtime owns repetition, waiting, capacity and scope disposal.</summary>
public interface IContinuousJobHandler
{
    /// <summary>Completes all owned iteration work before returning; cancellation requests cooperative stopping.</summary>
    Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>One exactly registered typed scheduled callback.</summary>
public interface IScheduledJobHandler<T>
{
    /// <summary>Uses the admitted original operation identity and returns established business completion.</summary>
    Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, T input, CancellationToken cancellationToken = default);
}

/// <summary>Trusted application hosting creates one independent workload scope, never an ambient HTTP caller projection.</summary>
public interface IJobExecutionScopeFactory
{
    /// <summary>Creates and owns services with existing established native authority for this registered target.</summary>
    ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The application-owned adapter can wrap the published SecurityHostContextScope.</summary>
public interface IJobExecutionScope : IAsyncDisposable
{
    /// <summary>Gets independently scoped existing authorization and handler services; no copied CallerContext grants authority.</summary>
    IServiceProvider Services
    {
        get;
    }
}

/// <summary>Bounded registered target metadata for a trusted scope factory, not an authentication credential.</summary>
public sealed record JobScopeRequest
{
    /// <summary>Copies the exact target and mode without establishing any permission.</summary>
    public JobScopeRequest(string executionScope, string jobId, string resourceId, JobExecutionMode mode)
    {
        ExecutionScope = JobText.Text(executionScope, 256, nameof(executionScope));
        JobId = JobText.Text(jobId, 128, nameof(jobId));
        ResourceId = JobText.Text(resourceId, 512, nameof(resourceId));
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
    }

    /// <summary>Gets the registered execution scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the registered job identity.</summary>
    public string JobId
    {
        get;
    }
    /// <summary>Gets the explicit resource/partition identity.</summary>
    public string ResourceId
    {
        get;
    }
    /// <summary>Gets the supported execution mode.</summary>
    public JobExecutionMode Mode
    {
        get;
    }
}
