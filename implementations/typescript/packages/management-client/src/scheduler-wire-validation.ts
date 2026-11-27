import { SchedulerWireError } from "./scheduler-wire.js";
import type { SchedulerIntegrityCheck, SchedulerSchema, SchedulerWireLimits } from "./scheduler-wire.js";
import { parseWireJson, stringifyWire, validUnicode } from "./wire-json.js";
import { isCursor, isOperationKey, isOrderedInteger, isScalarText, isStrongETag, isTimestamp, isUtf8Text, isUuid } from "./wire-validation.js";

type Row = Record<string, unknown>;
const kinds = ["job", "triggerProvider", "ruleEngine"];
const operations = ["inspect", "configure", "createConfigured", "manual", "cancel"];
const editors = ["text", "number", "boolean", "enum", "duration", "group", "calendar"];
const runStates = ["pending", "admitted", "running", "retryPending", "succeeded", "failed", "cancelled", "outcomeUnknown"];
const timeUnits = ["milliseconds", "seconds", "minutes", "hours", "days"];
const encoder = new TextEncoder();
const maxTicks = 42336000000000n;

export function validateScheduler(schema: SchedulerSchema, value: unknown, request: boolean, limits: SchedulerWireLimits,
  checks: SchedulerIntegrityCheck[], nonIntegerNumbers: ReadonlySet<string>, rawValues: ReadonlyMap<string, string>): void {
  const command = schema.startsWith("Command") || schema.endsWith("Command")
    || ["SchedulerAttemptReport", "SchedulerHistoryQuery", "CapabilityQuery"].includes(schema);
  if (request !== command) { throw new TypeError("Select the declared scheduler request or response direction."); }
  const validator = new SchedulerValidator(request, limits, checks, nonIntegerNumbers, rawValues);
  validator.index(value, "");
  validator.dto(schema.startsWith("Command") ? schema.slice(7) : schema, value);
}

/** Only these declared DTOs are interpreted. Future response fields remain inert frozen data. */
class SchedulerValidator {
  private readonly paths = new WeakMap<object, string>();
  constructor(private readonly request: boolean, private readonly limits: SchedulerWireLimits,
    private readonly checks: SchedulerIntegrityCheck[], private readonly nonIntegers: ReadonlySet<string>,
    private readonly rawValues: ReadonlyMap<string, string>) { }

  index(value: unknown, path: string): void {
    if (value === null || typeof value !== "object") { return; }
    this.paths.set(value, path);
    for (const [key, child] of Object.entries(value)) { this.index(child, path + "/" + escapePointer(key)); }
  }

  dto(name: string, value: unknown): void {
    switch (name) {
      case "MessageContract": this.contract(value); return;
      case "MessageOperationReference": this.businessOperation(value); return;
      case "OperationReference": this.operation(value); return;
      case "TransactionFacts": this.transaction(value); return;
      case "OperationRejection": this.rejection(value); return;
      case "WorkerBuildReference": this.build(value); return;
      case "JobExecutionSupport": this.support(value); return;
      case "CapabilityKey": this.key(value); return;
      case "ConfigurationMember": this.member(value, 1, { nodes: 0 }); return;
      case "ConfigurationShape": this.declaration(value, 1, { nodes: 0 }); return;
      case "ConfigurationContractReference": this.configuration(value); return;
      case "FormFieldHint": this.field(value); return;
      case "FormGroup": this.group(value); return;
      case "NamedLookupReference": this.lookup(value); return;
      case "FormDescription": this.form(value); return;
      case "CapabilityDefinition": this.definition(value); return;
      case "CapabilityParticipation": this.participation(value); return;
      case "CapabilityDefinitionPage": this.page(value, row => this.definition(row)); distinct((value as Row).items as unknown[], item => keyIdentity((item as Row).key)); return;
      case "CapabilityParticipationPage": this.page(value, row => this.participation(row)); return;
      case "SchedulerTriggerReference": this.triggerReference(value); return;
      case "SchedulerSettingsSelection": this.selection(value); return;
      case "SchedulerInputMapping": this.inputMapping(value); return;
      case "SchedulerResourceMapping": this.resource(value); return;
      case "SchedulerRetryPolicy": this.retry(value); return;
      case "SchedulerCalendar": this.calendar(value); return;
      case "SchedulerExecutionSettings": this.executionSettings(value); return;
      case "SchedulerTriggerDraft": this.triggerDraft(value); return;
      case "SchedulerTaskDraft": this.taskDraft(value); return;
      case "SchedulerProgress": this.progress(value); return;
      case "SchedulerCapturedSettings": this.captured(value); return;
      case "SchedulerExecutionIdentity": this.identity(value); return;
      case "SchedulerActor": this.actor(value); return;
      case "SchedulerTrigger": this.trigger(value); return;
      case "SchedulerTask": this.task(value); return;
      case "SchedulerTriggerCommand": this.triggerCommand(value); return;
      case "SchedulerTaskCommand": this.taskCommand(value); return;
      case "SchedulerStateCommand": this.stateCommand(value, ["enable", "disable", "retire", "activate", "resume", "cancel"]); return;
      case "SchedulerTriggerStateCommand": this.stateCommand(value, ["enable", "disable", "retire", "resume"]); return;
      case "SchedulerTaskStateCommand": this.stateCommand(value, ["enable", "disable", "retire", "activate", "resume"]); return;
      case "SchedulerRunCancellationCommand": this.stateCommand(value, ["cancel"]); return;
      case "SchedulerTaskPreviewCommand": this.previewCommand(value); return;
      case "SchedulerCalendarPreviewCommand": this.calendarPreview(value); return;
      case "SchedulerValidationIssue": this.issue(value); return;
      case "SchedulerAffectedTask": this.affectedTask(value); return;
      case "SchedulerTaskPreview": this.preview(value); return;
      case "SchedulerEventCommand": this.eventCommand(value); return;
      case "SchedulerEventReceipt": this.eventReceipt(value); return;
      case "SchedulerManualRunCommand": this.manual(value); return;
      case "SchedulerRunCapsule": this.capsule(value); return;
      case "SchedulerRun": this.run(value); return;
      case "SchedulerAttempt": this.attempt(value); return;
      case "SchedulerAttemptReport": this.report(value); return;
      case "SchedulerAttemptCommand": this.attemptCommand(value); return;
      case "SchedulerAttemptAdmission": this.admission(value); return;
      case "SchedulerRunDetail": this.runDetail(value); return;
      case "SchedulerEvent": this.event(value); return;
      case "SchedulerHistoryQuery": this.history(value); return;
      case "SchedulerTriggerPage": this.page(value, row => this.trigger(row)); return;
      case "SchedulerTaskPage": this.page(value, row => this.task(row)); return;
      case "SchedulerRunPage": this.page(value, row => this.run(row)); return;
      case "SchedulerAttemptPage": this.page(value, row => this.attempt(row)); return;
      case "SchedulerDecision": this.decision(value); return;
      case "SchedulerOperationResult": this.result(value); return;
      case "SchedulerProblem": this.problem(value); return;
      case "CapabilityQuery": this.query(value); return;
      case "AdministrativeAction": this.action(value); return;
      case "ConfigurationActions": this.actions(value); return;
      case "ConfigurationCohortChoice": this.cohort(value); return;
      case "ConfigurationCohortChoicePage": this.page(value, row => this.cohort(row)); return;
      default: throw new TypeError("Select a declared scheduler wire DTO.");
    }
  }

