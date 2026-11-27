import { ManagementWireError, OpaqueJsonObject, validUnicode } from "./wire-json.js";
import type { ManagementWireLimits } from "./wire-limits.js";

type ObjectValue = Record<string, unknown>;
export type ManagementSchema = "RegistrationRequest" | "Observation" | "InventorySnapshot" | "MetadataCommand"
  | "Application" | "MetadataResult" | "Problem" | "RegistrationReceipt" | "ReportReceipt" | "CloseReceipt"
  | "ServiceInfo" | "ControlConfirmation" | "Capabilities" | "Instance" | "InventoryItem" | "ApplicationPage" | "InstancePage" | "InventoryPage"
  | "EnvironmentEntry" | "EnvironmentPage";

const encoder = new TextEncoder();
const lifecycle = new Set(["starting", "ready", "stopping", "stopped", "unknown"]);
const health = new Set(["healthy", "degraded", "unhealthy", "unknown"]);
const jobStates = new Set(["disabled", "standby", "acquiring", "running", "idle", "waiting", "draining", "lost", "released", "faulted"]);

export function isUuid(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(value)
    && value !== "00000000-0000-0000-0000-000000000000";
}
export function isOperationKey(value: unknown): value is string {
  return typeof value === "string" && value !== "." && value !== ".." && /^[A-Za-z0-9._~-]{1,128}$/u.test(value);
}
export function isStrongETag(value: unknown): value is string {
  return typeof value === "string" && value.length <= 256 && /^"[\u0021\u0023-\u007e]+"$/u.test(value);
}
export function isOrderedInteger(value: unknown, zero = false): value is string {
  if (typeof value !== "string" || !(zero ? /^(0|[1-9][0-9]{0,18})$/u : /^[1-9][0-9]{0,18}$/u).test(value)) {
    return false;
  }
  return value.length < 19 || value <= "9223372036854775807";
}
export function isTimestamp(value: unknown): value is string {
  if (typeof value !== "string" || !/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z$/u.test(value)) {
    return false;
  }
  const year = Number(value.slice(0, 4));
  const month = Number(value.slice(5, 7));
  const day = Number(value.slice(8, 10));
  const hour = Number(value.slice(11, 13));
  const minute = Number(value.slice(14, 16));
  const second = Number(value.slice(17, 19));
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  date.setUTCHours(hour, minute, second, 0);
  return year >= 1 && date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
    && date.getUTCHours() === hour && date.getUTCMinutes() === minute && date.getUTCSeconds() === second;
}
export function isScalarText(value: unknown, maximum: number, empty = false): value is string {
  if (typeof value !== "string" || value.length > maximum * 2 || !validUnicode(value)
    || /\p{Cc}/u.test(value) || !empty && value.trim().length === 0) {
    return false;
  }
  let count = 0;
  for (const _character of value) {
    if (++count > maximum) { return false; }
  }
  return true;
}
export function isUtf8Text(value: unknown, maximum: number, empty = false): value is string {
  return typeof value === "string" && value.length <= maximum
    && (value === "" ? empty : isScalarText(value, maximum)) && encoder.encode(value).length <= maximum;
}
export function isCursor(value: unknown): value is string {
  return typeof value === "string" && /^[\u0021-\u007e]{1,2048}$/u.test(value);
}

