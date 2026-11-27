import { isOperationKey, isOrderedInteger, isScalarText, isStrongETag, isUuid } from "@cislib/management-client";
import type { ConfigurationAssignmentCommand, ConfigurationAssignmentMap, ConfigurationCohortChoice, ConfigurationOverride,
  ConfigurationPreview, ConfigurationRelease } from "@cislib/management-client";
import { captureConfigurationReason } from "./configuration-draft.ts";
import { projectConfigurationAssignment, projectConfigurationPreview } from "./configuration-editor-model.ts";

export interface ConfigurationAssignmentDraft {
  readonly baseline: ConfigurationAssignmentMap;
  readonly intent: ConfigurationAssignmentCommand["intent"];
  readonly defaultReleaseId?: string;
  readonly overrides: readonly ConfigurationOverride[];
  readonly reason: string;
}
export interface ConfigurationAssignmentRow {
  readonly kind: "default" | "cohort";
  readonly cohortId?: string;
  readonly beforeReleaseId?: string;
  readonly afterReleaseId?: string;
  readonly change: "added" | "removed" | "retained" | "changed";
}
export interface ConfigurationAssignmentReview {
  readonly baseline: ConfigurationAssignmentMap;
  readonly command: ConfigurationAssignmentCommand;
  readonly preview: ConfigurationPreview;
  readonly rows: readonly ConfigurationAssignmentRow[];
}
export class ConfigurationAssignmentReviewError extends Error {
  readonly code: "map-changed" | "scope-mismatch" | "proposal-mismatch" | "structural-refusal";
  constructor(code: ConfigurationAssignmentReviewError["code"]) {
    super(code === "map-changed" ? "The complete current assignment map changed. Compare it and deliberately review again."
      : code === "scope-mismatch" ? "The preview does not belong to this exact Configuration binding."
        : code === "proposal-mismatch" ? "The native preview does not echo this complete assignment intent."
          : "The native preview did not establish structural eligibility for this assignment.");
    this.name = "ConfigurationAssignmentReviewError";
    this.code = code;
  }
}

/** A whole-map draft starts with every actual pin, including pins whose independent metadata is unavailable. */
export function beginConfigurationAssignment(map: ConfigurationAssignmentMap): ConfigurationAssignmentDraft {
  const baseline = captureMap(map);
  return captureDraft({ baseline, intent: "replace", reason: "", overrides: baseline.overrides,
    ...(baseline.defaultReleaseId === undefined ? {} : { defaultReleaseId: baseline.defaultReleaseId }) });
}
export function setConfigurationAssignmentIntent(draft: ConfigurationAssignmentDraft,
  intent: ConfigurationAssignmentCommand["intent"]): ConfigurationAssignmentDraft {
  return captureDraft({ ...captureDraft(draft), intent: knownIntent(intent) });
}
/** Incomplete reason text is memory-only; command/review admission applies the exact native nonempty reason rule. */
export function setConfigurationAssignmentReason(draft: ConfigurationAssignmentDraft, raw: string): ConfigurationAssignmentDraft {
  return captureDraft({ ...captureDraft(draft), reason: raw });
}
/** Callers supply an actually authorized exact release read, never a guessed latest/current identifier. */
export function chooseConfigurationAssignmentDefault(draft: ConfigurationAssignmentDraft,
  release: ConfigurationRelease): ConfigurationAssignmentDraft {
  const captured = captureDraft(draft);
  return captureDraft({ ...captured, defaultReleaseId: releaseId(captured.baseline, release) });
}
/** A choice comes from the bounded native cohort read or an existing authorized map pin, not editable membership. */
export function chooseConfigurationAssignmentOverride(draft: ConfigurationAssignmentDraft,
  cohort: ConfigurationCohortChoice, release: ConfigurationRelease): ConfigurationAssignmentDraft {
  const captured = captureDraft(draft);
  requireValue(cohort !== null && typeof cohort === "object" && isOperationKey(cohort.cohortId)
    && isScalarText(cohort.displayName, 256), "Select one actual bounded native cohort choice.");
  const selected = releaseId(captured.baseline, release);
  const existing = captured.overrides.some(item => item.cohortId === cohort.cohortId);
  requireValue(existing || captured.overrides.length < 16, "A complete assignment supports at most sixteen cohort pins.");
  const overrides = captured.overrides.filter(item => item.cohortId !== cohort.cohortId);
  overrides.push({ cohortId: cohort.cohortId, releaseId: selected });
  return captureDraft({ ...captured, overrides });
}
/** Removal is explicit and changes no other cohort or default; the removed target will inherit the proposed default. */
export function removeConfigurationAssignmentOverride(draft: ConfigurationAssignmentDraft, cohortId: string): ConfigurationAssignmentDraft {
  requireValue(isOperationKey(cohortId), "Select one exact cohort pin.");
  const captured = captureDraft(draft);
  return captureDraft({ ...captured, overrides: captured.overrides.filter(item => item.cohortId !== cohortId) });
}
export function configurationAssignmentCommand(draft: ConfigurationAssignmentDraft): ConfigurationAssignmentCommand {
  const captured = captureDraft(draft);
  requireValue(isUuid(captured.defaultReleaseId), "Select an explicit default release before reviewing the complete map.");
  return Object.freeze({ commandVersion: 1, intent: captured.intent, defaultReleaseId: captured.defaultReleaseId!,
    overrides: sortedOverrides(captured.overrides), reason: captureConfigurationReason(captured.reason) });
}
/** Every current/proposed target remains visible, including retained unrelated pins and deliberately removed canaries. */
export function configurationAssignmentRows(draft: ConfigurationAssignmentDraft): readonly ConfigurationAssignmentRow[] {
  const captured = captureDraft(draft);
  const before = new Map(captured.baseline.overrides.map(item => [item.cohortId, item.releaseId]));
  const after = new Map(captured.overrides.map(item => [item.cohortId, item.releaseId]));
  const rows: ConfigurationAssignmentRow[] = [row("default", captured.baseline.defaultReleaseId, captured.defaultReleaseId)];
  for (const cohortId of [...new Set([...before.keys(), ...after.keys()])].sort(ordinal)) {
    rows.push(row("cohort", before.get(cohortId), after.get(cohortId), cohortId));
  }
  return Object.freeze(rows);
}
/** Exact tentative generation arithmetic only; native authorization, reference validity and commitment still decide the outcome. */
export function nextConfigurationAssignmentGeneration(map: ConfigurationAssignmentMap): string | undefined {
  const captured = captureMap(map);
  const current = BigInt(captured.generation ?? "0");
  return current === 9223372036854775807n ? undefined : (current + 1n).toString();
}