  private shape(value: unknown, required: readonly string[], optional: readonly string[] = [], closed = this.request): Row {
    need(value !== null && typeof value === "object" && !Array.isArray(value));
    const row = value as Row;
    for (const key of required) { need(has(row, key) && row[key] !== null && row[key] !== undefined); }
    for (const key of optional) { if (has(row, key)) { need(row[key] !== null && row[key] !== undefined); } }
    if (closed) { need(Object.keys(row).every(key => required.includes(key) || optional.includes(key))); }
    return row;
  }
  private number(row: Row, key: string, minimum: number, maximum: number): number {
    const value = row[key];
    need(typeof value === "number" && Number.isSafeInteger(value) && value >= minimum && value <= maximum);
    need(!this.nonIntegers.has((this.paths.get(row) ?? "") + "/" + escapePointer(key)));
    return value as number;
  }
  private command(row: Row): void { need(this.number(row, "commandVersion", 1, 1) === 1); }
  private bounded(value: unknown, maximum: number): void {
    const raw = value !== null && typeof value === "object" ? this.rawValues.get(this.paths.get(value) ?? "") : undefined;
    if (raw !== undefined) { need(encoder.encode(raw).length <= maximum); return; }
    try { stringifyWire(value, this.limits.maximumJsonDepth, maximum); }
    catch { throw new SchedulerWireError("scheduler.wire.tooLarge"); }
  }
  private payload(value: unknown, object = true, maximum = this.limits.maximumPayloadBytes): unknown {
    need(typeof value === "string" && value.length <= maximum && validUnicode(value));
    need(encoder.encode(value as string).length <= maximum);
    let parsed: unknown;
    try { parsed = parseWireJson(value as string, this.limits.maximumJsonDepth).value; }
    catch { throw new SchedulerWireError(); }
    if (object) { need(parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)); }
    return parsed;
  }
  private contract(value: unknown): void {
    const row = this.shape(value, ["name", "version"]);
    need(typeof row.name === "string" && /^[A-Za-z0-9._/-]{1,128}$/u.test(row.name));
    this.number(row, "version", 1, 2147483647);
  }
  private businessOperation(value: unknown): void {
    const row = this.shape(value, ["operationName", "requestedScope", "key"]);
    alias(row.operationName); text(row.requestedScope, 256);
    need(typeof row.key === "string" && /^[\u0021-\u007e]{1,128}$/u.test(row.key));
  }
  private operation(value: unknown): void {
    const row = this.shape(value, ["name", "scope", "key"]); alias(row.name); text(row.scope, 256); need(isOperationKey(row.key));
  }
  private transaction(value: unknown): void {
    const row = this.shape(value, ["outcome", "cleanupSucceeded"], ["cleanupCodes"]);
    scalar(row.outcome, 64); bool(row.cleanupSucceeded);
    optional(row, "cleanupCodes", item => array(item, 16).forEach(code => need(typeof code === "string" && /^[A-Za-z0-9._:-]{1,128}$/u.test(code))));
  }
  private rejection(value: unknown): void {
    const row = this.shape(value, ["code", "message"]);
    need(typeof row.code === "string" && /^[\u0020-\u007e]{1,128}$/u.test(row.code) && row.code.trim().length > 0);
    need(typeof row.message === "string" && row.message.length <= 1024 && validUnicode(row.message)
      && !row.message.includes("\0") && row.message.trim().length > 0 && encoder.encode(row.message).length <= 1024);
  }
  private build(value: unknown): void {
    const row = this.shape(value, ["applicationVersion", "buildId", "handlerId"], ["sourceRevision"]);
    for (const key of ["applicationVersion", "buildId", "handlerId"]) { text(row[key], 512); }
    optional(row, "sourceRevision", item => text(item, 128));
  }
  private support(value: unknown): void {
    const row = this.shape(value, ["currentContract", "supportedStarts", "supportedRecoveries"], ["checkpointContract", "operationName"]);
    this.contract(row.currentContract);
    for (const key of ["supportedStarts", "supportedRecoveries"]) {
      const contracts = array(row[key], 16); contracts.forEach(item => this.contract(item)); distinct(contracts, contractIdentity);
    }
    need((row.supportedStarts as unknown[]).some(item => contractIdentity(item) === contractIdentity(row.currentContract)));
    optional(row, "checkpointContract", item => this.contract(item)); optional(row, "operationName", alias);
  }
  private key(value: unknown): void {
    const row = this.shape(value, ["ownerApplication", "kind", "executionScope", "definitionId", "definitionVersion"]);
    scalar(row.ownerApplication, 255); token(row.kind); text(row.executionScope, 256); text(row.definitionId, 128); integer(row.definitionVersion);
    if (this.request) { need(kinds.includes(String(row.kind))); }
  }
  private configuration(value: unknown): void {
    const row = this.shape(value, ["configurationId", "schemaVersion", "shape"]);
    need(isOperationKey(row.configurationId)); integer(row.schemaVersion); this.declaration(row.shape, 1, { nodes: 0 });
  }
  private member(value: unknown, depth: number, budget: { nodes: number }): void {
    const row = this.shape(value, ["name", "required", "shape"]); scalar(row.name, 256); bool(row.required); this.declaration(row.shape, depth, budget);
  }
  private declaration(value: unknown, depth: number, budget: { nodes: number }): void {
    need(depth <= 16 && ++budget.nodes <= 1024);
    const row = this.shape(value, ["kind", "nullable"], ["members", "element", "allowedValues"]); bool(row.nullable);
    need(["object", "array", "dictionary", "string", "number", "integer", "boolean"].includes(String(row.kind)));
    if (row.kind === "object") {
      need(!has(row, "element") && !has(row, "allowedValues"));
      const members = array(row.members, 256); members.forEach(item => this.member(item, depth + 1, budget)); distinct(members, item => String((item as Row).name));
    } else if (row.kind === "array" || row.kind === "dictionary") {
      need(!has(row, "members") && !has(row, "allowedValues")); this.declaration(row.element, depth + 1, budget);
    } else {
      need(!has(row, "members") && !has(row, "element"));
      if (has(row, "allowedValues")) {
        need(row.kind === "string"); const values = array(row.allowedValues, 256); need(values.length > 0);
        values.forEach(item => text(item, 1024, true)); distinct(values, String);
      }
    }
  }
  private field(value: unknown): void {
    const row = this.shape(value, ["path", "editor", "label"], ["description", "groupId", "order", "unit", "readOnly", "lookupId"]);
    pointer(row.path); token(row.editor); text(row.label, 256); optional(row, "description", item => text(item, 2048, true));
    optional(row, "groupId", token); optional(row, "lookupId", token); optional(row, "readOnly", bool);
    if (has(row, "order")) { this.number(row, "order", -10000, 10000); }
    optional(row, "unit", item => need([...timeUnits, "bytes", "count", "percent"].includes(String(item))));
    if (this.request) { need(editors.includes(String(row.editor))); }
  }
  private group(value: unknown): void {
    const row = this.shape(value, ["id", "label"], ["order"]); token(row.id); text(row.label, 256);
    if (has(row, "order")) { this.number(row, "order", -10000, 10000); }
  }
  private lookup(value: unknown): void {
    const row = this.shape(value, ["id", "ownerApplication", "executionScope", "lookupName", "contract"]);
    token(row.id); scalar(row.ownerApplication, 255); text(row.executionScope, 256); token(row.lookupName); this.contract(row.contract);
  }
  private form(value: unknown, root?: unknown): void {
    const row = this.shape(value, ["fields"], ["groups", "lookups"]);
    const fields = array(row.fields, 128); fields.forEach(item => this.field(item)); distinct(fields, item => String((item as Row).path));
    const groups = has(row, "groups") ? array(row.groups, 32) : []; groups.forEach(item => this.group(item)); distinct(groups, item => String((item as Row).id));
    const lookups = has(row, "lookups") ? array(row.lookups, 16) : []; lookups.forEach(item => this.lookup(item)); distinct(lookups, item => String((item as Row).id));
    for (const item of fields) {
      const field = item as Row;
      if (has(field, "groupId")) { need(groups.some(item => (item as Row).id === field.groupId)); }
      if (has(field, "lookupId")) { need(lookups.some(item => (item as Row).id === field.lookupId)); }
      if (root !== undefined) {
        const target = resolve(root, field.path as string);
        switch (field.editor) {
          case "text": need(target.kind === "string" && !has(target, "allowedValues")); break;
          case "number": need(target.kind === "integer" || target.kind === "number"); break;
          case "boolean": need(target.kind === "boolean"); break;
          case "enum": need(target.kind === "string" && Array.isArray(target.allowedValues) && target.allowedValues.length > 0); break;
          case "duration": need(["integer", "number", "string"].includes(String(target.kind)) && timeUnits.includes(String(field.unit))); break;
          case "group": case "calendar": need(target.kind === "object"); break;
        }
      }
      if (has(field, "unit")) { need(field.editor === "duration" || field.editor === "number"); }
    }
  }
  private definition(value: unknown): void {
    const row = this.shape(value, ["key", "displayName", "supportedOperations"], ["description", "categoryId", "configurationContract",
      "inputContract", "inputShape", "eventContract", "eventShape", "resultSummaryContract", "resultSummaryShape", "form", "executionSupport"]);
    this.key(row.key); text(row.displayName, 256); optional(row, "description", item => text(item, 2048, true)); optional(row, "categoryId", item => text(item, 128));
    const declared = array(row.supportedOperations, 16); need(declared.length > 0); declared.forEach(token); distinct(declared, String);
    if (this.request) { need(declared.every(item => operations.includes(String(item)))); }
    optional(row, "configurationContract", item => this.configuration(item));
    for (const prefix of ["input", "event", "resultSummary"]) {
      need(has(row, prefix + "Contract") === has(row, prefix + "Shape"));
      optional(row, prefix + "Contract", item => this.contract(item));
      optional(row, prefix + "Shape", item => this.declaration(item, 1, { nodes: 0 }));
    }
    const kind = (row.key as Row).kind;
    if (declared.includes("configure") || declared.includes("createConfigured") || has(row, "form")) { need(has(row, "configurationContract")); }
    if (kind === "job") { need(!has(row, "eventContract") && !declared.includes("createConfigured")); if (declared.includes("manual")) { need(has(row, "inputContract")); } }
    if (kind === "triggerProvider") { need(has(row, "eventContract")); }
    if (has(row, "executionSupport")) { need(kind === "job"); this.support(row.executionSupport); }
    if (has(row, "form")) { this.form(row.form, (row.configurationContract as Row).shape); }
    this.bounded(value, this.limits.maximumDefinitionBytes);
  }
  private participation(value: unknown): void {
    const row = this.shape(value, ["key", "registrationId", "inventorySequence", "observedAtUtc", "receivedAtUtc", "availability"], ["draining", "build"]);
    this.key(row.key); id(row.registrationId); integer(row.inventorySequence); time(row.observedAtUtc); time(row.receivedAtUtc); token(row.availability);
    optional(row, "draining", bool); optional(row, "build", item => this.build(item));
  }
  private triggerReference(value: unknown): void {
    const row = this.shape(value, ["applicationId", "triggerId"]); id(row.applicationId); id(row.triggerId);
  }
  private selection(value: unknown): void {
    const row = this.shape(value, ["applicationId", "configurationId", "schemaVersion", "releaseId", "assignmentGeneration"]);
    id(row.applicationId); need(isOperationKey(row.configurationId)); integer(row.schemaVersion); id(row.releaseId); integer(row.assignmentGeneration);
  }
  private inputMapping(value: unknown): void {
    const row = this.shape(value, ["targetPath", "kind"], ["sourcePath", "constantJson", "transformation", "parametersJson"]);
    pointer(row.targetPath); token(row.kind); optional(row, "sourcePath", pointer);
    optional(row, "constantJson", item => this.payload(item, false)); optional(row, "transformation", item => this.contract(item));
    optional(row, "parametersJson", item => this.payload(item));
    switch (row.kind) {
      case "field": need(has(row, "sourcePath") && !has(row, "constantJson") && !has(row, "transformation") && !has(row, "parametersJson")); break;
      case "constant": need(has(row, "constantJson") && !has(row, "sourcePath") && !has(row, "transformation") && !has(row, "parametersJson")); break;
      case "transform": need(has(row, "sourcePath") && has(row, "transformation") && !has(row, "constantJson")); break;
      default: need(!this.request);
    }
  }
  private resource(value: unknown): void {
    const row = this.shape(value, ["kind"], ["constant", "eventPath", "resolver", "parametersJson"]); token(row.kind);
    optional(row, "constant", item => text(item, 512)); optional(row, "eventPath", pointer);
    optional(row, "resolver", item => this.contract(item)); optional(row, "parametersJson", item => this.payload(item));
    switch (row.kind) {
      case "constant": need(has(row, "constant") && !has(row, "eventPath") && !has(row, "resolver") && !has(row, "parametersJson")); break;
      case "field": need(has(row, "eventPath") && !has(row, "constant") && !has(row, "resolver") && !has(row, "parametersJson")); break;
      case "resolver": need(has(row, "resolver") && !has(row, "constant") && !has(row, "eventPath")); break;
      default: need(!this.request);
    }
  }
  private retry(value: unknown): void {
    const row = this.shape(value, ["enabled", "maximumAttempts", "initialDelayMs", "maximumDelayMs"]); bool(row.enabled);
    const attempts = this.number(row, "maximumAttempts", 1, 16); integer(row.initialDelayMs); integer(row.maximumDelayMs);
    need(BigInt(row.initialDelayMs as string) >= 5000n && BigInt(row.initialDelayMs as string) <= BigInt(row.maximumDelayMs as string)
      && BigInt(row.maximumDelayMs as string) <= 60000n && (row.enabled === true || attempts === 1));
  }
  private calendar(value: unknown): void {
    const row = this.shape(value, ["form", "missedRunPolicy"], ["delayMs", "localTime", "timeZoneId", "weekdays", "maximumCatchUp",
      "catchUpWindowMs", "initialDelayMs", "runImmediately", "failurePolicy"]);
    token(row.form); token(row.missedRunPolicy); optional(row, "failurePolicy", token); optional(row, "runImmediately", bool);
    optional(row, "delayMs", item => calendarMilliseconds(item, false)); optional(row, "catchUpWindowMs", item => calendarMilliseconds(item, false));
    optional(row, "initialDelayMs", item => calendarMilliseconds(item, true));
    optional(row, "localTime", localTime); optional(row, "timeZoneId", item => text(item, 128));
    if (has(row, "maximumCatchUp")) { this.number(row, "maximumCatchUp", 1, 10000); }
    if (has(row, "weekdays")) {
      const days = array(row.weekdays, 7); need(days.length > 0); distinct(days, String);
      const base = (this.paths.get(row) ?? "") + "/weekdays/";
      days.forEach((day, index) => need(typeof day === "number" && Number.isInteger(day) && day >= 0 && day <= 6 && !this.nonIntegers.has(base + index)));
    }
    if (this.request) {
      need(["fixedDelay", "daily", "weekly"].includes(String(row.form)) && ["skip", "runLatestOnce", "catchUpBounded"].includes(String(row.missedRunPolicy)));
      if (has(row, "failurePolicy")) { need(row.failurePolicy === "pause" || row.failurePolicy === "continue"); }
    }
    if (row.form === "fixedDelay") {
      need(has(row, "delayMs") && !["localTime", "timeZoneId", "weekdays", "maximumCatchUp", "catchUpWindowMs"].some(key => has(row, key)));
      if (row.initialDelayMs === "0") { need(row.runImmediately === true); }
      if (row.runImmediately === true && has(row, "initialDelayMs")) { need(row.initialDelayMs === "0"); }
    } else if (row.form === "daily" || row.form === "weekly") {
      need(has(row, "localTime") && has(row, "timeZoneId") && !has(row, "delayMs") && !has(row, "initialDelayMs"));
      need(row.runImmediately !== true && (!has(row, "failurePolicy") || row.failurePolicy === "pause"));
      const zone = row.timeZoneId as string;
      need((zone === "UTC" || zone.includes("/") && zone.split("/").every(part => part !== "" && part !== "." && part !== ".."))
        && /^[A-Za-z0-9/_+-]+$/u.test(zone));
      need(row.form === "weekly" ? has(row, "weekdays") : !has(row, "weekdays"));
      if (row.missedRunPolicy === "catchUpBounded") { need(has(row, "maximumCatchUp") && has(row, "catchUpWindowMs")); }
      else { need(!has(row, "maximumCatchUp") && !has(row, "catchUpWindowMs")); }
    }
  }
  private executionSettings(value: unknown): void {
    const row = this.shape(value, ["idleDelayTicks", "maximumDrainTicks", "acquisitionRetryDelayTicks"], ["timeoutTicks"]);
    for (const key of ["idleDelayTicks", "maximumDrainTicks", "acquisitionRetryDelayTicks", "timeoutTicks"]) {
      if (has(row, key)) { integer(row[key]); need(BigInt(row[key] as string) <= (key === "maximumDrainTicks" ? 864000000000n : maxTicks)); }
    }
  }
  private triggerDraft(value: unknown): void {
    const row = this.shape(value, ["name", "provider"], ["configuration", "enabled"]); text(row.name, 256); this.key(row.provider);
    if (this.request) { need((row.provider as Row).kind === "triggerProvider"); }
    optional(row, "configuration", item => this.selection(item)); optional(row, "enabled", bool);
  }
  private taskDraft(value: unknown): void {
    const row = this.shape(value, ["name", "trigger", "job", "resource", "inputMappings", "jobConfiguration"],
      ["triggerConfiguration", "executionProfileId", "retry", "enabled", "executionSettings"]);
    text(row.name, 256); this.triggerReference(row.trigger); this.key(row.job); if (this.request) { need((row.job as Row).kind === "job"); }
    this.resource(row.resource); const mappings = array(row.inputMappings, this.limits.maximumMappings);
    mappings.forEach(item => this.inputMapping(item)); distinct(mappings, item => String((item as Row).targetPath)); this.selection(row.jobConfiguration);
    optional(row, "triggerConfiguration", item => this.selection(item)); optional(row, "executionProfileId", item => scalar(item, 64));
    optional(row, "retry", item => this.retry(item)); optional(row, "enabled", bool); optional(row, "executionSettings", item => this.executionSettings(item));
  }
  private progress(value: unknown): void {
    const row = this.shape(value, [], ["completed", "total", "message"]); optional(row, "completed", item => integer(item, true));
    optional(row, "total", item => integer(item, true)); optional(row, "message", item => text(item, 1024));
    if (has(row, "completed") && has(row, "total")) { need(BigInt(row.completed as string) <= BigInt(row.total as string)); }
  }
  private captured(value: unknown): void {
    const row = this.shape(value, ["selection", "payloadJson", "sha256"]); this.selection(row.selection); this.payload(row.payloadJson); digest(row.sha256);
    this.checks.push({ payload: row.payloadJson as string, sha256: row.sha256 as string });
  }
  private identity(value: unknown): void {
    const row = this.shape(value, ["application", "environment", "issuer", "subject", "bindingId", "profileId"]);
    scalar(row.application, 255); scalar(row.environment, 255); scalar(row.issuer, 1024); scalar(row.subject, 1024); scalar(row.bindingId, 1024); scalar(row.profileId, 64);
  }
  private actor(value: unknown): void {
    const row = this.shape(value, ["kind", "issuer", "subject"], ["displayName", "application", "environment"]);
    token(row.kind); scalar(row.issuer, 1024); scalar(row.subject, 1024); optional(row, "displayName", item => text(item, 256));
    optional(row, "application", item => scalar(item, 255)); optional(row, "environment", item => scalar(item, 255));
  }
  private trigger(value: unknown): void {
    const row = this.shape(value, ["applicationId", "triggerId", "revision", "eTag", "definition", "state", "configuredBy", "changedAtUtc"], ["calendar", "waitingReason"]);
    id(row.applicationId); id(row.triggerId); integer(row.revision); etag(row.eTag); this.triggerDraft(row.definition); token(row.state);
    this.actor(row.configuredBy); time(row.changedAtUtc); optional(row, "calendar", item => this.calendar(item)); optional(row, "waitingReason", item => text(item, 128));
  }
  private task(value: unknown): void {
    const row = this.shape(value, ["applicationId", "taskId", "desiredRevision", "eTag", "definition", "state", "configuredBy", "changedAtUtc"], ["activeRevision", "waitingReason"]);
    id(row.applicationId); id(row.taskId); integer(row.desiredRevision); optional(row, "activeRevision", integer); etag(row.eTag);
    this.taskDraft(row.definition); token(row.state); this.actor(row.configuredBy); time(row.changedAtUtc); optional(row, "waitingReason", item => text(item, 128));
  }
  private triggerCommand(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "triggerId", "definition", "reason"], [], true);
    this.command(row); id(row.triggerId); this.triggerDraft(row.definition); text(row.reason, 1024);
  }
  private taskCommand(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "taskId", "definition", "reason"], ["expectedTriggerETag", "previewDigest"], true);
    this.command(row); id(row.taskId); this.taskDraft(row.definition); text(row.reason, 1024);
    optional(row, "expectedTriggerETag", etag); optional(row, "previewDigest", digest);
  }
  private stateCommand(value: unknown, intents: readonly string[]): void {
    const row = this.shape(value, ["commandVersion", "intent", "reason"], [], true); this.command(row); text(row.reason, 1024); need(intents.includes(String(row.intent)));
  }
  private previewCommand(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "definition"], ["candidateJobPayloadJson", "candidateTriggerPayloadJson", "taskId"], true);
    this.command(row); this.taskDraft(row.definition); optional(row, "candidateJobPayloadJson", item => this.payload(item));
    optional(row, "candidateTriggerPayloadJson", item => this.payload(item)); optional(row, "taskId", id);
  }
  private calendarPreview(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "calendar", "fromUtc"], ["count"], true); this.command(row); this.calendar(row.calendar); time(row.fromUtc);
    if (has(row, "count")) { this.number(row, "count", 1, 100); }
  }
  private issue(value: unknown): void {
    const row = this.shape(value, ["path", "code", "message"]); text(row.path, 1024, true); text(row.code, 128); text(row.message, 1024);
  }
  private affectedTask(value: unknown): void {
    const row = this.shape(value, ["applicationId", "taskId", "name", "revision"]); id(row.applicationId); id(row.taskId); text(row.name, 256); integer(row.revision);
  }
  private preview(value: unknown): void {
    const row = this.shape(value, ["valid", "issues", "affectedTasks", "impactFullyDisclosed", "nextOccurrencesUtc", "activationState"],
      ["draftDigest", "taskETag", "triggerETag", "preconditions", "definition"]);
    bool(row.valid); bool(row.impactFullyDisclosed); token(row.activationState);
    const issues = array(row.issues, 128); issues.forEach(item => this.issue(item));
    const affected = array(row.affectedTasks, this.limits.maximumAffectedTasks); affected.forEach(item => this.affectedTask(item));
    distinct(affected, item => JSON.stringify([(item as Row).applicationId, (item as Row).taskId]));
    array(row.nextOccurrencesUtc, 100).forEach(time); optional(row, "draftDigest", digest); optional(row, "taskETag", etag); optional(row, "triggerETag", etag);
    optional(row, "preconditions", item => array(item, 16).forEach(item => this.selection(item))); optional(row, "definition", item => this.taskDraft(item));
    if (row.valid === true) {
      need(issues.length === 0);
      const context = ["draftDigest", "triggerETag", "preconditions", "definition"].filter(key => has(row, key));
      need(context.length === 4 || context.length === 0 && !has(row, "taskETag"));
      if (context.length === 4) {
        const preconditions = row.preconditions as unknown[]; const definition = row.definition as Row;
        distinct(preconditions, selectionIdentity);
        for (const key of ["jobConfiguration", "triggerConfiguration"]) {
          if (has(definition, key)) { need(preconditions.some(item => selectionIdentity(item) === selectionIdentity(definition[key]))); }
        }
      }
    }
  }
  private eventCommand(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "eventId", "occurredAtUtc", "contract", "payloadJson"], ["scheduledAtUtc", "correlationId", "causationId"], true);
    this.command(row); id(row.eventId); time(row.occurredAtUtc); this.contract(row.contract); this.payload(row.payloadJson, false); this.context(row);
  }
  private eventReceipt(value: unknown): void {
    const row = this.shape(value, ["applicationId", "triggerId", "eventId", "disposition", "acceptedAtUtc"], ["runIds", "runCount"]);
    id(row.applicationId); id(row.triggerId); id(row.eventId); token(row.disposition); time(row.acceptedAtUtc);
    need(has(row, "runIds") === has(row, "runCount"));
    if (has(row, "runIds")) {
      const runs = array(row.runIds, this.limits.maximumFanout); runs.forEach(id); distinct(runs, String);
      need(this.number(row, "runCount", 0, this.limits.maximumFanout) === runs.length);
    }
  }
  private manual(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "eventId", "reason"], [], true); this.command(row); id(row.eventId); text(row.reason, 1024);
  }
  private context(row: Row): void {
    optional(row, "scheduledAtUtc", time); optional(row, "correlationId", item => scalar(item, 128)); optional(row, "causationId", id);
  }
  private capsule(value: unknown): void {
    const row = this.shape(value, ["runId", "applicationId", "taskId", "taskRevision", "trigger", "eventId", "job", "resourceId", "inputEnvelopeJson",
      "executionContract", "originalOperation", "jobSettings", "executionIdentity", "configuredBy", "producer", "executionSettings"],
      ["triggerSettings", "requestedBy", "scheduledAtUtc", "eventOccurredAtUtc", "eventReceivedAtUtc", "correlationId", "causationId"]);
    id(row.runId); id(row.applicationId); id(row.taskId); integer(row.taskRevision); this.triggerReference(row.trigger); id(row.eventId);
    this.key(row.job); if (this.request) { need((row.job as Row).kind === "job"); }
    text(row.resourceId, 512); this.contract(row.executionContract); this.businessOperation(row.originalOperation);
    this.envelope(row.inputEnvelopeJson, row.originalOperation); this.captured(row.jobSettings); this.identity(row.executionIdentity); this.actor(row.configuredBy); this.actor(row.producer);
    this.executionSettings(row.executionSettings); optional(row, "triggerSettings", item => this.captured(item)); optional(row, "requestedBy", item => this.actor(item));
    optional(row, "eventOccurredAtUtc", time); optional(row, "eventReceivedAtUtc", time); this.context(row); this.bounded(value, this.limits.maximumCapsuleBytes);
  }
  private envelope(value: unknown, original: unknown): void {
    this.payload(value, true, 81920);
    const parsed = parseWireJson(value as string, this.limits.maximumJsonDepth, new Set(["/payload"]));
    const required = ["envelopeVersion", "messageId", "kind", "messageType", "schemaVersion", "producerApplication", "producerEnvironment", "createdAtUtc", "operation"];
    const allowed = ["payload", "correlationId", "causationId", "headers"];
    const row = this.shape(parsed.value, required, ["correlationId", "causationId", "headers"], false);
    need(has(row, "payload") && Object.keys(row).every(key => required.includes(key) || allowed.includes(key)));
    // This nested object was parsed from the original string, preserving lexical integer evidence separately.
    need(!parsed.nonIntegerNumbers.has("/envelopeVersion") && !parsed.nonIntegerNumbers.has("/schemaVersion"));
    need(row.envelopeVersion === 1 && row.kind === "command"); messageId(row.messageId);
    need(typeof row.messageType === "string" && /^[A-Za-z0-9._/-]{1,128}$/u.test(row.messageType));
    need(typeof row.schemaVersion === "number" && Number.isSafeInteger(row.schemaVersion) && row.schemaVersion >= 1 && row.schemaVersion <= 2147483647);
    scalar(row.producerApplication, 255); scalar(row.producerEnvironment, 255); messageTime(row.createdAtUtc);
    this.shape(row.operation, ["operationName", "requestedScope", "key"], [], true);
    this.businessOperation(row.operation); const op = row.operation as Row; const expected = original as Row;
    need(op.operationName === expected.operationName && op.requestedScope === expected.requestedScope && op.key === expected.key);
    const raw = parsed.rawValues.get("/payload"); need(raw !== undefined && encoder.encode(raw).length <= this.limits.maximumPayloadBytes);
    // Existing MessageWire measures all original envelope bytes outside the exact payload token.
    need(encoder.encode(value as string).length - encoder.encode(raw).length <= 16384);
    optional(row, "correlationId", item => text(item, 128)); optional(row, "causationId", messageId);
    if (has(row, "headers")) {
      const headers = this.shape(row.headers, [], Object.keys(row.headers as object), true); need(Object.keys(headers).length <= 32);
      let bytes = 0;
      for (const [key, item] of Object.entries(headers)) { alias(key); text(item, 1024); bytes += encoder.encode(key).length + encoder.encode(item as string).length; }
      need(bytes <= 8192);
    }
  }
  private run(value: unknown): void {
    const row = this.shape(value, ["applicationId", "runId", "taskId", "taskRevision", "trigger", "eventId", "job", "resourceId", "state", "createdAtUtc", "attemptCount"],
      ["businessOutcome", "completedAtUtc", "waitingReason", "cancellationRequested", "scheduledAtUtc", "nextAttemptAtUtc", "correlationId", "causationId"]);
    id(row.applicationId); id(row.runId); id(row.taskId); integer(row.taskRevision); this.triggerReference(row.trigger); id(row.eventId); this.key(row.job);
    if (this.request) { need((row.job as Row).kind === "job"); }
    text(row.resourceId, 512); token(row.state); time(row.createdAtUtc); integer(row.attemptCount, true);
    optional(row, "businessOutcome", token); optional(row, "completedAtUtc", time); optional(row, "waitingReason", item => text(item, 128));
    optional(row, "cancellationRequested", bool); optional(row, "nextAttemptAtUtc", time); this.context(row);
  }
  private attempt(value: unknown): void {
    const row = this.shape(value, ["runId", "attemptId", "number", "role", "state", "admittedAtUtc", "build", "executor", "registrationId", "effectivePolicy", "sequence"],
      ["enteredAtUtc", "settledAtUtc", "businessCompletion", "callbackSettled", "cleanupSucceeded", "summaryJson", "acquisitionId", "durationMs"]);
    id(row.runId); id(row.attemptId); integer(row.number); token(row.role); token(row.state); time(row.admittedAtUtc); this.build(row.build);
    this.identity(row.executor); id(row.registrationId); scalar(row.effectivePolicy, 1024); integer(row.sequence);
    optional(row, "enteredAtUtc", time); optional(row, "settledAtUtc", time); optional(row, "businessCompletion", item => scalar(item, 64));
    optional(row, "callbackSettled", bool); optional(row, "cleanupSucceeded", bool); optional(row, "summaryJson", item => this.payload(item, true, this.limits.maximumSummaryBytes));
    optional(row, "acquisitionId", id); optional(row, "durationMs", item => integer(item, true));
  }
  private report(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "sequence", "observedAtUtc", "phase"],
      ["businessOutcome", "businessCompletion", "callbackSettled", "cleanupSucceeded", "progress", "summaryJson", "durationMs"], true);
    this.command(row); integer(row.sequence); time(row.observedAtUtc); need(["entered", "progress", "settled", "interrupted"].includes(String(row.phase)));
    optional(row, "businessOutcome", token); optional(row, "businessCompletion", item => scalar(item, 64)); optional(row, "callbackSettled", bool); optional(row, "cleanupSucceeded", bool);
    optional(row, "progress", item => this.progress(item)); optional(row, "summaryJson", item => this.payload(item, true, this.limits.maximumSummaryBytes)); optional(row, "durationMs", item => integer(item, true));
  }
  private attemptCommand(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "attemptId", "registrationId", "role", "executionContract", "build", "ownershipGeneration"], ["acquisitionId", "effectivePolicy"], true);
    this.command(row); id(row.attemptId); id(row.registrationId); need(row.role === "execution" || row.role === "recovery"); this.contract(row.executionContract); this.build(row.build);
    integer(row.ownershipGeneration); optional(row, "acquisitionId", id); optional(row, "effectivePolicy", item => scalar(item, 1024));
  }
  private admission(value: unknown): void {
    const row = this.shape(value, ["disposition"], ["reasonCode", "attempt"]); token(row.disposition); optional(row, "reasonCode", item => text(item, 128));
    optional(row, "attempt", item => this.attempt(item)); if (row.disposition === "admitted" || row.disposition === "replayed") { need(has(row, "attempt")); }
  }
  private runDetail(value: unknown): void {
    const row = this.shape(value, ["run", "attempts", "capturedContentAccess"], ["capsule", "nextAttemptCursor"]); this.run(row.run); token(row.capturedContentAccess);
    const run = row.run as Row; const attempts = array(row.attempts, this.limits.maximumPageSize);
    attempts.forEach(item => { this.attempt(item); need((item as Row).runId === run.runId); }); distinct(attempts, item => String((item as Row).attemptId));
    optional(row, "nextAttemptCursor", item => need(isCursor(item)));
    need((row.capturedContentAccess === "allowed") === has(row, "capsule"));
    if (has(row, "capsule")) {
      this.capsule(row.capsule); const capsule = row.capsule as Row;
      for (const key of ["applicationId", "runId", "taskId", "taskRevision", "eventId", "resourceId"]) { need(capsule[key] === run[key]); }
      need(triggerIdentity(capsule.trigger) === triggerIdentity(run.trigger) && keyIdentity(capsule.job) === keyIdentity(run.job));
    }
  }
  private event(value: unknown): void {
    const row = this.shape(value, ["applicationId", "triggerId", "eventId", "occurredAtUtc", "receivedAtUtc", "contract", "producer", "payloadAccess"],
      ["payloadJson", "sourceSettings", "scheduledAtUtc", "correlationId", "causationId"]);
    id(row.applicationId); id(row.triggerId); id(row.eventId); time(row.occurredAtUtc); time(row.receivedAtUtc); this.contract(row.contract); this.actor(row.producer); token(row.payloadAccess);
    if (row.payloadAccess === "allowed") { need(has(row, "payloadJson")); this.payload(row.payloadJson, false); optional(row, "sourceSettings", item => this.captured(item)); }
    else { need(!has(row, "payloadJson") && !has(row, "sourceSettings")); }
    this.context(row);
  }
  private history(value: unknown): void {
    const row = this.shape(value, [], ["taskId", "executionScope", "jobId", "eventId", "resourceId", "fromUtc", "untilUtc", "state", "applicationVersion", "buildId", "executionContract", "triggerApplicationId", "triggerId"], true);
    for (const key of ["taskId", "eventId", "triggerApplicationId", "triggerId"]) { optional(row, key, id); }
    optional(row, "executionScope", item => text(item, 256)); optional(row, "jobId", item => text(item, 128)); optional(row, "resourceId", item => text(item, 512));
    optional(row, "fromUtc", time); optional(row, "untilUtc", time); optional(row, "state", item => need(runStates.includes(String(item))));
    optional(row, "applicationVersion", item => text(item, 512)); optional(row, "buildId", item => text(item, 512)); optional(row, "executionContract", item => this.contract(item));
    const task = has(row, "taskId"); const job = has(row, "executionScope") || has(row, "jobId");
    const event = has(row, "eventId") || has(row, "triggerApplicationId") || has(row, "triggerId");
    need(Number(task) + Number(job) + Number(event) === 1);
    if (job) { need(has(row, "executionScope") && has(row, "jobId")); }
    if (event) { need(has(row, "eventId") && has(row, "triggerApplicationId") && has(row, "triggerId")); }
    if (has(row, "fromUtc") && has(row, "untilUtc")) { need((row.fromUtc as string) <= (row.untilUtc as string)); }
  }
  private page(value: unknown, validate: (value: unknown) => void): void {
    const row = this.shape(value, ["items"], ["nextCursor"]); array(row.items, this.limits.maximumPageSize).forEach(validate); optional(row, "nextCursor", item => need(isCursor(item)));
  }
  private decision(value: unknown): void {
    const row = this.shape(value, ["commandVersion", "kind", "decision", "code", "decidedAtUtc", "applicationId", "objectId"], ["revision", "eTag", "runId", "attemptId"]);
    this.command(row); token(row.kind); token(row.decision); text(row.code, 128); time(row.decidedAtUtc); id(row.applicationId); id(row.objectId);
    optional(row, "revision", integer); optional(row, "eTag", etag); optional(row, "runId", id); optional(row, "attemptId", id);
  }
  private result(value: unknown): void {
    const row = this.shape(value, ["operation", "resultSource", "originalCompletion"], ["result", "attemptCompletion", "rejection"]);
    this.operation(row.operation); token(row.resultSource); token(row.originalCompletion); need(has(row, "result") !== has(row, "rejection"));
    optional(row, "result", item => this.decision(item)); optional(row, "attemptCompletion", item => this.transaction(item)); optional(row, "rejection", item => this.rejection(item));
    if (row.resultSource === "replayed" || row.resultSource === "found") { need(row.originalCompletion === "committed"); }
  }
  private problem(value: unknown): void {
    const row = this.shape(value, ["type", "title", "status", "code"], ["detail", "correlationId", "operation", "result"]);
    scalar(row.type, 1024); try { new URL(row.type as string); } catch { throw new SchedulerWireError(); }
    text(row.title, 256); this.number(row, "status", 400, 599); text(row.code, 128); optional(row, "detail", item => text(item, 1024, true));
    optional(row, "correlationId", item => scalar(item, 128)); optional(row, "operation", item => this.operation(item)); optional(row, "result", item => this.result(item));
    if (has(row, "operation") && has(row, "result")) { need(operationIdentity(row.operation) === operationIdentity((row.result as Row).operation)); }
  }
  private query(value: unknown): void {
    const row = this.shape(value, [], ["kind", "executionScope", "definitionId", "definitionVersion"], true);
    optional(row, "kind", token); optional(row, "executionScope", item => text(item, 256)); optional(row, "definitionId", item => text(item, 128)); optional(row, "definitionVersion", integer);
  }
  private action(value: unknown): void {
    const row = this.shape(value, ["operationId", "supported", "allowed"], ["reasonCode"]); text(row.operationId, 128); bool(row.supported); bool(row.allowed);
    optional(row, "reasonCode", item => text(item, 128)); need(row.supported === true || row.allowed === false);
  }
  private actions(value: unknown): void {
    const row = this.shape(value, ["resourceId", "actions"]); scalar(row.resourceId, 1024); const actions = array(row.actions, 32);
    actions.forEach(item => this.action(item)); distinct(actions, item => String((item as Row).operationId));
  }
  private cohort(value: unknown): void { const row = this.shape(value, ["cohortId", "displayName"]); need(isOperationKey(row.cohortId)); scalar(row.displayName, 256); }
}