export function validateManagement(schema: ManagementSchema, value: unknown, request: boolean, limits: ManagementWireLimits): void {
  if (request && !["RegistrationRequest", "Observation", "InventorySnapshot", "MetadataCommand"].includes(schema)) {
    throw new TypeError("Select one of the four declared management request DTOs.");
  }
  switch (schema) {
    case "RegistrationRequest": {
      const object = shape(value, request, ["protocolMajor", "reportedInstanceId", "processStartedAtUtc", "applicationVersion"]);
      version(object.protocolMajor); scalar(object.reportedInstanceId, 1024); time(object.processStartedAtUtc); scalar(object.applicationVersion, 512);
      return;
    }
    case "Observation": observation(value, request); return;
    case "InventorySnapshot": inventory(value, request, limits); return;
    case "MetadataCommand": {
      const object = shape(value, request, ["commandVersion", "displayName"], ["description"]);
      version(object.commandVersion); scalar(object.displayName, 256); optional(object, "description", item => scalar(item, 2048, true));
      return;
    }
    case "Application": application(value); return;
    case "EnvironmentEntry": {
      const object = shape(value, false, ["environment"]); scalar(object.environment, 255); return;
    }
    case "MetadataResult": metadataResult(value); return;
    case "Problem": problem(value); return;
    case "RegistrationReceipt": {
      const object = shape(value, false, ["registrationId", "applicationId", "registeredAtUtc"]);
      id(object.registrationId); id(object.applicationId); time(object.registeredAtUtc); return;
    }
    case "ControlConfirmation": {
      const object = shape(value, false, ["authorityId", "registrationId", "controlRevision", "admissionAllowed", "validForMs", "attemptId"]);
      id(object.authorityId); id(object.registrationId); id(object.attemptId); scalar(object.controlRevision, 256);
      boolean(object.admissionAllowed); requireValue(positiveInt(object.validForMs) <= 15000); return;
    }
    case "ReportReceipt": {
      const object = shape(value, false, ["registrationId", "stream", "submittedSequence", "acceptedSequence", "disposition", "receivedAtUtc"]);
      id(object.registrationId); scalar(object.stream, 64); integer(object.submittedSequence); integer(object.acceptedSequence);
      scalar(object.disposition, 64); time(object.receivedAtUtc); return;
    }
    case "CloseReceipt": {
      const object = shape(value, false, ["registrationId", "closedAtUtc"]);
      id(object.registrationId); time(object.closedAtUtc); return;
    }
    case "ServiceInfo": {
      const object = shape(value, false, ["serviceVersion", "protocolMajors", "features"], ["maximumInventoryRequestBytes"]);
      scalar(object.serviceVersion, 512);
      const majors = array(object.protocolMajors, 16, 1); unique(majors.map(positiveInt)); features(object.features);
      optional(object, "maximumInventoryRequestBytes", item => requireValue(positiveInt(item) <= 16 * 1024 * 1024)); return;
    }
    case "Capabilities": {
      const object = shape(value, false, ["applicationId", "implementedApis", "callerHints"]);
      id(object.applicationId); features(object.implementedApis);
      unique(array(object.callerHints, 64).map(item => {
        const hint = shape(item, false, ["operationId", "allowed"]); boolean(hint.allowed); return alias(hint.operationId, 128);
      })); return;
    }
    case "Instance": instance(value); return;
    case "InventoryItem": inventoryItem(value, limits); return;
    case "ApplicationPage": case "InstancePage": case "InventoryPage": case "EnvironmentPage": {
      const object = shape(value, false, ["items"], ["nextCursor"]);
      const itemSchema = schema === "ApplicationPage" ? "Application" : schema === "InstancePage" ? "Instance"
        : schema === "EnvironmentPage" ? "EnvironmentEntry" : "InventoryItem";
      array(object.items, 500).forEach(item => validateManagement(itemSchema, item, false, limits));
      optional(object, "nextCursor", item => requireValue(isCursor(item))); return;
    }
    default: throw new TypeError("Select a declared management v1 DTO.");
  }
}

