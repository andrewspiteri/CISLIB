import type { ConfigurationCallResult, ConfigurationDecision, ConfigurationOperationResult, OperationReference, OperationRejection, TransactionFacts } from "@cislib/management-client";
import { isOperationKey, isOrderedInteger, isScalarText, isStrongETag, isUuid } from "@cislib/management-client";
import type { CommandReceiptIntent } from "./command-receipts.ts";

export type ConfigurationCommandIntent = Extract<CommandReceiptIntent, { readonly family:
  "management.configuration.create" | "management.configuration.source.publish" | "management.configuration.release.create" | "management.configuration.assignment.replace" }>;
export interface ConfigurationCommandObservation {
  readonly disposition: "applied" | "rejected" | "conflict" | "notCommitted" | "unresolved";
  readonly family: ConfigurationCommandIntent["family"];
  readonly target: ConfigurationCommandIntent["target"];
  readonly key: string;
  readonly operation?: OperationReference;
  readonly originalCompletion?: string;
  readonly resultSource?: string;
  readonly decision?: ConfigurationDecision;
  readonly rejection?: OperationRejection;
  readonly attemptCompletion?: TransactionFacts;
  readonly code?: string;
  readonly transport: { readonly kind: string; readonly status?: number; readonly cleanupSucceeded: boolean;
    readonly mutationUncertain: boolean; readonly code?: string };
  /** Private same-original recovery observation; never supplied as native mutation authority. */
  readonly recovery?: { readonly state: "retained" | "conflicting" | "confirmed";
    readonly previousTransport: ConfigurationCommandObservation["transport"]; readonly previousAttemptCompletion?: TransactionFacts;
    readonly currentDisposition: ConfigurationCommandObservation["disposition"]; readonly currentOriginalCompletion?: string;
    readonly currentResultSource?: string };
}

/** Original native facts remain distinct from the current read or transport's cleanup. */
export function projectConfigurationCommand(call: ConfigurationCallResult<ConfigurationOperationResult>,
  intent: ConfigurationCommandIntent, key: string): ConfigurationCommandObservation {
  const target = captureTarget(intent);
  if (!isOperationKey(key)) { throw new TypeError("Supply the exact original operation key."); }
  const base = { family: intent.family, target, key, transport: Object.freeze({ kind: call.kind,
    ...(call.status === undefined ? {} : { status: call.status }), cleanupSucceeded: call.cleanupSucceeded,
    mutationUncertain: call.mutationUncertain, ...(call.code === undefined ? {} : { code: call.code }) }) };
  const original = call.value ?? call.problem?.result;
  const code = call.problem?.code ?? call.code;
  if (original === undefined) {
    return Object.freeze({ ...base, disposition: "unresolved", ...(code === undefined ? {} : { code }) });
  }
  if (original.operation.name !== intent.family || original.operation.key !== key
    || !scopeMatches(original.operation.scope, intent, original.result) || original.result !== undefined && !decisionMatches(original.result, intent)) {
    return Object.freeze({ ...base, disposition: "unresolved", code: "portal.operation.target_mismatch" });
  }
  const operation = Object.freeze({ name: original.operation.name, scope: original.operation.scope, key: original.operation.key });
  const decision = original.result === undefined ? undefined : projectDecision(original.result);
  const rejection = original.rejection === undefined ? undefined
    : Object.freeze({ code: original.rejection.code, message: original.rejection.message });
  const attempt = original.attemptCompletion === undefined ? undefined : Object.freeze({
    outcome: original.attemptCompletion.outcome, cleanupSucceeded: original.attemptCompletion.cleanupSucceeded,
    ...(original.attemptCompletion.cleanupCodes === undefined ? {} : { cleanupCodes: Object.freeze([...original.attemptCompletion.cleanupCodes]) }) });
  let disposition: ConfigurationCommandObservation["disposition"] = "unresolved";
  if (original.originalCompletion === "notCommitted") { disposition = "notCommitted"; }
  else if (original.originalCompletion === "committed" && ["prepared", "replayed", "found"].includes(original.resultSource)) {
    if (decision?.decision === "applied" && expectedKind(intent) === decision.kind) { disposition = "applied"; }
    else if (decision?.decision === "rejected" && expectedKind(intent) === decision.kind || rejection !== undefined) {
      const rejectionCode = decision?.kind === expectedKind(intent) ? decision.code : rejection?.code;
      disposition = rejectionCode === "management.configuration.precondition_failed" ? "conflict" : "rejected";
    }
  }
  return Object.freeze({ ...base, disposition, operation, originalCompletion: original.originalCompletion,
    resultSource: original.resultSource, ...(decision === undefined ? {} : { decision }),
    ...(rejection === undefined ? {} : { rejection }), ...(attempt === undefined ? {} : { attemptCompletion: attempt }),
    ...(code === undefined ? {} : { code }) });
}

