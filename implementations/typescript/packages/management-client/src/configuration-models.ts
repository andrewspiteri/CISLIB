import type { ClientCallResult } from "@cislib/client-core";
import type { KnownValue, OperationReference, OperationRejection, Page, TransactionFacts } from "./models.js";
import { knownValue } from "./models.js";

/** Native generic-v1 creation scope. It is metadata, not a publication or mutable schema template. */
export interface ConfigurationCreationContext {
  readonly applicationId: string; readonly application: string; readonly environment: string;
  readonly eTag: string; readonly schemaVersion: "1"; readonly hostApplicationName: string;
}
export interface ConfigurationCreationCommand {
  readonly commandVersion: 1; readonly configurationId: string; readonly intent: "createGeneric" | "reuseGeneric"; readonly reason: string;
}
/** Metadata-only origin of one actual application binding; payload/defaults are not included. */
export interface ConfigurationSummary {
  readonly applicationId: string; readonly environment: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly sourceId: string; readonly bindingId: string; readonly hostApplicationName: string; readonly origin: string;
  readonly hasCohortOverrides: boolean; readonly description?: string;
  /** Default-assigned selected layer publication time, not latest source history or assignment time. */
  readonly modifiedAtUtc?: string;
}

export interface ConfigurationShape {
  readonly kind: "object" | "array" | "dictionary" | "string" | "number" | "integer" | "boolean";
  readonly nullable: boolean;
  readonly members?: readonly ConfigurationMember[];
  readonly element?: ConfigurationShape;
  readonly allowedValues?: readonly string[];
}
export interface ConfigurationMember { readonly name: string; readonly required: boolean; readonly shape: ConfigurationShape; }
/** Exact catalog source availability, independent of any publication or assignment. */
export type ConfigurationEditorSource = { readonly state: "available"; readonly sourceId: string }
  | { readonly state: "unavailable"; readonly sourceId?: string }
  | { readonly state: "absent" | "redacted"; readonly sourceId?: never };
/** Declared root defaults only; these are not a consumer's accepted/effective values. */
export type ConfigurationEditorDefaults = { readonly state: "available"; readonly defaultsJson: string }
  | { readonly state: "redacted" | "unavailable"; readonly defaultsJson?: never };
/** One authorized binding and its actual bounded source pair; no payload, history or action authority. */
export interface ConfigurationEditorContext {
  readonly bindingId: string;
  readonly applicationId: string;
  readonly application: string;
  readonly environment: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly hostApplicationName: string;
  readonly shape: ConfigurationShape;
  readonly selectedSource: "common" | "application";
  readonly commonSource: ConfigurationEditorSource;
  readonly applicationSource: ConfigurationEditorSource;
  readonly defaults: ConfigurationEditorDefaults;
  readonly description?: string;
}
/** One unpublished candidate layer and explicit immutable baseline references. */
export interface ConfigurationCandidatePreviewCommand {
  readonly commandVersion: 1;
  readonly applicationId: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly selectedSource: "common" | "application";
  readonly sourceId: string;
  readonly sourceETag: string;
  readonly assignmentETag: string;
  readonly commonSource: ConfigurationSourceReference;
  readonly applicationSource: ConfigurationSourceReference;
  readonly payloadJson: string;
}
/** Native effective-change preview; baseline references are not a publication of the draft. */
export interface ConfigurationCandidatePreview {
  readonly bindingId: string;
  readonly applicationId: string;
  readonly environment: string;
  readonly configurationId: string;
  readonly schemaVersion: string;
  readonly hostApplicationName: string;
  readonly selectedSource: "common" | "application";
  readonly sourceId: string;
  readonly sourceETag: string;
  readonly assignmentETag: string;
  readonly commonSource: ConfigurationSourceReference;
  readonly applicationSource: ConfigurationSourceReference;
  readonly draftSha256: string;
  readonly structurallyValid: boolean;
  readonly redacted: boolean;
  readonly changes: readonly ConfigurationChange[];
  readonly issues: readonly ConfigurationIssue[];
  readonly currentAssignment: ConfigurationAssignmentMap;
  readonly recipients: Page<ConfigurationPreviewRecipient>;
}
interface DefinitionFacts {
  readonly configurationId: string; readonly schemaVersion: string; readonly sourceId: string; readonly environment: string;
  readonly shape: ConfigurationShape; readonly defaultsJson?: string; readonly description?: string;
}
/** Discovery is by actual source; omitted defaults are redacted, never empty defaults. */
export type ConfigurationDefinition = DefinitionFacts & (
  | { readonly kind: "common"; readonly applicationId?: never; readonly bindingId?: never; readonly hostApplicationName?: never }
  | { readonly kind: "application"; readonly applicationId: string; readonly bindingId: string; readonly hostApplicationName: string }
);
export interface ConfigurationCatalog {
  readonly catalogId: string; readonly eTag: string; readonly loadedManifestDigest: string; readonly appliedManifestDigest?: string;
  readonly ready: boolean; readonly definitionCount: number; readonly sourceCount: number; readonly bindingCount: number;
  readonly quotaBytes: number; readonly usedBytes: number;
}
export interface ConfigurationCatalogCommand { readonly commandVersion: 1; readonly manifestDigest: string; readonly reason: string; }
export type ConfigurationSourceReference = { readonly state: "absent"; readonly publicationId?: never }
  | { readonly state: "present"; readonly publicationId: string };
