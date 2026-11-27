using System.Text.Json.Serialization;
using CIS.Messaging;

namespace CIS.Management;

/// <summary>One exact configured source, whose owner can differ from a task's application.</summary>
public sealed record ManagementSchedulerTriggerReference(string ApplicationId, string TriggerId);

/// <summary>Exact published configuration selection; the server resolves the real binding and owner.</summary>
public sealed record ManagementSchedulerSettingsSelection(string ApplicationId, string ConfigurationId,
    string SchemaVersion, string ReleaseId, string AssignmentGeneration);

/// <summary>Detached effective business settings and their immutable source provenance, not a live root.</summary>
public sealed record ManagementSchedulerCapturedSettings(ManagementSchedulerSettingsSelection Selection,
    string PayloadJson, string Sha256);

/// <summary>Captured issuer-qualified workload binding. This data does not authenticate its bearer.</summary>
public sealed record ManagementSchedulerExecutionIdentity(string Application, string Environment, string Issuer,
    string Subject, string BindingId, string ProfileId);

/// <summary>Attribution established by the native owner, never a command-supplied execution principal.</summary>
public sealed record ManagementSchedulerActor(string Kind, string Issuer, string Subject,
    string? DisplayName = null, string? Application = null, string? Environment = null);

/// <summary>Finite source mapping: field, constant, or an explicitly installed versioned transformation.</summary>
public sealed record ManagementSchedulerInputMapping(string TargetPath, string Kind, string? SourcePath = null,
    string? ConstantJson = null, MessageContract? Transformation = null, string? ParametersJson = null);

/// <summary>Exactly one resource, resolved from a constant, event field or installed resolver.</summary>
public sealed record ManagementSchedulerResourceMapping(string Kind, string? Constant = null,
    string? EventPath = null, MessageContract? Resolver = null, string? ParametersJson = null);

/// <summary>Explicit business retry selection; off by default, unrelated to acquisition and report retries.</summary>
public sealed record ManagementSchedulerRetryPolicy(bool Enabled, int MaximumAttempts,
    string InitialDelayMs, string MaximumDelayMs)
{
    /// <summary>Gets the immutable first-release no-business-retry policy.</summary>
    public static ManagementSchedulerRetryPolicy None { get; } = new(false, 1, "5000", "60000");

    /// <summary>Creates an explicit bounded opt-in; admission still validates the complete selected policy.</summary>
    public static ManagementSchedulerRetryPolicy Automatic(int maximumAttempts = 3)
        => new(true, maximumAttempts, "5000", "60000");
}

/// <summary>Existing calendar semantics in portable units. Fixed DST policies are not configurable scripts.</summary>
public sealed record ManagementSchedulerCalendar(string Form, string MissedRunPolicy,
    string? DelayMs = null, string? LocalTime = null, string? TimeZoneId = null,
    IReadOnlyList<int>? Weekdays = null, int? MaximumCatchUp = null, string? CatchUpWindowMs = null,
    string? InitialDelayMs = null, bool RunImmediately = false, string FailurePolicy = "pause")
{
    /// <summary>Gets a detached finite weekday selection, without changing its declared order.</summary>
    public IReadOnlyList<int>? Weekdays { get; } = Weekdays is null ? null : SchedulerValue.Copy(Weekdays, 7, nameof(Weekdays));

    /// <summary>Gets whether this form has a delivered v1 evaluator; unknown response forms stay explicit.</summary>
    [JsonIgnore]
    public bool IsKnownForm => Form is "fixedDelay" or "daily" or "weekly";
}

/// <summary>One source's desired configuration. Calendar data comes from its owner-managed configuration.</summary>
public sealed record ManagementSchedulerTriggerDraft(string Name, ManagementCapabilityKey Provider,
    ManagementSchedulerSettingsSelection? Configuration = null, bool Enabled = false);