function application(value: unknown): void {
  const object = shape(value, false, ["applicationId", "application", "environment", "metadata", "metadataRevision", "metadataETag"]);
  id(object.applicationId); scalar(object.application, 255); scalar(object.environment, 255); metadata(object.metadata);
  integer(object.metadataRevision); requireValue(isStrongETag(object.metadataETag));
}
function metadata(value: unknown): void {
  const object = shape(value, false, ["displayName"], ["description"]);
  scalar(object.displayName, 256); optional(object, "description", item => scalar(item, 2048, true));
}
function observation(value: unknown, request: boolean): void {
  const object = shape(value, request, ["sequence", "observedAtUtc", "lifecycle", "health"]);
  integer(object.sequence); time(object.observedAtUtc); scalar(object.lifecycle, 64); scalar(object.health, 64);
  if (request) { requireValue(lifecycle.has(object.lifecycle as string) && health.has(object.health as string)); }
}
function inventory(value: unknown, request: boolean, limits: ManagementWireLimits): void {
  const object = shape(value, request, ["sequence", "observedAtUtc", "modules", "categories", "jobs"]);
  integer(object.sequence); time(object.observedAtUtc);
  unique(array(object.modules, limits.maximumModules).map(item => {
    const module = shape(item, request, ["kind", "version"], ["details"]);
    const kind = alias(module.kind, 128); scalar(module.version, 128);
    optional(module, "details", details => requireValue(details instanceof OpaqueJsonObject || isObject(details)));
    return kind;
  }));
  const categories = new Set<string>();
  for (const item of array(object.categories, limits.maximumCategories)) {
    const category = shape(item, request, ["executionScope", "categoryId", "displayName"], ["description"]);
    const key = tuple(bytes(category.executionScope, 256), bytes(category.categoryId, 128));
    requireValue(!categories.has(key)); categories.add(key); bytes(category.displayName, 256);
    optional(category, "description", description => bytes(description, 2048, true));
  }
  const jobs = new Set<string>();
  let observations = 0;
  for (const item of array(object.jobs, limits.maximumJobs)) {
    const job = shape(item, request, ["executionScope", "jobId", "categoryId", "displayName", "supportedModes"],
      ["description", "effectiveConfiguration", "observations", "resourceScopeDescription"]);
    const scope = bytes(job.executionScope, 256);
    const key = tuple(scope, bytes(job.jobId, 128));
    requireValue(!jobs.has(key) && categories.has(tuple(scope, bytes(job.categoryId, 128)))); jobs.add(key);
    bytes(job.displayName, 256); optional(job, "description", description => bytes(description, 2048, true));
    optional(job, "resourceScopeDescription", description => bytes(description, 1024));
    const modes = array(job.supportedModes, 2, 1).map(mode => scalar(mode, 64)); unique(modes);
    if (request) { requireValue(modes.every(mode => mode === "continuous" || mode === "scheduled")); }
    optional(job, "effectiveConfiguration", item => {
      const configuration = shape(item, request, ["revision", "enabled"]); integer(configuration.revision, true); boolean(configuration.enabled);
    });
    optional(job, "observations", item => {
      const entries = array(item, limits.maximumJobObservations - observations); observations += entries.length;
      const resources: string[] = [];
      for (const entry of entries) {
        jobObservation(entry, request);
        const observation = entry as ObjectValue;
        requireValue(modes.includes(observation.mode as string)); resources.push(tuple(observation.resourceId, observation.mode));
      }
      unique(resources);
    });
  }
}
function jobObservation(value: unknown, request: boolean): void {
  const object = shape(value, request, ["resourceId", "mode", "state"], ["lastOutcome", "observedOwner", "lastOccurrenceUtc", "nextOccurrenceUtc"]);
  bytes(object.resourceId, 512); scalar(object.mode, 64); scalar(object.state, 64);
  if (request) { requireValue((object.mode === "continuous" || object.mode === "scheduled") && jobStates.has(object.state as string)); }
  optional(object, "lastOutcome", item => bytes(item, 128));
  optional(object, "lastOccurrenceUtc", time); optional(object, "nextOccurrenceUtc", time);
  optional(object, "observedOwner", item => {
    const owner = shape(item, request, ["ownerApplication", "instanceId", "generation", "expiresAtUtc", "observedAtUtc"]);
    bytes(owner.ownerApplication, 1024); bytes(owner.instanceId, 128); integer(owner.generation); time(owner.expiresAtUtc); time(owner.observedAtUtc);
  });
}
function instance(value: unknown): void {
  const object = shape(value, false, ["registrationId", "applicationId", "reportedInstanceId", "instanceEvidence", "processStartedAtUtc",
    "applicationVersion", "protocolMajor", "registeredAtUtc", "presence"], ["lastSeenAtUtc", "closedAtUtc", "observation",
    "observationReceivedAtUtc", "inventorySequence", "inventoryReceivedAtUtc"]);
  id(object.registrationId); id(object.applicationId); scalar(object.reportedInstanceId, 1024); scalar(object.applicationVersion, 512);
  positiveInt(object.protocolMajor); time(object.processStartedAtUtc); time(object.registeredAtUtc); scalar(object.presence, 64);
  for (const name of ["lastSeenAtUtc", "closedAtUtc", "observationReceivedAtUtc", "inventoryReceivedAtUtc"]) { optional(object, name, time); }
  optional(object, "inventorySequence", integer); optional(object, "observation", item => observation(item, false));
  const evidence = shape(object.instanceEvidence, false, ["kind"], ["reportedIdentifier", "verifiedIdentifier"]);
  scalar(evidence.kind, 64); optional(evidence, "reportedIdentifier", item => scalar(item, 1024));
  optional(evidence, "verifiedIdentifier", item => scalar(item, 1024));
  const reported = Object.hasOwn(evidence, "reportedIdentifier"); const verified = Object.hasOwn(evidence, "verifiedIdentifier");
  requireValue(evidence.kind === "notProvided" ? !reported && !verified : evidence.kind === "reported" ? reported && !verified : evidence.kind !== "verified" || verified);
}
function inventoryItem(value: unknown, limits: ManagementWireLimits): void {
  const object = shape(value, false, ["registrationId", "applicationVersion", "receivedAtUtc", "inventory"]);
  id(object.registrationId); scalar(object.applicationVersion, 512); time(object.receivedAtUtc); inventory(object.inventory, false, limits);
}
function metadataResult(value: unknown): void {
  // The opposite union field may deliberately be absent or null. Its nonnull
  // branch is checked below instead of using the ordinary known-field null rule.
  const object = shape(value, false, ["operation", "resultSource", "originalCompletion"], ["attemptCompletion"]);
  reference(object.operation); scalar(object.resultSource, 64); scalar(object.originalCompletion, 64);
  if (object.resultSource === "replayed" || object.resultSource === "found") { requireValue(object.originalCompletion !== "unknown" && object.originalCompletion !== "notCommitted"); }
  const hasResult = object.result !== null && object.result !== undefined;
  const hasRejection = object.rejection !== null && object.rejection !== undefined;
  requireValue(hasResult !== hasRejection);
  if (hasResult) {
    const result = shape(object.result, false, ["commandVersion", "applicationId", "decision", "code", "metadata", "metadataRevision", "metadataETag", "decidedAtUtc"]);
    version(result.commandVersion); id(result.applicationId); scalar(result.decision, 64); alias(result.code, 128); metadata(result.metadata);
    integer(result.metadataRevision); requireValue(isStrongETag(result.metadataETag)); time(result.decidedAtUtc);
  } else {
    const rejection = shape(object.rejection, false, ["code", "message"]);
    requireValue(typeof rejection.code === "string" && rejection.code.trim().length > 0 && /^[\u0020-\u007e]{1,128}$/u.test(rejection.code));
    requireValue(typeof rejection.message === "string" && rejection.message.length <= 1024
      && !/^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]*$/u.test(rejection.message)
      && !rejection.message.includes("\0") && validUnicode(rejection.message) && encoder.encode(rejection.message).length <= 1024);
  }
  optional(object, "attemptCompletion", item => {
    const attempt = shape(item, false, ["outcome", "cleanupSucceeded"], ["cleanupCodes"]); scalar(attempt.outcome, 64); boolean(attempt.cleanupSucceeded);
    optional(attempt, "cleanupCodes", codes => array(codes, 16).forEach(code => alias(code, 128)));
  });
}
function problem(value: unknown): void {
  const object = shape(value, false, ["type", "title", "status", "code"], ["detail", "correlationId", "operation", "result"]);
  scalar(object.type, 2048); try { new URL(object.type as string); } catch { throw new ManagementWireError(); }
  scalar(object.title, 256); requireValue(Number.isSafeInteger(object.status) && (object.status as number) >= 400 && (object.status as number) <= 599);
  alias(object.code, 128); optional(object, "detail", item => scalar(item, 2048, true)); optional(object, "correlationId", item => scalar(item, 128));
  optional(object, "operation", reference); optional(object, "result", item => {
    metadataResult(item); requireValue(Object.hasOwn(object, "operation"));
    const left = object.operation as ObjectValue; const right = (item as ObjectValue).operation as ObjectValue;
    requireValue(left.name === right.name && left.scope === right.scope && left.key === right.key);
  });
}
function reference(value: unknown): void {
  const object = shape(value, false, ["name", "scope", "key"]); alias(object.name, 64); bytes(object.scope, 256); requireValue(isOperationKey(object.key));
}
function features(value: unknown): void {
  unique(array(value, 64).map(item => {
    const feature = shape(item, false, ["operationId", "version", "method", "path", "requiredCapabilities"]);
    const name = alias(feature.operationId, 128); positiveInt(feature.version);
    requireValue(typeof feature.method === "string" && /^[A-Za-z]{1,64}$/u.test(feature.method));
    const path = bytes(feature.path, 512); requireValue(path.startsWith("/") && !path.startsWith("//") && !/[?#\\]/u.test(path));
    unique(array(feature.requiredCapabilities, 8).map(capability => alias(capability, 128))); return name;
  }));
}
function shape(value: unknown, closed: boolean, required: readonly string[], optional: readonly string[] = []): ObjectValue {
  requireValue(isObject(value) && !(value instanceof OpaqueJsonObject));
  const object = value as ObjectValue;
  for (const name of required) { requireValue(Object.hasOwn(object, name) && object[name] !== null && object[name] !== undefined); }
  for (const name in object) {
    if (!Object.hasOwn(object, name)) { continue; }
    const known = required.includes(name) || optional.includes(name);
    requireValue(!closed || known); if (known) { requireValue(object[name] !== null && object[name] !== undefined); }
  }
  return object;
}
function isObject(value: unknown): value is ObjectValue { return value !== null && typeof value === "object" && !Array.isArray(value); }
function array(value: unknown, maximum: number, minimum = 0): unknown[] { requireValue(Array.isArray(value) && value.length >= minimum && value.length <= maximum); return value as unknown[]; }
function optional(object: ObjectValue, name: string, validate: (value: unknown) => unknown): void { if (Object.hasOwn(object, name)) { validate(object[name]); } }
function scalar(value: unknown, maximum: number, empty = false): string { requireValue(isScalarText(value, maximum, empty)); return value as string; }
function bytes(value: unknown, maximum: number, empty = false): string { requireValue(isUtf8Text(value, maximum, empty)); return value as string; }
function alias(value: unknown, maximum: number): string { requireValue(typeof value === "string" && value.length <= maximum && /^[A-Za-z0-9._:-]+$/u.test(value)); return value as string; }
function id(value: unknown): void { requireValue(isUuid(value)); }
function time(value: unknown): void { requireValue(isTimestamp(value)); }
function integer(value: unknown, zero = false): void { requireValue(isOrderedInteger(value, zero)); }
function boolean(value: unknown): void { requireValue(typeof value === "boolean"); }
function version(value: unknown): void { if (value !== 1) { throw new ManagementWireError("wire.unsupported_version"); } }
function positiveInt(value: unknown): number { requireValue(Number.isSafeInteger(value) && (value as number) > 0 && (value as number) <= 2147483647); return value as number; }
function unique(values: readonly unknown[]): void { requireValue(new Set(values).size === values.length); }
function tuple(...values: unknown[]): string { return JSON.stringify(values); }
function requireValue(condition: boolean): asserts condition { if (!condition) { throw new ManagementWireError(); } }
