import { configurationSourceBytes, ConfigurationWireError } from "./configuration-wire.js";
import type { ConfigurationIntegrityCheck, ConfigurationSchema, ConfigurationWireLimits } from "./configuration-wire.js";
import type { ConfigurationDesiredContext, ConfigurationSnapshotTarget } from "./configuration-models.js";
import { isCursor, isOperationKey, isOrderedInteger, isScalarText, isStrongETag, isTimestamp, isUtf8Text, isUuid } from "./wire-validation.js";
import { validUnicode } from "./wire-json.js";

type ObjectValue = Record<string, unknown>;
export function validateConfiguration(schema: ConfigurationSchema, value: unknown, request: boolean,
  limits: ConfigurationWireLimits, checks: ConfigurationIntegrityCheck[]): void {
  if (request && !["ConfigurationCatalogCommand", "ConfigurationPublicationCommand", "ConfigurationReleaseCommand",
    "ConfigurationAssignmentCommand", "ConfigurationValidationCommand", "ConfigurationPreviewCommand", "ConfigurationCandidatePreviewCommand", "ConfigurationCreationCommand", "ConfigurationStatusReport"].includes(schema)) {
    throw new TypeError("Select a declared configuration command.");
  }
  if (!request && ["ConfigurationCatalogCommand", "ConfigurationPublicationCommand", "ConfigurationReleaseCommand",
    "ConfigurationAssignmentCommand", "ConfigurationValidationCommand", "ConfigurationPreviewCommand", "ConfigurationCandidatePreviewCommand", "ConfigurationCreationCommand"].includes(schema)) {
    throw new TypeError("Select a declared configuration response.");
  }
  switch (schema) {
    case "ConfigurationCreationCommand": {
      const object = shape(value, true, ["commandVersion", "configurationId", "intent", "reason"]);
      version(object.commandVersion); portable(object.configurationId); reason(object.reason);
      requireValue(object.intent === "createGeneric" || object.intent === "reuseGeneric"); return;
    }
    case "ConfigurationCreationContext": {
      const object = shape(value, false, ["applicationId", "application", "environment", "eTag", "schemaVersion", "hostApplicationName"]);
      id(object.applicationId); scalar(object.application, 255); scalar(object.environment, 255); etag(object.eTag);
      requireValue(object.schemaVersion === "1"); host(object.hostApplicationName); return;
    }
    case "ConfigurationSummary": summary(value); return;
    case "ConfigurationSummaryPage": {
      const sources = new Set<string>(), bindings = new Set<string>(), definitions = new Set<string>();
      page(value, item => {
        summary(item); const row = item as ObjectValue, definition = JSON.stringify([row.configurationId, row.schemaVersion]);
        requireValue(!sources.has(row.sourceId as string) && !bindings.has(row.bindingId as string) && !definitions.has(definition));
        sources.add(row.sourceId as string); bindings.add(row.bindingId as string); definitions.add(definition);
      }); return;
    }
    case "ConfigurationCatalogCommand": {
      const object = shape(value, true, ["commandVersion", "manifestDigest", "reason"]);
      version(object.commandVersion); digest(object.manifestDigest); reason(object.reason); return;
    }
    case "ConfigurationPublicationCommand": {
      const object = shape(value, true, ["commandVersion", "payloadJson", "reason"]);
      version(object.commandVersion); configurationSourceBytes(object.payloadJson, limits); reason(object.reason); return;
    }
    case "ConfigurationReleaseCommand": {
      const object = shape(value, true, ["commandVersion", "commonSource", "applicationSource", "reason"]);
      version(object.commandVersion); reference(object.commonSource, true); reference(object.applicationSource, true); reason(object.reason); return;
    }
    case "ConfigurationAssignmentCommand": assignmentCommand(value, true, limits); return;
    case "ConfigurationValidationCommand": {
      const object = shape(value, true, ["commandVersion", "sourceId", "payloadJson"]);
      version(object.commandVersion); id(object.sourceId); configurationSourceBytes(object.payloadJson, limits); return;
    }
    case "ConfigurationPreviewCommand": {
      const object = shape(value, true, ["commandVersion", "applicationId", "configurationId", "schemaVersion"], ["commonSource", "applicationSource", "assignment"]);
      version(object.commandVersion); applicationDefinition(object); previewVariant(object, "assignment", true, limits); return;
    }
    case "ConfigurationDefinition": definition(value, limits); return;
    case "ConfigurationDefinitionPage": page(value, item => definition(item, limits)); return;
    case "ConfigurationEditorContext": editorContext(value, limits); return;
    case "ConfigurationCandidatePreviewCommand": {
      const object = shape(value, true, ["commandVersion", "applicationId", "configurationId", "schemaVersion", "selectedSource", "sourceId",
        "sourceETag", "assignmentETag", "commonSource", "applicationSource", "payloadJson"]);
      version(object.commandVersion); candidateIdentity(object); reference(object.commonSource, true); reference(object.applicationSource, true);
      configurationSourceBytes(object.payloadJson, limits); return;
    }
    case "ConfigurationCandidatePreview": {
      const object = shape(value, false, ["bindingId", "applicationId", "environment", "configurationId", "schemaVersion", "hostApplicationName",
        "selectedSource", "sourceId", "sourceETag", "assignmentETag", "commonSource", "applicationSource", "draftSha256", "structurallyValid",
        "redacted", "changes", "issues", "currentAssignment", "recipients"]);
      candidateIdentity(object); id(object.bindingId); scalar(object.environment, 255); scalar(object.hostApplicationName, 255);
      requireValue(object.hostApplicationName !== "CommonConfiguration"); digest(object.draftSha256);
      preview(object, limits);
      requireValue(object.redacted !== true || (object.changes as unknown[]).length === 0);
      requireValue(object.redacted !== true || (object.issues as ObjectValue[]).every(item => !has(item, "path")));
      requireValue((object.currentAssignment as ObjectValue).eTag === object.assignmentETag
        && (object.currentAssignment as ObjectValue).bindingId === object.bindingId
        && (object.currentAssignment as ObjectValue).environment === object.environment);
      return;
    }
    case "ConfigurationCatalog": catalog(value); return;
    case "ConfigurationSource": {
      const object = shape(value, false, ["sourceId", "configurationId", "schemaVersion", "environment", "kind", "eTag", "publications"], ["applicationId"]);
      sourceIdentity(object); etag(object.eTag);
      page(object.publications, item => {
        publication(item, false, limits, checks);
        const row = item as ObjectValue;
        for (const key of ["sourceId", "configurationId", "schemaVersion", "environment", "kind", "applicationId"]) { requireValue(row[key] === object[key]); }
      }); return;
    }
    case "ConfigurationPublicationMetadata": publication(value, false, limits, checks); return;
    case "ConfigurationPublicationPage": page(value, item => publication(item, false, limits, checks)); return;
    case "ConfigurationPublication": publication(value, true, limits, checks); return;
    case "ConfigurationRelease": release(value); return;
    case "ConfigurationReleasePage": {
      const identities = new Set<string>();
      page(value, item => {
        release(item);
        const identity = (item as ObjectValue).releaseId as string;
        requireValue(!identities.has(identity)); identities.add(identity);
      });
      return;
    }
    case "ConfigurationAssignmentMap": assignmentMap(value, limits); return;
    case "ConfigurationSnapshot": validateDesired(value); validateSnapshotContent(value, limits, checks); return;
    case "ConfigurationValidationResult": {
      const object = shape(value, false, ["valid", "issues"]); boolean(object.valid); issues(object.issues);
      requireValue(object.valid !== true || (object.issues as unknown[]).length === 0); return;
    }
    case "ConfigurationPreview": preview(value, limits); return;
    case "ConfigurationStatusReport": {
      const object = shape(value, request, ["sequence", "observedAtUtc", "entries"]); integer(object.sequence); time(object.observedAtUtc);
      const identities = new Set<string>();
      for (const row of array(object.entries, limits.maximumDefinitions)) {
        status(row, request); const entry = row as ObjectValue; const identity = JSON.stringify([entry.configurationId, entry.schemaVersion]);
        requireValue(!identities.has(identity)); identities.add(identity);
      } return;
    }
    case "ConfigurationAdoptionPage": page(value, adoption); return;
    case "ConfigurationDecision": decision(value, limits); return;
    case "ConfigurationOperationResult": result(value, limits); return;
    case "ConfigurationProblem": problem(value, limits); return;
    default: throw new TypeError("Select a declared configuration wire DTO.");
  }
}