/// <summary>One immutable task definition. Invocation input and persistent settings remain distinct.</summary>
public sealed record ManagementSchedulerTaskDraft(string Name, ManagementSchedulerTriggerReference Trigger,
    ManagementCapabilityKey Job, ManagementSchedulerResourceMapping Resource,
    IReadOnlyList<ManagementSchedulerInputMapping> InputMappings,
    ManagementSchedulerSettingsSelection JobConfiguration,
    ManagementSchedulerSettingsSelection? TriggerConfiguration = null, string ExecutionProfileId = "app-default",
    ManagementSchedulerRetryPolicy? Retry = null, bool Enabled = false,
    ManagementSchedulerExecutionSettings? ExecutionSettings = null)
{
    /// <summary>Gets the detached complete input mapping, never a live caller collection.</summary>
    public IReadOnlyList<ManagementSchedulerInputMapping> InputMappings { get; }
        = SchedulerValue.Copy(InputMappings, ManagementSchedulerLimits.MaximumMappings, nameof(InputMappings));
}

/// <summary>Current configured source and derived immutable calendar, with original attribution.</summary>
public sealed record ManagementSchedulerTrigger(string ApplicationId, string TriggerId, string Revision, string ETag,
    ManagementSchedulerTriggerDraft Definition, string State, ManagementSchedulerActor ConfiguredBy,
    string ChangedAtUtc, ManagementSchedulerCalendar? Calendar = null, string? WaitingReason = null)
{
    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownState => new(State, State is "disabled" or "enabled" or "retired" or "pending");
}

/// <summary>Desired and active revisions remain separate until coherent guarded promotion.</summary>
public sealed record ManagementSchedulerTask(string ApplicationId, string TaskId, string DesiredRevision,
    string? ActiveRevision, string ETag, ManagementSchedulerTaskDraft Definition, string State,
    ManagementSchedulerActor ConfiguredBy, string ChangedAtUtc, string? WaitingReason = null)
{
    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownState => new(State, State is "disabled" or "pending" or "active" or "retired");
}

/// <summary>A closed create/update source intent. The caller retains TriggerId before its first submission.</summary>
public sealed record ManagementSchedulerTriggerCommand(int CommandVersion, string TriggerId,
    ManagementSchedulerTriggerDraft Definition, string Reason);

/// <summary>A closed task intent, retaining one TaskId across desired revisions and recovery.</summary>
public sealed record ManagementSchedulerTaskCommand(int CommandVersion, string TaskId,
    ManagementSchedulerTaskDraft Definition, string Reason, string? ExpectedTriggerETag = null,
    string? PreviewDigest = null);

/// <summary>A fixed enable, disable, retire, activate or resume intent, not arbitrary command dispatch.</summary>
public sealed record ManagementSchedulerStateCommand(int CommandVersion, string Intent, string Reason);

/// <summary>Whole-task preview can include unpublished candidate values without assigning or activating them.</summary>
public sealed record ManagementSchedulerTaskPreviewCommand(int CommandVersion,
    ManagementSchedulerTaskDraft Definition, string? CandidateJobPayloadJson = null,
    string? CandidateTriggerPayloadJson = null, string? TaskId = null);

/// <summary>Side-effect-free evaluation uses the same delivered calendar evaluator as production.</summary>
public sealed record ManagementSchedulerCalendarPreviewCommand(int CommandVersion,
    ManagementSchedulerCalendar Calendar, string FromUtc, int Count = 5);

/// <summary>One actual preview problem at a bounded path; no provider text or executable content.</summary>
public sealed record ManagementSchedulerValidationIssue(string Path, string Code, string Message);

/// <summary>One permitted affected task; no hidden identities or unauthorized global count is implied.</summary>
public sealed record ManagementSchedulerAffectedTask(string ApplicationId, string TaskId, string Name, string Revision);

