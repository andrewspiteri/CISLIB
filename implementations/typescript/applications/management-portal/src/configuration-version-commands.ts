import type { ConfigurationAssignmentCommand, ConfigurationAssignmentMap, ConfigurationCandidatePreview, ConfigurationCandidatePreviewCommand,
  ConfigurationPreview, ConfigurationPreviewCommand, ConfigurationPublicationCommand, ConfigurationRelease, ConfigurationReleaseCommand, ConfigurationSourceReference } from "@cislib/management-client";
import { computeConfigurationPayloadSha256, isStrongETag, isUuid } from "@cislib/management-client";
import type { ConfigurationEditorSnapshot } from "./configuration-editor-model.ts";
import { projectConfigurationAssignment, projectConfigurationReference } from "./configuration-editor-model.ts";
import { captureConfigurationDraft, captureConfigurationReason } from "./configuration-draft.ts";
import type { ConfigurationCommandObservation } from "./configuration-command-results.ts";
import { configurationOperationCanAdvance } from "./configuration-operation-facts.ts";
import { beginConfigurationAssignment, buildConfigurationAssignmentReview, chooseConfigurationAssignmentDefault,
  removeConfigurationAssignmentOverride, setConfigurationAssignmentReason } from "./configuration-assignment-model.ts";
import type { ConfigurationAssignmentReview } from "./configuration-assignment-model.ts";
import { configurationVersionMatches } from "./configuration-versions.ts";

export interface ConfigurationVersionSaveIntent {
  readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string; readonly environment: string;
  readonly bindingId: string; readonly sourceId: string; readonly hostApplicationName: string; readonly sessionKey: string;
  readonly raw: string; readonly note: string; readonly sha256: string; readonly sourceETag: string; readonly assignmentETag: string;
  readonly commonSource: ConfigurationSourceReference; readonly applicationSource: ConfigurationSourceReference;
  readonly baseline: ConfigurationAssignmentMap;
}

/** Capture one visible Save click. This owns bytes/intent, not credentials, receipts, transport or another lifetime. */
export async function captureConfigurationVersionSave(editor: ConfigurationEditorSnapshot, raw: string, note: string, sessionKey: string,
  base: ConfigurationRelease | "application-only", signal?: AbortSignal): Promise<ConfigurationVersionSaveIntent> {
  const context = editor.context, source = editor.source, map = editor.assignment;
  if (context.selectedSource !== "application" || context.applicationSource.state !== "available" || source === undefined
    || source.sourceId !== context.applicationSource.sourceId || source.kind !== "application" || source.applicationId !== context.applicationId
    || source.configurationId !== context.configurationId || source.schemaVersion !== context.schemaVersion || source.environment !== context.environment
    || context.defaults.state !== "available" || !isStrongETag(source.eTag) || map === undefined || map.bindingId !== context.bindingId
    || !configurationVersionMatches(map, context.applicationId, context.configurationId, context.schemaVersion, context.environment)
    || !allowed(editor, "source", "publishConfigurationSource") || !allowed(editor, "source", "validateConfiguration")
    || !allowed(editor, "binding", "previewConfiguration") || !allowed(editor, "binding", "createConfigurationRelease") || sessionKey.length === 0) {
    throw new TypeError("Refresh the exact Application source and Save permissions before saving a version.");
  }
  if (base !== "application-only" && !configurationVersionMatches(base, context.applicationId, context.configurationId, context.schemaVersion, context.environment)) {
    throw new TypeError("The saved base belongs to another Configuration or schema.");
  }
  const captured = captureConfigurationDraft(raw), reason = captureConfigurationReason(note);
  const baseline = beginConfigurationAssignment(map).baseline;
  signal?.throwIfAborted();
  const sha256 = await computeConfigurationPayloadSha256(captured, {}, signal);
  signal?.throwIfAborted();
  return Object.freeze({ applicationId: context.applicationId, configurationId: context.configurationId, schemaVersion: context.schemaVersion,
    environment: context.environment, bindingId: context.bindingId, sourceId: source.sourceId, hostApplicationName: context.hostApplicationName,
    sessionKey, raw: captured, note: reason, sha256, sourceETag: source.eTag, assignmentETag: map.eTag,
    commonSource: projectConfigurationReference(base === "application-only" ? { state: "absent" } : base.commonSource),
    applicationSource: projectConfigurationReference(base === "application-only" ? { state: "absent" } : base.applicationSource), baseline });
}