function definition(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["configurationId", "schemaVersion", "sourceId", "environment", "kind", "shape"],
    ["defaultsJson", "description", "applicationId", "bindingId", "hostApplicationName"]);
  sourceIdentity(object);
  if (object.kind === "application") { id(object.bindingId); scalar(object.hostApplicationName, 255); }
  else { requireValue(!has(object, "bindingId") && !has(object, "hostApplicationName")); }
  declaration(object.shape, 1, limits);
  optional(object, "defaultsJson", item => configurationSourceBytes(item, limits));
  optional(object, "description", item => scalar(item, 2048, true));
}

function host(value: unknown): void { scalar(value, 255); requireValue(value !== "CommonConfiguration"); }
function summary(value: unknown): void {
  const object = shape(value, false, ["applicationId", "environment", "configurationId", "schemaVersion", "sourceId", "bindingId",
    "hostApplicationName", "origin", "hasCohortOverrides"], ["description", "modifiedAtUtc"]);
  applicationDefinition(object); scalar(object.environment, 255); id(object.sourceId); id(object.bindingId); host(object.hostApplicationName);
  scalar(object.origin, 64); boolean(object.hasCohortOverrides); optional(object, "description", item => scalar(item, 2048, true));
  optional(object, "modifiedAtUtc", item => requireValue(isTimestamp(item)));
}
function editorContext(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["bindingId", "applicationId", "application", "environment", "configurationId", "schemaVersion",
    "hostApplicationName", "shape", "selectedSource", "commonSource", "applicationSource", "defaults"], ["description"]);
  id(object.bindingId); applicationDefinition(object);
  for (const name of ["application", "environment", "hostApplicationName"]) { scalar(object[name], 255); }
  requireValue(object.hostApplicationName !== "CommonConfiguration");
  declaration(object.shape, 1, limits);
  optional(object, "description", item => scalar(item, 2048, true));
  requireValue(object.selectedSource === "common" || object.selectedSource === "application");
  const common = editorSource(object.commonSource);
  const application = editorSource(object.applicationSource);
  requireValue(common.sourceId === undefined || application.sourceId === undefined || common.sourceId !== application.sourceId);
  const selected = object.selectedSource === "common" ? common : application;
  const defaults = shape(object.defaults, false, ["state"], ["defaultsJson"]);
  requireValue(["available", "redacted", "unavailable"].includes(String(defaults.state)));
  if (defaults.state === "available") {
    requireValue(selected.state === "available");
    configurationSourceBytes(defaults.defaultsJson, limits);
  } else { requireValue(!has(defaults, "defaultsJson")); }
  if (selected.state === "absent" || selected.state === "unavailable") { requireValue(defaults.state === "unavailable"); }
  if (selected.state === "redacted") { requireValue(defaults.state === "redacted"); }
}
function editorSource(value: unknown): ObjectValue {
  const object = shape(value, false, ["state"], ["sourceId"]);
  requireValue(["available", "absent", "redacted", "unavailable"].includes(String(object.state)));
  if (object.state === "available") { id(object.sourceId); }
  else if (object.state === "unavailable") { optional(object, "sourceId", id); }
  else { requireValue(!has(object, "sourceId")); }
  return object;
}
function candidateIdentity(object: ObjectValue): void {
  applicationDefinition(object); id(object.sourceId); etag(object.sourceETag); etag(object.assignmentETag);
  requireValue(object.selectedSource === "common" || object.selectedSource === "application");
}
function declaration(value: unknown, depth: number, limits: ConfigurationWireLimits): void {
  requireValue(depth <= limits.maximumJsonDepth);
  const object = shape(value, false, ["kind", "nullable"], ["members", "element", "allowedValues"]); boolean(object.nullable);
  requireValue(["object", "array", "dictionary", "string", "number", "integer", "boolean"].includes(String(object.kind)));
  if (object.kind === "object") {
    requireValue(!has(object, "element") && !has(object, "allowedValues"));
    const names = new Set<string>();
    for (const item of array(object.members, 256)) {
      const member = shape(item, false, ["name", "required", "shape"]); const name = scalar(member.name, 256);
      requireValue(!names.has(name)); names.add(name); boolean(member.required); declaration(member.shape, depth + 1, limits);
    }
  } else if (object.kind === "array" || object.kind === "dictionary") {
    requireValue(!has(object, "members") && !has(object, "allowedValues")); declaration(object.element, depth + 1, limits);
  } else {
    requireValue(!has(object, "members") && !has(object, "element"));
    optional(object, "allowedValues", values => {
      requireValue(object.kind === "string"); const seen = new Set<string>();
      for (const item of array(values, 4096, 1)) { const text = bytes(item, 65536, true); requireValue(!seen.has(text)); seen.add(text); }
    });
  }
}
function catalog(value: unknown): void {
  const object = shape(value, false, ["catalogId", "eTag", "loadedManifestDigest", "ready", "definitionCount", "sourceCount", "bindingCount", "quotaBytes", "usedBytes"], ["appliedManifestDigest"]);
  id(object.catalogId); etag(object.eTag); digest(object.loadedManifestDigest); boolean(object.ready);
  optional(object, "appliedManifestDigest", digest);
  for (const key of ["definitionCount", "sourceCount", "bindingCount"]) { count(object[key], 2147483647); }
  count(object.quotaBytes, Number.MAX_SAFE_INTEGER, 1); count(object.usedBytes, Number.MAX_SAFE_INTEGER);
  requireValue((object.usedBytes as number) <= (object.quotaBytes as number) && (object.ready !== true || has(object, "appliedManifestDigest")));
}
function sourceIdentity(object: ObjectValue): void {
  id(object.sourceId); definitionIdentity(object); scalar(object.environment, 255);
  requireValue(object.kind === "common" || object.kind === "application", "configuration.content.scope");
  if (object.kind === "application") { id(object.applicationId); } else { requireValue(!has(object, "applicationId"), "configuration.content.scope"); }
}
function publication(value: unknown, content: boolean, limits: ConfigurationWireLimits, checks: ConfigurationIntegrityCheck[]): void {
  const required = ["publicationId", "sourceId", "configurationId", "schemaVersion", "environment", "kind", "sha256", "actor", "createdAtUtc", "reason"];
  const object = shape(value, false, content ? [...required, "payloadJson"] : required, ["applicationId"]);
  sourceIdentity(object); id(object.publicationId); digest(object.sha256); scalar(object.actor, 256); time(object.createdAtUtc); reason(object.reason);
  if (content) { collectIntegrity(object, limits, checks); }
}
function reference(value: unknown, closed: boolean): void {
  const object = shape(value, closed, ["state"], ["publicationId"]); requireValue(object.state === "present" || object.state === "absent");
  if (object.state === "present") { id(object.publicationId); } else { requireValue(!has(object, "publicationId")); }
}
function release(value: unknown): void {
  const object = shape(value, false, ["releaseId", "applicationId", "configurationId", "schemaVersion", "environment", "commonSource", "applicationSource", "actor", "createdAtUtc", "reason", "eTag"]);
  id(object.releaseId); applicationDefinition(object); scalar(object.environment, 255);
  reference(object.commonSource, false); reference(object.applicationSource, false); scalar(object.actor, 256); time(object.createdAtUtc); reason(object.reason); etag(object.eTag);
}
function assignmentCommand(value: unknown, closed: boolean, limits: ConfigurationWireLimits): void {
  const object = shape(value, closed, ["commandVersion", "intent", "defaultReleaseId", "overrides", "reason"]);
  version(object.commandVersion); scalar(object.intent, 64);
  if (closed) { requireValue(object.intent === "replace" || object.intent === "promote" || object.intent === "rollback"); }
  id(object.defaultReleaseId); overrides(object.overrides, closed, limits); reason(object.reason);
}
function overrides(value: unknown, closed: boolean, limits: ConfigurationWireLimits): void {
  const cohorts = new Set<string>();
  for (const item of array(value, limits.maximumOverrides)) {
    const entry = shape(item, closed, ["cohortId", "releaseId"]); portable(entry.cohortId); id(entry.releaseId);
    requireValue(!cohorts.has(entry.cohortId as string)); cohorts.add(entry.cohortId as string);
  }
}
function assignmentMap(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["bindingId", "applicationId", "configurationId", "schemaVersion", "environment", "eTag", "overrides"], ["defaultReleaseId", "generation"]);
  id(object.bindingId); applicationDefinition(object); scalar(object.environment, 255); etag(object.eTag); overrides(object.overrides, false, limits);
  requireValue(has(object, "defaultReleaseId") === has(object, "generation"));
  if (has(object, "generation")) { id(object.defaultReleaseId); integer(object.generation); }
  else { requireValue((object.overrides as unknown[]).length === 0); }
}

