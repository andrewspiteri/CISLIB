import type { ConfigurationActions, ConfigurationAssignmentMap, ConfigurationCandidatePreview, ConfigurationCohortChoice, ConfigurationEditorContext,
  ConfigurationPreview, ConfigurationPublication, ConfigurationPublicationMetadata, ConfigurationRelease,
  ConfigurationShape, ConfigurationSource, ConfigurationSourceReference, ConfigurationValidationResult, Page } from "@cislib/management-client";

export type ConfigurationEditorContent = { readonly state: "present"; readonly publication: ConfigurationPublication }
  | { readonly state: "absent" }
  | { readonly state: "unselected" | "unavailable"; readonly message: string };
export type ConfigurationEditorActions = { readonly state: "available"; readonly source: ConfigurationActions; readonly binding: ConfigurationActions }
  | { readonly state: "unavailable"; readonly message: string };
export type ConfigurationSharedOverrideSeed = { readonly state: "present"; readonly publication: ConfigurationPublication }
  | { readonly state: "redacted" | "unavailable"; readonly message: string };
/** One exact selected source. An unassigned binding supplies no implicit publication baseline. */
export interface ConfigurationEditorSnapshot {
  readonly context: ConfigurationEditorContext;
  readonly source?: ConfigurationSource;
  readonly assignment?: ConfigurationAssignmentMap;
  readonly baselineRelease?: ConfigurationRelease;
  readonly content: ConfigurationEditorContent;
  readonly actions: ConfigurationEditorActions;
  /** Only the actual assigned Shared publication, independently authorized for initial Application copying. */
  readonly sharedOverrideSeed?: ConfigurationSharedOverrideSeed;
}

