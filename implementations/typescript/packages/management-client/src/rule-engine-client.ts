import { ClientProtocolFailure, createFetchTransport, executeRequest, validateReadRetries, validateTimeout } from "@cislib/client-core";
import type { ClientCallContext, ClientCallResult, ClientTransport, CredentialProvider, RequestObservation } from "@cislib/client-core";
import { captureBaseUrl } from "./client.js";
import type { ConfigurationAdoptionEntry } from "./configuration-models.js";
import { ConfigurationWireError, defaultConfigurationWireLimits } from "./configuration-wire.js";
import { validateConfiguration } from "./configuration-wire-validation.js";
import type { OperationReference, OperationRejection, Page, PageRequest, TransactionFacts } from "./models.js";
import { freezeWire, ManagementWireError, parseWireJson, validUnicode } from "./wire-json.js";
import { isCursor, isOperationKey, isOrderedInteger, isScalarText, isStrongETag, isTimestamp, isUtf8Text, isUuid } from "./wire-validation.js";

interface RuleScope {
  readonly applicationId: string;
  readonly environment: string;
  readonly rulesetId: string;
}

export interface RuleSetSummary extends RuleScope {
  readonly displayName: string;
  readonly schemaRevision: string;
  readonly editETag: string;
}

export interface RuleSchema extends RuleScope {
  readonly schemaRevision: string;
  /** Exact immutable schema JSON text. */
  readonly schemaJson: string;
}

export interface RuleVersionSummary extends RuleScope {
  readonly version: string;
  readonly schemaRevision: string;
  readonly savedAtUtc: string;
  readonly versionNote?: string;
}

export interface RuleVersion extends RuleVersionSummary {
  /** Exact complete portable definition, including original decimal and metadata lexemes. */
  readonly definitionJson: string;
}

export interface SaveRuleVersionCommand {
  readonly schemaRevision: string;
  readonly definitionJson: string;
  readonly reason: string;
  readonly baseVersion?: string;
}

export interface RuleVersionDecision {
  readonly version: string;
  readonly schemaRevision: string;
  readonly editETag: string;
  readonly savedAtUtc: string;
}

export interface RuleVersionSaveResult extends RuleScope {
  readonly operation: OperationReference;
  readonly resultSource: string;
  readonly originalCompletion: string;
  readonly result?: RuleVersionDecision;
  readonly attemptCompletion?: TransactionFacts;
  readonly rejection?: OperationRejection;
}

export interface RuleDeploymentSelection {
  readonly version: string;
  readonly schemaRevision: string;
  readonly releaseId: string;
  readonly generation: string;
}

export interface RuleDeployment extends RuleScope {
  readonly assignmentETag: string;
  readonly desired?: RuleDeploymentSelection;
  readonly requestedAtUtc?: string;
}

export interface RuleDeploymentInstance {
  readonly rulesetId: string;
  readonly configuration: ConfigurationAdoptionEntry;
  readonly requestedVersion?: string;
  readonly reportedDesiredVersion?: string;
  readonly appliedVersion?: string;
  readonly lastSuccessfulVersion?: string;
}

export type RuleExecutionStatus = "Completed" | "Failed" | "Cancelled" | "Incomplete";

export interface RuleExecutionQuery {
  readonly executionId?: string;
  readonly version?: string;
  readonly instanceId?: string;
  readonly status?: RuleExecutionStatus;
  readonly startedFromUtc?: string;
  readonly startedThroughUtc?: string;
}

export interface RuleExecutionSummary extends RuleScope {
  readonly executionId: string;
  readonly version: string;
  readonly schemaRevision: string;
  readonly producerApplication: string;
  readonly instanceId: string;
  readonly registrationId: string;
  readonly applicationBuild: string;
  readonly startedAtUtc: string;
  readonly status: RuleExecutionStatus;
  readonly startReceived: boolean;
  readonly completionReceived: boolean;
  readonly finishedAtUtc?: string;
  readonly returnedMatchCount?: number;
}

export interface RuleExecutionDetail {
  readonly execution: RuleExecutionSummary;
  /** Exact immutable definition used to interpret observed row and cell positions. */
  readonly definitionJson: string;
  /** Exact original Audit record, present only when that phase was received. */
  readonly startAuditJson?: string;
  /** Exact original Audit record, present only when that phase was received. */
  readonly completionAuditJson?: string;
}

export type RuleCounterAvailability = "fresh" | "stale" | "unavailable";
export type RuleCounterResetSelection = "one" | "selected" | "all";

export interface RuleCounterItem {
  readonly ruleId: string;
  readonly count?: string;
  readonly lastResetAtUtc?: string;
}

export interface RuleCounterPage extends RuleScope, Page<RuleCounterItem> {
  readonly availability: RuleCounterAvailability;
  readonly observedAtUtc?: string;
}

export interface RuleCounterResetCommand {
  readonly selection: RuleCounterResetSelection;
  readonly ruleIds: readonly string[];
  readonly reason: string;
}

export interface RuleCounterResetDecision {
  readonly selection: RuleCounterResetSelection;
  readonly resetAtUtc: string;
  readonly appliedCount: string;
  readonly rejectedCount: string;
}

export interface RuleCounterResetOutcome {
  readonly ruleId: string;
  readonly disposition: "reset" | "notFound";
}

export interface RuleCounterResetOutcomesPage extends RuleScope, Page<RuleCounterResetOutcome> {
  readonly operation: OperationReference;
}

export interface RuleCounterResetOperationResult extends RuleScope {
  readonly operation: OperationReference;
  readonly resultSource: string;
  readonly originalCompletion: string;
  readonly result?: RuleCounterResetDecision;
  readonly attemptCompletion?: TransactionFacts;
  readonly rejection?: OperationRejection;
}

export type RuleDeploymentIntent = "deploy" | "rollback";

export interface RequestRuleDeploymentCommand {
  readonly version: string;
  readonly intent: RuleDeploymentIntent;
  readonly reason: string;
}

