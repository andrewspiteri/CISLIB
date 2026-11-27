namespace CIS.Management;

/// <summary>Optional typed Scheduler APIs. Selection starts no worker, timer, provider or managed execution.</summary>
public interface IManagementSchedulerClient : IManagementTriggerEventPort
{
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTrigger>>> ListTriggersAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerTrigger>> GetTriggerAsync(string applicationId, string triggerId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CreateTriggerAsync(string applicationId,
        string operationKey, ManagementSchedulerTriggerCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> UpdateTriggerAsync(string applicationId,
        string triggerId, string operationKey, string ifMatch, ManagementSchedulerTriggerCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> ChangeTriggerStateAsync(string applicationId,
        string triggerId, string operationKey, string ifMatch, ManagementSchedulerStateCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Recovers the original decision under current authorization, retaining its original key.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetTriggerOperationAsync(string applicationId,
        string triggerId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerTask>>> ListTasksAsync(string applicationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerTask>> GetTaskAsync(string applicationId, string taskId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Assesses the complete scoped draft without publication, assignment or execution effects.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>> PreviewTaskAsync(string applicationId,
        ManagementSchedulerTaskPreviewCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Assesses the complete scoped draft without publication, assignment or execution effects.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerTaskPreview>> PreviewCalendarAsync(string applicationId,
        ManagementSchedulerCalendarPreviewCommand command, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CreateTaskAsync(string applicationId,
        string operationKey, ManagementSchedulerTaskCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> UpdateTaskAsync(string applicationId,
        string taskId, string operationKey, string ifMatch, ManagementSchedulerTaskCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> ChangeTaskStateAsync(string applicationId,
        string taskId, string operationKey, string ifMatch, ManagementSchedulerStateCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Recovers the original decision under current authorization, retaining its original key.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetTaskOperationAsync(string applicationId,
        string taskId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> RunTaskNowAsync(string applicationId,
        string taskId, string operationKey, string ifMatch, ManagementSchedulerManualRunCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits this exact guarded intent once; transport uncertainty retains the original key and is never an automatic replay.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> CancelRunAsync(string applicationId,
        string runId, string operationKey, ManagementSchedulerStateCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Recovers the original decision under current authorization, retaining its original key.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerOperationResult>> GetRunOperationAsync(string applicationId,
        string runId, string operationKey, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetRunsAsync(string applicationId,
        ManagementSchedulerHistoryQuery query, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerRunDetail>> GetRunAsync(string applicationId, string runId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerAttempt>>> GetAttemptsAsync(string applicationId,
        string runId, ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerEvent>> GetEventAsync(string applicationId, string triggerId, string eventId,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads only authorized produced runs for this exact source occurrence, including cross-application task targets.</summary>
    Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetEventRunsAsync(string applicationId,
        string triggerId, string eventId, ManagementPageRequest? page = null,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Common passive port for application callbacks and custom sources, retaining original occurrence responsibility.</summary>
public interface IManagementTriggerEventPort
{
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> SubmitEventAsync(string applicationId,
        string triggerId, ManagementSchedulerEventCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerEventReceipt>> GetEventReceiptAsync(string applicationId,
        string triggerId, string eventId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Optional workload-only handoff. Native authority and the existing Jobs ownership remain required.</summary>
public interface IManagementSchedulerWorkerClient
{
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementPage<ManagementSchedulerRun>>> GetReadyRunsAsync(string registrationId,
        ManagementPageRequest? page = null, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact currently authorized scope within the supplied finite page and operation budget.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerRunCapsule>> GetRunCapsuleAsync(string registrationId,
        string runId, ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Serializes exact attempt admission before final local native entry checks; this grants no lease authority.</summary>
    Task<ManagementSchedulerCallResult<ManagementSchedulerAttemptAdmission>> AdmitAttemptAsync(string applicationId,
        string runId, ManagementSchedulerAttemptCommand command,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
    /// <summary>Submits one original sequenced observation without claiming rollback after uncertainty.</summary>
    Task<ManagementSchedulerCallResult<ManagementReportReceipt>> ReportAttemptAsync(string applicationId,
        string runId, string attemptId, ManagementSchedulerAttemptReport report,
        ManagementCallOptions? options = null, CancellationToken cancellationToken = default);
}
