import { ClientProtocolFailure, createFetchTransport, executeRequest, validateReadRetries, validateTimeout } from "@cislib/client-core";
import type { ClientCallContext, ClientTransport, CredentialProvider, RequestObservation } from "@cislib/client-core";
import { captureBaseUrl } from "./client.js";
import type { ConfigurationAdoptionEntry, ConfigurationCallResult, ConfigurationCandidatePreview, ConfigurationCandidatePreviewCommand,
  ConfigurationCreationCommand, ConfigurationCreationContext, ConfigurationEditorContext, ConfigurationOperationResult, ConfigurationProblem,
  ConfigurationRelease, ConfigurationSourceReference, ConfigurationSummary } from "./configuration-models.js";
import { ConfigurationWireError, computeConfigurationPayloadSha256, decodeConfiguration, encodeConfiguration } from "./configuration-wire.js";
import type { Page, PageRequest } from "./models.js";
import type { ConfigurationActions, ConfigurationCohortChoice } from "./scheduler-models.js";
import type { ManagementSchedulerClientOptions } from "./scheduler-client.js";
import { captureSchedulerWireLimits, decodeScheduler, SchedulerWireError } from "./scheduler-wire.js";
import type { SchedulerSchema, SchedulerWireLimits } from "./scheduler-wire.js";
import { isCursor, isOperationKey, isOrderedInteger, isStrongETag, isUuid } from "./wire.js";

export type ManagementConfigurationAdministrationClientOptions = ManagementSchedulerClientOptions;
/** Optional exact editor/action/cohort discovery; every actual write/recovery reauthorizes. */
export class ManagementConfigurationAdministrationClient {
  readonly baseUrl: string;
  readonly wireLimits: SchedulerWireLimits;
  private readonly transport: ClientTransport;
  private readonly credentials: CredentialProvider | undefined;
  private readonly timeoutMs: number;
  private readonly readRetries: number;
  private readonly observe: ((event: RequestObservation) => void) | undefined;