export interface RuleDeploymentDecision {
  readonly intent: RuleDeploymentIntent;
  readonly assignmentETag: string;
  readonly requestedAtUtc: string;
  readonly desired: RuleDeploymentSelection;
}

export interface RuleDeploymentOperationResult extends RuleScope {
  readonly operation: OperationReference;
  readonly resultSource: string;
  readonly originalCompletion: string;
  readonly result?: RuleDeploymentDecision;
  readonly attemptCompletion?: TransactionFacts;
  readonly rejection?: OperationRejection;
}

export interface RuleEngineProblem {
  readonly type: string;
  readonly title: string;
  readonly status: number;
  readonly code: string;
  readonly detail?: string;
  readonly correlationId?: string;
  readonly operation?: OperationReference;
  readonly result?: RuleVersionSaveResult;
  readonly deploymentResult?: RuleDeploymentOperationResult;
  readonly counterResetResult?: RuleCounterResetOperationResult;
}

export type RuleEngineCallResult<T> = ClientCallResult<T, RuleEngineProblem>;

export interface ManagementRuleEngineClientOptions {
  readonly baseUrl: string;
  readonly transport?: ClientTransport;
  readonly credentials?: CredentialProvider;
  readonly timeoutMs?: number;
  readonly readRetries?: number;
  readonly observe?: (event: RequestObservation) => void;
}