export type ConfigurationSourceContent = { readonly state: "absent" }
  | { readonly state: "present"; readonly publicationId: string; readonly sha256: string; readonly payloadJson: string;
    readonly sourceId: string; readonly configurationId: string; readonly schemaVersion: string; readonly environment: string;
    readonly kind: "common" | "application"; readonly applicationId?: string };
export interface ConfigurationPublicationMetadata {
  readonly publicationId: string; readonly sourceId: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly environment: string; readonly kind: "common" | "application"; readonly applicationId?: string;
  readonly sha256: string; readonly actor: string; readonly createdAtUtc: string; readonly reason: string;
}
export interface ConfigurationPublication extends ConfigurationPublicationMetadata { readonly payloadJson: string; }
export interface ConfigurationSource {
  readonly sourceId: string; readonly configurationId: string; readonly schemaVersion: string; readonly environment: string;
  readonly kind: "common" | "application"; readonly applicationId?: string; readonly eTag: string;
  readonly publications: Page<ConfigurationPublicationMetadata>;
}
export interface ConfigurationPublicationCommand { readonly commandVersion: 1; readonly payloadJson: string; readonly reason: string; }
export interface ConfigurationReleaseCommand {
  readonly commandVersion: 1; readonly commonSource: ConfigurationSourceReference;
  readonly applicationSource: ConfigurationSourceReference; readonly reason: string;
}
export interface ConfigurationRelease {
  readonly releaseId: string; readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly environment: string; readonly commonSource: ConfigurationSourceReference; readonly applicationSource: ConfigurationSourceReference;
  readonly actor: string; readonly createdAtUtc: string; readonly reason: string; readonly eTag: string;
}
export interface ConfigurationOverride { readonly cohortId: string; readonly releaseId: string; }
export interface ConfigurationAssignmentCommand {
  readonly commandVersion: 1; readonly intent: "replace" | "promote" | "rollback"; readonly defaultReleaseId: string;
  readonly overrides: readonly ConfigurationOverride[]; readonly reason: string;
}
/** Response proposal preserves future bounded intent, with an explicit known-value helper. */
export interface ConfigurationAssignmentProposal extends Omit<ConfigurationAssignmentCommand, "intent"> { readonly intent: string; }
export interface ConfigurationAssignmentMap {
  readonly bindingId: string; readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly environment: string; readonly eTag: string; readonly overrides: readonly ConfigurationOverride[];
  readonly defaultReleaseId?: string; readonly generation?: string;
}
export interface ConfigurationSnapshotTarget {
  readonly authorityId: string; readonly applicationId: string; readonly environment: string; readonly registrationId: string;
  readonly configurationId: string; readonly schemaVersion: string; readonly hostApplicationName: string; readonly cohortId?: string;
}
export interface ConfigurationDesiredContext extends ConfigurationSnapshotTarget { readonly releaseId: string; readonly generation: string; }
export interface ConfigurationSnapshot {
  readonly desired: ConfigurationDesiredContext; readonly eTag: string;
  readonly commonSource: ConfigurationSourceContent; readonly applicationSource: ConfigurationSourceContent;
}
export interface ConfigurationIssue { readonly code: string; readonly path?: string; }
export interface ConfigurationValidationCommand { readonly commandVersion: 1; readonly sourceId: string; readonly payloadJson: string; }
export interface ConfigurationValidationResult { readonly valid: boolean; readonly issues: readonly ConfigurationIssue[]; }
interface PreviewIdentity { readonly commandVersion: 1; readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string; }
export type ConfigurationPreviewCommand = PreviewIdentity & (
  | { readonly commonSource: ConfigurationSourceReference; readonly applicationSource: ConfigurationSourceReference; readonly assignment?: never }
  | { readonly commonSource?: never; readonly applicationSource?: never; readonly assignment: ConfigurationAssignmentCommand }
);
export interface ConfigurationChange { readonly path: string; readonly kind: string; }
export interface ConfigurationPreviewRecipient { readonly registrationId: string; readonly compatibility: string; readonly cohortId?: string; }
interface PreviewFacts {
  readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly structurallyValid: boolean; readonly redacted: boolean; readonly changes: readonly ConfigurationChange[];
  readonly issues: readonly ConfigurationIssue[]; readonly currentAssignment: ConfigurationAssignmentMap;
  readonly recipients: Page<ConfigurationPreviewRecipient>;
}
export type ConfigurationPreview = PreviewFacts & (
  | { readonly commonSource: ConfigurationSourceReference; readonly applicationSource: ConfigurationSourceReference; readonly proposedAssignment?: never }
  | { readonly commonSource?: never; readonly applicationSource?: never; readonly proposedAssignment: ConfigurationAssignmentProposal }
);
export interface ConfigurationVersion { readonly releaseId: string; readonly generation: string; }
export interface ConfigurationAppliedVersion extends ConfigurationVersion { readonly localRevision: string; }
export interface ConfigurationStatusEntry {
  readonly configurationId: string; readonly schemaVersion: string; readonly updateMode: string; readonly state: string;
  readonly liveStateValid: boolean; readonly desired?: ConfigurationVersion; readonly applied?: ConfigurationAppliedVersion;
  readonly lastSuccessful?: ConfigurationAppliedVersion; readonly candidateFailureCode?: string; readonly notificationFailureCode?: string;
}
export interface ConfigurationStatusReport { readonly sequence: string; readonly observedAtUtc: string; readonly entries: readonly ConfigurationStatusEntry[]; }
export interface ConfigurationAdoptionEntry {
  readonly applicationId: string; readonly environment: string; readonly configurationId: string; readonly schemaVersion: string;
  readonly registrationFreshness: string; readonly adoptionFreshness: string; readonly convergence: string; readonly cohortId?: string;
  readonly registrationId?: string; readonly reportedInstanceId?: string; readonly applicationVersion?: string;
  readonly desired?: ConfigurationVersion; readonly reported?: ConfigurationStatusEntry; readonly observedAtUtc?: string; readonly receivedAtUtc?: string;
}
export interface ConfigurationDecision {
  readonly commandVersion: 1; readonly kind: string; readonly decision: string; readonly code: string; readonly decidedAtUtc: string;
  readonly catalogId?: string; readonly manifestDigest?: string; readonly sourceId?: string; readonly publicationId?: string;
  readonly sha256?: string; readonly applicationId?: string; readonly configurationId?: string; readonly schemaVersion?: string;
  readonly releaseId?: string; readonly generation?: string; readonly eTag?: string; readonly intent?: string; readonly assignment?: ConfigurationAssignmentMap;
  readonly bindingId?: string; readonly hostApplicationName?: string; readonly sourceETag?: string;
}
interface ConfigurationResultFacts {
  readonly operation: OperationReference; readonly resultSource: string; readonly originalCompletion: string; readonly attemptCompletion?: TransactionFacts;
}
export type ConfigurationOperationResult = ConfigurationResultFacts & (
  | { readonly result: ConfigurationDecision; readonly rejection?: never }
  | { readonly result?: never; readonly rejection: OperationRejection }
);
export interface ConfigurationProblem {
  readonly type: string; readonly title: string; readonly status: number; readonly code: string; readonly detail?: string;
  readonly correlationId?: string; readonly operation?: OperationReference; readonly result?: ConfigurationOperationResult;
}
export interface ConfigurationContentRejection { readonly desired: ConfigurationDesiredContext; readonly code: string; }
export type ConfigurationCallResult<T> =
  | ClientCallResult<T, ConfigurationProblem> & { readonly contentRejection?: never }
  | Omit<ClientCallResult<T, ConfigurationProblem>, "kind" | "value" | "problem"> & {
    readonly kind: "protocolFailure"; readonly value?: never; readonly problem?: never;
    readonly contentRejection: ConfigurationContentRejection;
  };