  constructor(options: ManagementConfigurationAdministrationClientOptions) {
    this.baseUrl = captureBaseUrl(options.baseUrl);
    this.wireLimits = captureSchedulerWireLimits(options.wireLimits);
    this.transport = options.transport ?? createFetchTransport();
    this.credentials = options.credentials;
    this.timeoutMs = validateTimeout(options.timeoutMs ?? 30000);
    this.readRetries = validateReadRetries(options.readRetries ?? 0);
    this.observe = options.observe;
  }
  getSourceActions(sourceId: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationActions>> {
    return this.call(() => "configuration/sources/" + uuid(sourceId) + "/actions", "ConfigurationActions", context,
      value => { if ((value as ConfigurationActions).resourceId !== sourceId) { throw new ClientProtocolFailure("response.configuration_echo_mismatch"); } });
  }
  getCreationContext(applicationId: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationCreationContext>> {
    return this.call(() => "applications/" + uuid(applicationId) + "/configuration-creations/context", "ConfigurationCreationContext", context,
      value => { if ((value as ConfigurationCreationContext).applicationId !== applicationId) { throw new ClientProtocolFailure("response.configuration_echo_mismatch"); } });
  }
  createConfiguration(applicationId: string, operationKey: string, ifMatch: string, command: ConfigurationCreationCommand,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call(() => "applications/" + uuid(applicationId) + "/configuration-creations", "ConfigurationOperationResult", context,
      undefined, { applicationId, key: operationKey, ifMatch, command, write: true });
  }
  getCreationOperation(applicationId: string, operationKey: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call(() => "applications/" + uuid(applicationId) + "/configuration-creation-operations/" + portable(operationKey),
      "ConfigurationOperationResult", context, undefined, { applicationId, key: operationKey });
  }
  getConfigurationSummaries(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationSummary>>> {
    let maximumRows = this.wireLimits.maximumPageSize;
    return this.call(() => {
      const parameters = pageParameters(page, this.wireLimits.maximumPageSize); maximumRows = Number(parameters.get("pageSize"));
      return "applications/" + uuid(applicationId) + "/configuration-summaries?" + parameters.toString();
    }, "ConfigurationSummaryPage", context, value => {
      const rows = (value as Page<ConfigurationSummary>).items;
      if (rows.length > maximumRows) { throw new ClientProtocolFailure("response.configuration_page_limit"); }
      if (!rows.every(item => item.applicationId === applicationId)) { throw new ClientProtocolFailure("response.configuration_echo_mismatch"); }
    });
  }
  getBindingActions(applicationId: string, configurationId: string, schemaVersion: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationActions>> {
    return this.call(() => bindingPath(applicationId, configurationId) + "/actions" + schemaQuery(schemaVersion), "ConfigurationActions", context);
  }
  getCohorts(applicationId: string, configurationId: string, schemaVersion: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationCohortChoice>>> {
    return this.call(() => bindingPath(applicationId, configurationId) + "/cohorts" + schemaQuery(schemaVersion) + "&" + pageParameters(page).toString(),
      "ConfigurationCohortChoicePage", context);
  }
  /** Retained immutable releases for one actual binding, not a latest/current pointer. */
  getReleases(applicationId: string, configurationId: string, schemaVersion: string, page?: PageRequest,
    context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationRelease>>> {
    let maximumRows = this.wireLimits.maximumPageSize;
    return this.call(() => {
      const parameters = pageParameters(page, this.wireLimits.maximumPageSize);
      maximumRows = Number(parameters.get("pageSize"));
      return bindingPath(applicationId, configurationId) + "/releases" + schemaQuery(schemaVersion) + "&" + parameters.toString();
    }, "ConfigurationReleasePage", context, value => {
      const rows = (value as Page<ConfigurationRelease>).items;
      if (rows.length > maximumRows) { throw new ClientProtocolFailure("response.configuration_page_limit"); }
      if (!rows.every(item => item.applicationId === applicationId
        && item.configurationId === configurationId && item.schemaVersion === schemaVersion)) {
        throw new ClientProtocolFailure("response.configuration_echo_mismatch");
      }
    });
  }
  /** The native owner filters the exact definition before recipient expansion and paging. */
  getBindingStatus(applicationId: string, configurationId: string, schemaVersion: string, page?: PageRequest,
    context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationAdoptionEntry>>> {
    let maximumRows = this.wireLimits.maximumPageSize;
    return this.call(() => {
      const parameters = pageParameters(page, this.wireLimits.maximumPageSize);
      maximumRows = Number(parameters.get("pageSize"));
      return bindingPath(applicationId, configurationId) + "/status" + schemaQuery(schemaVersion) + "&" + parameters.toString();
    }, "ConfigurationAdoptionPage", context, value => {
      const rows = (value as Page<ConfigurationAdoptionEntry>).items;
      if (rows.length > maximumRows) { throw new ClientProtocolFailure("response.configuration_page_limit"); }
      if (!rows.every(item => item.applicationId === applicationId
        && item.configurationId === configurationId && item.schemaVersion === schemaVersion)) {
        throw new ClientProtocolFailure("response.configuration_echo_mismatch");
      }
    });
  }
  getBindingEditorContext(applicationId: string, configurationId: string, schemaVersion: string, source: "common" | "application",
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationEditorContext>> {
    return this.call(() => {
      if (source !== "common" && source !== "application") { throw new TypeError("Select the Common or Application source."); }
      return bindingPath(applicationId, configurationId) + "/editor-context" + schemaQuery(schemaVersion) + "&source=" + source;
    }, "ConfigurationEditorContext", context, value => {
      const result = value as ConfigurationEditorContext;
      if (result.applicationId !== applicationId || result.configurationId !== configurationId
        || result.schemaVersion !== schemaVersion || result.selectedSource !== source) {
        throw new ClientProtocolFailure("response.configuration_echo_mismatch");
      }
    });
  }
  /** Read-only POST; a candidate is neither a publication nor an automatic write. */
  async previewCandidate(command: ConfigurationCandidatePreviewCommand, page?: PageRequest,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationCandidatePreview>> {
    const limits = { maximumBodyBytes: Math.min(this.wireLimits.maximumBodyBytes, 1048576),
      maximumJsonDepth: Math.min(this.wireLimits.maximumJsonDepth, 16) };
    let captured: ConfigurationCandidatePreviewCommand | undefined;
    const result = await executeRequest<ConfigurationCandidatePreview, ConfigurationProblem>({
      operation: "configuration", transport: this.transport, timeoutMs: this.timeoutMs, readRetries: this.readRetries,
      ...(context === undefined ? {} : { context }), ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }),
      prepare: async signal => {
        const url = this.baseUrl + "management/v1/configuration/candidate-preview?" + pageParameters(page).toString();
        const body = await encodeConfiguration("ConfigurationCandidatePreviewCommand", command, "request", limits, signal);
        captured = await decodeConfiguration<ConfigurationCandidatePreviewCommand>("ConfigurationCandidatePreviewCommand", body, "request", limits, signal);
        return { url, method: "POST", headers: Object.freeze({ Accept: "application/json, application/problem+json", "Content-Type": "application/json" }),
          body, mutating: false, maximumResponseBytes: limits.maximumBodyBytes, maximumErrorBytes: 65536 };
      },
      decode: async (response, bytes, signal) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            const value = await decodeConfiguration<ConfigurationCandidatePreview>("ConfigurationCandidatePreview", bytes, "response", limits, signal);
            if (captured === undefined) { throw new ClientProtocolFailure("response.configuration_echo_mismatch"); }
            const draftSha = await computeConfigurationPayloadSha256(captured.payloadJson, limits, signal);
            if (value.applicationId !== captured.applicationId || value.configurationId !== captured.configurationId
              || value.schemaVersion !== captured.schemaVersion || value.selectedSource !== captured.selectedSource
              || value.sourceId !== captured.sourceId || value.sourceETag !== captured.sourceETag
              || value.assignmentETag !== captured.assignmentETag || value.draftSha256 !== draftSha
              || !sameReference(value.commonSource, captured.commonSource) || !sameReference(value.applicationSource, captured.applicationSource)) {
              throw new ClientProtocolFailure("response.configuration_echo_mismatch");
            }
            return { value };
          }
          if (contentType !== "application/problem+json") {
            if (bytes.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const problem = await decodeConfiguration<ConfigurationProblem>("ConfigurationProblem", bytes, "response", limits, signal);
          if (problem.status !== response.status) { throw new ClientProtocolFailure("response.problem_status_mismatch"); }
          return { problem };
        } catch (error) {
          if (error instanceof ConfigurationWireError) { signal.throwIfAborted(); throw new ClientProtocolFailure(error.code); }
          throw error;
        }
      }
    });
    return Object.freeze(result);
  }
  private async call<T>(path: () => string, schema: SchedulerSchema | "ConfigurationEditorContext" | "ConfigurationReleasePage" | "ConfigurationAdoptionPage"
    | "ConfigurationCreationContext" | "ConfigurationSummaryPage" | "ConfigurationOperationResult", context?: ClientCallContext,
    check?: (value: unknown) => void, original?: { readonly applicationId: string; readonly key: string; readonly ifMatch?: string; readonly command?: ConfigurationCreationCommand; readonly write?: boolean }): Promise<ConfigurationCallResult<T>> {
    const configurationSchema = schema === "ConfigurationEditorContext" || schema === "ConfigurationReleasePage" || schema === "ConfigurationAdoptionPage"
      || schema === "ConfigurationCreationContext" || schema === "ConfigurationSummaryPage" || schema === "ConfigurationOperationResult" ? schema : undefined;
    const configurationLimits = { maximumBodyBytes: Math.min(this.wireLimits.maximumBodyBytes, 1048576),
      maximumJsonDepth: Math.min(this.wireLimits.maximumJsonDepth, 16) };
    let captured: ConfigurationCreationCommand | undefined;
    const result = await executeRequest<T, ConfigurationProblem>({
      operation: "configuration", transport: this.transport, timeoutMs: this.timeoutMs, readRetries: this.readRetries,
      ...(context === undefined ? {} : { context }), ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }),
      ...(original === undefined ? {} : { operationKey: original.key }),
      prepare: async signal => {
        const url = this.baseUrl + "management/v1/" + path(), headers: Record<string, string> = { Accept: "application/json, application/problem+json" };
        let body: Uint8Array | undefined;
        if (original !== undefined) {
          uuid(original.applicationId); portable(original.key);
          if (original.write === true) {
            if (!isStrongETag(original.ifMatch)) { throw new TypeError("Retain the original key and exact strong creation context ETag."); }
            body = await encodeConfiguration("ConfigurationCreationCommand", original.command, "request", configurationLimits, signal);
            captured = await decodeConfiguration<ConfigurationCreationCommand>("ConfigurationCreationCommand", body, "request", configurationLimits, signal);
            headers["Content-Type"] = "application/json"; headers["Idempotency-Key"] = original.key; headers["If-Match"] = original.ifMatch;
          }
        }
        return { url, method: body === undefined ? "GET" : "POST", headers: Object.freeze(headers), ...(body === undefined ? {} : { body }),
          mutating: body !== undefined, maximumResponseBytes: configurationSchema === undefined ? this.wireLimits.maximumBodyBytes : configurationLimits.maximumBodyBytes,
          maximumErrorBytes: Math.min(this.wireLimits.maximumBodyBytes, 65536) };
      },
      decode: async (response, bytes, signal) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            const value = configurationSchema === undefined
              ? await decodeScheduler<T>(schema as SchedulerSchema, bytes, "response", this.wireLimits, signal)
              : await decodeConfiguration<T>(configurationSchema, bytes, "response", configurationLimits, signal);
            if (original !== undefined) { checkCreation(value as ConfigurationOperationResult, original.applicationId, original.key, captured); }
            check?.(value); return { value };
          }
          if (contentType !== "application/problem+json") {
            if (bytes.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const problem = await decodeConfiguration<ConfigurationProblem>("ConfigurationProblem", bytes, "response", {
            maximumBodyBytes: Math.min(this.wireLimits.maximumBodyBytes, 65536),
            maximumJsonDepth: Math.min(this.wireLimits.maximumJsonDepth, 16)
          }, signal);
          if (problem.status !== response.status) { throw new ClientProtocolFailure("response.problem_status_mismatch"); }
          if (original !== undefined) {
            if (problem.operation !== undefined) { checkCreationOperation(problem.operation, original.applicationId, original.key); }
            if (problem.result !== undefined) { checkCreation(problem.result, original.applicationId, original.key, captured); }
          }
          return { problem };
        } catch (error) {
          if (error instanceof SchedulerWireError || error instanceof ConfigurationWireError) {
            signal.throwIfAborted(); throw new ClientProtocolFailure(error.code);
          }
          throw error;
        }
      }
    });
    const uncertain = original?.write === true && (result.problem?.result?.originalCompletion === "unknown"
      || result.status !== undefined && result.status >= 500 && result.problem?.result === undefined);
    return Object.freeze({ ...result, ...(uncertain ? { mutationUncertain: true } : {}) });
  }
}
export type ManagementConfigurationAdministrationClientPort = Pick<ManagementConfigurationAdministrationClient,
  "getSourceActions" | "getBindingActions" | "getCohorts" | "getBindingEditorContext" | "previewCandidate">;
/** Additive companion: existing administration substitutes acquire no new compulsory methods. */
export type ManagementConfigurationRolloutClientPort = Pick<ManagementConfigurationAdministrationClient, "getReleases" | "getBindingStatus">;
/** Optional additive creation/summary boundary; existing administration substitutes remain compatible. */
export type ManagementConfigurationCreationClientPort = Pick<ManagementConfigurationAdministrationClient,
  "getCreationContext" | "createConfiguration" | "getCreationOperation" | "getConfigurationSummaries">;

function checkCreationOperation(operation: { readonly name: string; readonly scope: string; readonly key: string }, applicationId: string, key: string): void {
  const prefix = `management.configuration.v1/management.configuration.create/${applicationId}/`;
  if (operation.name !== "management.configuration.create" || operation.key !== key || !operation.scope.startsWith(prefix)
    || !/^management\.actor\.v1\.[0-9a-f]{64}$/u.test(operation.scope.slice(prefix.length))) {
    throw new ClientProtocolFailure("response.configuration_echo_mismatch");
  }
}
function checkCreation(value: ConfigurationOperationResult, applicationId: string, key: string, command?: ConfigurationCreationCommand): void {
  checkCreationOperation(value.operation, applicationId, key);
  const decision = value.result;
  if (decision === undefined) { return; }
  if (["catalogApplied", "sourcePublished", "releaseCreated", "assignmentsReplaced"].includes(decision.kind)
    || decision.kind === "configurationCreated" && (decision.applicationId !== applicationId || command !== undefined && decision.configurationId !== command.configurationId)) {
    throw new ClientProtocolFailure("response.configuration_echo_mismatch");
  }
}

function uuid(value: string): string { if (!isUuid(value)) { throw new TypeError("Supply a canonical nonempty lowercase UUID."); } return value; }
function sameReference(left: ConfigurationSourceReference, right: ConfigurationSourceReference): boolean {
  return left.state === right.state && (left.state !== "present" || right.state === "present" && left.publicationId === right.publicationId);
}
function portable(value: string): string { if (!isOperationKey(value)) { throw new TypeError("Supply an exact portable ID excluding dot segments."); } return encodeURIComponent(value); }
function bindingPath(applicationId: string, configurationId: string): string { return "applications/" + uuid(applicationId) + "/configuration/" + portable(configurationId); }
function schemaQuery(schemaVersion: string): string { if (!isOrderedInteger(schemaVersion)) { throw new TypeError("Supply a positive Int64 schema string."); } return "?schemaVersion=" + schemaVersion; }
function pageParameters(page: PageRequest = {}, maximum = 500): URLSearchParams {
  const size = page.pageSize ?? Math.min(100, maximum);
  if (!Number.isSafeInteger(size) || size < 1 || size > maximum || page.cursor !== undefined && !isCursor(page.cursor)) {
    throw new TypeError("Supply a finite page size and opaque same-endpoint cursor.");
  }
  const fields = new URLSearchParams({ pageSize: String(size) });
  if (page.cursor !== undefined) { fields.set("cursor", page.cursor); }
  return fields;
}