export interface ManagementRuleEngineClientPort {
  listRulesets(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleSetSummary>>>;
  getSchema(applicationId: string, rulesetId: string, schemaRevision: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleSchema>>;
  listVersions(applicationId: string, rulesetId: string, page?: PageRequest,
    context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleVersionSummary>>>;
  getVersion(applicationId: string, rulesetId: string, version: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleVersion>>;
  saveVersion(applicationId: string, rulesetId: string, operationKey: string, ifMatch: string, command: SaveRuleVersionCommand,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleVersionSaveResult>>;
  getSaveOperation(applicationId: string, rulesetId: string, operationKey: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleVersionSaveResult>>;
}

export interface ManagementRuleEngineDeploymentClientPort {
  getDeployment(applicationId: string, rulesetId: string, context?: ClientCallContext): Promise<RuleEngineCallResult<RuleDeployment>>;
  listDeploymentInstances(applicationId: string, rulesetId: string, page?: PageRequest,
    context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleDeploymentInstance>>>;
  requestDeployment(applicationId: string, rulesetId: string, operationKey: string, ifMatch: string,
    command: RequestRuleDeploymentCommand, context?: ClientCallContext): Promise<RuleEngineCallResult<RuleDeploymentOperationResult>>;
  getDeploymentOperation(applicationId: string, rulesetId: string, operationKey: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleDeploymentOperationResult>>;
}

export interface ManagementRuleEngineExecutionHistoryClientPort {
  listExecutions(applicationId: string, rulesetId: string, query?: RuleExecutionQuery, page?: PageRequest,
    context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleExecutionSummary>>>;
  getExecution(applicationId: string, rulesetId: string, executionId: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleExecutionDetail>>;
}

export interface ManagementRuleEngineCounterClientPort {
  listCounters(applicationId: string, rulesetId: string, page?: PageRequest,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterPage>>;
  resetCounters(applicationId: string, rulesetId: string, operationKey: string, command: RuleCounterResetCommand,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterResetOperationResult>>;
  getCounterResetOperation(applicationId: string, rulesetId: string, operationKey: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterResetOperationResult>>;
  listCounterResetOutcomes(applicationId: string, rulesetId: string, operationKey: string, page?: PageRequest,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterResetOutcomesPage>>;
}

type WireObject = Record<string, unknown>;
type Route = "" | "/schema" | "/versions" | "/version" | "/version-operations"
  | "/deployment" | "/deployment-instances" | "/deployments" | "/deployment-operations" | "/executions"
  | "/counters" | "/counter-resets" | "/counter-reset-operations";

type Mutation =
  | { readonly kind: "save"; readonly ifMatch: string; readonly command: SaveRuleVersionCommand }
  | { readonly kind: "deployment"; readonly ifMatch: string; readonly command: RequestRuleDeploymentCommand }
  | { readonly kind: "counterReset"; readonly command: RuleCounterResetCommand };

interface Scope {
  readonly applicationId: string;
  readonly rulesetId?: string;
  readonly schemaRevision?: string;
  readonly version?: string;
  readonly operationKey?: string;
  readonly operationName?: string;
}

interface Call<T> {
  readonly route: Route;
  readonly scope: Scope;
  readonly page?: PageRequest;
  readonly write?: Mutation;
  readonly executionQuery?: RuleExecutionQuery;
  readonly executionId?: string;
  readonly lookupSuffix?: "/outcomes";
  readonly read: (value: WireObject, headers: Readonly<Record<string, string>>, pageSize: number) => T;
}

const saveOperationName = "management.rule-engine.version.save";
const deploymentOperationName = "management.rule-engine.deployment.request";
const counterResetOperationName = "management.rule-engine.counter.reset";
const maximumExplicitCounterResetIds = 48;
const maximumBodyBytes = 1024 * 1024;
const maximumDefinitionBytes = 128 * 1024;
const encoder = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true });

/** Typed scoped reads and immutable version authoring over the selected portable transport. */
export class ManagementRuleEngineClient implements ManagementRuleEngineClientPort, ManagementRuleEngineDeploymentClientPort,
  ManagementRuleEngineExecutionHistoryClientPort, ManagementRuleEngineCounterClientPort {
  readonly baseUrl: string;
  private readonly transport: ClientTransport;
  private readonly credentials: CredentialProvider | undefined;
  private readonly timeoutMs: number;
  private readonly readRetries: number;
  private readonly observe: ((event: RequestObservation) => void) | undefined;

  constructor(options: ManagementRuleEngineClientOptions) {
    this.baseUrl = captureBaseUrl(options.baseUrl);
    this.transport = options.transport ?? createFetchTransport();
    this.credentials = options.credentials;
    this.timeoutMs = validateTimeout(options.timeoutMs ?? 30000);
    this.readRetries = validateReadRetries(options.readRetries ?? 0);
    this.observe = options.observe;
  }

  listRulesets(applicationId: string, page: PageRequest = {}, context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleSetSummary>>> {
    return this.call({ route: "", scope: { applicationId }, page, read: (value, _headers, size) =>
      validatePage<RuleSetSummary>(value, size, "rulesetId", item => {
        validateScope(item, { applicationId });
        requireWire(isScalarText(item.displayName, 512) && isRuleId(item.schemaRevision) && isStrongETag(item.editETag));
      }) }, context);
  }

  getSchema(applicationId: string, rulesetId: string, schemaRevision: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleSchema>> {
    return this.call({ route: "/schema", scope: { applicationId, rulesetId, schemaRevision }, read: value => {
      validateScope(value, { applicationId, rulesetId });
      requireWire(value.schemaRevision === schemaRevision);
      requireWire(readDefinitionRows(value.schemaJson, rulesetId, schemaRevision)?.length === 0);
      return value as unknown as RuleSchema;
    } }, context);
  }

  listVersions(applicationId: string, rulesetId: string, page: PageRequest = {},
    context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleVersionSummary>>> {
    return this.call({ route: "/versions", scope: { applicationId, rulesetId }, page, read: (value, _headers, size) =>
      validatePage<RuleVersionSummary>(value, size, "version", item => validateVersion(item, { applicationId, rulesetId })) }, context);
  }

  getVersion(applicationId: string, rulesetId: string, version: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleVersion>> {
    return this.call({ route: "/version", scope: { applicationId, rulesetId, version }, read: value => {
      validateVersion(value, { applicationId, rulesetId });
      requireWire(value.version === version && isRuleId(value.schemaRevision));
      requireWire(readDefinitionRows(value.definitionJson, rulesetId, value.schemaRevision, version) !== undefined);
      return value as unknown as RuleVersion;
    } }, context);
  }

  saveVersion(applicationId: string, rulesetId: string, operationKey: string, ifMatch: string, command: SaveRuleVersionCommand,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleVersionSaveResult>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: saveOperationName };
    return this.call({ route: "/versions", scope, write: { kind: "save", ifMatch, command }, read: (value, headers) => {
      const result = validateResult(value, scope);
      requireWire((result.resultSource === "prepared" || result.resultSource === "replayed")
        && result.originalCompletion === "committed" && result.result !== undefined
        && (headers.etag === undefined || headers.etag === result.result.editETag));
      return result;
    } }, context);
  }

  getSaveOperation(applicationId: string, rulesetId: string, operationKey: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleVersionSaveResult>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: saveOperationName };
    return this.call({ route: "/version-operations", scope, read: value => {
      const result = validateResult(value, scope);
      requireWire(result.resultSource === "found" && result.originalCompletion === "committed");
      return result;
    } }, context);
  }

  getDeployment(applicationId: string, rulesetId: string, context?: ClientCallContext): Promise<RuleEngineCallResult<RuleDeployment>> {
    const scope = { applicationId, rulesetId };
    return this.call({ route: "/deployment", scope, read: (value, headers) => {
      validateScope(value, scope);
      requireWire(isStrongETag(value.assignmentETag) && (headers.etag === undefined || headers.etag === value.assignmentETag));
      if (value.desired !== undefined) { validateDeploymentSelection(value.desired); }
      requireWire(value.requestedAtUtc === undefined || value.desired !== undefined && isTimestamp(value.requestedAtUtc));
      return value as unknown as RuleDeployment;
    } }, context);
  }

  listDeploymentInstances(applicationId: string, rulesetId: string, page: PageRequest = {},
    context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleDeploymentInstance>>> {
    return this.call({ route: "/deployment-instances", scope: { applicationId, rulesetId }, page, read: (value, _headers, size) =>
      validateDeploymentInstances(value, size, applicationId, rulesetId) }, context);
  }

  requestDeployment(applicationId: string, rulesetId: string, operationKey: string, ifMatch: string,
    command: RequestRuleDeploymentCommand, context?: ClientCallContext): Promise<RuleEngineCallResult<RuleDeploymentOperationResult>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: deploymentOperationName };
    return this.call({ route: "/deployments", scope, write: { kind: "deployment", ifMatch, command }, read: (value, headers) => {
      const result = validateDeploymentResult(value, scope);
      requireWire((result.resultSource === "prepared" || result.resultSource === "replayed")
        && result.originalCompletion === "committed" && result.result !== undefined
        && (headers.etag === undefined || headers.etag === result.result.assignmentETag));
      return result;
    } }, context);
  }

  getDeploymentOperation(applicationId: string, rulesetId: string, operationKey: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleDeploymentOperationResult>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: deploymentOperationName };
    return this.call({ route: "/deployment-operations", scope, read: value => {
      const result = validateDeploymentResult(value, scope);
      requireWire(result.resultSource === "found" && result.originalCompletion === "committed");
      return result;
    } }, context);
  }

  listExecutions(applicationId: string, rulesetId: string, query: RuleExecutionQuery = {}, page: PageRequest = {},
    context?: ClientCallContext): Promise<RuleEngineCallResult<Page<RuleExecutionSummary>>> {
    const scope = { applicationId, rulesetId };
    const filters = { ...query };
    return this.call({ route: "/executions", scope, executionQuery: filters, page, read: (value, _headers, size) =>
      validatePage<RuleExecutionSummary>(value, size, "executionId", item => {
        const execution = validateExecutionSummary(item, scope);
        requireWire(matchesExecutionQuery(execution, filters));
      }) }, context);
  }

  getExecution(applicationId: string, rulesetId: string, executionId: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleExecutionDetail>> {
    const scope = { applicationId, rulesetId };
    return this.call({ route: "/executions", scope, executionId,
      read: value => validateExecutionDetail(value, scope, executionId) }, context);
  }

  listCounters(applicationId: string, rulesetId: string, page: PageRequest = {},
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterPage>> {
    const scope = { applicationId, rulesetId };
    return this.call({ route: "/counters", scope, page, read: (value, _headers, size) =>
      validateCounterPage(value, scope, size) }, context);
  }

  resetCounters(applicationId: string, rulesetId: string, operationKey: string, command: RuleCounterResetCommand,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterResetOperationResult>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: counterResetOperationName };
    return this.call({ route: "/counter-resets", scope, write: { kind: "counterReset", command }, read: value => {
      const result = validateCounterResetResult(value, scope);
      requireWire(result.originalCompletion === "committed" && result.result !== undefined
        && (result.resultSource === "prepared" || result.resultSource === "replayed"));
      return result;
    } }, context);
  }

  getCounterResetOperation(applicationId: string, rulesetId: string, operationKey: string,
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterResetOperationResult>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: counterResetOperationName };
    return this.call({ route: "/counter-reset-operations", scope, read: value => {
      const result = validateCounterResetResult(value, scope);
      requireWire(result.originalCompletion === "committed" && result.resultSource === "found");
      return result;
    } }, context);
  }

  listCounterResetOutcomes(applicationId: string, rulesetId: string, operationKey: string, page: PageRequest = {},
    context?: ClientCallContext): Promise<RuleEngineCallResult<RuleCounterResetOutcomesPage>> {
    const scope = { applicationId, rulesetId, operationKey, operationName: counterResetOperationName };
    return this.call({ route: "/counter-reset-operations", scope, lookupSuffix: "/outcomes", page, read: (value, _headers, size) =>
      validateCounterResetOutcomes(value, scope, size) }, context);
  }

  private async call<T>(call: Call<T>, context: ClientCallContext | undefined): Promise<RuleEngineCallResult<T>> {
    const mutating = call.write !== undefined;
    let pageSize = 100;
    let submitted: SaveRuleVersionCommand | RequestRuleDeploymentCommand | RuleCounterResetCommand | undefined;
    const exchange = await executeRequest<T, RuleEngineProblem>({
      operation: "rule-engine",
      transport: this.transport,
      timeoutMs: this.timeoutMs,
      readRetries: this.readRetries,
      ...(context === undefined ? {} : { context }),
      ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }),
      ...(call.scope.operationKey === undefined ? {} : { operationKey: call.scope.operationKey }),
      prepare: signal => {
        const { applicationId, rulesetId, schemaRevision, version, operationKey } = call.scope;
        if (!isUuid(applicationId) || call.route !== "" && !isRuleId(rulesetId)
          || call.route === "/schema" && !isRuleId(schemaRevision) || call.route === "/version" && !isRuleId(version)) {
          throw new TypeError("Supply a canonical application ID and exact bounded rule identifiers.");
        }
        const lookup = call.scope.operationName !== undefined && !mutating;
        if (call.scope.operationName !== undefined && !isOperationKey(operationKey)) {
          throw new TypeError("Supply the original bounded operation key.");
        }
        const query = new URLSearchParams();
        if (rulesetId !== undefined) { query.set("rulesetId", rulesetId); }
        if (schemaRevision !== undefined) { query.set("schemaRevision", schemaRevision); }
        if (version !== undefined) { query.set("version", version); }
        if (call.executionQuery !== undefined) { addExecutionQuery(query, call.executionQuery); }
        if (call.route === "/executions" && call.executionQuery === undefined && !isUuid(call.executionId)) {
          throw new TypeError("Supply the exact canonical execution ID.");
        }
        if (call.page !== undefined) {
          const { cursor } = call.page;
          pageSize = call.page.pageSize ?? 100;
          if (!Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 500 || cursor !== undefined && !isCursor(cursor)) {
            throw new TypeError("Supply a bounded page and opaque cursor.");
          }
          query.set("pageSize", String(pageSize));
          if (cursor !== undefined) { query.set("cursor", cursor); }
        }
        const headers: Record<string, string> = { Accept: "application/json, application/problem+json", "Cache-Control": "no-store" };
        let body: Uint8Array | undefined;
        if (call.write !== undefined) {
          const write = call.write;
          if (!isOperationKey(operationKey) || rulesetId === undefined) {
            throw new TypeError("Supply the original operation key and exact ruleset.");
          }
          if (write.kind !== "counterReset") {
            if (!isStrongETag(write.ifMatch)) { throw new TypeError("Supply one strong precondition ETag."); }
            headers["If-Match"] = write.ifMatch;
          }
          headers["Idempotency-Key"] = operationKey;
          headers["Content-Type"] = "application/json";
          switch (write.kind) {
            case "save": submitted = captureCommand(write.command, rulesetId); break;
            case "deployment": submitted = captureDeploymentCommand(write.command); break;
            case "counterReset": submitted = captureCounterResetCommand(write.command); break;
          }
          body = encoder.encode(JSON.stringify(submitted));
          if (body.length > maximumBodyBytes) { throw new TypeError("The encoded Rule Engine command exceeds its bound."); }
        }
        signal.throwIfAborted();
        const suffix = (call.route === "" ? "/rulesets" : call.route) + (lookup ? "/" + operationKey + (call.lookupSuffix ?? "") : "")
          + (call.executionId === undefined ? "" : "/" + call.executionId);
        const encodedQuery = query.toString();
        return { url: this.baseUrl + "management/v1/applications/" + applicationId + "/rule-engine" + suffix
          + (encodedQuery.length === 0 ? "" : "?" + encodedQuery), method: mutating ? "POST" : "GET",
          headers: Object.freeze(headers), ...(body === undefined ? {} : { body }), maximumResponseBytes: maximumBodyBytes,
          maximumErrorBytes: 65536, mutating };
      },
      decode: (response, body, signal) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            const parsed = parseWireJson(decoder.decode(body), 16);
            if (call.route === "/executions") {
              requireWire(![...parsed.nonIntegerNumbers].some(path => /^(?:\/items\/[0-9]+|\/execution)\/returnedMatchCount$/u.test(path)));
            }
            const raw = object(parsed.value);
            const value = call.read(raw, response.headers, pageSize);
            checkSubmittedDecision(raw.result, submitted);
            signal.throwIfAborted();
            return { value: freezeWire(value) };
          }
          if (contentType !== "application/problem+json") {
            if (body.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const parsed = parseWireJson(decoder.decode(body), 16);
          requireWire(!parsed.nonIntegerNumbers.has("/status"));
          const problem = validateProblem(object(parsed.value), response.status, call.scope);
          checkSubmittedDecision(problem.counterResetResult?.result ?? problem.deploymentResult?.result ?? problem.result?.result, submitted);
          signal.throwIfAborted();
          return { problem: freezeWire(problem) };
        } catch (error) {
          signal.throwIfAborted();
          if (error instanceof ManagementWireError || error instanceof ConfigurationWireError || error instanceof TypeError) {
            throw new ClientProtocolFailure("rule-engine.wire.invalid");
          }
          throw error;
        }
      }
    });
    const original = exchange.problem?.counterResetResult ?? exchange.problem?.deploymentResult ?? exchange.problem?.result;
    const uncertain = mutating && (original?.originalCompletion === "unknown"
      || exchange.status !== undefined && exchange.status >= 500 && original === undefined);
    return Object.freeze({ ...exchange, ...(uncertain ? { mutationUncertain: true } : {}) });
  }
}

function captureCommand(command: SaveRuleVersionCommand, rulesetId: string): SaveRuleVersionCommand {
  const { schemaRevision, definitionJson, reason, baseVersion } = command;
  if (!isRuleId(schemaRevision) || !isUtf8Text(reason, 1024) || baseVersion !== undefined && !isRuleId(baseVersion)
    || Object.keys(command).some(key => key !== "schemaRevision" && key !== "definitionJson" && key !== "reason" && key !== "baseVersion")
    || !Object.hasOwn(command, "schemaRevision") || !Object.hasOwn(command, "definitionJson") || !Object.hasOwn(command, "reason")
    || readDefinitionRows(definitionJson, rulesetId, schemaRevision) === undefined) {
    throw new TypeError("Supply a complete exact definition, its schema revision, a bounded reason and optional immutable base version.");
  }
  return { schemaRevision, definitionJson, reason, ...(baseVersion === undefined ? {} : { baseVersion }) };
}

function captureDeploymentCommand(command: RequestRuleDeploymentCommand): RequestRuleDeploymentCommand {
  const { version, intent, reason } = command;
  if (!isUuid(version) || !isDeploymentIntent(intent) || !isUtf8Text(reason, 1024)
    || Object.keys(command).some(key => key !== "version" && key !== "intent" && key !== "reason")
    || !Object.hasOwn(command, "version") || !Object.hasOwn(command, "intent") || !Object.hasOwn(command, "reason")) {
    throw new TypeError("Supply one exact saved version, deployment intent and bounded reason.");
  }
  return { version, intent, reason };
}

function captureCounterResetCommand(command: RuleCounterResetCommand): RuleCounterResetCommand {
  const { selection, ruleIds, reason } = command;
  if (!isCounterResetSelection(selection) || !Array.isArray(ruleIds) || ruleIds.length > maximumExplicitCounterResetIds
    || selection === "one" && ruleIds.length !== 1 || selection === "selected" && ruleIds.length === 0
    || selection === "all" && ruleIds.length !== 0 || !isUtf8Text(reason, 1024)
    || Object.keys(command).some(key => key !== "selection" && key !== "ruleIds" && key !== "reason")
    || !Object.hasOwn(command, "selection") || !Object.hasOwn(command, "ruleIds") || !Object.hasOwn(command, "reason")) {
    throw new TypeError("Supply the exact reset selection and bounded original reason.");
  }
  const ids = Array.from(ruleIds);
  if (!ids.every(isRuleId) || new Set(ids).size !== ids.length) {
    throw new TypeError("Supply exact distinct rule IDs.");
  }
  return { selection, ruleIds: ids, reason };
}

function isDeploymentIntent(value: unknown): value is RuleDeploymentIntent {
  return value === "deploy" || value === "rollback";
}

function isRuleId(value: unknown): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= 128 && validUnicode(value)
    && !/\p{Cc}/u.test(value) && encoder.encode(value).length <= 128;
}

function readRawObject(raw: unknown): WireObject | undefined {
  if (typeof raw !== "string" || raw.length === 0 || raw.length > maximumDefinitionBytes
    || !validUnicode(raw) || encoder.encode(raw).length > maximumDefinitionBytes) {
    return undefined;
  }
  try {
    return object(parseWireJson(raw, 16).value);
  } catch (error) {
    if (error instanceof ManagementWireError) { return undefined; }
    throw error;
  }
}

function readDefinitionRows(raw: unknown, rulesetId: string, schemaRevision: string, version?: string): readonly unknown[] | undefined {
  const root = readRawObject(raw);
  if (root === undefined || root.rulesetId !== rulesetId || root.schemaRevision !== schemaRevision
    || !isRuleId(root.version) || version !== undefined && root.version !== version
    || root.mode !== "firstMatch" && root.mode !== "allMatching" || !Array.isArray(root.columns) || !Array.isArray(root.rows)) {
    return undefined;
  }
  return root.rows;
}

function validatePage<T>(page: WireObject, maximum: number, identity: "rulesetId" | "version" | "executionId" | "ruleId",
  validateItem: (item: WireObject) => void): Page<T> {
  const seen = new Set<unknown>();
  let environment: unknown;
  for (const item of pageItems(page, maximum)) {
    validateItem(item);
    environment ??= item.environment;
    requireWire(environment === item.environment && !seen.has(item[identity]));
    seen.add(item[identity]);
  }
  return page as unknown as Page<T>;
}

function pageItems(page: WireObject, maximum: number): WireObject[] {
  const items = page.items;
  requireWire(Array.isArray(items) && items.length <= maximum && (page.nextCursor === undefined || isCursor(page.nextCursor)));
  return (items as unknown[]).map(object);
}

function validateDeploymentInstances(page: WireObject, maximum: number, applicationId: string, rulesetId: string): Page<RuleDeploymentInstance> {
  const items = pageItems(page, maximum);
  const observations = items.map(item => object(item.configuration));
  validateConfiguration("ConfigurationAdoptionPage", { items: observations }, false, defaultConfigurationWireLimits, []);
  const first = observations[0];
  for (const [index, observation] of observations.entries()) {
    const item = items[index]!;
    requireWire(item.rulesetId === rulesetId && observation.applicationId === applicationId
      && observation.environment === first?.environment && observation.configurationId === first?.configurationId
      && observation.schemaVersion === first?.schemaVersion);
    const reported = observation.reported === undefined ? undefined : object(observation.reported);
    for (const [field, release] of [
      ["requestedVersion", observation.desired], ["reportedDesiredVersion", reported?.desired],
      ["appliedVersion", reported?.applied], ["lastSuccessfulVersion", reported?.lastSuccessful]
    ] as const) {
      requireWire(item[field] === undefined || release !== undefined && isUuid(item[field]));
    }
  }
  return page as unknown as Page<RuleDeploymentInstance>;
}

function validateScope(value: WireObject, expected: Scope): void {
  requireWire(value.applicationId === expected.applicationId && isScalarText(value.environment, 255)
    && isRuleId(value.rulesetId) && (expected.rulesetId === undefined || value.rulesetId === expected.rulesetId));
}

function validateVersion(value: WireObject, expected: Scope): void {
  validateScope(value, expected);
  requireWire(isRuleId(value.version) && isRuleId(value.schemaRevision) && isTimestamp(value.savedAtUtc)
    && (value.versionNote === undefined || isUtf8Text(value.versionNote, 1024)));
}

function isTerminalExecutionStatus(value: unknown): value is Exclude<RuleExecutionStatus, "Incomplete"> {
  return value === "Completed" || value === "Failed" || value === "Cancelled";
}

function isExecutionStatus(value: unknown): value is RuleExecutionStatus {
  return value === "Incomplete" || isTerminalExecutionStatus(value);
}

function addExecutionQuery(target: URLSearchParams, filters: RuleExecutionQuery): void {
  const { executionId, version, instanceId, status, startedFromUtc, startedThroughUtc } = filters;
  if ([executionId, version, instanceId].some(value => value !== undefined && !isUuid(value))
    || status !== undefined && !isExecutionStatus(status)
    || startedFromUtc !== undefined && !isTimestamp(startedFromUtc)
    || startedThroughUtc !== undefined && !isTimestamp(startedThroughUtc)
    || startedFromUtc !== undefined && startedThroughUtc !== undefined && startedFromUtc > startedThroughUtc) {
    throw new TypeError("Supply exact execution filters and an ordered UTC range.");
  }
  for (const [key, value] of Object.entries({ executionId, version, instanceId, status, startedFromUtc, startedThroughUtc })) {
    if (value !== undefined) { target.set(key, value); }
  }
}

function matchesExecutionQuery(execution: RuleExecutionSummary, query: RuleExecutionQuery): boolean {
  return (query.executionId === undefined || query.executionId === execution.executionId)
    && (query.version === undefined || query.version === execution.version)
    && (query.instanceId === undefined || query.instanceId === execution.instanceId)
    && (query.status === undefined || query.status === execution.status)
    && (query.startedFromUtc === undefined || query.startedFromUtc <= execution.startedAtUtc)
    && (query.startedThroughUtc === undefined || execution.startedAtUtc <= query.startedThroughUtc);
}

function validateExecutionSummary(value: WireObject, expected: Scope): RuleExecutionSummary {
  validateScope(value, expected);
  const { startReceived, completionReceived, status, finishedAtUtc, returnedMatchCount } = value;
  requireWire(isUuid(value.executionId) && isUuid(value.version) && isRuleId(value.schemaRevision)
    && isScalarText(value.producerApplication, 255) && isUuid(value.instanceId) && isUuid(value.registrationId)
    && isScalarText(value.applicationBuild, 512) && isTimestamp(value.startedAtUtc)
    && typeof startReceived === "boolean" && typeof completionReceived === "boolean" && (startReceived || completionReceived)
    && isExecutionStatus(status) && (startReceived && completionReceived) === (status !== "Incomplete")
    && (completionReceived ? isTimestamp(finishedAtUtc) : finishedAtUtc === undefined)
    && (returnedMatchCount === undefined || status === "Completed" && typeof returnedMatchCount === "number"
      && Number.isSafeInteger(returnedMatchCount) && returnedMatchCount >= 0 && returnedMatchCount <= 48));
  return value as unknown as RuleExecutionSummary;
}

function validateExecutionDetail(value: WireObject, expected: Scope, executionId: string): RuleExecutionDetail {
  const execution = validateExecutionSummary(object(value.execution), expected);
  requireWire(execution.executionId === executionId);
  const rows = readDefinitionRows(value.definitionJson, execution.rulesetId, execution.schemaRevision, execution.version);
  requireWire(rows !== undefined && execution.startReceived === (value.startAuditJson !== undefined)
    && execution.completionReceived === (value.completionAuditJson !== undefined));
  const start = execution.startReceived ? validateExecutionAudit(value.startAuditJson, execution, rows.length, false) : undefined;
  const completion = execution.completionReceived ? validateExecutionAudit(value.completionAuditJson, execution, rows.length, true) : undefined;
  if (start !== undefined && completion !== undefined) {
    requireWire(completion.causationAuditId === start.auditId && completion.auditId !== start.auditId
      && completion.authorityId === start.authorityId && completion.correlationId === start.correlationId);
  }
  return value as unknown as RuleExecutionDetail;
}

const executionAuditFields = new Set(["auditVersion", "auditId", "eventType", "eventVersion", "category", "occurredAtUtc",
  "recordedAtUtc", "producer", "actor", "outcome", "target", "reason", "operationReference", "correlationId",
  "causationAuditId", "domainEventId", "details"]);
const executionAuditVersions = new Set(["/auditVersion", "/eventVersion", "/details/engineContractVersion"]);
const selectedAuditIndexPath = "/details/selectedRowIndices/";
const executionAuditTokens = new Set([
  ...executionAuditVersions,
  ...Array.from({ length: 48 }, (_, index) => selectedAuditIndexPath + index)
]);

function validateExecutionAudit(raw: unknown, execution: RuleExecutionSummary, rowCount: number, completion: boolean): {
  readonly auditId: string; readonly causationAuditId?: string; readonly authorityId: string; readonly correlationId?: string;
} {
  requireWire(typeof raw === "string" && raw.length <= maximumDefinitionBytes && encoder.encode(raw).length <= maximumDefinitionBytes);
  const parsed = parseWireJson(raw, 16, executionAuditTokens);
  const record = object(parsed.value);
  const producer = object(record.producer);
  const instance = object(producer.instance);
  const actor = object(record.actor);
  const facts = object(record.details);
  const phase = completion ? "complete" : "start";
  const correlation = facts.correlationId;
  const cause = record.causationAuditId;
  requireWire(Object.keys(record).every(field => executionAuditFields.has(field))
    && [...executionAuditVersions].every(path => parsed.rawValues.get(path) === "1")
    && isUuid(record.auditId) && record.eventType === "cis.rule-engine.execution." + phase && record.category === "business"
    && record.domainEventId === execution.executionId + ":" + phase
    && isTimestamp(record.occurredAtUtc) && record.occurredAtUtc === (completion ? execution.finishedAtUtc : execution.startedAtUtc)
    && isTimestamp(record.recordedAtUtc) && (completion ? isUuid(cause) : cause === undefined)
    && producer.application === execution.producerApplication && producer.environment === execution.environment
    && instance.kind === "reported" && instance.reportedIdentifier === execution.instanceId
    && typeof actor.kind === "string" && ["user", "application", "system", "anonymous", "unknown"].includes(actor.kind)
    && isUuid(facts.authorityId) && (correlation === undefined || isRuleId(correlation))
    && record.correlationId === (correlation === undefined || /^\p{White_Space}*$/u.test(correlation) ? undefined : correlation)
    && facts.executionId === execution.executionId && facts.applicationId === execution.applicationId
    && facts.application === execution.producerApplication && facts.environment === execution.environment
    && facts.instanceId === execution.instanceId && facts.registrationId === execution.registrationId
    && facts.applicationBuild === execution.applicationBuild && facts.rulesetId === execution.rulesetId
    && facts.schemaRevision === execution.schemaRevision && facts.version === execution.version
    && executionAuditTime(facts.startedAtUtc) === execution.startedAtUtc);
  if (completion) {
    requireWire(isTerminalExecutionStatus(facts.status) && (execution.status === "Incomplete" || facts.status === execution.status)
      && executionAuditTime(facts.finishedAtUtc) === execution.finishedAtUtc
      && record.outcome === (facts.status === "Completed" ? "succeeded" : facts.status === "Cancelled" ? "canceled" : "failed"));
    const selected = facts.selectedRowIndices;
    requireWire(Array.isArray(facts.rows) && facts.rows.length === rowCount && Array.isArray(selected)
      && selected.length <= rowCount && (facts.status === "Completed" || selected.length === 0)
      && (execution.returnedMatchCount === undefined || execution.returnedMatchCount === selected.length)
      && selected.every((index, position) => Number.isSafeInteger(index) && index >= 0 && index < rowCount
        && parsed.rawValues.get(selectedAuditIndexPath + position) === String(index))
      && new Set(selected).size === selected.length);
  } else {
    requireWire(record.outcome === "accepted" && Array.isArray(facts.inputs));
  }
  return { auditId: record.auditId, authorityId: facts.authorityId,
    ...(isUuid(cause) ? { causationAuditId: cause } : {}), ...(correlation === undefined ? {} : { correlationId: correlation }) };
}

function executionAuditTime(raw: unknown): string | undefined {
  if (typeof raw !== "string") { return undefined; }
  const match = /^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.([0-9]{1,7}))?(?:Z|\+00:00)$/u.exec(raw);
  if (match === null || match[0] !== raw) { return undefined; }
  const canonical = match[1] + "." + (match[2] ?? "").padEnd(7, "0") + "Z";
  return isTimestamp(canonical) ? canonical : undefined;
}

function validateOperation(value: unknown, expected: Scope): OperationReference {
  const operation = object(value);
  requireWire(expected.operationName !== undefined && expected.operationKey !== undefined && operation.name === expected.operationName
    && operation.key === expected.operationKey && isUtf8Text(operation.scope, 256));
  return operation as unknown as OperationReference;
}

function validateResult(value: WireObject, expected: Scope): RuleVersionSaveResult {
  requireWire(expected.operationName === saveOperationName);
  validateOriginalFacts(value, expected);
  if (value.result !== undefined) {
    const result = object(value.result);
    requireWire(isRuleId(result.version) && isRuleId(result.schemaRevision) && isStrongETag(result.editETag)
      && isTimestamp(result.savedAtUtc));
  }
  return value as unknown as RuleVersionSaveResult;
}

function validateDeploymentResult(value: WireObject, expected: Scope): RuleDeploymentOperationResult {
  requireWire(expected.operationName === deploymentOperationName);
  validateOriginalFacts(value, expected);
  if (value.result !== undefined) {
    const result = object(value.result);
    validateDeploymentSelection(result.desired);
    requireWire(isDeploymentIntent(result.intent) && isStrongETag(result.assignmentETag) && isTimestamp(result.requestedAtUtc));
  }
  return value as unknown as RuleDeploymentOperationResult;
}

function validateDeploymentSelection(value: unknown): void {
  const selection = object(value);
  requireWire(isUuid(selection.version) && isRuleId(selection.schemaRevision) && isUuid(selection.releaseId)
    && isOrderedInteger(selection.generation));
}

function isCounterResetSelection(value: unknown): value is RuleCounterResetSelection {
  return value === "one" || value === "selected" || value === "all";
}

function validateCounterPage(value: WireObject, expected: Scope, maximumPageSize: number): RuleCounterPage {
  validateScope(value, expected);
  const { availability, observedAtUtc } = value;
  requireWire(availability === "fresh" || availability === "stale" || availability === "unavailable");
  requireWire(observedAtUtc === undefined ? availability !== "fresh" : isTimestamp(observedAtUtc));
  validatePage<RuleCounterItem>(value, maximumPageSize, "ruleId", item => {
    requireWire(isRuleId(item.ruleId) && (item.lastResetAtUtc === undefined || isTimestamp(item.lastResetAtUtc)));
    requireWire(item.count === undefined ? availability !== "fresh"
      : availability !== "unavailable" && observedAtUtc !== undefined && isOrderedInteger(item.count, true));
  });
  return value as unknown as RuleCounterPage;
}

function validateCounterResetResult(value: WireObject, expected: Scope): RuleCounterResetOperationResult {
  requireWire(expected.operationName === counterResetOperationName);
  validateOriginalFacts(value, expected);
  if (value.result !== undefined) {
    const decision = object(value.result);
    requireWire(isCounterResetSelection(decision.selection) && isTimestamp(decision.resetAtUtc)
      && isOrderedInteger(decision.appliedCount, true) && isOrderedInteger(decision.rejectedCount, true));
    if (decision.selection !== "all") {
      const total = BigInt(decision.appliedCount) + BigInt(decision.rejectedCount);
      requireWire(total > 0n && total <= BigInt(decision.selection === "one" ? 1 : maximumExplicitCounterResetIds));
    }
  }
  return value as unknown as RuleCounterResetOperationResult;
}

function validateCounterResetOutcomes(value: WireObject, expected: Scope, maximumPageSize: number): RuleCounterResetOutcomesPage {
  validateScope(value, expected);
  validateOperation(value.operation, expected);
  validatePage<RuleCounterResetOutcome>(value, maximumPageSize, "ruleId", item => {
    requireWire(isRuleId(item.ruleId) && (item.disposition === "reset" || item.disposition === "notFound"));
  });
  return value as unknown as RuleCounterResetOutcomesPage;
}

function validateOriginalFacts(value: WireObject, expected: Scope): void {
  validateScope(value, expected);
  validateOperation(value.operation, expected);
  const established = value.result !== undefined || value.rejection !== undefined;
  const knownSource = value.resultSource === "prepared" || value.resultSource === "replayed" || value.resultSource === "found";
  requireWire(isScalarText(value.resultSource, 64) && isScalarText(value.originalCompletion, 64)
    && (value.result === undefined || value.rejection === undefined) && (!knownSource || established)
    && (value.originalCompletion !== "committed" || established)
    && (value.originalCompletion !== "unknown" && value.originalCompletion !== "notCommitted" || !established));
  if (value.rejection !== undefined) {
    const rejection = object(value.rejection);
    requireWire(typeof rejection.code === "string" && /^[\u0020-\u007e]{1,128}$/u.test(rejection.code)
      && rejection.code.trim().length !== 0 && typeof rejection.message === "string" && validUnicode(rejection.message)
      && rejection.message.trim().length !== 0 && !rejection.message.includes("\0") && encoder.encode(rejection.message).length <= 1024);
  }
  if (value.attemptCompletion !== undefined) {
    const attempt = object(value.attemptCompletion);
    const codes = attempt.cleanupCodes;
    requireWire(isScalarText(attempt.outcome, 64) && typeof attempt.cleanupSucceeded === "boolean"
      && (codes === undefined || Array.isArray(codes) && codes.length <= 16 && codes.every(isCode)));
  }
}

function validateProblem(value: WireObject, status: number, expected: Scope): RuleEngineProblem {
  requireWire(status >= 400 && status <= 599 && value.status === status && isAbsoluteType(value.type)
    && isScalarText(value.title, 256) && isCode(value.code) && (value.detail === undefined || isScalarText(value.detail, 1024, true))
    && (value.correlationId === undefined || isScalarText(value.correlationId, 128)));
  const operation = value.operation === undefined ? undefined : validateOperation(value.operation, expected);
  requireWire([value.result, value.deploymentResult, value.counterResetResult].filter(item => item !== undefined).length <= 1);
  const result = value.counterResetResult !== undefined ? validateCounterResetResult(object(value.counterResetResult), expected)
    : value.deploymentResult !== undefined ? validateDeploymentResult(object(value.deploymentResult), expected)
      : value.result !== undefined ? validateResult(object(value.result), expected) : undefined;
  if (result !== undefined) {
    requireWire(operation !== undefined && operation.name === result.operation.name
      && operation.key === result.operation.key && operation.scope === result.operation.scope);
  }
  return value as unknown as RuleEngineProblem;
}

function checkSubmittedDecision(decision: unknown,
  expected: SaveRuleVersionCommand | RequestRuleDeploymentCommand | RuleCounterResetCommand | undefined): void {
  if (expected !== undefined && decision !== undefined) {
    const result = object(decision);
    if ("selection" in expected) {
      requireWire(result.selection === expected.selection && isOrderedInteger(result.appliedCount, true)
        && isOrderedInteger(result.rejectedCount, true) && (expected.selection === "all"
          || BigInt(result.appliedCount) + BigInt(result.rejectedCount) === BigInt(expected.ruleIds.length)));
    } else if ("schemaRevision" in expected) {
      requireWire(result.schemaRevision === expected.schemaRevision);
    } else {
      requireWire(result.intent === expected.intent && object(result.desired).version === expected.version);
    }
  }
}

function isAbsoluteType(value: unknown): boolean {
  if (!isScalarText(value, 2048)) { return false; }
  try {
    new URL(value);
    return true;
  } catch {
    return false;
  }
}

function isCode(value: unknown): boolean {
  return typeof value === "string" && /^[A-Za-z0-9._-]{1,128}$/u.test(value);
}

function object(value: unknown): WireObject {
  requireWire(typeof value === "object" && value !== null && !Array.isArray(value));
  return value as WireObject;
}

function requireWire(valid: boolean): asserts valid {
  if (!valid) { throw new ManagementWireError(); }
}
