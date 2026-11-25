import { ClientProtocolFailure, createFetchTransport, executeRequest, validateReadRetries, validateTimeout } from "@cislib/client-core";
import type { ClientCallContext, ClientTransport, CredentialProvider, RequestObservation } from "@cislib/client-core";
import { captureBaseUrl } from "./client.js";
import type {
  ConfigurationAdoptionEntry, ConfigurationAssignmentCommand, ConfigurationAssignmentMap, ConfigurationCallResult,
  ConfigurationCatalog, ConfigurationCatalogCommand, ConfigurationContentRejection, ConfigurationDefinition,
  ConfigurationOperationResult, ConfigurationPreview, ConfigurationPreviewCommand, ConfigurationProblem,
  ConfigurationPublication, ConfigurationPublicationCommand, ConfigurationRelease, ConfigurationReleaseCommand,
  ConfigurationSnapshot, ConfigurationSnapshotTarget, ConfigurationSource, ConfigurationStatusReport,
  ConfigurationValidationCommand, ConfigurationValidationResult
} from "./configuration-models.js";
import type { Page, PageRequest, ReportReceipt } from "./models.js";
import { captureConfigurationWireLimits, ConfigurationWireError, decodeConfiguration, decodeConfigurationSnapshot,
  encodeConfiguration, sameConfigurationTarget } from "./configuration-wire.js";
import type { ConfigurationSchema, ConfigurationWireLimits } from "./configuration-wire.js";
import { validateTarget } from "./configuration-wire-validation.js";
import { decodeManagement, isCursor, isOperationKey, isOrderedInteger, isStrongETag, isUuid, ManagementWireError } from "./wire.js";

