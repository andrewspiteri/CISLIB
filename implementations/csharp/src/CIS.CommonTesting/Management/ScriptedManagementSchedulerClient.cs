using CIS.Management;
using CIS.Management.Wire;

namespace CIS.CommonTesting;

/// <summary>The finite optional Scheduler, catalog and configuration-administration API selected by a script.</summary>
public enum ScriptedManagementSchedulerCallKind
{
    /// <summary>The ListTriggers API.</summary>
    ListTriggers,
    /// <summary>The GetTrigger API.</summary>
    GetTrigger,
    /// <summary>The CreateTrigger API.</summary>
    CreateTrigger,
    /// <summary>The UpdateTrigger API.</summary>
    UpdateTrigger,
    /// <summary>The ChangeTriggerState API.</summary>
    ChangeTriggerState,
    /// <summary>The GetTriggerOperation API.</summary>
    GetTriggerOperation,
    /// <summary>The ListTasks API.</summary>
    ListTasks,
    /// <summary>The GetTask API.</summary>
    GetTask,
    /// <summary>The PreviewTask API.</summary>
    PreviewTask,
    /// <summary>The PreviewCalendar API.</summary>
    PreviewCalendar,
    /// <summary>The CreateTask API.</summary>
    CreateTask,
    /// <summary>The UpdateTask API.</summary>
    UpdateTask,
    /// <summary>The ChangeTaskState API.</summary>
    ChangeTaskState,
    /// <summary>The GetTaskOperation API.</summary>
    GetTaskOperation,
    /// <summary>The RunTaskNow API.</summary>
    RunTaskNow,
    /// <summary>The CancelRun API.</summary>
    CancelRun,
    /// <summary>The GetRunOperation API.</summary>
    GetRunOperation,
    /// <summary>The GetRuns API.</summary>
    GetRuns,
    /// <summary>The GetRun API.</summary>
    GetRun,
    /// <summary>The GetAttempts API.</summary>
    GetAttempts,
    /// <summary>The GetEvent API.</summary>
    GetEvent,
    /// <summary>The GetEventRuns API.</summary>
    GetEventRuns,
    /// <summary>The SubmitEvent API.</summary>
    SubmitEvent,
    /// <summary>The GetEventReceipt API.</summary>
    GetEventReceipt,
    /// <summary>The GetReadyRuns API.</summary>
    GetReadyRuns,
    /// <summary>The GetRunCapsule API.</summary>
    GetRunCapsule,
    /// <summary>The AdmitAttempt API.</summary>
    AdmitAttempt,
    /// <summary>The ReportAttempt API.</summary>
    ReportAttempt,
    /// <summary>The GetCapabilityDefinitions API.</summary>
    GetCapabilityDefinitions,
    /// <summary>The GetCapabilityDefinition API.</summary>
    GetCapabilityDefinition,
    /// <summary>The GetCapabilityParticipation API.</summary>
    GetCapabilityParticipation,
    /// <summary>The GetSourceActions API.</summary>
    GetSourceActions,
    /// <summary>The GetBindingActions API.</summary>
    GetBindingActions,
    /// <summary>The GetCohorts API.</summary>
    GetCohorts,
}

/// <summary>Bounded safe identities only; no business input, values, ETag, credential or caller context is retained.</summary>
public sealed record ScriptedManagementSchedulerCall(ScriptedManagementSchedulerCallKind Kind,
    string? ApplicationId = null, string? RegistrationId = null, string? ResourceId = null,
    string? RelatedId = null, string? OperationKey = null);