export function configurationVersionSaveMatches(intent: ConfigurationVersionSaveIntent, editor: ConfigurationEditorSnapshot | undefined,
  sessionKey: string | undefined, phase: "publication" | "release" = "publication"): boolean {
  const context = editor?.context;
  return editor !== undefined && context !== undefined && intent.sessionKey === sessionKey && context.selectedSource === "application"
    && context.applicationId === intent.applicationId && context.configurationId === intent.configurationId && context.schemaVersion === intent.schemaVersion
    && context.environment === intent.environment && context.bindingId === intent.bindingId && context.hostApplicationName === intent.hostApplicationName
    && context.applicationSource.state === "available" && context.applicationSource.sourceId === intent.sourceId
    && editor.source?.sourceId === intent.sourceId && sameMap(intent.baseline, editor.assignment)
    && (phase === "release" || editor.source.eTag === intent.sourceETag)
    && allowed(editor, "binding", "createConfigurationRelease") && allowed(editor, "binding", "previewConfiguration")
    && (phase === "release" || allowed(editor, "source", "publishConfigurationSource") && allowed(editor, "source", "validateConfiguration"));
}
export function configurationVersionSaveCandidate(intent: ConfigurationVersionSaveIntent): ConfigurationCandidatePreviewCommand {
  return Object.freeze({ commandVersion: 1, applicationId: intent.applicationId, configurationId: intent.configurationId, schemaVersion: intent.schemaVersion,
    selectedSource: "application", sourceId: intent.sourceId, sourceETag: intent.sourceETag, assignmentETag: intent.assignmentETag,
    commonSource: intent.commonSource, applicationSource: intent.applicationSource, payloadJson: intent.raw });
}
export function acceptConfigurationVersionSavePreview(intent: ConfigurationVersionSaveIntent, value: ConfigurationCandidatePreview): void {
  if (!value.structurallyValid || value.redacted || value.issues.length !== 0 || value.applicationId !== intent.applicationId || value.configurationId !== intent.configurationId
    || value.schemaVersion !== intent.schemaVersion || value.environment !== intent.environment || value.bindingId !== intent.bindingId
    || value.hostApplicationName !== intent.hostApplicationName || value.selectedSource !== "application" || value.sourceId !== intent.sourceId
    || value.sourceETag !== intent.sourceETag || value.assignmentETag !== intent.assignmentETag || value.draftSha256 !== intent.sha256
    || !sameReference(value.commonSource, intent.commonSource) || !sameReference(value.applicationSource, intent.applicationSource)
    || !sameMap(intent.baseline, value.currentAssignment)) { throw new TypeError("The native preview did not confirm this exact saved-version intent."); }
}
export function configurationVersionPublicationCommand(intent: ConfigurationVersionSaveIntent): ConfigurationPublicationCommand {
  return Object.freeze({ commandVersion: 1, payloadJson: intent.raw, reason: intent.note });
}
export function configurationVersionReleaseCommand(intent: ConfigurationVersionSaveIntent, publication: ConfigurationCommandObservation): ConfigurationReleaseCommand {
  if (!knownPublication(intent, publication) || !cleanObservation(publication)) {
    throw new TypeError("Recover the exact committed publication and its cleanup before continuing this Save.");
  }
  return Object.freeze({ commandVersion: 1, commonSource: intent.commonSource,
    applicationSource: Object.freeze({ state: "present", publicationId: publication.decision!.publicationId! }), reason: intent.note });
}