export function validateTarget(value: ConfigurationSnapshotTarget): void {
  // The caller supplies this expected identity independently; it is never learned from the response under validation.
  const object = shape(value, false, ["authorityId", "applicationId", "environment", "registrationId", "configurationId", "schemaVersion", "hostApplicationName"], ["cohortId"]);
  targetFields(object);
}
function targetFields(object: ObjectValue): void {
  id(object.authorityId); id(object.registrationId); applicationDefinition(object); scalar(object.environment, 255); scalar(object.hostApplicationName, 255);
  optional(object, "cohortId", portable);
}
export function sameConfigurationTarget(value: ConfigurationSnapshotTarget, expected: ConfigurationSnapshotTarget): boolean {
  return value.authorityId === expected.authorityId && value.applicationId === expected.applicationId && value.environment === expected.environment
    && value.registrationId === expected.registrationId && value.configurationId === expected.configurationId && value.schemaVersion === expected.schemaVersion
    && value.hostApplicationName === expected.hostApplicationName && value.cohortId === expected.cohortId;
}
export function validateDesired(value: unknown): { readonly desired: ConfigurationDesiredContext } {
  const outer = shape(value, false, ["desired", "eTag", "commonSource", "applicationSource"]);
  const desired = shape(outer.desired, false, ["authorityId", "applicationId", "environment", "registrationId", "configurationId", "schemaVersion", "hostApplicationName", "releaseId", "generation"], ["cohortId"]);
  targetFields(desired); id(desired.releaseId); integer(desired.generation); etag(outer.eTag);
  return { desired: desired as unknown as ConfigurationDesiredContext };
}
export function validateSnapshotContent(value: unknown, limits: ConfigurationWireLimits, checks: ConfigurationIntegrityCheck[]): void {
  const object = value as ObjectValue; const desired = object.desired as ObjectValue;
  sourceContent(object.commonSource, desired, "common", limits, checks);
  sourceContent(object.applicationSource, desired, "application", limits, checks);
}
function sourceContent(value: unknown, desired: ObjectValue, expectedKind: string, limits: ConfigurationWireLimits, checks: ConfigurationIntegrityCheck[]): void {
  const fields = ["publicationId", "sha256", "payloadJson", "sourceId", "configurationId", "schemaVersion", "environment", "kind", "applicationId"];
  const object = shape(value, false, ["state"], fields); requireValue(object.state === "present" || object.state === "absent", "configuration.content.invalid");
  if (object.state === "absent") { requireValue(fields.every(field => !has(object, field)), "configuration.content.invalid"); return; }
  shape(value, false, ["state", "publicationId", "sha256", "payloadJson", "sourceId", "configurationId", "schemaVersion", "environment", "kind"], ["applicationId"]);
  sourceIdentity(object); id(object.publicationId);
  requireValue(object.configurationId === desired.configurationId && object.schemaVersion === desired.schemaVersion, "configuration.content.schema");
  requireValue(object.environment === desired.environment && object.kind === expectedKind
    && (expectedKind === "common" || object.applicationId === desired.applicationId), "configuration.content.scope");
  collectIntegrity(object, limits, checks);
}
function collectIntegrity(object: ObjectValue, limits: ConfigurationWireLimits, checks: ConfigurationIntegrityCheck[]): void {
  digest(object.sha256); configurationSourceBytes(object.payloadJson, limits);
  checks.push({ payload: object.payloadJson as string, sha256: object.sha256 as string });
}