/// <summary>A finite ordered substitute for optional ports; it proves no native wire, authority, scheduling or durability.</summary>
public sealed class ScriptedManagementSchedulerClient : IManagementSchedulerClient, IManagementSchedulerWorkerClient,
    IManagementApplicationCatalogClient, IManagementConfigurationAdministrationClient
{
    private readonly object gate = new();
    private readonly Queue<Step> steps = new();
    private readonly BoundedRecords<ScriptedManagementSchedulerCall> calls;
    private readonly int capacity;

    /// <summary>Bounds pending steps and safe records, initially sixteen and at most 65,536.</summary>
    public ScriptedManagementSchedulerClient(int capacity = 16)
    {
        calls = new(capacity);
        this.capacity = capacity;
    }

    /// <summary>Gets a detached safe call snapshot.</summary>
    public IReadOnlyList<ScriptedManagementSchedulerCall> Calls => calls.Snapshot();
    /// <summary>Gets the saturating count since Clear.</summary>
    public long TotalCalls => calls.Total;
    /// <summary>Gets the saturating evicted record count.</summary>
    public long DroppedCalls => calls.Dropped;

    /// <summary>Clears pending scripts and safe records without abandoning admitted pending work.</summary>
    public void Clear()
    {
        lock (gate)
        {
            steps.Clear();
            calls.Clear();
        }
    }

    /// <summary>Queues one exact typed call-result wrapper; a mismatched API or wrapper type fails the call.</summary>
    public void EnqueueResult<T>(ScriptedManagementSchedulerCallKind kind, T result) where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        Enqueue(new ResultStep<T>(kind, result));
    }

    /// <summary>Queues the original exception without converting it to a successful result.</summary>
    public void EnqueueException(ScriptedManagementSchedulerCallKind kind, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Enqueue(new FaultStep(kind, exception));
    }

    /// <summary>Holds one actual logical call until released or until its selected cancellation behavior settles.</summary>
    public PendingManagementCall EnqueuePending<T>(ScriptedManagementSchedulerCallKind kind, T result,
        bool observeCancellation = true) where T : class
    {
        ArgumentNullException.ThrowIfNull(result);
        var pending = new PendingManagementCall(observeCancellation);
        Enqueue(new PendingStep<T>(kind, result, pending));
        return pending;
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTrigger>>> ListTriggersAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTrigger>>>(new(ScriptedManagementSchedulerCallKind.ListTriggers, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTrigger>> GetTriggerAsync(string applicationId, string triggerId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerTrigger>>(new(ScriptedManagementSchedulerCallKind.GetTrigger, ApplicationId: applicationId, ResourceId: triggerId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CreateTriggerAsync(string applicationId,
        string operationKey, ManagementSchedulerTriggerCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.CreateTrigger, ApplicationId: applicationId, ResourceId: command.TriggerId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> UpdateTriggerAsync(string applicationId,
        string triggerId, string operationKey, string ifMatch, ManagementSchedulerTriggerCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.UpdateTrigger, ApplicationId: applicationId, ResourceId: triggerId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> ChangeTriggerStateAsync(string applicationId,
        string triggerId, string operationKey, string ifMatch, ManagementSchedulerStateCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.ChangeTriggerState, ApplicationId: applicationId, ResourceId: triggerId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetTriggerOperationAsync(string applicationId,
        string triggerId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.GetTriggerOperation, ApplicationId: applicationId, ResourceId: triggerId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTask>>> ListTasksAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTask>>>(new(ScriptedManagementSchedulerCallKind.ListTasks, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTask>> GetTaskAsync(string applicationId, string taskId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerTask>>(new(ScriptedManagementSchedulerCallKind.GetTask, ApplicationId: applicationId, ResourceId: taskId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>> PreviewTaskAsync(string applicationId,
        ManagementSchedulerTaskPreviewCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>>(new(ScriptedManagementSchedulerCallKind.PreviewTask, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>> PreviewCalendarAsync(string applicationId,
        ManagementSchedulerCalendarPreviewCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>>(new(ScriptedManagementSchedulerCallKind.PreviewCalendar, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CreateTaskAsync(string applicationId,
        string operationKey, ManagementSchedulerTaskCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.CreateTask, ApplicationId: applicationId, ResourceId: command.TaskId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> UpdateTaskAsync(string applicationId,
        string taskId, string operationKey, string ifMatch, ManagementSchedulerTaskCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.UpdateTask, ApplicationId: applicationId, ResourceId: taskId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> ChangeTaskStateAsync(string applicationId,
        string taskId, string operationKey, string ifMatch, ManagementSchedulerStateCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.ChangeTaskState, ApplicationId: applicationId, ResourceId: taskId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetTaskOperationAsync(string applicationId,
        string taskId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.GetTaskOperation, ApplicationId: applicationId, ResourceId: taskId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> RunTaskNowAsync(string applicationId,
        string taskId, string operationKey, string ifMatch, ManagementSchedulerManualRunCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.RunTaskNow, ApplicationId: applicationId, ResourceId: taskId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CancelRunAsync(string applicationId,
        string runId, string operationKey, ManagementSchedulerStateCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.CancelRun, ApplicationId: applicationId, ResourceId: runId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetRunOperationAsync(string applicationId,
        string runId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>>(new(ScriptedManagementSchedulerCallKind.GetRunOperation, ApplicationId: applicationId, ResourceId: runId, OperationKey: operationKey), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetRunsAsync(string applicationId,
        ManagementSchedulerHistoryQuery query, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>>(new(ScriptedManagementSchedulerCallKind.GetRuns, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerRunDetail>> GetRunAsync(string applicationId, string runId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerRunDetail>>(new(ScriptedManagementSchedulerCallKind.GetRun, ApplicationId: applicationId, ResourceId: runId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerAttempt>>> GetAttemptsAsync(string applicationId,
        string runId, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerAttempt>>>(new(ScriptedManagementSchedulerCallKind.GetAttempts, ApplicationId: applicationId, ResourceId: runId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerEvent>> GetEventAsync(string applicationId, string triggerId, string eventId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerEvent>>(new(ScriptedManagementSchedulerCallKind.GetEvent, ApplicationId: applicationId, ResourceId: triggerId, RelatedId: eventId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetEventRunsAsync(string applicationId,
        string triggerId, string eventId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>>(new(ScriptedManagementSchedulerCallKind.GetEventRuns, ApplicationId: applicationId, ResourceId: triggerId, RelatedId: eventId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> SubmitEventAsync(string applicationId,
        string triggerId, ManagementSchedulerEventCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>>(new(ScriptedManagementSchedulerCallKind.SubmitEvent, ApplicationId: applicationId, ResourceId: triggerId, RelatedId: command.EventId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> GetEventReceiptAsync(string applicationId,
        string triggerId, string eventId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>>(new(ScriptedManagementSchedulerCallKind.GetEventReceipt, ApplicationId: applicationId, ResourceId: triggerId, RelatedId: eventId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetReadyRunsAsync(string registrationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>>(new(ScriptedManagementSchedulerCallKind.GetReadyRuns, RegistrationId: registrationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerRunCapsule>> GetRunCapsuleAsync(string registrationId,
        string runId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerRunCapsule>>(new(ScriptedManagementSchedulerCallKind.GetRunCapsule, RegistrationId: registrationId, ResourceId: runId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerAttemptAdmission>> AdmitAttemptAsync(string applicationId,
        string runId, ManagementSchedulerAttemptCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementSchedulerAttemptAdmission>>(new(ScriptedManagementSchedulerCallKind.AdmitAttempt, ApplicationId: applicationId, ResourceId: runId, RelatedId: command.AttemptId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementReportReceipt>> ReportAttemptAsync(string applicationId,
        string runId, string attemptId, ManagementSchedulerAttemptReport report,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementSchedulerCallResult<ManagementReportReceipt>>(new(ScriptedManagementSchedulerCallKind.ReportAttempt, ApplicationId: applicationId, ResourceId: runId, RelatedId: attemptId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementCapabilityDefinition>>> GetCapabilityDefinitionsAsync(
        string applicationId, ManagementCapabilityQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementCallResult<ManagementPage<ManagementCapabilityDefinition>>>(new(ScriptedManagementSchedulerCallKind.GetCapabilityDefinitions, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementCapabilityDefinition>> GetCapabilityDefinitionAsync(string applicationId,
        ManagementCapabilityKey key, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementCallResult<ManagementCapabilityDefinition>>(new(ScriptedManagementSchedulerCallKind.GetCapabilityDefinition, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementCallResult<ManagementPage<ManagementCapabilityParticipation>>> GetCapabilityParticipationAsync(
        string applicationId, ManagementCapabilityQuery? query = null, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementCallResult<ManagementPage<ManagementCapabilityParticipation>>>(new(ScriptedManagementSchedulerCallKind.GetCapabilityParticipation, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> GetSourceActionsAsync(string sourceId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationCallResult<ManagementConfigurationActions>>(new(ScriptedManagementSchedulerCallKind.GetSourceActions, ResourceId: sourceId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementConfigurationActions>> GetBindingActionsAsync(string applicationId,
        string configurationId, string schemaVersion, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationCallResult<ManagementConfigurationActions>>(new(ScriptedManagementSchedulerCallKind.GetBindingActions, ApplicationId: applicationId), cancellationToken);

    /// <inheritdoc />
    public Task<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationCohortChoice>>> GetCohortsAsync(
        string applicationId, string configurationId, string schemaVersion, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => Dispatch<ManagementConfigurationCallResult<ManagementPage<ManagementConfigurationCohortChoice>>>(new(ScriptedManagementSchedulerCallKind.GetCohorts, ApplicationId: applicationId), cancellationToken);

    private void Enqueue(Step step)
    {
        if (!Enum.IsDefined(step.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }
        lock (gate)
        {
            if (steps.Count >= capacity)
            {
                throw new InvalidOperationException("The bounded Scheduler script is full.");
            }
            steps.Enqueue(step);
        }
    }

    private Task<T> Dispatch<T>(ScriptedManagementSchedulerCall call, CancellationToken token) where T : class
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(token);
        }
        if (new[] { call.ApplicationId, call.RegistrationId, call.ResourceId, call.RelatedId }
                .Any(value => value is not null && !ManagementWire.IsUuid(value))
            || call.OperationKey is not null && !ManagementWire.IsOperationKey(call.OperationKey))
        {
            return Task.FromException<T>(new ArgumentException("Supply bounded exact call identities."));
        }
        Step step;
        lock (gate)
        {
            if (steps.Count == 0)
            {
                return Task.FromException<T>(new InvalidOperationException("No Scheduler response is scripted."));
            }
            step = steps.Dequeue();
            calls.Add(call);
        }
        return step.Kind == call.Kind ? step.ExecuteAsync<T>(token)
            : Task.FromException<T>(new InvalidOperationException("The Scheduler script API kind does not match."));
    }

    private abstract class Step(ScriptedManagementSchedulerCallKind kind)
    {
        internal ScriptedManagementSchedulerCallKind Kind { get; } = kind;
        internal abstract Task<T> ExecuteAsync<T>(CancellationToken token) where T : class;
    }

    private sealed class ResultStep<TValue>(ScriptedManagementSchedulerCallKind kind, TValue value) : Step(kind) where TValue : class
    {
        internal override Task<T> ExecuteAsync<T>(CancellationToken token)
            => typeof(T) == typeof(TValue) ? Task.FromResult((T)(object)value)
                : Task.FromException<T>(new InvalidOperationException("The Scheduler response wrapper type does not match."));
    }

    private sealed class FaultStep(ScriptedManagementSchedulerCallKind kind, Exception error) : Step(kind)
    {
        internal override Task<T> ExecuteAsync<T>(CancellationToken token) => Task.FromException<T>(error);
    }

    private sealed class PendingStep<TValue>(ScriptedManagementSchedulerCallKind kind, TValue value, PendingManagementCall pending)
        : Step(kind) where TValue : class
    {
        internal override async Task<T> ExecuteAsync<T>(CancellationToken token)
        {
            if (typeof(T) != typeof(TValue))
            {
                throw new InvalidOperationException("The Scheduler response wrapper type does not match.");
            }
            await pending.WaitAsync(token).ConfigureAwait(false);
            return (T)(object)value;
        }
    }
}

