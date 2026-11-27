import type { ManagementCallResult, MetadataDecision, MetadataResult, OperationReference, OperationRejection, TransactionFacts } from "@cislib/management-client";
import { isOperationKey, isUuid } from "@cislib/management-client";

export interface MetadataCommandObservation {
  readonly disposition: "applied" | "rejected" | "conflict" | "notCommitted" | "unresolved";
  readonly family: "management.metadata.replace";
  readonly target: { readonly applicationId: string };
  readonly key: string;
  readonly operation?: OperationReference;
  readonly originalCompletion?: string;
  readonly resultSource?: string;
  readonly decision?: MetadataDecision;
  readonly rejection?: OperationRejection;
  readonly attemptCompletion?: TransactionFacts;
  readonly code?: string;
  readonly transport: {
    readonly kind: string;
    readonly status?: number;
    readonly cleanupSucceeded: boolean;
    readonly mutationUncertain: boolean;
    readonly code?: string;
  };
}

/** Project facts, not a new workflow: later cleanup cannot erase an established original commit. */
export function projectMetadataCommand(call: ManagementCallResult<MetadataResult>, applicationId: string, key: string): MetadataCommandObservation {
  if (!isUuid(applicationId) || !isOperationKey(key)) { throw new TypeError("Select the exact portal application and original key."); }
  const base = { family: "management.metadata.replace" as const, target: Object.freeze({ applicationId }), key,
    transport: Object.freeze({ kind: call.kind, ...(call.status === undefined ? {} : { status: call.status }),
      cleanupSucceeded: call.cleanupSucceeded, mutationUncertain: call.mutationUncertain,
      ...(call.code === undefined ? {} : { code: call.code }) }) };
  const original = call.value ?? call.problem?.result;
  const code = call.problem?.code ?? call.code;
  if (original === undefined) { return Object.freeze({ ...base, disposition: "unresolved", ...(code === undefined ? {} : { code }) }); }
  const prefix = `management.metadata.v1/${applicationId}/management.actor.v1.`;
  if (original.operation.name !== base.family || original.operation.key !== key
    || !original.operation.scope.startsWith(prefix) || !/^[a-f0-9]{64}$/u.test(original.operation.scope.slice(prefix.length))
    || original.result != null && original.result.applicationId !== applicationId) {
    return Object.freeze({ ...base, disposition: "unresolved", code: "portal.operation.target_mismatch" });
  }
  const operation = Object.freeze({ name: original.operation.name, scope: original.operation.scope, key: original.operation.key });
  const decision = original.result == null ? undefined : pickDecision(original.result);
  const rejection = original.rejection == null ? undefined : Object.freeze({ code: original.rejection.code, message: original.rejection.message });
  const completion = original.attemptCompletion === undefined ? undefined : Object.freeze({ outcome: original.attemptCompletion.outcome,
    cleanupSucceeded: original.attemptCompletion.cleanupSucceeded, ...(original.attemptCompletion.cleanupCodes === undefined ? {}
      : { cleanupCodes: Object.freeze([...original.attemptCompletion.cleanupCodes]) }) });
  const knownSource = ["prepared", "replayed", "found"].includes(original.resultSource);
  let disposition: MetadataCommandObservation["disposition"] = "unresolved";
  if (original.originalCompletion === "notCommitted") { disposition = "notCommitted"; }
  else if (original.originalCompletion === "committed" && knownSource) {
    if (decision?.decision === "applied") { disposition = "applied"; }
    else if (decision?.decision === "rejected" || rejection !== undefined) {
      disposition = (decision?.code ?? rejection?.code) === "management.precondition_failed" ? "conflict" : "rejected";
    }
  }
  return Object.freeze({ ...base, disposition, operation, originalCompletion: original.originalCompletion, resultSource: original.resultSource,
    ...(decision === undefined ? {} : { decision }), ...(rejection === undefined ? {} : { rejection }),
    ...(completion === undefined ? {} : { attemptCompletion: completion }), ...(code === undefined ? {} : { code }) });
}

function pickDecision(value: MetadataDecision): MetadataDecision {
  return Object.freeze({ commandVersion: value.commandVersion, applicationId: value.applicationId, decision: value.decision, code: value.code,
    metadata: Object.freeze({ displayName: value.metadata.displayName,
      ...(value.metadata.description === undefined ? {} : { description: value.metadata.description }) }),
    metadataRevision: value.metadataRevision, metadataETag: value.metadataETag, decidedAtUtc: value.decidedAtUtc });
}
