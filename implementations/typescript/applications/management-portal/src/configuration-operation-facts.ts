import type { ConfigurationCommandObservation } from "./configuration-command-results.ts";
import { resolvedConfigurationOriginal } from "./configuration-command-results.ts";
import type { ConfigurationDecision } from "@cislib/management-client";

/** A weaker lookup cannot erase an already established business fact or turn it into a fresh successful confirmation. */
export function mergeConfigurationOperationFact(previous: ConfigurationCommandObservation | undefined,
  current: ConfigurationCommandObservation): ConfigurationCommandObservation {
  if (previous === undefined) { return current; }
  if (!sameIdentity(previous, current)) { throw new TypeError("The lookup belongs to a different original Configuration operation."); }
  if (!resolvedConfigurationOriginal(previous)) { return current; }
  const resolved = resolvedConfigurationOriginal(current);
  const matching = resolved && sameBusiness(previous, current);
  const confirmed = matching && ["found", "replayed"].includes(current.resultSource ?? "");
  const prior = previous.recovery;
  const recovery = Object.freeze({ state: confirmed ? "confirmed" as const : resolved && !matching ? "conflicting" as const : "retained" as const,
    previousTransport: prior?.previousTransport ?? previous.transport,
    ...((prior?.previousAttemptCompletion ?? previous.attemptCompletion) === undefined ? {}
      : { previousAttemptCompletion: prior?.previousAttemptCompletion ?? previous.attemptCompletion }),
    currentDisposition: current.disposition,
    ...(current.originalCompletion === undefined ? {} : { currentOriginalCompletion: current.originalCompletion }),
    ...(current.resultSource === undefined ? {} : { currentResultSource: current.resultSource }) });
  if (confirmed) { return Object.freeze({ ...current, recovery }); }
  const { attemptCompletion: _oldAttempt, code: _oldCode, ...known } = previous;
  return Object.freeze({ ...known, transport: current.transport,
    ...(current.attemptCompletion === undefined ? {} : { attemptCompletion: current.attemptCompletion }),
    ...(current.code === undefined ? {} : { code: current.code }), recovery });
}

/** Advancement needs the actual matching current confirmation, not merely a retained old applied fact plus a clean weak HTTP200. */
export function configurationOperationCanAdvance(value: ConfigurationCommandObservation | undefined): boolean {
  return value?.disposition === "applied" && value.originalCompletion === "committed"
    && (value.recovery === undefined || value.recovery.state === "confirmed")
    && value.transport.kind === "http" && value.transport.status === 200 && value.transport.cleanupSucceeded
    && !value.transport.mutationUncertain && value.attemptCompletion?.cleanupSucceeded !== false;
}
function sameIdentity(left: ConfigurationCommandObservation, right: ConfigurationCommandObservation): boolean {
  const keys = Object.keys(left.target);
  return left.family === right.family && left.key === right.key && keys.length === Object.keys(right.target).length
    && keys.every(key => Reflect.get(left.target, key) === Reflect.get(right.target, key));
}
function sameBusiness(left: ConfigurationCommandObservation, right: ConfigurationCommandObservation): boolean {
  return left.disposition === right.disposition && left.originalCompletion === right.originalCompletion
    && left.operation?.name === right.operation?.name && left.operation?.scope === right.operation?.scope && left.operation?.key === right.operation?.key
    && left.rejection?.code === right.rejection?.code && left.rejection?.message === right.rejection?.message
    && sameDecision(left.decision, right.decision);
}
function sameDecision(left: ConfigurationDecision | undefined, right: ConfigurationDecision | undefined): boolean {
  if (left === undefined || right === undefined) { return left === right; }
  const fields: readonly (keyof ConfigurationDecision)[] = ["commandVersion", "kind", "decision", "code", "decidedAtUtc", "applicationId", "configurationId",
    "schemaVersion", "sourceId", "publicationId", "sha256", "releaseId", "generation", "eTag", "intent", "bindingId", "hostApplicationName", "sourceETag"];
  if (!fields.every(field => left[field] === right[field])) { return false; }
  const a = left.assignment, b = right.assignment;
  if (a === undefined || b === undefined) { return a === b; }
  return a.bindingId === b.bindingId && a.applicationId === b.applicationId && a.configurationId === b.configurationId && a.schemaVersion === b.schemaVersion
    && a.environment === b.environment && a.eTag === b.eTag && a.generation === b.generation && a.defaultReleaseId === b.defaultReleaseId
    && a.overrides.length === b.overrides.length && a.overrides.every(row => b.overrides.some(other => other.cohortId === row.cohortId && other.releaseId === row.releaseId));
}
