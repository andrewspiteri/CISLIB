import type { ClientCallResult } from "@cislib/client-core";
import type { ConfigurationShape } from "./configuration-models.js";
import type { KnownValue, OperationReference, OperationRejection, TransactionFacts } from "./models.js";
import { knownValue } from "./models.js";

/** Existing Messaging identity: an exact ASCII name and positive Int32 wire version. */
export interface MessageContract { readonly name: string; readonly version: number; }
/** Business keys retain Messaging's visible-ASCII profile, separate from administrative route keys. */
export interface MessageOperationReference { readonly operationName: string; readonly requestedScope: string; readonly key: string; }
export interface WorkerBuildReference {
  readonly applicationVersion: string; readonly buildId: string; readonly handlerId: string; readonly sourceRevision?: string;
}
export interface JobExecutionSupport {
  readonly currentContract: MessageContract; readonly supportedStarts: readonly MessageContract[];
  readonly supportedRecoveries: readonly MessageContract[]; readonly checkpointContract?: MessageContract; readonly operationName?: string;
}
export interface CapabilityKey {
  readonly ownerApplication: string; readonly kind: string; readonly executionScope: string;
  readonly definitionId: string; readonly definitionVersion: string;
}
export interface CapabilityQuery { readonly kind?: string; readonly executionScope?: string; readonly definitionId?: string; readonly definitionVersion?: string; }
export interface ConfigurationContractReference { readonly configurationId: string; readonly schemaVersion: string; readonly shape: ConfigurationShape; }
export interface FormFieldHint {
  readonly path: string; readonly editor: string; readonly label: string; readonly description?: string;
  readonly groupId?: string; readonly order?: number; readonly unit?: string; readonly readOnly?: boolean; readonly lookupId?: string;
}
export interface FormGroup { readonly id: string; readonly label: string; readonly order?: number; }
export interface NamedLookupReference {
  readonly id: string; readonly ownerApplication: string; readonly executionScope: string; readonly lookupName: string; readonly contract: MessageContract;
}
export interface FormDescription { readonly fields: readonly FormFieldHint[]; readonly groups?: readonly FormGroup[]; readonly lookups?: readonly NamedLookupReference[]; }
export interface CapabilityDefinition {
  readonly key: CapabilityKey; readonly displayName: string; readonly supportedOperations: readonly string[];
  readonly description?: string; readonly categoryId?: string; readonly configurationContract?: ConfigurationContractReference;
  readonly inputContract?: MessageContract; readonly inputShape?: ConfigurationShape; readonly eventContract?: MessageContract;
  readonly eventShape?: ConfigurationShape; readonly resultSummaryContract?: MessageContract; readonly resultSummaryShape?: ConfigurationShape;
  readonly form?: FormDescription; readonly executionSupport?: JobExecutionSupport;
}
export interface CapabilityParticipation {
  readonly key: CapabilityKey; readonly registrationId: string; readonly inventorySequence: string;
  readonly observedAtUtc: string; readonly receivedAtUtc: string; readonly availability: string; readonly draining?: boolean; readonly build?: WorkerBuildReference;
}
export interface AdministrativeAction { readonly operationId: string; readonly supported: boolean; readonly allowed: boolean; readonly reasonCode?: string; }
export interface ConfigurationActions { readonly resourceId: string; readonly actions: readonly AdministrativeAction[]; }
export interface ConfigurationCohortChoice { readonly cohortId: string; readonly displayName: string; }