/** Caller must first dismiss this exact resolved unsuccessful release receipt. Unknown outcomes never permit a replacement key. */
export function rebindConfigurationVersionRelease(intent: ConfigurationVersionSaveIntent, editor: ConfigurationEditorSnapshot, sessionKey: string,
  publication: ConfigurationCommandObservation, dismissedRelease: ConfigurationCommandObservation, dismissedKey: string): ConfigurationVersionSaveIntent {
  const context = editor.context, map = editor.assignment;
  const closed = dismissedRelease.originalCompletion === "committed" && ["rejected", "conflict"].includes(dismissedRelease.disposition)
    || dismissedRelease.originalCompletion === "notCommitted" && dismissedRelease.disposition === "notCommitted";
  if (!closed || dismissedRelease.recovery?.state === "conflicting" || dismissedRelease.key !== dismissedKey || dismissedRelease.family !== "management.configuration.release.create"
    || !("applicationId" in dismissedRelease.target) || !("configurationId" in dismissedRelease.target) || !("schemaVersion" in dismissedRelease.target)
    || dismissedRelease.target.applicationId !== intent.applicationId || dismissedRelease.target.configurationId !== intent.configurationId
    || dismissedRelease.target.schemaVersion !== intent.schemaVersion || !knownPublication(intent, publication) || !cleanObservation(publication)
    || sessionKey !== intent.sessionKey || context.selectedSource !== "application" || context.applicationId !== intent.applicationId
    || context.configurationId !== intent.configurationId || context.schemaVersion !== intent.schemaVersion || context.environment !== intent.environment
    || context.bindingId !== intent.bindingId || context.hostApplicationName !== intent.hostApplicationName
    || context.applicationSource.state !== "available" || context.applicationSource.sourceId !== intent.sourceId || editor.source?.sourceId !== intent.sourceId
    || map === undefined || map.bindingId !== intent.bindingId || !configurationVersionMatches(map, intent.applicationId, intent.configurationId, intent.schemaVersion, intent.environment)
    || !allowed(editor, "binding", "previewConfiguration") || !allowed(editor, "binding", "createConfigurationRelease")) {
    throw new TypeError("Resolve and dismiss the exact unsuccessful release, then refresh this same Save's current release permissions and map.");
  }
  const baseline = beginConfigurationAssignment(map).baseline;
  return Object.freeze({ ...intent, baseline, assignmentETag: baseline.eTag });
}
export function configurationVersionReleasePreview(intent: ConfigurationVersionSaveIntent, publication: ConfigurationCommandObservation): ConfigurationPreviewCommand {
  const command = configurationVersionReleaseCommand(intent, publication);
  return Object.freeze({ commandVersion: 1, applicationId: intent.applicationId, configurationId: intent.configurationId, schemaVersion: intent.schemaVersion,
    commonSource: command.commonSource, applicationSource: command.applicationSource });
}
export function acceptConfigurationVersionReleasePreview(intent: ConfigurationVersionSaveIntent, publication: ConfigurationCommandObservation,
  value: ConfigurationPreview): void {
  const command = configurationVersionReleaseCommand(intent, publication);
  if (!value.structurallyValid || value.issues.length !== 0 || value.applicationId !== intent.applicationId
    || value.configurationId !== intent.configurationId || value.schemaVersion !== intent.schemaVersion
    || !sameMap(intent.baseline, value.currentAssignment) || !sameReference(value.commonSource, command.commonSource)
    || !sameReference(value.applicationSource, command.applicationSource) || value.proposedAssignment !== undefined) {
    throw new TypeError("The native preview did not confirm this exact remaining source pair and fresh assignment map.");
  }
}
export function configurationVersionSavedReleaseId(intent: ConfigurationVersionSaveIntent, publication: ConfigurationCommandObservation | undefined,
  release: ConfigurationCommandObservation | undefined): string | undefined {
  return publication !== undefined && knownPublication(intent, publication) && release?.family === "management.configuration.release.create"
    && "applicationId" in release.target && "configurationId" in release.target && "schemaVersion" in release.target
    && release.target.applicationId === intent.applicationId && release.target.configurationId === intent.configurationId && release.target.schemaVersion === intent.schemaVersion
    && release.disposition === "applied" && release.originalCompletion === "committed" && release.decision?.kind === "releaseCreated"
    && release.decision.applicationId === intent.applicationId && release.decision.configurationId === intent.configurationId
    && release.decision.schemaVersion === intent.schemaVersion && isUuid(release.decision.releaseId) ? release.decision.releaseId : undefined;
}

/** Prepare the explicit all-instance deployment: every actual existing cohort pin is deliberately removed. */
export function configurationVersionDeployCommand(map: ConfigurationAssignmentMap, version: ConfigurationRelease, note: string): ConfigurationAssignmentCommand {
  let draft = chooseConfigurationAssignmentDefault(beginConfigurationAssignment(map), version);
  for (const row of map.overrides) { draft = removeConfigurationAssignmentOverride(draft, row.cohortId); }
  draft = setConfigurationAssignmentReason(draft, captureConfigurationReason(note));
  return Object.freeze({ commandVersion: 1, intent: "replace", defaultReleaseId: version.releaseId, overrides: Object.freeze([]), reason: draft.reason });
}
export function reviewConfigurationVersionDeploy(map: ConfigurationAssignmentMap, version: ConfigurationRelease, note: string,
  preview: ConfigurationPreview): ConfigurationAssignmentReview {
  const command = configurationVersionDeployCommand(map, version, note);
  let draft = chooseConfigurationAssignmentDefault(beginConfigurationAssignment(map), version);
  for (const row of map.overrides) { draft = removeConfigurationAssignmentOverride(draft, row.cohortId); }
  return buildConfigurationAssignmentReview(setConfigurationAssignmentReason(draft, command.reason), preview);
}