function previewVariant(object: ObjectValue, assignmentName: string, closed: boolean, limits: ConfigurationWireLimits): void {
  const common = has(object, "commonSource"); const application = has(object, "applicationSource"); const assignment = has(object, assignmentName);
  requireValue(common === application && assignment !== common);
  if (assignment) { assignmentCommand(object[assignmentName], closed, limits); }
  else { reference(object.commonSource, closed); reference(object.applicationSource, closed); }
}
function issues(value: unknown): void {
  for (const row of array(value, 64)) {
    const object = shape(row, false, ["code"], ["path"]); code(object.code); optional(object, "path", item => bytes(item, 1024, true));
  }
}
function preview(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["applicationId", "configurationId", "schemaVersion", "structurallyValid", "redacted", "changes", "issues", "currentAssignment", "recipients"], ["commonSource", "applicationSource", "proposedAssignment"]);
  applicationDefinition(object); boolean(object.structurallyValid); boolean(object.redacted); issues(object.issues);
  requireValue(object.structurallyValid !== true || (object.issues as unknown[]).length === 0);
  for (const row of array(object.changes, 256)) { const item = shape(row, false, ["path", "kind"]); bytes(item.path, 1024, true); scalar(item.kind, 64); }
  assignmentMap(object.currentAssignment, limits); const current = object.currentAssignment as ObjectValue;
  for (const field of ["applicationId", "configurationId", "schemaVersion"]) { requireValue(object[field] === current[field]); }
  const registrations = new Set<string>();
  page(object.recipients, row => {
    const recipient = shape(row, false, ["registrationId", "compatibility"], ["cohortId"]); id(recipient.registrationId); scalar(recipient.compatibility, 64);
    optional(recipient, "cohortId", portable); requireValue(!registrations.has(recipient.registrationId as string)); registrations.add(recipient.registrationId as string);
  });
  previewVariant(object, "proposedAssignment", false, limits);
}
function decision(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["commandVersion", "kind", "decision", "code", "decidedAtUtc"],
    ["catalogId", "manifestDigest", "sourceId", "publicationId", "sha256", "applicationId", "configurationId", "schemaVersion", "releaseId", "generation", "eTag", "intent", "assignment", "bindingId", "hostApplicationName", "sourceETag"]);
  version(object.commandVersion); scalar(object.kind, 64); scalar(object.decision, 64); code(object.code); time(object.decidedAtUtc);
  for (const field of ["catalogId", "sourceId", "publicationId", "applicationId", "releaseId", "bindingId"]) { optional(object, field, id); }
  for (const field of ["manifestDigest", "sha256"]) { optional(object, field, digest); }
  optional(object, "configurationId", portable); optional(object, "schemaVersion", integer); optional(object, "generation", integer);
  optional(object, "eTag", etag); optional(object, "intent", item => scalar(item, 64)); optional(object, "assignment", item => assignmentMap(item, limits));
  optional(object, "hostApplicationName", host); optional(object, "sourceETag", etag);
  switch (object.kind) {
    case "catalogApplied": required(object, ["catalogId", "manifestDigest", "eTag"]); break;
    case "sourcePublished": required(object, ["sourceId", "publicationId", "sha256", "eTag"]); break;
    case "releaseCreated": required(object, ["applicationId", "configurationId", "schemaVersion", "releaseId", "eTag"]); break;
    case "configurationCreated":
      required(object, ["applicationId", "configurationId", "schemaVersion", "sourceId", "bindingId", "hostApplicationName", "sourceETag", "eTag"]);
      requireValue(object.schemaVersion === "1"); break;
    case "assignmentsReplaced":
      required(object, ["applicationId", "configurationId", "schemaVersion", "generation", "eTag", "intent", "assignment"]);
      for (const field of ["applicationId", "configurationId", "schemaVersion", "generation", "eTag"]) { requireValue(object[field] === (object.assignment as ObjectValue)[field]); }
      break;
  }
}
function operation(value: unknown): void {
  const object = shape(value, false, ["name", "scope", "key"]); code(object.name, 64); bytes(object.scope, 256); portable(object.key);
}
function result(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["operation", "resultSource", "originalCompletion"], ["result", "rejection", "attemptCompletion"]);
  operation(object.operation); scalar(object.resultSource, 64); scalar(object.originalCompletion, 64);
  requireValue(has(object, "result") !== has(object, "rejection"));
  if (has(object, "result")) { decision(object.result, limits); }
  else {
    const rejection = shape(object.rejection, false, ["code", "message"]);
    requireValue(typeof rejection.code === "string" && rejection.code.trim().length > 0 && /^[\u0020-\u007e]{1,128}$/u.test(rejection.code));
    requireValue(typeof rejection.message === "string" && validUnicode(rejection.message)
      && !/^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]*$/u.test(rejection.message)
      && !rejection.message.includes("\0") && rejection.message.length <= 1024 && new TextEncoder().encode(rejection.message).length <= 1024);
  }
  if (["prepared", "replayed", "found"].includes(String(object.resultSource))) { requireValue(object.originalCompletion !== "notCommitted" && object.originalCompletion !== "unknown"); }
  optional(object, "attemptCompletion", item => {
    const attempt = shape(item, false, ["outcome", "cleanupSucceeded"], ["cleanupCodes"]); scalar(attempt.outcome, 64); boolean(attempt.cleanupSucceeded);
    optional(attempt, "cleanupCodes", codes => array(codes, 16).forEach(item => code(item)));
  });
}
function problem(value: unknown, limits: ConfigurationWireLimits): void {
  const object = shape(value, false, ["type", "title", "status", "code"], ["detail", "correlationId", "operation", "result"]);
  scalar(object.type, 2048); try { new URL(object.type as string); } catch { throw new ConfigurationWireError(); }
  scalar(object.title, 256); count(object.status, 599, 400); code(object.code);
  optional(object, "detail", item => scalar(item, 1024, true)); optional(object, "correlationId", item => scalar(item, 128));
  optional(object, "operation", operation); optional(object, "result", item => {
    result(item, limits); required(object, ["operation"]); const original = object.operation as ObjectValue; const nested = (item as ObjectValue).operation as ObjectValue;
    for (const field of ["name", "scope", "key"]) { requireValue(original[field] === nested[field]); }
  });
}
function configurationVersion(value: unknown, closed: boolean, applied = false): void {
  const object = shape(value, closed, applied ? ["releaseId", "generation", "localRevision"] : ["releaseId", "generation"]);
  id(object.releaseId); integer(object.generation); if (applied) { integer(object.localRevision, true); }
}
function status(value: unknown, closed: boolean): void {
  const object = shape(value, closed, ["configurationId", "schemaVersion", "updateMode", "state", "liveStateValid"],
    ["desired", "applied", "lastSuccessful", "candidateFailureCode", "notificationFailureCode"]);
  definitionIdentity(object); scalar(object.updateMode, 64); scalar(object.state, 64); boolean(object.liveStateValid);
  if (closed) { requireValue(object.updateMode === "HotReload" || object.updateMode === "RestartRequired");
    requireValue(["Pending", "Applied", "AppliedUnchanged", "Rejected", "PendingRestart", "Faulted"].includes(String(object.state))); }
  optional(object, "desired", item => configurationVersion(item, closed));
  for (const field of ["applied", "lastSuccessful"]) { optional(object, field, item => configurationVersion(item, closed, true)); }
  for (const field of ["candidateFailureCode", "notificationFailureCode"]) { optional(object, field, item => code(item)); }
  requireValue(object.liveStateValid === true || !has(object, "applied"));
  if (object.state === "Applied" || object.state === "AppliedUnchanged") {
    requireValue(object.liveStateValid === true); required(object, ["desired", "applied"]);
    for (const field of ["releaseId", "generation"]) { requireValue((object.desired as ObjectValue)[field] === (object.applied as ObjectValue)[field]); }
  }
  if (object.state === "Rejected" || object.state === "PendingRestart") { requireValue(object.liveStateValid === true); required(object, ["desired", "applied"]); }
  if (object.state === "Faulted") { requireValue(object.liveStateValid === false); }
}
function adoption(value: unknown): void {
  const object = shape(value, false, ["applicationId", "environment", "configurationId", "schemaVersion", "registrationFreshness", "adoptionFreshness", "convergence"],
    ["cohortId", "registrationId", "reportedInstanceId", "applicationVersion", "desired", "reported", "observedAtUtc", "receivedAtUtc"]);
  applicationDefinition(object); scalar(object.environment, 255);
  for (const field of ["registrationFreshness", "adoptionFreshness", "convergence"]) { scalar(object[field], 64); }
  optional(object, "cohortId", portable); optional(object, "registrationId", id); optional(object, "reportedInstanceId", item => scalar(item, 1024));
  optional(object, "applicationVersion", item => scalar(item, 512)); optional(object, "desired", item => configurationVersion(item, false));
  optional(object, "reported", item => { status(item, false); const report = item as ObjectValue;
    requireValue(report.configurationId === object.configurationId && report.schemaVersion === object.schemaVersion); });
  optional(object, "observedAtUtc", time); optional(object, "receivedAtUtc", time);
  const report = has(object, "reported"); requireValue(report === has(object, "observedAtUtc") && report === has(object, "receivedAtUtc"));
  if (report) { required(object, ["registrationId"]); }
}