export interface ManagementConfigurationClientOptions {
  readonly baseUrl: string; readonly transport?: ClientTransport; readonly credentials?: CredentialProvider;
  readonly timeoutMs?: number; readonly readRetries?: number; readonly wireLimits?: Partial<ConfigurationWireLimits>;
  readonly observe?: (event: RequestObservation) => void;
}
/** Structural public substitution port. It starts no polling or framework-owned runtime. */
export interface ManagementConfigurationClientPort {
  getDefinitions(page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationDefinition>>>;
  getApplicationDefinitions?(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationDefinition>>>;
  validate(command: ConfigurationValidationCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationValidationResult>>;
  preview(command: ConfigurationPreviewCommand, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationPreview>>;
  getSource(sourceId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationSource>>;
  publishSource(sourceId: string, operationKey: string, ifMatch: string, command: ConfigurationPublicationCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getPublication(publicationId: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationPublication>>;
  createRelease(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string, ifMatch: string,
    command: ConfigurationReleaseCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getRelease(releaseId: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationRelease>>;
  getAssignments(applicationId: string, configurationId: string, schemaVersion: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationAssignmentMap>>;
  replaceAssignments(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string, ifMatch: string,
    command: ConfigurationAssignmentCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getRegistrationConfiguration(expectedTarget: ConfigurationSnapshotTarget, retainedSnapshot?: ConfigurationSnapshot,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationSnapshot>>;
  reportStatus(registrationId: string, report: ConfigurationStatusReport, context?: ClientCallContext): Promise<ConfigurationCallResult<ReportReceipt>>;
  getStatus(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationAdoptionEntry>>>;
  getCatalog(context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationCatalog>>;
  applyCatalog(operationKey: string, ifMatch: string, command: ConfigurationCatalogCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getCatalogOperation(operationKey: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getPublicationOperation(sourceId: string, operationKey: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getReleaseOperation(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
  getAssignmentOperation(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>>;
}
type Method = "GET" | "POST" | "PUT";
interface Call {
  readonly method: Method; readonly path: () => string; readonly schema: ConfigurationSchema | "ReportReceipt";
  readonly context?: ClientCallContext; readonly bodySchema?: ConfigurationSchema; readonly body?: unknown;
  readonly operationKey?: string; readonly ifMatch?: string; readonly mutating?: boolean; readonly operationName?: string;
  readonly expectedTarget?: ConfigurationSnapshotTarget; readonly retainedSnapshot?: ConfigurationSnapshot;
  readonly check?: (value: unknown) => void;
  readonly sourceId?: string;
  readonly binding?: { readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string };
}
const names = Object.freeze({ catalog: "management.configuration.catalog.apply", publication: "management.configuration.source.publish",
  release: "management.configuration.release.create", assignment: "management.configuration.assignment.replace" });

/** Portable reusable transport client; it retains no bearer, user context, snapshot history or timer. */
export class ManagementConfigurationClient implements ManagementConfigurationClientPort {
  readonly baseUrl: string;
  readonly wireLimits: ConfigurationWireLimits;
  private readonly transport: ClientTransport;
  private readonly credentials: CredentialProvider | undefined;
  private readonly timeoutMs: number;
  private readonly readRetries: number;
  private readonly observe: ((event: RequestObservation) => void) | undefined;
  constructor(options: ManagementConfigurationClientOptions) {
    this.baseUrl = captureBaseUrl(options.baseUrl); this.wireLimits = captureConfigurationWireLimits(options.wireLimits);
    this.transport = options.transport ?? createFetchTransport(); this.credentials = options.credentials;
    this.timeoutMs = validateTimeout(options.timeoutMs ?? 30000); this.readRetries = validateReadRetries(options.readRetries ?? 0);
    this.observe = options.observe;
  }
  getDefinitions(page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationDefinition>>> {
    return this.call({ method: "GET", path: () => "configuration/definitions" + pageQuery(page), schema: "ConfigurationDefinitionPage", ...withContext(context) });
  }
  getApplicationDefinitions(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationDefinition>>> {
    return this.call({ method: "GET", path: () => "applications/" + uuid(applicationId) + "/configuration-definitions" + pageQuery(page),
      schema: "ConfigurationDefinitionPage", ...withContext(context) });
  }
  validate(command: ConfigurationValidationCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationValidationResult>> {
    return this.call({ method: "POST", path: () => "configuration/validate", schema: "ConfigurationValidationResult",
      bodySchema: "ConfigurationValidationCommand", body: command, mutating: false, ...withContext(context) });
  }
  preview(command: ConfigurationPreviewCommand, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationPreview>> {
    return this.call({ method: "POST", path: () => "configuration/preview" + pageQuery(page), schema: "ConfigurationPreview",
      bodySchema: "ConfigurationPreviewCommand", body: command, mutating: false, ...withContext(context) });
  }
  getSource(sourceId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationSource>> {
    return this.call({ method: "GET", path: () => "configuration/sources/" + uuid(sourceId) + pageQuery(page), schema: "ConfigurationSource",
      check: value => echo((value as ConfigurationSource).sourceId === sourceId), ...withContext(context) });
  }
  publishSource(sourceId: string, operationKey: string, ifMatch: string, command: ConfigurationPublicationCommand,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "POST", path: () => "configuration/sources/" + uuid(sourceId) + "/publications", schema: "ConfigurationOperationResult",
      bodySchema: "ConfigurationPublicationCommand", body: command, operationKey, ifMatch, operationName: names.publication, sourceId, ...withContext(context) });
  }
  getPublication(publicationId: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationPublication>> {
    return this.call({ method: "GET", path: () => "configuration/publications/" + uuid(publicationId), schema: "ConfigurationPublication",
      check: value => echo((value as ConfigurationPublication).publicationId === publicationId), ...withContext(context) });
  }
  createRelease(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string, ifMatch: string,
    command: ConfigurationReleaseCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "POST", path: () => bindingPath(applicationId, configurationId) + "/releases" + schemaQuery(schemaVersion),
      schema: "ConfigurationOperationResult", bodySchema: "ConfigurationReleaseCommand", body: command, operationKey, ifMatch,
      operationName: names.release, binding: { applicationId, configurationId, schemaVersion }, ...withContext(context) });
  }
  getRelease(releaseId: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationRelease>> {
    return this.call({ method: "GET", path: () => "configuration/releases/" + uuid(releaseId), schema: "ConfigurationRelease",
      check: value => echo((value as ConfigurationRelease).releaseId === releaseId), ...withContext(context) });
  }
  getAssignments(applicationId: string, configurationId: string, schemaVersion: string,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationAssignmentMap>> {
    return this.call({ method: "GET", path: () => bindingPath(applicationId, configurationId) + "/assignments" + schemaQuery(schemaVersion),
      schema: "ConfigurationAssignmentMap", check: value => {
        const map = value as ConfigurationAssignmentMap;
        echo(map.applicationId === applicationId && map.configurationId === configurationId && map.schemaVersion === schemaVersion);
      }, ...withContext(context) });
  }
  replaceAssignments(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string, ifMatch: string,
    command: ConfigurationAssignmentCommand, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "PUT", path: () => bindingPath(applicationId, configurationId) + "/assignments" + schemaQuery(schemaVersion),
      schema: "ConfigurationOperationResult", bodySchema: "ConfigurationAssignmentCommand", body: command, operationKey, ifMatch,
      operationName: names.assignment, binding: { applicationId, configurationId, schemaVersion }, ...withContext(context) });
  }
  getRegistrationConfiguration(expectedTarget: ConfigurationSnapshotTarget, retainedSnapshot?: ConfigurationSnapshot,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationSnapshot>> {
    return this.call({ method: "GET", path: () => "registrations/" + uuid(expectedTarget.registrationId) + "/configuration/"
      + portable(expectedTarget.configurationId) + schemaQuery(expectedTarget.schemaVersion), schema: "ConfigurationSnapshot", expectedTarget,
      ...(retainedSnapshot === undefined ? {} : { retainedSnapshot }), ...withContext(context) });
  }
  reportStatus(registrationId: string, report: ConfigurationStatusReport, context?: ClientCallContext): Promise<ConfigurationCallResult<ReportReceipt>> {
    return this.call({ method: "PUT", path: () => "registrations/" + uuid(registrationId) + "/configuration-status", schema: "ReportReceipt",
      bodySchema: "ConfigurationStatusReport", body: report, check: value => {
        const receipt = value as ReportReceipt;
        echo(receipt.registrationId === registrationId && receipt.stream === "configuration-status");
      }, ...withContext(context) });
  }
  getStatus(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ConfigurationCallResult<Page<ConfigurationAdoptionEntry>>> {
    return this.call({ method: "GET", path: () => "applications/" + uuid(applicationId) + "/configuration-status" + pageQuery(page),
      schema: "ConfigurationAdoptionPage", check: value => echo((value as Page<ConfigurationAdoptionEntry>).items.every(row => row.applicationId === applicationId)), ...withContext(context) });
  }
  getCatalog(context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationCatalog>> {
    return this.call({ method: "GET", path: () => "configuration/catalog", schema: "ConfigurationCatalog", ...withContext(context) });
  }
  applyCatalog(operationKey: string, ifMatch: string, command: ConfigurationCatalogCommand,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "POST", path: () => "configuration/catalog/apply", schema: "ConfigurationOperationResult",
      bodySchema: "ConfigurationCatalogCommand", body: command, operationKey, ifMatch, operationName: names.catalog, ...withContext(context) });
  }
  getCatalogOperation(operationKey: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "GET", path: () => "configuration/catalog/operations/" + key(operationKey), schema: "ConfigurationOperationResult",
      operationKey, operationName: names.catalog, ...withContext(context) });
  }
  getPublicationOperation(sourceId: string, operationKey: string, context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "GET", path: () => "configuration/sources/" + uuid(sourceId) + "/publication-operations/" + key(operationKey),
      schema: "ConfigurationOperationResult", operationKey, operationName: names.publication, sourceId, ...withContext(context) });
  }
  getReleaseOperation(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "GET", path: () => bindingPath(applicationId, configurationId) + "/release-operations/" + key(operationKey) + schemaQuery(schemaVersion),
      schema: "ConfigurationOperationResult", operationKey, operationName: names.release,
      binding: { applicationId, configurationId, schemaVersion }, ...withContext(context) });
  }
  getAssignmentOperation(applicationId: string, configurationId: string, schemaVersion: string, operationKey: string,
    context?: ClientCallContext): Promise<ConfigurationCallResult<ConfigurationOperationResult>> {
    return this.call({ method: "GET", path: () => bindingPath(applicationId, configurationId) + "/assignment-operations/" + key(operationKey) + schemaQuery(schemaVersion),
      schema: "ConfigurationOperationResult", operationKey, operationName: names.assignment,
      binding: { applicationId, configurationId, schemaVersion }, ...withContext(context) });
  }

  private async call<T>(call: Call): Promise<ConfigurationCallResult<T>> {
    let contentRejection: ConfigurationContentRejection | undefined;
    let target: ConfigurationSnapshotTarget | undefined;
    let retained: ConfigurationSnapshot | undefined;
    let submittedSequence: string | undefined;
    const result = await executeRequest<T, ConfigurationProblem>({
      operation: "configuration", transport: this.transport, timeoutMs: this.timeoutMs, readRetries: this.readRetries,
      ...withContext(call.context), ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }), ...(call.operationKey === undefined ? {} : { operationKey: call.operationKey }),
      prepare: async signal => {
        let suffix = call.path();
        const headers: Record<string, string> = { Accept: "application/json, application/problem+json" };
        if (call.operationKey !== undefined) { key(call.operationKey); }
        if (call.ifMatch !== undefined) {
          if (!isStrongETag(call.ifMatch) || call.operationKey === undefined) { throw new TypeError("Supply an original key and strong current resource ETag."); }
          headers["Idempotency-Key"] = call.operationKey; headers["If-Match"] = call.ifMatch;
        }
        if (call.expectedTarget !== undefined) {
          target = captureTarget(call.expectedTarget);
          suffix = "registrations/" + target.registrationId + "/configuration/" + portable(target.configurationId) + schemaQuery(target.schemaVersion);
          headers["Cache-Control"] = "no-store";
          if (call.retainedSnapshot !== undefined) {
            const bytes = await encodeConfiguration("ConfigurationSnapshot", call.retainedSnapshot, "response", this.wireLimits, signal);
            const prior = await decodeConfigurationSnapshot(bytes, target, this.wireLimits, signal);
            if (prior.snapshot === undefined) { throw new TypeError("A conditional read requires a complete validated snapshot for this exact target/session/schema."); }
            retained = prior.snapshot; headers["If-None-Match"] = retained.eTag;
          }
        }
        const body = call.bodySchema === undefined ? undefined : await encodeConfiguration(call.bodySchema, call.body, "request", this.wireLimits, signal);
        if (call.bodySchema === "ConfigurationStatusReport" && body !== undefined) {
          submittedSequence = (JSON.parse(new TextDecoder().decode(body)) as ConfigurationStatusReport).sequence;
        }
        if (body !== undefined) { headers["Content-Type"] = "application/json"; }
        return { url: this.baseUrl + "management/v1/" + suffix, method: call.method, headers: Object.freeze(headers),
          ...(body === undefined ? {} : { body }), maximumResponseBytes: this.wireLimits.maximumBodyBytes,
          maximumErrorBytes: 65536, mutating: call.mutating ?? call.method !== "GET", allowNotModified: retained !== undefined };
      },
      decode: async (response, bytes, signal) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status === 304) {
            if (retained === undefined || target === undefined || bytes.length !== 0 || response.headers.etag !== retained.eTag
              || !sameConfigurationTarget(retained.desired, target)) { throw new ClientProtocolFailure("configuration.conditional.invalid"); }
            return { value: retained as T };
          }
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            let value: T;
            if (call.schema === "ConfigurationSnapshot") {
              if (target === undefined) { throw new TypeError("A runtime snapshot requires an independently trusted target."); }
              const read = await decodeConfigurationSnapshot(bytes, target, this.wireLimits, signal);
              if (read.snapshot === undefined) {
                if (read.desired !== undefined) { contentRejection = Object.freeze({ desired: read.desired, code: read.failureCode ?? "configuration.content.invalid" }); }
                throw new ClientProtocolFailure(read.failureCode ?? "configuration.wire.invalid");
              }
              value = read.snapshot as T;
            } else if (call.schema === "ReportReceipt") {
              value = decodeManagement<T>("ReportReceipt", bytes, "response");
              echo((value as ReportReceipt).submittedSequence === submittedSequence && (value as ReportReceipt).acceptedSequence === submittedSequence);
            }
            else { value = await decodeConfiguration<T>(call.schema, bytes, "response", this.wireLimits, signal); }
            if (call.schema === "ConfigurationOperationResult") { checkOperation(value as ConfigurationOperationResult, call); }
            call.check?.(value); return { value };
          }
          if (contentType !== "application/problem+json") {
            if (bytes.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const problem = await decodeConfiguration<ConfigurationProblem>("ConfigurationProblem", bytes, "response", this.wireLimits, signal);
          if (problem.status !== response.status) { throw new ClientProtocolFailure("response.problem_status_mismatch"); }
          if (problem.operation !== undefined) {
            echo((call.operationKey === undefined || problem.operation.key === call.operationKey)
              && (call.operationName === undefined || problem.operation.name === call.operationName));
          }
          return { problem };
        } catch (error) {
          if (error instanceof ConfigurationWireError || error instanceof ManagementWireError) {
            signal.throwIfAborted(); throw new ClientProtocolFailure(error.code);
          }
          throw error;
        }
      }
    });
    const uncertain = (call.mutating ?? call.method !== "GET") && (result.problem?.result?.originalCompletion === "unknown"
      || result.status !== undefined && result.status >= 500 && result.problem?.result === undefined);
    if (contentRejection !== undefined && result.kind === "protocolFailure" && result.code === contentRejection.code) {
      const { value: _value, problem: _problem, ...facts } = result;
      return Object.freeze({ ...facts, kind: "protocolFailure", ...(uncertain ? { mutationUncertain: true } : {}), contentRejection });
    }
    return Object.freeze({ ...result, ...(uncertain ? { mutationUncertain: true } : {}) });
  }
}

function withContext(context: ClientCallContext | undefined): { readonly context?: ClientCallContext } { return context === undefined ? {} : { context }; }
function uuid(value: string): string { if (!isUuid(value)) { throw new TypeError("Supply a canonical nonempty lowercase UUID."); } return value; }
function portable(value: string): string { if (!isOperationKey(value)) { throw new TypeError("Supply an exact portable ID excluding dot segments."); } return encodeURIComponent(value); }
function key(value: string): string { return portable(value); }
function bindingPath(applicationId: string, configurationId: string): string { return "applications/" + uuid(applicationId) + "/configuration/" + portable(configurationId); }
function schemaQuery(schema: string): string { if (!isOrderedInteger(schema)) { throw new TypeError("Supply an exact positive Int64 schema string."); } return "?schemaVersion=" + schema; }
function pageQuery(page: PageRequest = {}): string {
  const size = page.pageSize ?? 100;
  if (!Number.isSafeInteger(size) || size < 1 || size > 500 || page.cursor !== undefined && !isCursor(page.cursor)) { throw new TypeError("Supply finite page and authenticated same-endpoint cursor."); }
  const query = new URLSearchParams({ pageSize: String(size) }); if (page.cursor !== undefined) { query.set("cursor", page.cursor); }
  return "?" + query.toString();
}
function captureTarget(expected: ConfigurationSnapshotTarget): ConfigurationSnapshotTarget {
  const value: ConfigurationSnapshotTarget = { authorityId: expected.authorityId, applicationId: expected.applicationId,
    environment: expected.environment, registrationId: expected.registrationId, configurationId: expected.configurationId,
    schemaVersion: expected.schemaVersion, hostApplicationName: expected.hostApplicationName,
    ...(expected.cohortId === undefined ? {} : { cohortId: expected.cohortId }) };
  validateTarget(value); return Object.freeze(value);
}
function checkOperation(value: ConfigurationOperationResult, call: Call): void {
  echo((call.operationKey === undefined || value.operation.key === call.operationKey)
    && (call.operationName === undefined || value.operation.name === call.operationName));
  if (value.result !== undefined) {
    const expectedKind = call.operationName === names.catalog ? "catalogApplied" : call.operationName === names.publication ? "sourcePublished"
      : call.operationName === names.release ? "releaseCreated" : call.operationName === names.assignment ? "assignmentsReplaced" : undefined;
    if (["catalogApplied", "sourcePublished", "releaseCreated", "assignmentsReplaced"].includes(value.result.kind)) {
      echo(expectedKind === undefined || value.result.kind === expectedKind);
      if (call.sourceId !== undefined) { echo(value.result.sourceId === call.sourceId); }
      if (call.binding !== undefined) {
        echo(value.result.applicationId === call.binding.applicationId && value.result.configurationId === call.binding.configurationId
          && value.result.schemaVersion === call.binding.schemaVersion);
      }
    }
  }
}
function echo(valid: boolean): void { if (!valid) { throw new ClientProtocolFailure("response.configuration_echo_mismatch"); } }