/// <summary>Complete draft assessment with independent impact disclosure and future activation facts.</summary>
public sealed record ManagementSchedulerTaskPreview(bool Valid,
    IReadOnlyList<ManagementSchedulerValidationIssue> Issues, IReadOnlyList<ManagementSchedulerAffectedTask> AffectedTasks,
    bool ImpactFullyDisclosed, IReadOnlyList<string> NextOccurrencesUtc, string ActivationState,
    string? DraftDigest = null, string? TaskETag = null, string? TriggerETag = null,
    IReadOnlyList<ManagementSchedulerSettingsSelection>? Preconditions = null,
    ManagementSchedulerTaskDraft? Definition = null)
{
    /// <summary>Gets the detached issues data.</summary>
    public IReadOnlyList<ManagementSchedulerValidationIssue> Issues { get; }
        = SchedulerValue.Copy(Issues, 128, nameof(Issues));
    /// <summary>Gets the detached affected tasks data.</summary>
    public IReadOnlyList<ManagementSchedulerAffectedTask> AffectedTasks { get; }
        = SchedulerValue.Copy(AffectedTasks, ManagementSchedulerLimits.MaximumTasks, nameof(AffectedTasks));
    /// <summary>Gets the detached next occurrences utc data.</summary>
    public IReadOnlyList<string> NextOccurrencesUtc { get; }
        = SchedulerValue.Copy(NextOccurrencesUtc, 100, nameof(NextOccurrencesUtc));
    /// <summary>Gets the detached preconditions data.</summary>
    public IReadOnlyList<ManagementSchedulerSettingsSelection>? Preconditions { get; }
        = Preconditions is null ? null : SchedulerValue.Copy(Preconditions, 16, nameof(Preconditions));
}

/// <summary>A retained source occurrence. Producer identity is supplied by native ingress, not this command.</summary>
public sealed record ManagementSchedulerEventCommand(int CommandVersion, string EventId, string OccurredAtUtc,
    MessageContract Contract, string PayloadJson, string? ScheduledAtUtc = null,
    string? CorrelationId = null, string? CausationId = null);

/// <summary>Original acceptance recovery, including zero-run receipts and redacted run disclosure.</summary>
public sealed record ManagementSchedulerEventReceipt(string ApplicationId, string TriggerId, string EventId,
    string Disposition, string AcceptedAtUtc, IReadOnlyList<string>? RunIds = null, int? RunCount = null)
{
    /// <summary>Gets the detached run ids data.</summary>
    public IReadOnlyList<string>? RunIds { get; }
        = RunIds is null ? null : SchedulerValue.Copy(RunIds, ManagementSchedulerLimits.MaximumFanout, nameof(RunIds));

    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownDisposition => new(Disposition, Disposition is "accepted" or "replayed");
}

/// <summary>An explicit manual occurrence for one task, never a firing of all tasks sharing its trigger.</summary>
public sealed record ManagementSchedulerManualRunCommand(int CommandVersion, string EventId, string Reason);

/// <summary>One immutable execution capsule; its Command uses the existing Messaging envelope unchanged.</summary>
public sealed record ManagementSchedulerRunCapsule(string RunId, string ApplicationId, string TaskId, string TaskRevision,
    ManagementSchedulerTriggerReference Trigger, string EventId, ManagementCapabilityKey Job, string ResourceId,
    string InputEnvelopeJson, ManagementSchedulerExecutionSettings ExecutionSettings,
    MessageContract ExecutionContract, MessageOperationReference OriginalOperation,
    ManagementSchedulerCapturedSettings JobSettings, ManagementSchedulerExecutionIdentity ExecutionIdentity,
    ManagementSchedulerActor ConfiguredBy, ManagementSchedulerActor Producer,
    ManagementSchedulerCapturedSettings? TriggerSettings = null, ManagementSchedulerActor? RequestedBy = null,
    string? ScheduledAtUtc = null, string? EventOccurredAtUtc = null, string? EventReceivedAtUtc = null,
    string? CorrelationId = null, string? CausationId = null);

/// <summary>Bounded logical run facts; mutable latest reports never replace captured intent.</summary>
public sealed record ManagementSchedulerRun(string ApplicationId, string RunId, string TaskId, string TaskRevision,
    ManagementSchedulerTriggerReference Trigger, string EventId, ManagementCapabilityKey Job, string ResourceId, string State, string CreatedAtUtc,
    string AttemptCount, string? BusinessOutcome = null, string? CompletedAtUtc = null,
    string? WaitingReason = null, bool CancellationRequested = false, string? ScheduledAtUtc = null,
    string? NextAttemptAtUtc = null, string? CorrelationId = null, string? CausationId = null)
{
    /// <summary>Gets the explicit known interpretation; a future value never becomes success.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownState => new(State,
        State is "pending" or "admitted" or "running" or "retryPending" or "succeeded" or "failed" or "cancelled" or "outcomeUnknown");
}