/** Native compatible additions do not automatically become browser fields. No value merge is performed here. */
export function projectConfigurationEditor(value: ConfigurationEditorSnapshot): ConfigurationEditorSnapshot {
  const context = value.context;
  const content: ConfigurationEditorContent = value.content.state === "present"
    ? { state: "present", publication: projectConfigurationPublication(value.content.publication) }
    : value.content.state === "absent" ? { state: "absent" }
      : { state: value.content.state, message: value.content.message };
  return Object.freeze({ context: Object.freeze({ bindingId: context.bindingId, applicationId: context.applicationId,
    application: context.application, environment: context.environment, configurationId: context.configurationId,
    schemaVersion: context.schemaVersion, hostApplicationName: context.hostApplicationName, selectedSource: context.selectedSource,
    shape: projectShape(context.shape), commonSource: projectEditorSource(context.commonSource), applicationSource: projectEditorSource(context.applicationSource),
    defaults: context.defaults.state === "available" ? Object.freeze({ state: "available" as const, defaultsJson: context.defaults.defaultsJson })
      : Object.freeze({ state: context.defaults.state }), ...(context.description === undefined ? {} : { description: context.description }) }),
    ...(value.source === undefined ? {} : { source: projectConfigurationSource(value.source) }),
    ...(value.assignment === undefined ? {} : { assignment: projectConfigurationAssignment(value.assignment) }),
    ...(value.baselineRelease === undefined ? {} : { baselineRelease: projectConfigurationRelease(value.baselineRelease) }),
    ...(value.sharedOverrideSeed === undefined ? {} : { sharedOverrideSeed: value.sharedOverrideSeed.state === "present"
      ? Object.freeze({ state: "present" as const, publication: projectConfigurationPublication(value.sharedOverrideSeed.publication) })
      : Object.freeze({ state: value.sharedOverrideSeed.state, message: value.sharedOverrideSeed.message }) }),
    content: Object.freeze(content), actions: value.actions.state === "available"
      ? Object.freeze({ state: "available" as const, source: projectConfigurationActions(value.actions.source), binding: projectConfigurationActions(value.actions.binding) })
      : Object.freeze({ state: "unavailable" as const, message: value.actions.message }) });
}
function projectShape(value: ConfigurationShape): ConfigurationShape {
  return Object.freeze({ kind: value.kind, nullable: value.nullable,
    ...(value.members === undefined ? {} : { members: Object.freeze(value.members.map(item => Object.freeze({ name: item.name, required: item.required, shape: projectShape(item.shape) }))) }),
    ...(value.element === undefined ? {} : { element: projectShape(value.element) }),
    ...(value.allowedValues === undefined ? {} : { allowedValues: Object.freeze([...value.allowedValues]) }) });
}
function projectEditorSource(value: ConfigurationEditorContext["commonSource"]): ConfigurationEditorContext["commonSource"] {
  if (value.state === "available") { return Object.freeze({ state: value.state, sourceId: value.sourceId }); }
  if (value.state === "unavailable") { return Object.freeze({ state: value.state, ...(value.sourceId === undefined ? {} : { sourceId: value.sourceId }) }); }
  return Object.freeze({ state: value.state });
}
export function projectConfigurationActions(value: ConfigurationActions): ConfigurationActions {
  return Object.freeze({ resourceId: value.resourceId, actions: Object.freeze(value.actions.map(item => Object.freeze({
    operationId: item.operationId, supported: item.supported, allowed: item.allowed,
    ...(item.reasonCode === undefined ? {} : { reasonCode: item.reasonCode }) }))) });
}
export function projectConfigurationReleasePage(value: Page<ConfigurationRelease>): Page<ConfigurationRelease> {
  return Object.freeze({ items: Object.freeze(value.items.map(projectConfigurationRelease)),
    ...(value.nextCursor === undefined ? {} : { nextCursor: value.nextCursor }) });
}
export function projectConfigurationCohortPage(value: Page<ConfigurationCohortChoice>): Page<ConfigurationCohortChoice> {
  return Object.freeze({ items: Object.freeze(value.items.map(item => Object.freeze({ cohortId: item.cohortId, displayName: item.displayName }))),
    ...(value.nextCursor === undefined ? {} : { nextCursor: value.nextCursor }) });
}
export function projectConfigurationReference(value: ConfigurationSourceReference): ConfigurationSourceReference {
  return Object.freeze(value.state === "present" ? { state: "present" as const, publicationId: value.publicationId } : { state: "absent" as const });
}
export function projectConfigurationPublicationMetadata(value: ConfigurationPublicationMetadata): ConfigurationPublicationMetadata {
  return Object.freeze({ publicationId: value.publicationId, sourceId: value.sourceId, configurationId: value.configurationId,
    schemaVersion: value.schemaVersion, environment: value.environment, kind: value.kind,
    ...(value.applicationId === undefined ? {} : { applicationId: value.applicationId }), sha256: value.sha256,
    actor: value.actor, createdAtUtc: value.createdAtUtc, reason: value.reason });
}
export function projectConfigurationPublication(value: ConfigurationPublication): ConfigurationPublication {
  return Object.freeze({ ...projectConfigurationPublicationMetadata(value), payloadJson: value.payloadJson });
}
export function projectConfigurationSource(value: ConfigurationSource): ConfigurationSource {
  return Object.freeze({ sourceId: value.sourceId, configurationId: value.configurationId, schemaVersion: value.schemaVersion,
    environment: value.environment, kind: value.kind, eTag: value.eTag,
    ...(value.applicationId === undefined ? {} : { applicationId: value.applicationId }),
    publications: Object.freeze({ items: Object.freeze(value.publications.items.map(projectConfigurationPublicationMetadata)),
      ...(value.publications.nextCursor === undefined ? {} : { nextCursor: value.publications.nextCursor }) }) });
}
export function projectConfigurationRelease(value: ConfigurationRelease): ConfigurationRelease {
  return Object.freeze({ releaseId: value.releaseId, applicationId: value.applicationId, configurationId: value.configurationId,
    schemaVersion: value.schemaVersion, environment: value.environment, commonSource: projectConfigurationReference(value.commonSource),
    applicationSource: projectConfigurationReference(value.applicationSource), actor: value.actor, createdAtUtc: value.createdAtUtc,
    reason: value.reason, eTag: value.eTag });
}
export function projectConfigurationAssignment(value: ConfigurationAssignmentMap): ConfigurationAssignmentMap {
  return Object.freeze({ bindingId: value.bindingId, applicationId: value.applicationId, configurationId: value.configurationId,
    schemaVersion: value.schemaVersion, environment: value.environment, eTag: value.eTag,
    ...(value.defaultReleaseId === undefined ? {} : { defaultReleaseId: value.defaultReleaseId }),
    ...(value.generation === undefined ? {} : { generation: value.generation }),
    overrides: Object.freeze(value.overrides.map(item => Object.freeze({ cohortId: item.cohortId, releaseId: item.releaseId }))) });
}
export function projectConfigurationValidation(value: ConfigurationValidationResult): ConfigurationValidationResult {
  return Object.freeze({ valid: value.valid, issues: projectIssues(value.issues) });
}
function projectIssues(value: ConfigurationValidationResult["issues"]): ConfigurationValidationResult["issues"] {
  return Object.freeze(value.map(item => Object.freeze({ code: item.code, ...(item.path === undefined ? {} : { path: item.path }) })));
}
export function projectConfigurationPreview(value: ConfigurationPreview): ConfigurationPreview {
  return Object.freeze({ applicationId: value.applicationId, configurationId: value.configurationId, schemaVersion: value.schemaVersion,
    structurallyValid: value.structurallyValid, redacted: value.redacted,
    changes: Object.freeze(value.changes.map(item => Object.freeze({ path: item.path, kind: item.kind }))), issues: projectIssues(value.issues),
    currentAssignment: projectConfigurationAssignment(value.currentAssignment), recipients: Object.freeze({
      items: Object.freeze(value.recipients.items.map(item => Object.freeze({ registrationId: item.registrationId, compatibility: item.compatibility,
        ...(item.cohortId === undefined ? {} : { cohortId: item.cohortId }) }))),
      ...(value.recipients.nextCursor === undefined ? {} : { nextCursor: value.recipients.nextCursor }) }),
    ...(value.commonSource === undefined ? { proposedAssignment: Object.freeze({ commandVersion: value.proposedAssignment.commandVersion,
      intent: value.proposedAssignment.intent, defaultReleaseId: value.proposedAssignment.defaultReleaseId,
      overrides: Object.freeze(value.proposedAssignment.overrides.map(item => Object.freeze({ cohortId: item.cohortId, releaseId: item.releaseId }))),
      reason: value.proposedAssignment.reason }) }
      : { commonSource: projectConfigurationReference(value.commonSource), applicationSource: projectConfigurationReference(value.applicationSource) }) });
}
export function projectConfigurationCandidate(value: ConfigurationCandidatePreview): ConfigurationCandidatePreview {
  return Object.freeze({ bindingId: value.bindingId, applicationId: value.applicationId, environment: value.environment,
    configurationId: value.configurationId, schemaVersion: value.schemaVersion, hostApplicationName: value.hostApplicationName,
    selectedSource: value.selectedSource, sourceId: value.sourceId, sourceETag: value.sourceETag, assignmentETag: value.assignmentETag,
    commonSource: projectConfigurationReference(value.commonSource), applicationSource: projectConfigurationReference(value.applicationSource),
    draftSha256: value.draftSha256, structurallyValid: value.structurallyValid, redacted: value.redacted,
    changes: Object.freeze(value.changes.map(item => Object.freeze({ path: item.path, kind: item.kind }))), issues: projectIssues(value.issues),
    currentAssignment: projectConfigurationAssignment(value.currentAssignment), recipients: Object.freeze({
      items: Object.freeze(value.recipients.items.map(item => Object.freeze({ registrationId: item.registrationId, compatibility: item.compatibility,
        ...(item.cohortId === undefined ? {} : { cohortId: item.cohortId }) }))),
      ...(value.recipients.nextCursor === undefined ? {} : { nextCursor: value.recipients.nextCursor }) }) });
}
