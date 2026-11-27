using System.Globalization;
using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>Optional typed Scheduler, worker and catalog ports borrowing one native REST owner; construction starts no work.</summary>
public sealed partial class ManagementSchedulerClient : IManagementSchedulerClient, IManagementSchedulerWorkerClient,
    IManagementApplicationCatalogClient, IManagementConfigurationAdministrationClient
{
    internal static readonly string[] ObservationAliases = ["scheduler", "catalog", "configuration"];
    private readonly IRestClient rest;
    private readonly ManagementSchedulerClientSnapshot settings;
    private readonly RestRetryPolicy[] registeredPolicies;
    private readonly TimeProvider clock;

    /// <summary>Captures finite options and the actual existing read policies without creating another native owner.</summary>
    public ManagementSchedulerClient(IRestClient rest, ManagementSchedulerClientOptions options,
        IEnumerable<RestRetryPolicy>? registeredReadPolicies = null, TimeProvider? clock = null)
        : this(rest, ManagementSchedulerClientSnapshot.Capture(options), registeredReadPolicies, clock)
    {
    }

    internal ManagementSchedulerClient(IRestClient rest, ManagementSchedulerClientSnapshot settings,
        IEnumerable<RestRetryPolicy>? registeredReadPolicies, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(rest);
        this.rest = rest;
        this.settings = settings;
        this.clock = clock ?? TimeProvider.System;
        registeredPolicies = (registeredReadPolicies ?? []).Take(33).ToArray();
        if (registeredPolicies.Length > 32 || registeredPolicies.Any(policy => policy is null)
            || registeredPolicies.Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Count() != registeredPolicies.Length)
        {
            throw new ArgumentException("Supply the actual finite native retry registrations.", nameof(registeredReadPolicies));
        }
        SelectReadPolicy(settings.ReadRetryPolicy);
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTrigger>>> ListTriggersAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementSchedulerTrigger>>("GET", App(applicationId) + "/triggers",
            page: page ?? new(), check: value => Require(((ManagementPage<ManagementSchedulerTrigger>)value!).Items.All(item => item.ApplicationId == applicationId)),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTrigger>> GetTriggerAsync(string applicationId, string triggerId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerTrigger>("GET", Trigger(applicationId, triggerId),
            check: value => Require(value is ManagementSchedulerTrigger item && item.ApplicationId == applicationId && item.TriggerId == triggerId),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CreateTriggerAsync(string applicationId,
        string operationKey, ManagementSchedulerTriggerCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => OperationAsync("POST", App(applicationId) + "/triggers", applicationId, Uuid(command.TriggerId), operationKey,
            ManagementSchedulerOperations.TriggerWrite, command, create: true, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> UpdateTriggerAsync(string applicationId, string triggerId,
        string operationKey, string ifMatch, ManagementSchedulerTriggerCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        RequireArgument(command.TriggerId == triggerId, nameof(command));
        return OperationAsync("PUT", Trigger(applicationId, triggerId), applicationId, triggerId, operationKey,
            ManagementSchedulerOperations.TriggerWrite, command, ifMatch, options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> ChangeTriggerStateAsync(string applicationId, string triggerId,
        string operationKey, string ifMatch, ManagementSchedulerStateCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        State(command, ManagementSchedulerStateTarget.Trigger);
        return OperationAsync("POST", Trigger(applicationId, triggerId) + "/state", applicationId, triggerId, operationKey,
            ManagementSchedulerOperations.TriggerWrite, command, ifMatch, options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetTriggerOperationAsync(string applicationId, string triggerId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => OperationAsync("GET", Trigger(applicationId, triggerId) + "/operations/" + Key(operationKey), applicationId, triggerId, operationKey,
            ManagementSchedulerOperations.TriggerWrite, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTask>>> ListTasksAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementSchedulerTask>>("GET", App(applicationId) + "/tasks", page: page ?? new(),
            check: value => Require(((ManagementPage<ManagementSchedulerTask>)value!).Items.All(item => item.ApplicationId == applicationId)),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTask>> GetTaskAsync(string applicationId, string taskId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerTask>("GET", TaskPath(applicationId, taskId),
            check: value => Require(value is ManagementSchedulerTask item && item.ApplicationId == applicationId && item.TaskId == taskId),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>> PreviewTaskAsync(string applicationId,
        ManagementSchedulerTaskPreviewCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerTaskPreview>("POST", App(applicationId) + "/tasks/preview", command,
            check: value => CheckPreview((ManagementSchedulerTaskPreview)value!, command), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>> PreviewCalendarAsync(string applicationId,
        ManagementSchedulerCalendarPreviewCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerTaskPreview>("POST", App(applicationId) + "/calendar/preview", command,
            check: value => Require(value is ManagementSchedulerTaskPreview preview
                && ManagementSchedulerWire.IsCalendarPreview(preview, command.Count)),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CreateTaskAsync(string applicationId,
        string operationKey, ManagementSchedulerTaskCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => OperationAsync("POST", App(applicationId) + "/tasks", applicationId, Uuid(command.TaskId), operationKey,
            ManagementSchedulerOperations.TaskWrite, command, create: true, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> UpdateTaskAsync(string applicationId, string taskId,
        string operationKey, string ifMatch, ManagementSchedulerTaskCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        RequireArgument(command.TaskId == taskId, nameof(command));
        return OperationAsync("PUT", TaskPath(applicationId, taskId), applicationId, taskId, operationKey,
            ManagementSchedulerOperations.TaskWrite, command, ifMatch, options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> ChangeTaskStateAsync(string applicationId, string taskId,
        string operationKey, string ifMatch, ManagementSchedulerStateCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        State(command, ManagementSchedulerStateTarget.Task);
        return OperationAsync("POST", TaskPath(applicationId, taskId) + "/state", applicationId, taskId, operationKey,
            ManagementSchedulerOperations.TaskWrite, command, ifMatch, options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetTaskOperationAsync(string applicationId, string taskId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => OperationAsync("GET", TaskPath(applicationId, taskId) + "/operations/" + Key(operationKey), applicationId, taskId, operationKey,
            ManagementSchedulerOperations.TaskWrite, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> RunTaskNowAsync(string applicationId, string taskId,
        string operationKey, string ifMatch, ManagementSchedulerManualRunCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => OperationAsync("POST", TaskPath(applicationId, taskId) + "/runs", applicationId, taskId, operationKey,
            ManagementSchedulerOperations.TaskWrite, command, ifMatch, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CancelRunAsync(string applicationId, string runId,
        string operationKey, ManagementSchedulerStateCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
    {
        State(command, ManagementSchedulerStateTarget.Run);
        return OperationAsync("POST", Run(applicationId, runId) + "/cancellation", applicationId, runId, operationKey,
            ManagementSchedulerOperations.RunWrite, command, options: options, token: cancellationToken);
    }

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetRunOperationAsync(string applicationId, string runId,
        string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => OperationAsync("GET", Run(applicationId, runId) + "/operations/" + Key(operationKey), applicationId, runId, operationKey,
            ManagementSchedulerOperations.RunWrite, options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetRunsAsync(string applicationId,
        ManagementSchedulerHistoryQuery query, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementSchedulerRun>>("GET", App(applicationId) + "/runs", page: page ?? new(), query: History(query),
            check: value => Require(((ManagementPage<ManagementSchedulerRun>)value!).Items.All(item => item.ApplicationId == applicationId && MatchesHistory(item, query))),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerRunDetail>> GetRunAsync(string applicationId, string runId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerRunDetail>("GET", Run(applicationId, runId),
            check: value => Require(value is ManagementSchedulerRunDetail detail && detail.Run.ApplicationId == applicationId && detail.Run.RunId == runId),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerAttempt>>> GetAttemptsAsync(string applicationId, string runId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementSchedulerAttempt>>("GET", Run(applicationId, runId) + "/attempts", page: page ?? new(),
            check: value => Require(((ManagementPage<ManagementSchedulerAttempt>)value!).Items.All(item => item.RunId == runId)),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerEvent>> GetEventAsync(string applicationId, string triggerId, string eventId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerEvent>("GET", Event(applicationId, triggerId, eventId),
            check: value => Require(value is ManagementSchedulerEvent item && item.ApplicationId == applicationId && item.TriggerId == triggerId && item.EventId == eventId),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetEventRunsAsync(string applicationId, string triggerId, string eventId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementSchedulerRun>>("GET", Event(applicationId, triggerId, eventId) + "/runs", page: page ?? new(),
            check: value => Require(((ManagementPage<ManagementSchedulerRun>)value!).Items.All(item =>
                item.Trigger.ApplicationId == applicationId && item.Trigger.TriggerId == triggerId && item.EventId == eventId)),
            options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> SubmitEventAsync(string applicationId, string triggerId,
        ManagementSchedulerEventCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => EventReceiptAsync("POST", applicationId, triggerId, command.EventId, command, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> GetEventReceiptAsync(string applicationId, string triggerId, string eventId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => EventReceiptAsync("GET", applicationId, triggerId, eventId, null, options, cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetReadyRunsAsync(string registrationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementPage<ManagementSchedulerRun>>("GET", Registration(registrationId) + "/ready-runs",
            page: page ?? new(), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerRunCapsule>> GetRunCapsuleAsync(string registrationId, string runId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerRunCapsule>("GET", Registration(registrationId) + "/runs/" + Uuid(runId) + "/capsule",
            check: value => Require(value is ManagementSchedulerRunCapsule capsule && capsule.RunId == runId), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementSchedulerAttemptAdmission>> AdmitAttemptAsync(string applicationId, string runId,
        ManagementSchedulerAttemptCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementSchedulerAttemptAdmission>("POST", Run(applicationId, runId) + "/attempts", command,
            check: value => CheckAdmission((ManagementSchedulerAttemptAdmission)value!, runId, command), options: options, token: cancellationToken);

    /// <inheritdoc />
    public Task<ManagementSchedulerCallResult<ManagementReportReceipt>> ReportAttemptAsync(string applicationId, string runId, string attemptId,
        ManagementSchedulerAttemptReport report, ManagementCallOptions? options = null, CancellationToken cancellationToken = default)
        => CallAsync<ManagementReportReceipt>("PUT", Run(applicationId, runId) + "/attempts/" + Uuid(attemptId) + "/report", report,
            check: value => Require(value is ManagementReportReceipt receipt && receipt.Stream == "scheduler-attempt"
                && receipt.SubmittedSequence == report.Sequence && receipt.AcceptedSequence == report.Sequence),
            options: options, token: cancellationToken);
}