function need(value: unknown): asserts value { if (!value) { throw new SchedulerWireError(); } }
function has(row: Row, key: string): boolean { return Object.prototype.hasOwnProperty.call(row, key); }
function optional(row: Row, key: string, validate: (value: unknown) => unknown): void { if (has(row, key)) { validate(row[key]); } }
function array(value: unknown, maximum: number): unknown[] { need(Array.isArray(value) && value.length <= maximum); return value; }
function scalar(value: unknown, maximum: number, empty = false): void { need(isScalarText(value, maximum, empty)); }
function text(value: unknown, maximum: number, empty = false): void { need(isUtf8Text(value, maximum, empty)); }
function token(value: unknown): void { need(typeof value === "string" && /^[A-Za-z0-9._-]{1,128}$/u.test(value)); }
function alias(value: unknown): void { need(typeof value === "string" && /^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/u.test(value)); }
function bool(value: unknown): void { need(typeof value === "boolean"); }
function id(value: unknown): void { need(isUuid(value)); }
function integer(value: unknown, zero = false): void { need(isOrderedInteger(value, zero)); }
function time(value: unknown): void { need(isTimestamp(value)); }
function messageId(value: unknown): void {
  need(typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu.test(value)
    && value !== "00000000-0000-0000-0000-000000000000");
}
function messageTime(value: unknown): void {
  need(typeof value === "string" && /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z$/u.test(value));
  const normalized = value.length === 20 ? value.slice(0, -1) + ".0000000Z" : value.slice(0, -1).padEnd(27, "0") + "Z";
  time(normalized);
}
function etag(value: unknown): void { need(isStrongETag(value)); }
function digest(value: unknown): void { need(typeof value === "string" && /^[0-9a-f]{64}$/u.test(value)); }
function pointer(value: unknown): void { text(value, 1024); need(typeof value === "string" && /^\/(?:[^~]|~[01])*$/u.test(value)); }
function distinct(values: readonly unknown[], key: (value: unknown) => string): void { need(new Set(values.map(key)).size === values.length); }
function contractIdentity(value: unknown): string { const row = value as Row; return JSON.stringify([row.name, row.version]); }
function keyIdentity(value: unknown): string { const row = value as Row; return JSON.stringify([row.ownerApplication, row.kind, row.executionScope, row.definitionId, row.definitionVersion]); }
function triggerIdentity(value: unknown): string { const row = value as Row; return JSON.stringify([row.applicationId, row.triggerId]); }
function operationIdentity(value: unknown): string { const row = value as Row; return JSON.stringify([row.name, row.scope, row.key]); }
function selectionIdentity(value: unknown): string { const row = value as Row; return JSON.stringify([row.applicationId, row.configurationId, row.schemaVersion, row.releaseId, row.assignmentGeneration]); }
function escapePointer(value: string): string { return value.replace(/~/gu, "~0").replace(/\//gu, "~1"); }
function resolve(root: unknown, path: string): Row {
  let current = root as Row;
  for (const segment of path.slice(1).split("/")) {
    need(current?.kind === "object" && Array.isArray(current.members));
    const name = segment.replace(/~1/gu, "/").replace(/~0/gu, "~");
    const member = current.members.find(item => (item as Row).name === name) as Row | undefined; need(member !== undefined);
    current = member.shape as Row;
  }
  return current;
}
function localTime(value: unknown): void {
  need(typeof value === "string" && /^([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]\.[0-9]{7}$/u.test(value));
}
function calendarMilliseconds(value: unknown, zero: boolean): void {
  need(typeof value === "string" && /^(0|[1-9][0-9]*)(?:\.[0-9]{0,3}[1-9])?$/u.test(value) && value.length <= 15);
  const [whole, fraction = ""] = (value as string).split(".");
  const ticks = BigInt(whole!) * 10000n + BigInt(fraction.padEnd(4, "0"));
  need((zero ? ticks >= 0n : ticks > 0n) && ticks <= maxTicks);
}