export interface ConfigurationVersionSavePorts {
  readonly isCurrent: (phase: "publication" | "release") => boolean;
  readonly preview: (command: ConfigurationCandidatePreviewCommand) => Promise<ConfigurationCandidatePreview>;
  readonly publish: (command: ConfigurationPublicationCommand, sourceETag: string) => Promise<ConfigurationCommandObservation>;
  readonly release: (command: ConfigurationReleaseCommand, assignmentETag: string) => Promise<ConfigurationCommandObservation>;
}
export interface ConfigurationVersionSaveProgress {
  readonly stage: "preview" | "publication" | "release" | "saved";
  readonly publication?: ConfigurationCommandObservation;
  readonly release?: ConfigurationCommandObservation;
  readonly savedReleaseId?: string;
}
export class ConfigurationVersionSaveInterrupted extends Error {
  readonly progress: ConfigurationVersionSaveProgress;
  constructor(progress: ConfigurationVersionSaveProgress, cause: unknown) {
    super("The Save did not confirm its remaining stage. Keep its exact original references.", { cause });
    this.name = "ConfigurationVersionSaveInterrupted"; this.progress = progress;
  }
}

/** Fixed Save sequence only. Caller ports retain each original before dispatch and own actual request/cleanup lifetimes. */
export async function executeConfigurationVersionSave(intent: ConfigurationVersionSaveIntent, ports: ConfigurationVersionSavePorts,
  knownPublicationResult?: ConfigurationCommandObservation): Promise<ConfigurationVersionSaveProgress> {
  let progress: ConfigurationVersionSaveProgress = Object.freeze({ stage: knownPublicationResult === undefined ? "preview" : "publication",
    ...(knownPublicationResult === undefined ? {} : { publication: knownPublicationResult }) });
  try {
    if (knownPublicationResult === undefined) {
      if (!ports.isCurrent("publication")) { return progress; }
      const preview = await ports.preview(configurationVersionSaveCandidate(intent));
      acceptConfigurationVersionSavePreview(intent, preview);
      if (!ports.isCurrent("publication")) { return progress; }
      progress = Object.freeze({ stage: "publication" });
      const publication = await ports.publish(configurationVersionPublicationCommand(intent), intent.sourceETag);
      progress = Object.freeze({ stage: "publication", publication });
    }
    const publication = progress.publication;
    if (publication === undefined || !knownPublication(intent, publication) || !cleanObservation(publication)) { return progress; }
    progress = Object.freeze({ stage: "release", publication });
    if (!ports.isCurrent("release")) { return progress; }
    const release = await ports.release(configurationVersionReleaseCommand(intent, publication), intent.assignmentETag);
    if (!ports.isCurrent("release")) { return Object.freeze({ stage: "release", publication, release }); }
    const savedReleaseId = configurationVersionSavedReleaseId(intent, publication, release);
    progress = Object.freeze({ stage: savedReleaseId === undefined ? "release" : "saved", publication, release,
      ...(savedReleaseId === undefined ? {} : { savedReleaseId }) });
    return progress;
  } catch (cause) { throw new ConfigurationVersionSaveInterrupted(progress, cause); }
}

function knownPublication(intent: ConfigurationVersionSaveIntent, value: ConfigurationCommandObservation): boolean {
  return value.family === "management.configuration.source.publish" && "sourceId" in value.target && value.target.sourceId === intent.sourceId
    && value.disposition === "applied" && value.originalCompletion === "committed" && value.decision?.kind === "sourcePublished"
    && value.decision.sourceId === intent.sourceId && value.decision.sha256 === intent.sha256 && isUuid(value.decision.publicationId);
}
function cleanObservation(value: ConfigurationCommandObservation): boolean {
  return configurationOperationCanAdvance(value);
}
function allowed(editor: ConfigurationEditorSnapshot, resource: "source" | "binding", operation: string): boolean {
  return editor.actions.state === "available" && editor.actions[resource].resourceId === (resource === "source" ? editor.source?.sourceId : editor.context.bindingId)
    && editor.actions[resource].actions.some(value => value.operationId === operation && value.supported && value.allowed);
}
function sameReference(left: ConfigurationSourceReference | undefined, right: ConfigurationSourceReference): boolean {
  return left?.state === right.state && left.publicationId === right.publicationId;
}
function sameMap(left: ConfigurationAssignmentMap, right: ConfigurationAssignmentMap | undefined): boolean {
  if (right === undefined) { return false; }
  const a = projectConfigurationAssignment(left), b = projectConfigurationAssignment(right);
  return a.bindingId === b.bindingId && a.applicationId === b.applicationId && a.configurationId === b.configurationId && a.schemaVersion === b.schemaVersion
    && a.environment === b.environment && a.eTag === b.eTag && a.generation === b.generation && a.defaultReleaseId === b.defaultReleaseId
    && a.overrides.length === b.overrides.length && a.overrides.every(row => b.overrides.some(value => value.cohortId === row.cohortId && value.releaseId === row.releaseId));
}