export function resolvedConfigurationOriginal(value: ConfigurationCommandObservation | undefined): boolean {
  return value !== undefined && (value.originalCompletion === "committed" && ["applied", "rejected", "conflict"].includes(value.disposition)
    || value.originalCompletion === "notCommitted" && value.disposition === "notCommitted");
}
function captureTarget(intent: ConfigurationCommandIntent): ConfigurationCommandIntent["target"] {
  if (intent.family === "management.configuration.create") {
    if (!isUuid(intent.target.applicationId)) { throw new TypeError("Select an exact application."); }
    return Object.freeze({ applicationId: intent.target.applicationId });
  }
  if (intent.family === "management.configuration.source.publish") {
    if (!isUuid(intent.target.sourceId)) { throw new TypeError("Select an exact source."); }
    return Object.freeze({ sourceId: intent.target.sourceId });
  }
  if (!["management.configuration.release.create", "management.configuration.assignment.replace"].includes(intent.family)
    || !isUuid(intent.target.applicationId) || !isOperationKey(intent.target.configurationId) || !isOrderedInteger(intent.target.schemaVersion)) {
    throw new TypeError("Select an exact application, Configuration and schema.");
  }
  return Object.freeze({ applicationId: intent.target.applicationId, configurationId: intent.target.configurationId, schemaVersion: intent.target.schemaVersion });
}
function scopeMatches(scope: string, intent: ConfigurationCommandIntent, decision?: ConfigurationDecision): boolean {
  const prefix = "management.configuration.v1/" + intent.family + "/";
  if (!scope.startsWith(prefix)) { return false; }
  const parts = scope.slice(prefix.length).split("/");
  if (parts.length !== 2 || !isUuid(parts[0]) || !parts[1]!.startsWith("management.actor.v1.")
    || !/^[a-f0-9]{64}$/u.test(parts[1]!.slice("management.actor.v1.".length))) { return false; }
  // Binding lookups resolve and authorize the exact tuple natively, without a DefinitionsRead prefetch.
  if (intent.family === "management.configuration.source.publish") { return parts[0] === intent.target.sourceId; }
  if (intent.family === "management.configuration.create") { return parts[0] === intent.target.applicationId; }
  return intent.family !== "management.configuration.assignment.replace" || decision?.assignment === undefined
    || decision.assignment.bindingId === parts[0];
}
function expectedKind(intent: ConfigurationCommandIntent): string {
  return intent.family === "management.configuration.create" ? "configurationCreated" : intent.family === "management.configuration.source.publish" ? "sourcePublished"
    : intent.family === "management.configuration.release.create" ? "releaseCreated" : "assignmentsReplaced";
}
function decisionMatches(value: ConfigurationDecision, intent: ConfigurationCommandIntent): boolean {
  if (intent.family === "management.configuration.create") {
    if (value.applicationId !== undefined && value.applicationId !== intent.target.applicationId) { return false; }
    if (value.kind !== "configurationCreated") { return !["sourcePublished", "releaseCreated", "assignmentsReplaced", "catalogApplied"].includes(value.kind); }
    return value.applicationId === intent.target.applicationId && (value.decision !== "applied"
      || isOperationKey(value.configurationId) && value.schemaVersion === "1" && isUuid(value.sourceId) && isUuid(value.bindingId)
        && isScalarText(value.hostApplicationName, 255) && isStrongETag(value.sourceETag) && isStrongETag(value.eTag));
  }
  if (intent.family === "management.configuration.source.publish") {
    if (value.sourceId !== undefined && value.sourceId !== intent.target.sourceId) { return false; }
  } else if (value.applicationId !== undefined && value.applicationId !== intent.target.applicationId
    || value.configurationId !== undefined && value.configurationId !== intent.target.configurationId
    || value.schemaVersion !== undefined && value.schemaVersion !== intent.target.schemaVersion) { return false; }
  if (intent.family === "management.configuration.assignment.replace" && value.assignment !== undefined
    && (value.assignment.applicationId !== intent.target.applicationId || value.assignment.configurationId !== intent.target.configurationId
      || value.assignment.schemaVersion !== intent.target.schemaVersion)) { return false; }
  if (value.kind !== expectedKind(intent)) {
    return !["configurationCreated", "sourcePublished", "releaseCreated", "assignmentsReplaced", "catalogApplied"].includes(value.kind);
  }
  if (intent.family === "management.configuration.source.publish") { return value.sourceId === intent.target.sourceId; }
  return value.applicationId === intent.target.applicationId && value.configurationId === intent.target.configurationId
    && value.schemaVersion === intent.target.schemaVersion;
}
function projectDecision(value: ConfigurationDecision): ConfigurationDecision {
  const fields: readonly (keyof ConfigurationDecision)[] = value.kind === "configurationCreated"
    ? ["applicationId", "configurationId", "schemaVersion", "sourceId", "bindingId", "hostApplicationName", "sourceETag", "eTag"] : value.kind === "sourcePublished"
    ? ["sourceId", "publicationId", "sha256", "eTag"] : value.kind === "releaseCreated"
      ? ["applicationId", "configurationId", "schemaVersion", "releaseId", "eTag"] : value.kind === "assignmentsReplaced"
        ? ["applicationId", "configurationId", "schemaVersion", "generation", "eTag", "intent"] : [];
  const selected: Record<string, unknown> = { commandVersion: value.commandVersion, kind: value.kind, decision: value.decision,
    code: value.code, decidedAtUtc: value.decidedAtUtc };
  for (const name of fields) { if (value[name] !== undefined) { selected[name] = value[name]; } }
  if (value.kind === "assignmentsReplaced" && value.assignment !== undefined) {
    const map = value.assignment;
    selected.assignment = Object.freeze({ bindingId: map.bindingId, applicationId: map.applicationId,
      configurationId: map.configurationId, schemaVersion: map.schemaVersion, environment: map.environment, eTag: map.eTag,
      ...(map.defaultReleaseId === undefined ? {} : { defaultReleaseId: map.defaultReleaseId }),
      ...(map.generation === undefined ? {} : { generation: map.generation }),
      overrides: Object.freeze(map.overrides.map(item => Object.freeze({ cohortId: item.cohortId, releaseId: item.releaseId }))) });
  }
  return Object.freeze(selected) as unknown as ConfigurationDecision;
}