function page(value: unknown, validate: (item: unknown) => void): void {
  const object = shape(value, false, ["items"], ["nextCursor"]); array(object.items, 500).forEach(validate); optional(object, "nextCursor", item => requireValue(isCursor(item)));
}
function shape(value: unknown, closed: boolean, names: readonly string[], optionalNames: readonly string[] = []): ObjectValue {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value)); const object = value as ObjectValue;
  required(object, names);
  for (const name of Object.keys(object)) {
    const known = names.includes(name) || optionalNames.includes(name); requireValue(!closed || known);
    if (known) { requireValue(object[name] !== null && object[name] !== undefined); }
  }
  return object;
}
function required(object: ObjectValue, names: readonly string[]): void { for (const name of names) { requireValue(has(object, name) && object[name] !== null && object[name] !== undefined); } }
function has(object: ObjectValue, name: string): boolean { return Object.hasOwn(object, name); }
function optional(object: ObjectValue, name: string, validate: (item: unknown) => unknown): void { if (has(object, name)) { validate(object[name]); } }
function array(value: unknown, maximum: number, minimum = 0): unknown[] { requireValue(Array.isArray(value) && value.length >= minimum && value.length <= maximum); return value as unknown[]; }
function scalar(value: unknown, maximum: number, empty = false): string { requireValue(isScalarText(value, maximum, empty)); return value as string; }
function bytes(value: unknown, maximum: number, empty = false): string { requireValue(isUtf8Text(value, maximum, empty)); return value as string; }
function id(value: unknown): void { requireValue(isUuid(value)); }
function portable(value: unknown): void { requireValue(isOperationKey(value)); }
function integer(value: unknown, zero = false): void { requireValue(isOrderedInteger(value, zero)); }
function time(value: unknown): void { requireValue(isTimestamp(value)); }
function etag(value: unknown): void { requireValue(isStrongETag(value)); }
function boolean(value: unknown): void { requireValue(typeof value === "boolean"); }
function count(value: unknown, maximum: number, minimum = 0): void { requireValue(Number.isSafeInteger(value) && (value as number) >= minimum && (value as number) <= maximum); }
function version(value: unknown): void { requireValue(value === 1, "configuration.version.unsupported"); }
function code(value: unknown, maximum = 128): void { requireValue(typeof value === "string" && value.length <= maximum && /^[A-Za-z0-9._:-]+$/u.test(value)); }
function reason(value: unknown): void { bytes(value, 1024); }
function digest(value: unknown): void { requireValue(typeof value === "string" && /^[0-9a-f]{64}$/u.test(value), "configuration.integrity"); }
function definitionIdentity(object: ObjectValue): void { portable(object.configurationId); integer(object.schemaVersion); }
function applicationDefinition(object: ObjectValue): void { id(object.applicationId); definitionIdentity(object); }
function requireValue(condition: boolean, code = "configuration.wire.invalid"): asserts condition { if (!condition) { throw new ConfigurationWireError(code); } }
