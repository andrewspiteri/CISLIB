import type { ClientCallResult } from "@cislib/client-core";
import type { OpaqueJsonObject } from "./wire-json.js";

export interface KnownValue { readonly value: string; readonly isKnown: boolean; }
export interface ApplicationMetadata { readonly displayName: string; readonly description?: string; }
export interface Application {
  readonly applicationId: string; readonly application: string; readonly environment: string;
  readonly metadata: ApplicationMetadata; readonly metadataRevision: string; readonly metadataETag: string;
}
export interface Page<T> { readonly items: readonly T[]; readonly nextCursor?: string; }
export interface PageRequest { readonly pageSize?: number; readonly cursor?: string; }
/** Literal case-sensitive substrings and exact environments; no trimming, normalization or wildcards. */
export interface ApplicationQuery { readonly query?: string; readonly environment?: string; }
/** An environment belonging to at least one currently authorized application. */
export interface EnvironmentEntry { readonly environment: string; }
export interface Feature {
  readonly operationId: string; readonly version: number; readonly method: string;
  readonly path: string; readonly requiredCapabilities: readonly string[];
}
export interface ServiceInfo {
  readonly serviceVersion: string; readonly protocolMajors: readonly number[]; readonly features: readonly Feature[];
  readonly maximumInventoryRequestBytes?: number;
}
export interface Capabilities {
  readonly applicationId: string; readonly implementedApis: readonly Feature[];
  readonly callerHints: readonly { readonly operationId: string; readonly allowed: boolean }[];
}
export interface RegistrationRequest { readonly protocolMajor: 1; readonly reportedInstanceId: string; readonly processStartedAtUtc: string; readonly applicationVersion: string; }
export interface RegistrationReceipt { readonly registrationId: string; readonly applicationId: string; readonly registeredAtUtc: string; }
export interface ControlConfirmation {
  readonly authorityId: string; readonly registrationId: string; readonly controlRevision: string;
  readonly admissionAllowed: boolean; readonly validForMs: number; readonly attemptId: string;
}
export interface Observation { readonly sequence: string; readonly observedAtUtc: string; readonly lifecycle: string; readonly health: string; }
export interface ReportReceipt {
  readonly registrationId: string; readonly stream: string; readonly submittedSequence: string;
  readonly acceptedSequence: string; readonly disposition: string; readonly receivedAtUtc: string;
}
export interface CloseReceipt { readonly registrationId: string; readonly closedAtUtc: string; }
export interface InstanceEvidence { readonly kind: string; readonly reportedIdentifier?: string; readonly verifiedIdentifier?: string; }
export interface Instance {
  readonly registrationId: string; readonly applicationId: string; readonly reportedInstanceId: string;
  readonly instanceEvidence: InstanceEvidence; readonly processStartedAtUtc: string; readonly applicationVersion: string;
  readonly protocolMajor: number; readonly registeredAtUtc: string; readonly presence: string;
  readonly lastSeenAtUtc?: string; readonly closedAtUtc?: string; readonly observation?: Observation;
  readonly observationReceivedAtUtc?: string; readonly inventorySequence?: string; readonly inventoryReceivedAtUtc?: string;
}
export interface ModuleInventory { readonly kind: string; readonly version: string; readonly details?: OpaqueJsonObject | Readonly<Record<string, unknown>>; }
export interface CategoryInventory { readonly executionScope: string; readonly categoryId: string; readonly displayName: string; readonly description?: string; }
export interface OwnerObservation { readonly ownerApplication: string; readonly instanceId: string; readonly generation: string; readonly expiresAtUtc: string; readonly observedAtUtc: string; }
export interface JobObservation {
  readonly resourceId: string; readonly mode: string; readonly state: string; readonly lastOutcome?: string;
  readonly observedOwner?: OwnerObservation; readonly lastOccurrenceUtc?: string; readonly nextOccurrenceUtc?: string;
}
export interface JobInventory {
  readonly executionScope: string; readonly jobId: string; readonly categoryId: string; readonly displayName: string;
  readonly supportedModes: readonly string[]; readonly description?: string; readonly resourceScopeDescription?: string;
  readonly effectiveConfiguration?: { readonly revision: string; readonly enabled: boolean };
  readonly observations?: readonly JobObservation[];
}
export interface InventorySnapshot {
  readonly sequence: string; readonly observedAtUtc: string; readonly modules: readonly ModuleInventory[];
  readonly categories: readonly CategoryInventory[]; readonly jobs: readonly JobInventory[];
}
export interface InventoryItem { readonly registrationId: string; readonly applicationVersion: string; readonly receivedAtUtc: string; readonly inventory: InventorySnapshot; }
export interface MetadataCommand { readonly commandVersion: 1; readonly displayName: string; readonly description?: string; }
export interface OperationReference { readonly name: string; readonly scope: string; readonly key: string; }
export interface MetadataDecision {
  readonly commandVersion: 1; readonly applicationId: string; readonly decision: string; readonly code: string;
  readonly metadata: ApplicationMetadata; readonly metadataRevision: string; readonly metadataETag: string; readonly decidedAtUtc: string;
}
export interface TransactionFacts { readonly outcome: string; readonly cleanupSucceeded: boolean; readonly cleanupCodes?: readonly string[]; }
export interface OperationRejection { readonly code: string; readonly message: string; }
export interface MetadataResultFacts {
  readonly operation: OperationReference; readonly resultSource: string;
  readonly originalCompletion: string; readonly attemptCompletion?: TransactionFacts;
}
/** Native RM-03 returns a success value or its original rejection, never invented metadata. */
export type MetadataResult = MetadataResultFacts & (
  | { readonly result: MetadataDecision; readonly rejection?: null }
  | { readonly result?: null; readonly rejection: OperationRejection }
);
export interface Problem {
  readonly type: string; readonly title: string; readonly status: number; readonly code: string;
  readonly detail?: string; readonly correlationId?: string; readonly operation?: OperationReference; readonly result?: MetadataResult;
}
export type ManagementCallResult<T> = ClientCallResult<T, Problem>;
export const knownValue = (value: string, known: readonly string[]): KnownValue => Object.freeze({ value, isKnown: known.includes(value) });
export const knownOriginalCompletion = (result: MetadataResult): KnownValue => knownValue(result.originalCompletion, ["committed", "notCommitted", "unknown"]);
export const knownResultSource = (result: MetadataResult): KnownValue => knownValue(result.resultSource, ["prepared", "replayed", "found"]);
export const knownDecision = (result: MetadataDecision): KnownValue => knownValue(result.decision, ["applied", "rejected"]);
