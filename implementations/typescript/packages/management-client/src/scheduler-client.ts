import { ClientProtocolFailure, createFetchTransport, executeRequest, validateReadRetries, validateTimeout } from "@cislib/client-core";
import type { ClientCallContext, ClientTransport, CredentialProvider, RequestObservation } from "@cislib/client-core";
import { captureBaseUrl } from "./client.js";
import type { Page, PageRequest, ReportReceipt } from "./models.js";
import type {
  SchedulerAttempt, SchedulerAttemptAdmission, SchedulerAttemptCommand, SchedulerAttemptReport, SchedulerCalendarPreviewCommand,
  SchedulerCallResult, SchedulerEvent, SchedulerEventCommand, SchedulerEventReceipt, SchedulerHistoryQuery, SchedulerManualRunCommand,
  SchedulerOperationResult, SchedulerProblem, SchedulerRun, SchedulerRunCapsule, SchedulerRunDetail, SchedulerStateCommand,
  SchedulerSettingsSelection, SchedulerTask, SchedulerTaskCommand, SchedulerTaskDraft, SchedulerTaskPreview, SchedulerTaskPreviewCommand, SchedulerTrigger, SchedulerTriggerCommand
} from "./scheduler-models.js";
import { captureSchedulerWireLimits, decodeScheduler, encodeScheduler, SchedulerWireError } from "./scheduler-wire.js";
import type { SchedulerSchema, SchedulerWireLimits } from "./scheduler-wire.js";
import { decodeManagement, isCursor, isOperationKey, isStrongETag, isUuid, ManagementWireError } from "./wire.js";

export interface ManagementSchedulerClientOptions {
  readonly baseUrl: string;
  readonly transport?: ClientTransport;
  readonly credentials?: CredentialProvider;
  readonly timeoutMs?: number;
  readonly readRetries?: number;
  readonly wireLimits?: Partial<SchedulerWireLimits>;
  readonly observe?: (event: RequestObservation) => void;
}
type Captured = Readonly<Record<string, unknown>>;
interface Call {
  readonly method: "GET" | "POST" | "PUT";
  readonly path: (signal: AbortSignal) => string | Promise<string>;
  readonly schema: SchedulerSchema | "ReportReceipt";
  readonly context?: ClientCallContext;
  readonly bodySchema?: SchedulerSchema;
  readonly body?: unknown;
  readonly operationKey?: string;
  readonly operationName?: string;
  readonly ifMatch?: string;
  readonly create?: boolean;
  readonly mutating?: boolean;
  readonly applicationId?: string;
  readonly objectId?: string;
  readonly bodyId?: "triggerId" | "taskId";
  readonly decisionKinds?: readonly string[];
  readonly check?: (value: unknown, captured: Captured | undefined) => void;
}
const operations = Object.freeze({ trigger: "management.scheduler.trigger.write", task: "management.scheduler.task.write", run: "management.scheduler.run.write" });
const triggerKinds = Object.freeze(["triggerCreated", "triggerUpdated", "triggerStateChanged"]);
const taskKinds = Object.freeze(["taskCreated", "taskUpdated", "taskActivated", "taskStateChanged", "manualRunAdmitted"]);

/** Passive typed transport. Selection constructs no handler, producer, worker or recurrence loop. */
export class ManagementSchedulerClient {
  readonly baseUrl: string;
  readonly wireLimits: SchedulerWireLimits;
  private readonly transport: ClientTransport;
  private readonly credentials: CredentialProvider | undefined;
  private readonly timeoutMs: number;
  private readonly readRetries: number;
  private readonly observe: ((event: RequestObservation) => void) | undefined;

  constructor(options: ManagementSchedulerClientOptions) {
    this.baseUrl = captureBaseUrl(options.baseUrl);
    this.wireLimits = captureSchedulerWireLimits(options.wireLimits);
    this.transport = options.transport ?? createFetchTransport();
    this.credentials = options.credentials;
    this.timeoutMs = validateTimeout(options.timeoutMs ?? 30000);
    this.readRetries = validateReadRetries(options.readRetries ?? 0);
    this.observe = options.observe;
  }

