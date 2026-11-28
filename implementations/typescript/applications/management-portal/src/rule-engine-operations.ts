import type { RuleCounterResetOperationResult, RuleDeploymentOperationResult, RuleEngineCallResult, RuleVersionSaveResult } from "@cislib/management-client";

export type RuleOperationFamily = "management.rule-engine.version.save" | "management.rule-engine.deployment.request" | "management.rule-engine.counter.reset";
export interface RuleOperationTarget { readonly applicationId: string; readonly rulesetId: string; }
export interface RuleOperationObservation {
  readonly family: RuleOperationFamily;
  readonly target: RuleOperationTarget;
  readonly key: string;
  readonly disposition: "committed" | "rejected" | "conflict" | "notCommitted" | "unresolved";
  readonly code?: string;
  readonly version?: string;
  readonly cleanupSucceeded: boolean;
  readonly requestedAtUtc?: string;
  readonly counterReset?: { readonly selection: "one" | "selected" | "all"; readonly resetAtUtc: string; readonly appliedCount: string; readonly rejectedCount: string };
}
type NativeResult = RuleVersionSaveResult | RuleDeploymentOperationResult | RuleCounterResetOperationResult;

export function projectRuleOperation(call: RuleEngineCallResult<NativeResult>, family: RuleOperationFamily, target: RuleOperationTarget,
  key: string): RuleOperationObservation {
  const problem = family === "management.rule-engine.version.save" ? call.problem?.result
    : family === "management.rule-engine.deployment.request" ? call.problem?.deploymentResult : call.problem?.counterResetResult;
  const original = call.value ?? problem;
  const code = original?.rejection?.code ?? call.problem?.code ?? call.code;
  const base = { family, target, key, cleanupSucceeded: call.cleanupSucceeded && original?.attemptCompletion?.cleanupSucceeded !== false,
    ...(code === undefined ? {} : { code }) };
  if (original === undefined || original.applicationId !== target.applicationId || original.rulesetId !== target.rulesetId
    || original.operation.name !== family || original.operation.key !== key) {
    return { ...base, disposition: "unresolved" };
  }
  if (original.originalCompletion === "notCommitted") { return { ...base, disposition: "notCommitted" }; }
  if (original.originalCompletion !== "committed" || !["found", "prepared", "replayed"].includes(original.resultSource)) {
    return { ...base, disposition: "unresolved" };
  }
  if (original.rejection !== undefined) {
    return { ...base, disposition: code === "management.rule-engine.precondition_failed" || code === "management.rule-engine.schema_changed" ? "conflict" : "rejected" };
  }
  const decision = original.result;
  if (decision === undefined) { return { ...base, disposition: "unresolved" }; }
  if ("selection" in decision) {
    return { ...base, disposition: "committed", counterReset: { selection: decision.selection,
      resetAtUtc: decision.resetAtUtc, appliedCount: decision.appliedCount, rejectedCount: decision.rejectedCount } };
  }
  return "desired" in decision ? { ...base, disposition: "committed", version: decision.desired.version, requestedAtUtc: decision.requestedAtUtc }
    : { ...base, disposition: "committed", version: decision.version };
}