/** Native preview is authoritative for eligibility, but must match the frozen base and the complete intended command. */
export function buildConfigurationAssignmentReview(draft: ConfigurationAssignmentDraft,
  preview: ConfigurationPreview): ConfigurationAssignmentReview {
  const captured = captureDraft(draft);
  const baseline = captured.baseline;
  const command = configurationAssignmentCommand(captured);
  if (preview === null || typeof preview !== "object" || preview.applicationId !== baseline.applicationId
    || preview.configurationId !== baseline.configurationId || preview.schemaVersion !== baseline.schemaVersion
    || preview.currentAssignment?.bindingId !== baseline.bindingId
    || preview.currentAssignment.applicationId !== baseline.applicationId || preview.currentAssignment.configurationId !== baseline.configurationId
    || preview.currentAssignment.schemaVersion !== baseline.schemaVersion || preview.currentAssignment.environment !== baseline.environment) {
    throw new ConfigurationAssignmentReviewError("scope-mismatch");
  }
  let current: ConfigurationAssignmentMap;
  try { current = captureMap(preview.currentAssignment); }
  catch { throw new ConfigurationAssignmentReviewError("scope-mismatch"); }
  if (!sameMap(baseline, current)) { throw new ConfigurationAssignmentReviewError("map-changed"); }
  if (preview.commonSource !== undefined || preview.applicationSource !== undefined || preview.proposedAssignment === undefined
    || !sameCommand(command, preview.proposedAssignment)) {
    throw new ConfigurationAssignmentReviewError("proposal-mismatch");
  }
  if (preview.structurallyValid !== true || !Array.isArray(preview.issues) || preview.issues.length !== 0
    || typeof preview.redacted !== "boolean") {
    throw new ConfigurationAssignmentReviewError("structural-refusal");
  }
  // Redacted assignment preview still carries permitted full map metadata; it conveys no source-value authority.
  return Object.freeze({ baseline, command, preview: projectConfigurationPreview(preview), rows: configurationAssignmentRows(captured) });
}