export interface SchedulerTriggerReference { readonly applicationId: string; readonly triggerId: string; }
export interface SchedulerSettingsSelection {
  readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly releaseId: string; readonly assignmentGeneration: string;
}
export interface SchedulerCapturedSettings { readonly selection: SchedulerSettingsSelection; readonly payloadJson: string; readonly sha256: string; }
export interface SchedulerExecutionIdentity {
  readonly application: string; readonly environment: string; readonly issuer: string; readonly subject: string; readonly bindingId: string; readonly profileId: string;
}
export interface SchedulerActor { readonly kind: string; readonly issuer: string; readonly subject: string; readonly displayName?: string; readonly application?: string; readonly environment?: string; }
export interface SchedulerInputMapping {
  readonly targetPath: string; readonly kind: string; readonly sourcePath?: string; readonly constantJson?: string;
  readonly transformation?: MessageContract; readonly parametersJson?: string;
}
export interface SchedulerResourceMapping { readonly kind: string; readonly constant?: string; readonly eventPath?: string; readonly resolver?: MessageContract; readonly parametersJson?: string; }
export interface SchedulerRetryPolicy { readonly enabled: boolean; readonly maximumAttempts: number; readonly initialDelayMs: string; readonly maximumDelayMs: string; }
/** Durations are canonical decimal milliseconds, retaining up to four fractional digits (one tick). */
export interface SchedulerCalendar {
  readonly form: string; readonly missedRunPolicy: string; readonly delayMs?: string; readonly localTime?: string;
  readonly timeZoneId?: string; readonly weekdays?: readonly number[]; readonly maximumCatchUp?: number; readonly catchUpWindowMs?: string;
  readonly initialDelayMs?: string; readonly runImmediately?: boolean; readonly failurePolicy?: string;
}
/** Exact positive 100 ns ticks, independently captured from business Job settings. */
export interface SchedulerExecutionSettings { readonly idleDelayTicks: string; readonly timeoutTicks?: string; readonly maximumDrainTicks: string; readonly acquisitionRetryDelayTicks: string; }
export interface SchedulerTriggerDraft { readonly name: string; readonly provider: CapabilityKey; readonly configuration?: SchedulerSettingsSelection; readonly enabled?: boolean; }
export interface SchedulerTaskDraft {
  readonly name: string; readonly trigger: SchedulerTriggerReference; readonly job: CapabilityKey; readonly resource: SchedulerResourceMapping;
  readonly inputMappings: readonly SchedulerInputMapping[]; readonly jobConfiguration: SchedulerSettingsSelection;
  readonly triggerConfiguration?: SchedulerSettingsSelection; readonly executionProfileId?: string; readonly retry?: SchedulerRetryPolicy;
  readonly enabled?: boolean; readonly executionSettings?: SchedulerExecutionSettings;
}
export interface SchedulerTrigger {
  readonly applicationId: string; readonly triggerId: string; readonly revision: string; readonly eTag: string;
  readonly definition: SchedulerTriggerDraft; readonly state: string; readonly configuredBy: SchedulerActor;
  readonly changedAtUtc: string; readonly calendar?: SchedulerCalendar; readonly waitingReason?: string;
}
export interface SchedulerTask {
  readonly applicationId: string; readonly taskId: string; readonly desiredRevision: string; readonly activeRevision?: string;
  readonly eTag: string; readonly definition: SchedulerTaskDraft; readonly state: string; readonly configuredBy: SchedulerActor;
  readonly changedAtUtc: string; readonly waitingReason?: string;
}
export interface SchedulerTriggerCommand { readonly commandVersion: 1; readonly triggerId: string; readonly definition: SchedulerTriggerDraft; readonly reason: string; }
export interface SchedulerTaskCommand {
  readonly commandVersion: 1; readonly taskId: string; readonly definition: SchedulerTaskDraft; readonly reason: string;
  readonly expectedTriggerETag?: string; readonly previewDigest?: string;
}
export interface SchedulerStateCommand { readonly commandVersion: 1; readonly intent: string; readonly reason: string; }
export type SchedulerTriggerStateCommand = SchedulerStateCommand;
export type SchedulerTaskStateCommand = SchedulerStateCommand;
export type SchedulerRunCancellationCommand = SchedulerStateCommand;
export interface SchedulerTaskPreviewCommand {
  readonly commandVersion: 1; readonly definition: SchedulerTaskDraft; readonly candidateJobPayloadJson?: string;
  readonly candidateTriggerPayloadJson?: string; readonly taskId?: string;
}
export interface SchedulerCalendarPreviewCommand { readonly commandVersion: 1; readonly calendar: SchedulerCalendar; readonly fromUtc: string; readonly count?: number; }
export interface SchedulerValidationIssue { readonly path: string; readonly code: string; readonly message: string; }
export interface SchedulerAffectedTask { readonly applicationId: string; readonly taskId: string; readonly name: string; readonly revision: string; }
export interface SchedulerTaskPreview {
  readonly valid: boolean; readonly issues: readonly SchedulerValidationIssue[]; readonly affectedTasks: readonly SchedulerAffectedTask[];
  readonly impactFullyDisclosed: boolean; readonly nextOccurrencesUtc: readonly string[]; readonly activationState: string;
  readonly draftDigest?: string; readonly taskETag?: string; readonly triggerETag?: string;
  readonly preconditions?: readonly SchedulerSettingsSelection[]; readonly definition?: SchedulerTaskDraft;
}
export interface SchedulerEventCommand {
  readonly commandVersion: 1; readonly eventId: string; readonly occurredAtUtc: string; readonly contract: MessageContract;
  readonly payloadJson: string; readonly scheduledAtUtc?: string; readonly correlationId?: string; readonly causationId?: string;
}
export interface SchedulerEventReceipt {
  readonly applicationId: string; readonly triggerId: string; readonly eventId: string; readonly disposition: string;
  readonly acceptedAtUtc: string; readonly runIds?: readonly string[]; readonly runCount?: number;
}
export interface SchedulerManualRunCommand { readonly commandVersion: 1; readonly eventId: string; readonly reason: string; }
export interface SchedulerRunCapsule {
  readonly runId: string; readonly applicationId: string; readonly taskId: string; readonly taskRevision: string;
  readonly trigger: SchedulerTriggerReference; readonly eventId: string; readonly job: CapabilityKey; readonly resourceId: string;
  readonly inputEnvelopeJson: string; readonly executionSettings: SchedulerExecutionSettings; readonly executionContract: MessageContract;
  readonly originalOperation: MessageOperationReference; readonly jobSettings: SchedulerCapturedSettings;
  readonly executionIdentity: SchedulerExecutionIdentity; readonly configuredBy: SchedulerActor; readonly producer: SchedulerActor;
  readonly triggerSettings?: SchedulerCapturedSettings; readonly requestedBy?: SchedulerActor; readonly scheduledAtUtc?: string;
  readonly eventOccurredAtUtc?: string; readonly eventReceivedAtUtc?: string; readonly correlationId?: string; readonly causationId?: string;
}
export interface SchedulerRun {
  readonly applicationId: string; readonly runId: string; readonly taskId: string; readonly taskRevision: string;
  readonly trigger: SchedulerTriggerReference; readonly eventId: string; readonly job: CapabilityKey; readonly resourceId: string;
  readonly state: string; readonly createdAtUtc: string; readonly attemptCount: string; readonly businessOutcome?: string;
  readonly completedAtUtc?: string; readonly waitingReason?: string; readonly cancellationRequested?: boolean;
  readonly scheduledAtUtc?: string; readonly nextAttemptAtUtc?: string; readonly correlationId?: string; readonly causationId?: string;
}
export interface SchedulerAttempt {
  readonly runId: string; readonly attemptId: string; readonly number: string; readonly role: string; readonly state: string;
  readonly admittedAtUtc: string; readonly build: WorkerBuildReference; readonly executor: SchedulerExecutionIdentity;
  readonly registrationId: string; readonly effectivePolicy: string; readonly sequence: string; readonly enteredAtUtc?: string;
  readonly settledAtUtc?: string; readonly businessCompletion?: string; readonly callbackSettled?: boolean;
  readonly cleanupSucceeded?: boolean; readonly summaryJson?: string; readonly acquisitionId?: string; readonly durationMs?: string;
}
export interface SchedulerProgress { readonly completed?: string; readonly total?: string; readonly message?: string; }
export interface SchedulerAttemptReport {
  readonly commandVersion: 1; readonly sequence: string; readonly observedAtUtc: string; readonly phase: string;
  readonly businessOutcome?: string; readonly businessCompletion?: string; readonly callbackSettled?: boolean;
  readonly cleanupSucceeded?: boolean; readonly progress?: SchedulerProgress; readonly summaryJson?: string; readonly durationMs?: string;
}
export interface SchedulerAttemptCommand {
  readonly commandVersion: 1; readonly attemptId: string; readonly registrationId: string; readonly role: string;
  readonly executionContract: MessageContract; readonly build: WorkerBuildReference; readonly ownershipGeneration: string;
  readonly acquisitionId?: string; readonly effectivePolicy?: string;
}
export interface SchedulerAttemptAdmission { readonly disposition: string; readonly reasonCode?: string; readonly attempt?: SchedulerAttempt; }
export interface SchedulerRunDetail {
  readonly run: SchedulerRun; readonly attempts: readonly SchedulerAttempt[]; readonly capturedContentAccess: string;
  readonly capsule?: SchedulerRunCapsule; readonly nextAttemptCursor?: string;
}
export interface SchedulerEvent {
  readonly applicationId: string; readonly triggerId: string; readonly eventId: string; readonly occurredAtUtc: string;
  readonly receivedAtUtc: string; readonly contract: MessageContract; readonly producer: SchedulerActor; readonly payloadAccess: string;
  readonly payloadJson?: string; readonly sourceSettings?: SchedulerCapturedSettings; readonly scheduledAtUtc?: string;
  readonly correlationId?: string; readonly causationId?: string;
}
export interface SchedulerHistoryQuery {
  readonly taskId?: string; readonly executionScope?: string; readonly jobId?: string; readonly eventId?: string;
  readonly resourceId?: string; readonly fromUtc?: string; readonly untilUtc?: string; readonly state?: string;
  readonly applicationVersion?: string; readonly buildId?: string; readonly executionContract?: MessageContract;
  readonly triggerApplicationId?: string; readonly triggerId?: string;
}
export interface SchedulerDecision {
  readonly commandVersion: 1; readonly kind: string; readonly decision: string; readonly code: string;
  readonly decidedAtUtc: string; readonly applicationId: string; readonly objectId: string; readonly revision?: string;
  readonly eTag?: string; readonly runId?: string; readonly attemptId?: string;
}
export interface SchedulerOperationResult {
  readonly operation: OperationReference; readonly resultSource: string; readonly originalCompletion: string;
  readonly result?: SchedulerDecision; readonly attemptCompletion?: TransactionFacts; readonly rejection?: OperationRejection;
}
export interface SchedulerProblem {
  readonly type: string; readonly title: string; readonly status: number; readonly code: string; readonly detail?: string;
  readonly correlationId?: string; readonly operation?: OperationReference; readonly result?: SchedulerOperationResult;
}
export type SchedulerCallResult<T> = ClientCallResult<T, SchedulerProblem>;