  listTriggers(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<SchedulerCallResult<Page<SchedulerTrigger>>> {
    return this.call({ method: "GET", path: () => base(applicationId) + "/triggers" + pageQuery(page), schema: "SchedulerTriggerPage",
      check: value => echo((value as Page<SchedulerTrigger>).items.every(row => row.applicationId === applicationId)), ...withContext(context) });
  }

  getTrigger(applicationId: string, triggerId: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerTrigger>> {
    return this.call({ method: "GET", path: () => triggerPath(applicationId, triggerId), schema: "SchedulerTrigger",
      check: value => { const row = value as SchedulerTrigger; echo(row.applicationId === applicationId && row.triggerId === triggerId); }, ...withContext(context) });
  }

  createTrigger(applicationId: string, operationKey: string, command: SchedulerTriggerCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "POST", path: () => base(applicationId) + "/triggers", schema: "SchedulerOperationResult",
      bodySchema: "SchedulerTriggerCommand", body: command, applicationId, bodyId: "triggerId", operationKey,
      operationName: operations.trigger, create: true, decisionKinds: ["triggerCreated"], ...withContext(context) });
  }

  updateTrigger(applicationId: string, triggerId: string, operationKey: string, ifMatch: string, command: SchedulerTriggerCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "PUT", path: () => triggerPath(applicationId, triggerId), schema: "SchedulerOperationResult",
      bodySchema: "SchedulerTriggerCommand", body: command, applicationId, objectId: triggerId, bodyId: "triggerId", operationKey,
      operationName: operations.trigger, ifMatch, decisionKinds: ["triggerUpdated"], ...withContext(context) });
  }

  changeTriggerState(applicationId: string, triggerId: string, operationKey: string, ifMatch: string, command: SchedulerStateCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "POST", path: () => triggerPath(applicationId, triggerId) + "/state", schema: "SchedulerOperationResult",
      bodySchema: "SchedulerTriggerStateCommand", body: command, applicationId, objectId: triggerId, operationKey,
      operationName: operations.trigger, ifMatch, decisionKinds: ["triggerStateChanged"], ...withContext(context) });
  }

  getTriggerOperation(applicationId: string, triggerId: string, operationKey: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "GET", path: () => triggerPath(applicationId, triggerId) + "/operations/" + key(operationKey), schema: "SchedulerOperationResult",
      applicationId, objectId: triggerId, operationKey, operationName: operations.trigger, decisionKinds: triggerKinds, ...withContext(context) });
  }

  listTasks(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<SchedulerCallResult<Page<SchedulerTask>>> {
    return this.call({ method: "GET", path: () => base(applicationId) + "/tasks" + pageQuery(page), schema: "SchedulerTaskPage",
      check: value => echo((value as Page<SchedulerTask>).items.every(row => row.applicationId === applicationId)), ...withContext(context) });
  }

  getTask(applicationId: string, taskId: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerTask>> {
    return this.call({ method: "GET", path: () => taskPath(applicationId, taskId), schema: "SchedulerTask",
      check: value => { const row = value as SchedulerTask; echo(row.applicationId === applicationId && row.taskId === taskId); }, ...withContext(context) });
  }

  createTask(applicationId: string, operationKey: string, command: SchedulerTaskCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "POST", path: () => base(applicationId) + "/tasks", schema: "SchedulerOperationResult",
      bodySchema: "SchedulerTaskCommand", body: command, applicationId, bodyId: "taskId", operationKey,
      operationName: operations.task, create: true, decisionKinds: ["taskCreated"], ...withContext(context) });
  }

  updateTask(applicationId: string, taskId: string, operationKey: string, ifMatch: string, command: SchedulerTaskCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "PUT", path: () => taskPath(applicationId, taskId), schema: "SchedulerOperationResult",
      bodySchema: "SchedulerTaskCommand", body: command, applicationId, objectId: taskId, bodyId: "taskId", operationKey,
      operationName: operations.task, ifMatch, decisionKinds: ["taskUpdated"], ...withContext(context) });
  }

  changeTaskState(applicationId: string, taskId: string, operationKey: string, ifMatch: string, command: SchedulerStateCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "POST", path: () => taskPath(applicationId, taskId) + "/state", schema: "SchedulerOperationResult",
      bodySchema: "SchedulerTaskStateCommand", body: command, applicationId, objectId: taskId, operationKey,
      operationName: operations.task, ifMatch, decisionKinds: ["taskActivated", "taskStateChanged"], ...withContext(context) });
  }

  getTaskOperation(applicationId: string, taskId: string, operationKey: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "GET", path: () => taskPath(applicationId, taskId) + "/operations/" + key(operationKey), schema: "SchedulerOperationResult",
      applicationId, objectId: taskId, operationKey, operationName: operations.task, decisionKinds: taskKinds, ...withContext(context) });
  }

  previewTask(applicationId: string, command: SchedulerTaskPreviewCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerTaskPreview>> {
    return this.call({ method: "POST", path: () => base(applicationId) + "/tasks/preview", schema: "SchedulerTaskPreview", bodySchema: "SchedulerTaskPreviewCommand", body: command, mutating: false,
      check: (value, captured) => {
        const preview = value as SchedulerTaskPreview;
        if (preview.valid) {
          const requested = captured?.definition as SchedulerTaskDraft | undefined;
          echo(preview.definition !== undefined && preview.draftDigest !== undefined && preview.triggerETag !== undefined && preview.preconditions !== undefined
            && (captured?.taskId === undefined || preview.taskETag !== undefined) && requested !== undefined && sameDraft(preview.definition, requested)
            && hasSettingsPreconditions(preview.definition, preview.preconditions));
        }
      }, ...withContext(context) });
  }

  previewCalendar(applicationId: string, command: SchedulerCalendarPreviewCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerTaskPreview>> {
    return this.call({ method: "POST", path: () => base(applicationId) + "/calendar/preview", schema: "SchedulerTaskPreview", bodySchema: "SchedulerCalendarPreviewCommand", body: command, mutating: false,
      check: (value, captured) => {
        const preview = value as SchedulerTaskPreview;
        echo(preview.definition === undefined && preview.draftDigest === undefined && preview.triggerETag === undefined && preview.taskETag === undefined
          && preview.preconditions === undefined && preview.nextOccurrencesUtc.length <= (captured?.count as number | undefined ?? 5));
      }, ...withContext(context) });
  }

  runTaskNow(applicationId: string, taskId: string, operationKey: string, ifMatch: string, command: SchedulerManualRunCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "POST", path: () => taskPath(applicationId, taskId) + "/runs", schema: "SchedulerOperationResult",
      bodySchema: "SchedulerManualRunCommand", body: command, applicationId, objectId: taskId, operationKey,
      operationName: operations.task, ifMatch, decisionKinds: ["manualRunAdmitted"], ...withContext(context) });
  }

  cancelRun(applicationId: string, runId: string, operationKey: string, command: SchedulerStateCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "POST", path: () => runPath(applicationId, runId) + "/cancellation", schema: "SchedulerOperationResult",
      bodySchema: "SchedulerRunCancellationCommand", body: command, applicationId, objectId: runId, operationKey,
      operationName: operations.run, decisionKinds: ["cancellationRequested"], ...withContext(context) });
  }

  getRunOperation(applicationId: string, runId: string, operationKey: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerOperationResult>> {
    return this.call({ method: "GET", path: () => runPath(applicationId, runId) + "/operations/" + key(operationKey), schema: "SchedulerOperationResult",
      applicationId, objectId: runId, operationKey, operationName: operations.run, decisionKinds: ["cancellationRequested"], ...withContext(context) });
  }

  getRuns(applicationId: string, query: SchedulerHistoryQuery, page?: PageRequest, context?: ClientCallContext): Promise<SchedulerCallResult<Page<SchedulerRun>>> {
    let captured: SchedulerHistoryQuery;
    return this.call({ method: "GET", path: async signal => {
      captured = await detach<SchedulerHistoryQuery>("SchedulerHistoryQuery", query, this.wireLimits, signal);
      return base(applicationId) + "/runs" + historyQuery(captured, page);
    }, schema: "SchedulerRunPage", check: value => echo((value as Page<SchedulerRun>).items.every(row => row.applicationId === applicationId
      && (captured.taskId === undefined || row.taskId === captured.taskId)
      && (captured.executionScope === undefined || row.job.executionScope === captured.executionScope && row.job.definitionId === captured.jobId)
      && (captured.eventId === undefined || row.eventId === captured.eventId && row.trigger.applicationId === captured.triggerApplicationId && row.trigger.triggerId === captured.triggerId)
      && (captured.resourceId === undefined || row.resourceId === captured.resourceId)
      && (captured.state === undefined || row.state === captured.state))), ...withContext(context) });
  }

  getRun(applicationId: string, runId: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerRunDetail>> {
    return this.call({ method: "GET", path: () => runPath(applicationId, runId), schema: "SchedulerRunDetail",
      check: value => { const row = (value as SchedulerRunDetail).run; echo(row.applicationId === applicationId && row.runId === runId); }, ...withContext(context) });
  }

  getAttempts(applicationId: string, runId: string, page?: PageRequest, context?: ClientCallContext): Promise<SchedulerCallResult<Page<SchedulerAttempt>>> {
    return this.call({ method: "GET", path: () => runPath(applicationId, runId) + "/attempts" + pageQuery(page), schema: "SchedulerAttemptPage",
      check: value => echo((value as Page<SchedulerAttempt>).items.every(row => row.runId === runId)), ...withContext(context) });
  }

  submitEvent(applicationId: string, triggerId: string, command: SchedulerEventCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerEventReceipt>> {
    return this.call({ method: "POST", path: () => triggerPath(applicationId, triggerId) + "/events", schema: "SchedulerEventReceipt",
      bodySchema: "SchedulerEventCommand", body: command, check: (value, captured) => {
        const row = value as SchedulerEventReceipt; echo(row.applicationId === applicationId && row.triggerId === triggerId && row.eventId === captured?.eventId);
      }, ...withContext(context) });
  }

  getEventReceipt(applicationId: string, triggerId: string, eventId: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerEventReceipt>> {
    return this.call({ method: "GET", path: () => eventPath(applicationId, triggerId, eventId) + "/receipt", schema: "SchedulerEventReceipt",
      check: value => { const row = value as SchedulerEventReceipt; echo(row.applicationId === applicationId && row.triggerId === triggerId && row.eventId === eventId); }, ...withContext(context) });
  }

  getEvent(applicationId: string, triggerId: string, eventId: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerEvent>> {
    return this.call({ method: "GET", path: () => eventPath(applicationId, triggerId, eventId) + "", schema: "SchedulerEvent",
      check: value => { const row = value as SchedulerEvent; echo(row.applicationId === applicationId && row.triggerId === triggerId && row.eventId === eventId); }, ...withContext(context) });
  }

  getEventRuns(applicationId: string, triggerId: string, eventId: string, page?: PageRequest, context?: ClientCallContext): Promise<SchedulerCallResult<Page<SchedulerRun>>> {
    return this.call({ method: "GET", path: () => eventPath(applicationId, triggerId, eventId) + "/runs" + pageQuery(page), schema: "SchedulerRunPage",
      check: value => echo((value as Page<SchedulerRun>).items.every(row => row.eventId === eventId && row.trigger.applicationId === applicationId && row.trigger.triggerId === triggerId)), ...withContext(context) });
  }

  getReadyRuns(registrationId: string, page?: PageRequest, context?: ClientCallContext): Promise<SchedulerCallResult<Page<SchedulerRun>>> {
    return this.call({ method: "GET", path: () => registrationPath(registrationId) + "/ready-runs" + pageQuery(page), schema: "SchedulerRunPage", ...withContext(context) });
  }

  getRunCapsule(registrationId: string, runId: string, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerRunCapsule>> {
    return this.call({ method: "GET", path: () => registrationPath(registrationId) + "/runs/" + uuid(runId) + "/capsule", schema: "SchedulerRunCapsule",
      check: value => echo((value as SchedulerRunCapsule).runId === runId), ...withContext(context) });
  }

  admitAttempt(applicationId: string, runId: string, command: SchedulerAttemptCommand, context?: ClientCallContext): Promise<SchedulerCallResult<SchedulerAttemptAdmission>> {
    return this.call({ method: "POST", path: () => runPath(applicationId, runId) + "/attempts", schema: "SchedulerAttemptAdmission",
      bodySchema: "SchedulerAttemptCommand", body: command, check: (value, captured) => {
        const attempt = (value as SchedulerAttemptAdmission).attempt;
        if (attempt !== undefined) {
          const build = captured?.build as SchedulerAttempt["build"];
          echo(attempt.runId === runId && attempt.attemptId === captured?.attemptId && attempt.registrationId === captured?.registrationId
            && attempt.role === captured?.role && attempt.build.applicationVersion === build.applicationVersion && attempt.build.buildId === build.buildId
            && attempt.build.handlerId === build.handlerId && attempt.build.sourceRevision === build.sourceRevision
            && attempt.acquisitionId === captured?.acquisitionId && (captured?.effectivePolicy === undefined || attempt.effectivePolicy === captured.effectivePolicy));
        }
      }, ...withContext(context) });
  }

  reportAttempt(applicationId: string, runId: string, attemptId: string, report: SchedulerAttemptReport, context?: ClientCallContext): Promise<SchedulerCallResult<ReportReceipt>> {
    return this.call({ method: "PUT", path: () => runPath(applicationId, runId) + "/attempts/" + uuid(attemptId) + "/report", schema: "ReportReceipt",
      bodySchema: "SchedulerAttemptReport", body: report, check: (value, captured) => {
        const row = value as ReportReceipt; echo(row.stream === "scheduler-attempt" && row.submittedSequence === captured?.sequence && row.acceptedSequence === captured?.sequence);
      }, ...withContext(context) });
  }

  private async call<T>(call: Call): Promise<SchedulerCallResult<T>> {
    let captured: Captured | undefined;
    let objectId = call.objectId;
    const result = await executeRequest<T, SchedulerProblem>({
      operation: "scheduler", transport: this.transport, timeoutMs: this.timeoutMs, readRetries: this.readRetries,
      ...withContext(call.context), ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }), ...(call.operationKey === undefined ? {} : { operationKey: call.operationKey }),
      prepare: async signal => {
        const suffix = await call.path(signal);
        const headers: Record<string, string> = { Accept: "application/json, application/problem+json" };
        if (call.operationKey !== undefined) { key(call.operationKey); }
        if (call.method !== "GET" && call.operationKey !== undefined) { headers["Idempotency-Key"] = call.operationKey; }
        if (call.create === true) { headers["If-None-Match"] = "*"; }
        if (call.ifMatch !== undefined) {
          if (!isStrongETag(call.ifMatch)) { throw new TypeError("Supply one strong current resource ETag."); }
          headers["If-Match"] = call.ifMatch;
        }
        const body = call.bodySchema === undefined ? undefined : await encodeScheduler(call.bodySchema, call.body, "request", this.wireLimits, signal);
        if (body !== undefined) {
          captured = JSON.parse(new TextDecoder().decode(body)) as Captured;
          if (call.bodyId !== undefined) {
            const bodyId = captured[call.bodyId];
            if (typeof bodyId !== "string" || objectId !== undefined && objectId !== bodyId) { throw new TypeError("The retained command identifier must match the route."); }
            objectId = bodyId;
          }
          headers["Content-Type"] = "application/json";
        }
        return { url: this.baseUrl + "management/v1/" + suffix, method: call.method, headers: Object.freeze(headers),
          ...(body === undefined ? {} : { body }), maximumResponseBytes: this.wireLimits.maximumBodyBytes,
          maximumErrorBytes: 65536, mutating: call.mutating ?? call.method !== "GET" };
      },
      decode: async (response, bytes, signal) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            const value = call.schema === "ReportReceipt" ? decodeManagement<T>("ReportReceipt", bytes, "response")
              : await decodeScheduler<T>(call.schema, bytes, "response", this.wireLimits, signal);
            if (call.schema === "SchedulerOperationResult") { checkOperation(value as SchedulerOperationResult, call, objectId); }
            call.check?.(value, captured);
            return { value };
          }
          if (contentType !== "application/problem+json") {
            if (bytes.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const problem = await decodeScheduler<SchedulerProblem>("SchedulerProblem", bytes, "response", this.wireLimits, signal);
          if (problem.status !== response.status) { throw new ClientProtocolFailure("response.problem_status_mismatch"); }
          if (problem.operation !== undefined) { checkReference(problem.operation, call); }
          if (problem.result !== undefined) { checkOperation(problem.result, call, objectId); }
          return { problem };
        } catch (error) {
          if (error instanceof SchedulerWireError || error instanceof ManagementWireError) {
            signal.throwIfAborted(); throw new ClientProtocolFailure(error.code);
          }
          throw error;
        }
      }
    });
    const uncertain = (call.mutating ?? call.method !== "GET") && (result.problem?.result?.originalCompletion === "unknown"
      || result.status !== undefined && result.status >= 500 && result.problem?.result === undefined);
    return Object.freeze({ ...result, ...(uncertain ? { mutationUncertain: true } : {}) });
  }
}

/** Structural optional ports keep existing Management client substitutes source-compatible. */
export type ManagementTriggerEventPort = Pick<ManagementSchedulerClient, "submitEvent" | "getEventReceipt">;
export type ManagementSchedulerWorkerClientPort = Pick<ManagementSchedulerClient, "getReadyRuns" | "getRunCapsule" | "admitAttempt" | "reportAttempt">;
export type ManagementSchedulerClientPort = Omit<ManagementSchedulerClient, "baseUrl" | "wireLimits" | keyof ManagementSchedulerWorkerClientPort>;

function withContext(context: ClientCallContext | undefined): { readonly context?: ClientCallContext } { return context === undefined ? {} : { context }; }
function uuid(value: string): string { if (!isUuid(value)) { throw new TypeError("Supply a canonical nonempty lowercase UUID."); } return value; }
function key(value: string): string { if (!isOperationKey(value)) { throw new TypeError("Retain the original bounded unreserved operation key excluding dot segments."); } return encodeURIComponent(value); }
function base(applicationId: string): string { return "applications/" + uuid(applicationId) + "/scheduler"; }
function triggerPath(applicationId: string, triggerId: string): string { return base(applicationId) + "/triggers/" + uuid(triggerId); }
function taskPath(applicationId: string, taskId: string): string { return base(applicationId) + "/tasks/" + uuid(taskId); }
function runPath(applicationId: string, runId: string): string { return base(applicationId) + "/runs/" + uuid(runId); }
function eventPath(applicationId: string, triggerId: string, eventId: string): string { return triggerPath(applicationId, triggerId) + "/events/" + uuid(eventId); }
function registrationPath(registrationId: string): string { return "registrations/" + uuid(registrationId) + "/scheduler"; }
function pageQuery(page: PageRequest = {}): string {
  const size = page.pageSize ?? 100;
  if (!Number.isSafeInteger(size) || size < 1 || size > 500 || page.cursor !== undefined && !isCursor(page.cursor)) {
    throw new TypeError("Supply a finite page size and opaque same-endpoint cursor.");
  }
  const query = new URLSearchParams({ pageSize: String(size) });
  if (page.cursor !== undefined) { query.set("cursor", page.cursor); }
  return "?" + query.toString();
}
function historyQuery(query: SchedulerHistoryQuery, page?: PageRequest): string {
  const fields = new URLSearchParams(pageQuery(page).slice(1));
  for (const [name, value] of Object.entries(query)) {
    if (value === undefined) { continue; }
    if (name === "executionContract") {
      const contract = value as SchedulerHistoryQuery["executionContract"];
      if (contract !== undefined) { fields.set("executionContractName", contract.name); fields.set("executionContractVersion", String(contract.version)); }
    } else { fields.set(name, String(value)); }
  }
  return "?" + fields.toString();
}
async function detach<T>(schema: SchedulerSchema, value: unknown, limits: SchedulerWireLimits, signal: AbortSignal): Promise<T> {
  const bytes = await encodeScheduler(schema, value, "request", limits, signal);
  return JSON.parse(new TextDecoder().decode(bytes)) as T;
}
function checkReference(reference: SchedulerOperationResult["operation"], call: Call): void {
  echo((call.operationKey === undefined || reference.key === call.operationKey)
    && (call.operationName === undefined || reference.name === call.operationName));
}
function checkOperation(value: SchedulerOperationResult, call: Call, objectId: string | undefined): void {
  checkReference(value.operation, call);
  if (value.result !== undefined) {
    echo((call.applicationId === undefined || value.result.applicationId === call.applicationId)
      && (objectId === undefined || value.result.objectId === objectId));
    if (["triggerCreated", "triggerUpdated", "triggerStateChanged", "taskCreated", "taskUpdated", "taskActivated", "taskStateChanged", "manualRunAdmitted", "cancellationRequested"].includes(value.result.kind)) {
      echo(call.decisionKinds === undefined || call.decisionKinds.includes(value.result.kind));
    }
  }
}
function echo(valid: boolean): void { if (!valid) { throw new ClientProtocolFailure("response.scheduler_echo_mismatch"); } }

function hasSettingsPreconditions(definition: SchedulerTaskDraft, preconditions: readonly SchedulerSettingsSelection[]): boolean {
  const fingerprint = (value: SchedulerSettingsSelection): string => JSON.stringify([value.applicationId, value.configurationId, value.schemaVersion, value.releaseId, value.assignmentGeneration]);
  const facts = preconditions.map(fingerprint);
  return new Set(facts).size === facts.length && facts.includes(fingerprint(definition.jobConfiguration))
    && (definition.triggerConfiguration === undefined || facts.includes(fingerprint(definition.triggerConfiguration)));
}

// Compare the finite known draft, retaining array order and exact embedded JSON strings.
// Compatible additional response fields neither change the submitted intent nor become commands.
function sameDraft(left: SchedulerTaskDraft, right: SchedulerTaskDraft): boolean {
  const facts = (value: SchedulerTaskDraft): unknown => [value.name, [value.trigger.applicationId, value.trigger.triggerId],
    [value.job.ownerApplication, value.job.kind, value.job.executionScope, value.job.definitionId, value.job.definitionVersion],
    [value.resource.kind, value.resource.constant, value.resource.eventPath, value.resource.resolver?.name, value.resource.resolver?.version, value.resource.parametersJson],
    value.inputMappings.map(row => [row.targetPath, row.kind, row.sourcePath, row.constantJson, row.transformation?.name, row.transformation?.version, row.parametersJson]),
    [value.jobConfiguration.applicationId, value.jobConfiguration.configurationId, value.jobConfiguration.schemaVersion, value.jobConfiguration.releaseId, value.jobConfiguration.assignmentGeneration],
    value.triggerConfiguration === undefined ? undefined : [value.triggerConfiguration.applicationId, value.triggerConfiguration.configurationId,
      value.triggerConfiguration.schemaVersion, value.triggerConfiguration.releaseId, value.triggerConfiguration.assignmentGeneration],
    value.executionProfileId ?? "app-default", value.enabled ?? false,
    value.retry === undefined ? undefined : [value.retry.enabled, value.retry.maximumAttempts, value.retry.initialDelayMs, value.retry.maximumDelayMs],
    value.executionSettings === undefined ? undefined : [value.executionSettings.idleDelayTicks, value.executionSettings.timeoutTicks,
      value.executionSettings.maximumDrainTicks, value.executionSettings.acquisitionRetryDelayTicks]];
  return JSON.stringify(facts(left)) === JSON.stringify(facts(right));
}