function captureDraft(value: ConfigurationAssignmentDraft): ConfigurationAssignmentDraft {
  requireValue(value !== null && typeof value === "object", "Supply one complete bounded assignment draft.");
  const baseline = captureMap(value.baseline);
  const intent = knownIntent(value.intent);
  requireValue(value.defaultReleaseId === undefined || isUuid(value.defaultReleaseId), "Select a canonical default release identifier.");
  requireValue(isScalarText(value.reason, 1024, true) && new TextEncoder().encode(value.reason).length <= 1024,
    "Keep the exact reason within 1,024 UTF-8 bytes without controls.");
  return Object.freeze({ baseline, intent, reason: value.reason, overrides: captureOverrides(value.overrides),
    ...(value.defaultReleaseId === undefined ? {} : { defaultReleaseId: value.defaultReleaseId }) });
}
function captureMap(value: ConfigurationAssignmentMap): ConfigurationAssignmentMap {
  requireValue(value !== null && typeof value === "object" && isUuid(value.bindingId) && isUuid(value.applicationId)
    && isOperationKey(value.configurationId) && isOrderedInteger(value.schemaVersion) && isScalarText(value.environment, 255)
    && isStrongETag(value.eTag), "Supply the actual complete bounded native assignment map.");
  const overrides = captureOverrides(value.overrides);
  requireValue(value.generation === undefined ? value.defaultReleaseId === undefined && overrides.length === 0
    : isOrderedInteger(value.generation) && isUuid(value.defaultReleaseId), "Keep native unassigned/default/generation facts coherent.");
  return projectConfigurationAssignment({ ...value, overrides });
}
function captureOverrides(value: readonly ConfigurationOverride[]): readonly ConfigurationOverride[] {
  requireValue(Array.isArray(value) && value.length <= 16, "A complete assignment supports at most sixteen cohort pins.");
  const seen = new Set<string>();
  return Object.freeze(value.map(item => {
    requireValue(item !== null && typeof item === "object" && isOperationKey(item.cohortId) && isUuid(item.releaseId)
      && !seen.has(item.cohortId), "Supply unique exact native cohort/release pins.");
    seen.add(item.cohortId);
    return Object.freeze({ cohortId: item.cohortId, releaseId: item.releaseId });
  }));
}
function releaseId(map: ConfigurationAssignmentMap, release: ConfigurationRelease): string {
  requireValue(release !== null && typeof release === "object" && isUuid(release.releaseId)
    && release.applicationId === map.applicationId && release.configurationId === map.configurationId
    && release.schemaVersion === map.schemaVersion && release.environment === map.environment,
  "Select an actual retained release for the exact application, Configuration, schema and environment.");
  return release.releaseId;
}
function knownIntent(value: string): ConfigurationAssignmentCommand["intent"] {
  requireValue(value === "replace" || value === "promote" || value === "rollback", "Select a supported explicit assignment intent.");
  return value;
}
function sortedOverrides(value: readonly ConfigurationOverride[]): readonly ConfigurationOverride[] {
  return Object.freeze([...value].sort((left, right) => ordinal(left.cohortId, right.cohortId)));
}
function sameMap(left: ConfigurationAssignmentMap, right: ConfigurationAssignmentMap): boolean {
  return left.bindingId === right.bindingId && left.applicationId === right.applicationId && left.configurationId === right.configurationId
    && left.schemaVersion === right.schemaVersion && left.environment === right.environment && left.eTag === right.eTag
    && left.generation === right.generation && left.defaultReleaseId === right.defaultReleaseId && sameOverrides(left.overrides, right.overrides);
}
function sameCommand(left: ConfigurationAssignmentCommand, right: ConfigurationPreview["proposedAssignment"]): boolean {
  if (right === undefined || right.commandVersion !== 1 || right.intent !== left.intent || right.defaultReleaseId !== left.defaultReleaseId
    || right.reason !== left.reason) { return false; }
  try { return sameOverrides(left.overrides, captureOverrides(right.overrides)); }
  catch { return false; }
}
function sameOverrides(left: readonly ConfigurationOverride[], right: readonly ConfigurationOverride[]): boolean {
  const first = sortedOverrides(left), second = sortedOverrides(right);
  return first.length === second.length && first.every((item, index) => item.cohortId === second[index]!.cohortId && item.releaseId === second[index]!.releaseId);
}
function row(kind: ConfigurationAssignmentRow["kind"], before: string | undefined, after: string | undefined,
  cohortId?: string): ConfigurationAssignmentRow {
  return Object.freeze({ kind, ...(cohortId === undefined ? {} : { cohortId }),
    ...(before === undefined ? {} : { beforeReleaseId: before }), ...(after === undefined ? {} : { afterReleaseId: after }),
    change: before === after ? "retained" : before === undefined ? "added" : after === undefined ? "removed" : "changed" });
}
function ordinal(left: string, right: string): number { return left < right ? -1 : left > right ? 1 : 0; }
function requireValue(condition: boolean, message: string): asserts condition { if (!condition) { throw new TypeError(message); } }
