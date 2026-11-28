import { ClientProtocolFailure, createFetchTransport, executeRequest, validateReadRetries, validateTimeout } from "@cislib/client-core";
import type { ClientCallContext, ClientTransport, CredentialProvider, RequestObservation } from "@cislib/client-core";
import type {
  Application, ApplicationQuery, EnvironmentEntry, Capabilities, CloseReceipt, ControlConfirmation, Instance, InventoryItem, InventorySnapshot, ManagementCallResult,
  MetadataCommand, MetadataResult, Observation, Page, PageRequest, Problem, RegistrationReceipt, RegistrationRequest,
  ReportReceipt, ServiceInfo, ServiceFeaturePage, ApplicationApiCapabilityPage
} from "./models.js";
import { bodyLimit, captureWireLimits, decodeManagement, encodeManagement, isCursor, isOperationKey, isStrongETag, isUuid, ManagementWireError } from "./wire.js";
import { isScalarText } from "./wire-validation.js";
import type { ManagementSchema, ManagementWireLimits } from "./wire.js";

export interface ManagementClientOptions {
  readonly baseUrl: string;
  readonly transport?: ClientTransport;
  readonly credentials?: CredentialProvider;
  readonly timeoutMs?: number;
  readonly readRetries?: number;
  readonly wireLimits?: Partial<ManagementWireLimits>;
  readonly observe?: (event: RequestObservation) => void;
}