export const knownCapabilityKind = (value: string): KnownValue => knownValue(value, ["job", "triggerProvider", "ruleEngine"]);
export const knownCapabilityOperation = (value: string): KnownValue => knownValue(value, ["inspect", "configure", "createConfigured", "manual", "cancel"]);
export const knownFormEditor = (value: string): KnownValue => knownValue(value, ["text", "number", "boolean", "enum", "duration", "group", "calendar"]);
export const knownSchedulerCalendarForm = (value: string): KnownValue => knownValue(value, ["fixedDelay", "daily", "weekly"]);
export const knownSchedulerRunState = (value: string): KnownValue => knownValue(value, ["pending", "admitted", "running", "retryPending", "succeeded", "failed", "cancelled", "outcomeUnknown"]);
export const knownSchedulerTaskState = (value: string): KnownValue => knownValue(value, ["disabled", "pending", "active", "retired"]);
export const knownSchedulerTriggerState = (value: string): KnownValue => knownValue(value, ["disabled", "enabled", "retired", "pending"]);
export const knownSchedulerAttemptRole = (value: string): KnownValue => knownValue(value, ["execution", "recovery"]);
export const knownSchedulerAttemptState = (value: string): KnownValue => knownValue(value, ["admitted", "running", "settled", "interrupted"]);
export const knownSchedulerContentAccess = (value: string): KnownValue => knownValue(value, ["allowed", "redacted", "expired", "unavailable"]);
export const knownSchedulerDecision = (value: string): KnownValue => knownValue(value, ["applied", "rejected"]);
export const knownSchedulerDecisionKind = (value: string): KnownValue => knownValue(value, ["triggerCreated", "triggerUpdated", "triggerStateChanged", "taskCreated", "taskUpdated", "taskActivated", "taskStateChanged", "manualRunAdmitted", "cancellationRequested"]);
export const knownSchedulerResultSource = (value: string): KnownValue => knownValue(value, ["prepared", "replayed", "found"]);
export const knownSchedulerCompletion = (value: string): KnownValue => knownValue(value, ["committed", "notCommitted", "unknown"]);
export const knownSchedulerEventDisposition = (value: string): KnownValue => knownValue(value, ["accepted", "replayed"]);
export const knownSchedulerAdmissionDisposition = (value: string): KnownValue => knownValue(value, ["admitted", "replayed", "deferred", "rejected"]);