/// <summary>One durable attempt and actual worker attribution; an observation is not current lease authority.</summary>
public sealed record ManagementSchedulerAttempt(string RunId, string AttemptId, string Number, string Role,
    string State, string AdmittedAtUtc, ManagementWorkerBuildReference Build,
    ManagementSchedulerExecutionIdentity Executor, string RegistrationId, string EffectivePolicy,
    string Sequence, string? EnteredAtUtc = null, string? SettledAtUtc = null, string? BusinessCompletion = null,
    bool? CallbackSettled = null, bool? CleanupSucceeded = null, string? SummaryJson = null,
    string? AcquisitionId = null, string? DurationMs = null);

/// <summary>One current progress sample, not an unbounded stream of retained observations.</summary>
public sealed record ManagementSchedulerProgress(string? Completed, string? Total, string? Message = null);

/// <summary>Strictly sequenced observed evidence. Original business commit and callback settlement stay separate.</summary>
public sealed record ManagementSchedulerAttemptReport(int CommandVersion, string Sequence, string ObservedAtUtc,
    string Phase, string? BusinessOutcome = null, string? BusinessCompletion = null,
    bool? CallbackSettled = null, bool? CleanupSucceeded = null, ManagementSchedulerProgress? Progress = null,
    string? SummaryJson = null, string? DurationMs = null);

/// <summary>Request at the durable start serialization point, after actual local native scope and ownership.</summary>
public sealed record ManagementSchedulerAttemptCommand(int CommandVersion, string AttemptId,
    string RegistrationId, string Role, MessageContract ExecutionContract, ManagementWorkerBuildReference Build,
    string OwnershipGeneration, string? AcquisitionId = null, string? EffectivePolicy = null);

/// <summary>Explicit admission result; only an admitted exact attempt may proceed to final local entry checks.</summary>
public sealed record ManagementSchedulerAttemptAdmission(string Disposition, string? ReasonCode = null,
    ManagementSchedulerAttempt? Attempt = null);

/// <summary>Run details with independently authorized captured input/settings disclosure.</summary>
public sealed record ManagementSchedulerRunDetail(ManagementSchedulerRun Run,
    IReadOnlyList<ManagementSchedulerAttempt> Attempts, string CapturedContentAccess,
    ManagementSchedulerRunCapsule? Capsule = null, string? NextAttemptCursor = null)
{
    /// <summary>Gets the detached attempts data.</summary>
    public IReadOnlyList<ManagementSchedulerAttempt> Attempts { get; }
        = SchedulerValue.Copy(Attempts, ManagementSchedulerLimits.MaximumPageSize, nameof(Attempts));
}

/// <summary>Original event facts and captured source configuration; produced runs are independently authorized.</summary>
public sealed record ManagementSchedulerEvent(string ApplicationId, string TriggerId, string EventId,
    string OccurredAtUtc, string ReceivedAtUtc, MessageContract Contract, ManagementSchedulerActor Producer,
    string PayloadAccess, string? PayloadJson = null, ManagementSchedulerCapturedSettings? SourceSettings = null,
    string? ScheduledAtUtc = null, string? CorrelationId = null, string? CausationId = null);

/// <summary>One exact history filter. Job, Task and Event views read the same stored logical runs.</summary>
public sealed record ManagementSchedulerHistoryQuery(string? TaskId = null, string? ExecutionScope = null,
    string? JobId = null, string? EventId = null, string? ResourceId = null,
    string? FromUtc = null, string? UntilUtc = null, string? State = null,
    string? ApplicationVersion = null, string? BuildId = null, MessageContract? ExecutionContract = null,
    string? TriggerApplicationId = null, string? TriggerId = null);