export interface ConfigurationSnapshotRead {
  readonly snapshot?: ConfigurationSnapshot; readonly desired?: ConfigurationDesiredContext; readonly failureCode?: string;
}
export const knownConfigurationState = (value: string): KnownValue => knownValue(value, ["Pending", "Applied", "AppliedUnchanged", "Rejected", "PendingRestart", "Faulted"]);
export const knownConfigurationUpdateMode = (value: string): KnownValue => knownValue(value, ["HotReload", "RestartRequired"]);
export const knownConfigurationIntent = (value: string): KnownValue => knownValue(value, ["replace", "promote", "rollback"]);
export const knownConfigurationCompatibility = (value: string): KnownValue => knownValue(value, ["reportedCompatible", "unknown"]);
export const knownConfigurationFreshness = (value: string): KnownValue => knownValue(value, ["fresh", "stale", "offline", "unknown", "noInstances"]);
export const knownConfigurationConvergence = (value: string): KnownValue => knownValue(value, ["pending", "applied", "rejected", "pendingRestart", "faulted", "unknown", "noInstances"]);
export const knownConfigurationDecisionKind = (value: string): KnownValue => knownValue(value, ["catalogApplied", "sourcePublished", "releaseCreated", "assignmentsReplaced", "configurationCreated"]);
export const knownConfigurationOrigin = (value: string): KnownValue => knownValue(value, ["shared", "applicationOverride", "applicationOnly", "defaultsOnly", "unassigned", "mixed", "protected", "unavailable"]);
export const knownConfigurationOriginalCompletion = (value: string): KnownValue => knownValue(value, ["committed", "notCommitted", "unknown"]);
export const knownConfigurationResultSource = (value: string): KnownValue => knownValue(value, ["prepared", "replayed", "found"]);
export function knownConfigurationBusinessDecision(value: ConfigurationOperationResult): KnownValue {
  if (value.rejection !== undefined) { return knownValue("rejected", ["rejected"]); }
  return knownConfigurationDecisionKind(value.result.kind).isKnown
    ? knownValue(value.result.decision, ["applied", "rejected"]) : knownValue(value.result.decision, []);
}