/** Explicit optional complete-discovery port; existing client substitutes gain no mandatory members. */
export interface ManagementApiDiscoveryClientPort {
  listServiceFeatures(page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<ServiceFeaturePage>>;
  listApplicationApiCapabilities(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<ApplicationApiCapabilityPage>>;
}

/** One reusable typed client. It stores no per-user credential, registration loop or caller context. */
export class ManagementClient implements ManagementApiDiscoveryClientPort {
  readonly baseUrl: string;
  readonly wireLimits: ManagementWireLimits;
  private readonly transport: ClientTransport;
  private readonly credentials: CredentialProvider | undefined;
  private readonly timeoutMs: number;
  private readonly readRetries: number;
  private readonly observe: ((event: RequestObservation) => void) | undefined;

  constructor(options: ManagementClientOptions) {
    this.baseUrl = captureBaseUrl(options.baseUrl);
    this.wireLimits = captureWireLimits(options.wireLimits);
    this.transport = options.transport ?? createFetchTransport();
    this.credentials = options.credentials;
    this.timeoutMs = validateTimeout(options.timeoutMs ?? 30000);
    this.readRetries = validateReadRetries(options.readRetries ?? 0);
    this.observe = options.observe;
  }

  getServiceInfo(context?: ClientCallContext): Promise<ManagementCallResult<ServiceInfo>> {
    return this.call("GET", "service-info", () => "service-info", "ServiceInfo", context);
  }
  register(registrationId: string, request: RegistrationRequest, context?: ClientCallContext): Promise<ManagementCallResult<RegistrationReceipt>> {
    return this.call("PUT", "registration", () => "registrations/" + uuid(registrationId), "RegistrationReceipt", context, "RegistrationRequest", request);
  }
  getControl(registrationId: string, attemptId: string, context?: ClientCallContext): Promise<ManagementCallResult<ControlConfirmation>> {
    return this.call("GET", "control", () => "registrations/" + uuid(registrationId) + "/control?attemptId=" + uuid(attemptId),
      "ControlConfirmation", context, undefined, undefined, undefined, undefined, { registrationId, attemptId });
  }
  reportObservation(registrationId: string, observation: Observation, context?: ClientCallContext): Promise<ManagementCallResult<ReportReceipt>> {
    return this.call("PUT", "observation", () => "registrations/" + uuid(registrationId) + "/observation", "ReportReceipt", context, "Observation", observation);
  }
  reportInventory(registrationId: string, inventory: InventorySnapshot, context?: ClientCallContext): Promise<ManagementCallResult<ReportReceipt>> {
    return this.call("PUT", "inventory-report", () => "registrations/" + uuid(registrationId) + "/inventory", "ReportReceipt", context, "InventorySnapshot", inventory);
  }
  closeRegistration(registrationId: string, context?: ClientCallContext): Promise<ManagementCallResult<CloseReceipt>> {
    return this.call("DELETE", "registration-close", () => "registrations/" + uuid(registrationId), "CloseReceipt", context);
  }
  listApplications(page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<Application>>> {
    return this.call("GET", "applications", () => "applications" + pageQuery(page), "ApplicationPage", context);
  }
  queryApplications(query: ApplicationQuery, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<Application>>> {
    return this.call("GET", "applications", () => {
      if (query === null || typeof query !== "object"
        || query.query !== undefined && !isScalarText(query.query, 128)
        || query.environment !== undefined && !isScalarText(query.environment, 255)) {
        throw new TypeError("Supply bounded literal application and exact environment filters.");
      }
      const parameters = new URLSearchParams(pageQuery(page).slice(1));
      if (query.query !== undefined) { parameters.set("query", query.query); }
      if (query.environment !== undefined) { parameters.set("environment", query.environment); }
      return "applications?" + parameters.toString();
    }, "ApplicationPage", context);
  }
  listEnvironments(page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<EnvironmentEntry>>> {
    return this.call("GET", "applications", () => "environments" + pageQuery(page), "EnvironmentPage", context);
  }
  getApplication(applicationId: string, context?: ClientCallContext): Promise<ManagementCallResult<Application>> {
    return this.call("GET", "application", () => "applications/" + uuid(applicationId), "Application", context);
  }
  listInstances(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<Instance>>> {
    return this.call("GET", "instances", () => "applications/" + uuid(applicationId) + "/instances" + pageQuery(page), "InstancePage", context);
  }
  listInventory(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<InventoryItem>>> {
    return this.call("GET", "inventory", () => "applications/" + uuid(applicationId) + "/inventory" + pageQuery(page), "InventoryPage", context);
  }
  getCapabilities(applicationId: string, context?: ClientCallContext): Promise<ManagementCallResult<Capabilities>> {
    return this.call("GET", "capabilities", () => "applications/" + uuid(applicationId) + "/capabilities", "Capabilities", context);
  }
  listServiceFeatures(page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<ServiceFeaturePage>> {
    return this.call("GET", "service-features", () => "service-features" + apiDiscoveryPageQuery(page), "ServiceFeaturePage", context);
  }
  listApplicationApiCapabilities(applicationId: string, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<ApplicationApiCapabilityPage>> {
    return this.call("GET", "api-capabilities", () => "applications/" + uuid(applicationId) + "/api-capabilities" + apiDiscoveryPageQuery(page),
      "ApplicationApiCapabilityPage", context, undefined, undefined, undefined, undefined, undefined, applicationId);
  }
  replaceMetadata(applicationId: string, operationKey: string, ifMatch: string, command: MetadataCommand,
    context?: ClientCallContext): Promise<ManagementCallResult<MetadataResult>> {
    return this.call("PUT", "metadata-replace", () => {
      if (!isStrongETag(ifMatch)) { throw new TypeError("Supply one strong metadata ETag returned for this application."); }
      key(operationKey);
      return "applications/" + uuid(applicationId) + "/metadata";
    }, "MetadataResult", context, "MetadataCommand", command, { "Idempotency-Key": operationKey, "If-Match": ifMatch }, operationKey);
  }
  getMetadataOperation(applicationId: string, operationKey: string, context?: ClientCallContext): Promise<ManagementCallResult<MetadataResult>> {
    return this.call("GET", "metadata-result", () => "applications/" + uuid(applicationId) + "/metadata-operations/" + key(operationKey),
      "MetadataResult", context, undefined, undefined, undefined, operationKey);
  }

  private async call<T>(method: "GET" | "PUT" | "DELETE", operation: string, path: () => string, responseSchema: ManagementSchema,
    context?: ClientCallContext, requestSchema?: ManagementSchema, requestBody?: unknown,
    commandHeaders?: Readonly<Record<string, string>>, operationKey?: string,
    control?: { readonly registrationId: string; readonly attemptId: string }, applicationId?: string): Promise<ManagementCallResult<T>> {
    const result = await executeRequest<T, Problem>({
      operation,
      transport: this.transport,
      ...(context === undefined ? {} : { context }),
      ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }),
      ...(operationKey === undefined ? {} : { operationKey }),
      timeoutMs: this.timeoutMs,
      readRetries: this.readRetries,
      readRetriesAllowed: operation !== "control",
      prepare: () => {
        const suffix = path();
        const url = this.baseUrl + "management/v1/" + suffix;
        const body = requestSchema === undefined ? undefined : encodeManagement(requestSchema, requestBody, "request", this.wireLimits);
        return {
          url, method,
          headers: Object.freeze({ Accept: "application/json, application/problem+json", ...(body === undefined ? {} : { "Content-Type": "application/json" }),
            ...(control === undefined ? {} : { "Cache-Control": "no-store" }), ...commandHeaders }),
          ...(body === undefined ? {} : { body }),
          maximumResponseBytes: bodyLimit(responseSchema, "response", this.wireLimits),
          maximumErrorBytes: 65536
        };
      },
      decode: (response, bytes) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            const value = decodeManagement<T>(responseSchema, bytes, "response", this.wireLimits);
            if (control !== undefined && ((value as ControlConfirmation).registrationId !== control.registrationId
              || (value as ControlConfirmation).attemptId !== control.attemptId)) {
              throw new ClientProtocolFailure("response.control_echo_mismatch");
            }
            if ((responseSchema === "ServiceFeaturePage" || responseSchema === "ApplicationApiCapabilityPage")
              && ((value as ServiceFeaturePage).profile !== "complete-v1"
                || responseSchema === "ApplicationApiCapabilityPage" && (value as ApplicationApiCapabilityPage).applicationId !== applicationId)) {
              throw new ClientProtocolFailure("response.api_discovery_echo_mismatch");
            }
            if (responseSchema === "MetadataResult" && operationKey !== undefined && (value as MetadataResult).operation.key !== operationKey) {
              throw new ClientProtocolFailure("response.operation_key_mismatch");
            }
            return { value };
          }
          if (contentType !== "application/problem+json") {
            // Native TLS/authentication rejection can supply no problem body.
            if (bytes.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const problem = decodeManagement<Problem>("Problem", bytes, "response", this.wireLimits);
          if (problem.status !== response.status) { throw new ClientProtocolFailure("response.problem_status_mismatch"); }
          if (operationKey !== undefined && problem.operation !== undefined && problem.operation.key !== operationKey) {
            throw new ClientProtocolFailure("response.operation_key_mismatch");
          }
          return { problem };
        } catch (error) {
          if (error instanceof ManagementWireError) { throw new ClientProtocolFailure(error.code); }
          throw error;
        }
      }
    });
    if (operation === "metadata-replace" && (result.problem?.result?.originalCompletion === "unknown"
      || result.status !== undefined && result.status >= 500 && result.problem?.result === undefined)) {
      return Object.freeze({ ...result, mutationUncertain: true });
    }
    return Object.freeze(result);
  }
}

export function captureBaseUrl(value: string): string {
  if (typeof value !== "string" || value.length === 0 || value.length > 4096 || value.trim() !== value
    || /[\p{Cc}\\?#]/u.test(value)) {
    throw new TypeError("Configure an absolute HTTPS endpoint without URI credentials, query, fragment or controls.");
  }
  const authority = value.indexOf("://");
  if (authority < 0) { throw new TypeError("Configure an absolute HTTPS endpoint."); }
  const start = value.indexOf("/", authority + 3);
  const rawAuthority = value.slice(authority + 3, start < 0 ? undefined : start);
  if (rawAuthority.length === 0 || rawAuthority.includes("@")) { throw new TypeError("Supply an HTTPS authority without URI credentials."); }
  if (start >= 0) {
    for (const segment of value.slice(start).split("/")) {
      let decoded: string;
      try { decoded = decodeURIComponent(segment); } catch { throw new TypeError("The base path contains an invalid escape."); }
      if (decoded === "." || decoded === ".." || /[\/\\\p{Cc}]/u.test(decoded)) {
        throw new TypeError("The management base path contains an ambiguous segment.");
      }
    }
  }
  const url = new URL(value);
  if (url.protocol !== "https:" || url.username !== "" || url.password !== "" || url.search !== "" || url.hash !== "") {
    throw new TypeError("Configure an absolute HTTPS endpoint without credentials, query or fragment.");
  }
  return url.href.endsWith("/") ? url.href : url.href + "/";
}

function uuid(value: string): string { if (!isUuid(value)) { throw new TypeError("Supply a canonical nonempty lowercase UUID D identifier."); } return value; }
function key(value: string): string { if (!isOperationKey(value)) { throw new TypeError("Supply the original bounded unreserved operation key excluding dot segments."); } return encodeURIComponent(value); }
function pageQuery(page: PageRequest = {}): string {
  const size = page.pageSize ?? 100;
  if (!Number.isSafeInteger(size) || size < 1 || size > 500 || page.cursor !== undefined && !isCursor(page.cursor)) {
    throw new TypeError("Supply a finite page size and an opaque same-endpoint cursor.");
  }
  const query = new URLSearchParams({ pageSize: String(size) });
  if (page.cursor !== undefined) { query.set("cursor", page.cursor); }
  return "?" + query.toString();
}

function apiDiscoveryPageQuery(page: PageRequest = {}): string {
  const size = page.pageSize ?? 32;
  if (!Number.isSafeInteger(size) || size < 1 || size > 64 || page.cursor !== undefined && !isCursor(page.cursor)) {
    throw new TypeError("Supply a complete-discovery page size from 1 through 64 and its opaque same-endpoint cursor.");
  }
  const query = new URLSearchParams({ pageSize: String(size) });
  if (page.cursor !== undefined) { query.set("cursor", page.cursor); }
  return "?" + query.toString();
}
