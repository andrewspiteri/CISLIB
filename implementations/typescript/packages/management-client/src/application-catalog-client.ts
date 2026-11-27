import { ClientProtocolFailure, createFetchTransport, executeRequest, validateReadRetries, validateTimeout } from "@cislib/client-core";
import type { ClientCallContext, ClientTransport, CredentialProvider, RequestObservation } from "@cislib/client-core";
import { captureBaseUrl } from "./client.js";
import type { ManagementCallResult, Page, PageRequest, Problem } from "./models.js";
import type { CapabilityDefinition, CapabilityKey, CapabilityParticipation, CapabilityQuery } from "./scheduler-models.js";
import type { ManagementSchedulerClientOptions } from "./scheduler-client.js";
import { captureSchedulerWireLimits, decodeScheduler, encodeScheduler, SchedulerWireError } from "./scheduler-wire.js";
import type { SchedulerSchema, SchedulerWireLimits } from "./scheduler-wire.js";
import { decodeManagement, isCursor, isUuid, ManagementWireError } from "./wire.js";

export type ManagementApplicationCatalogClientOptions = ManagementSchedulerClientOptions;
/** Trusted logical definitions and independent instance observations; HTTP API capabilities remain separate. */
export class ManagementApplicationCatalogClient {
  readonly baseUrl: string;
  readonly wireLimits: SchedulerWireLimits;
  private readonly transport: ClientTransport;
  private readonly credentials: CredentialProvider | undefined;
  private readonly timeoutMs: number;
  private readonly readRetries: number;
  private readonly observe: ((event: RequestObservation) => void) | undefined;

  constructor(options: ManagementApplicationCatalogClientOptions) {
    this.baseUrl = captureBaseUrl(options.baseUrl);
    this.wireLimits = captureSchedulerWireLimits(options.wireLimits);
    this.transport = options.transport ?? createFetchTransport();
    this.credentials = options.credentials;
    this.timeoutMs = validateTimeout(options.timeoutMs ?? 30000);
    this.readRetries = validateReadRetries(options.readRetries ?? 0);
    this.observe = options.observe;
  }
  getCapabilityDefinitions(applicationId: string, query?: CapabilityQuery, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<CapabilityDefinition>>> {
    return this.call(async signal => prefix(applicationId) + "/capability-definitions" + await this.query(query, page, signal),
      "CapabilityDefinitionPage", context);
  }
  getCapabilityDefinition(applicationId: string, key: CapabilityKey, context?: ClientCallContext): Promise<ManagementCallResult<CapabilityDefinition>> {
    let captured: CapabilityKey;
    return this.call(async signal => {
      const bytes = await encodeScheduler("CommandCapabilityKey", key, "request", this.wireLimits, signal);
      captured = JSON.parse(new TextDecoder().decode(bytes)) as CapabilityKey;
      return prefix(applicationId) + "/capability-definition?" + new URLSearchParams(captured as unknown as Record<string, string>).toString();
    }, "CapabilityDefinition", context, value => {
      const actual = (value as CapabilityDefinition).key;
      echo(actual.ownerApplication === captured.ownerApplication && actual.kind === captured.kind && actual.executionScope === captured.executionScope
        && actual.definitionId === captured.definitionId && actual.definitionVersion === captured.definitionVersion);
    });
  }
  getCapabilityParticipation(applicationId: string, query?: CapabilityQuery, page?: PageRequest, context?: ClientCallContext): Promise<ManagementCallResult<Page<CapabilityParticipation>>> {
    return this.call(async signal => prefix(applicationId) + "/capability-participation" + await this.query(query, page, signal),
      "CapabilityParticipationPage", context);
  }

  private async query(query: CapabilityQuery | undefined, page: PageRequest | undefined, signal: AbortSignal): Promise<string> {
    const fields = pageParameters(page);
    const bytes = await encodeScheduler("CapabilityQuery", query ?? {}, "request", this.wireLimits, signal);
    const captured = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, string>;
    for (const [name, value] of Object.entries(captured)) { fields.set(name, value); }
    return "?" + fields.toString();
  }
  private async call<T>(path: (signal: AbortSignal) => Promise<string>, schema: SchedulerSchema, context?: ClientCallContext,
    check?: (value: unknown) => void): Promise<ManagementCallResult<T>> {
    const result = await executeRequest<T, Problem>({
      operation: "catalog", transport: this.transport, timeoutMs: this.timeoutMs, readRetries: this.readRetries,
      ...(context === undefined ? {} : { context }), ...(this.credentials === undefined ? {} : { credentials: this.credentials }),
      ...(this.observe === undefined ? {} : { observe: this.observe }),
      prepare: async signal => ({ url: this.baseUrl + "management/v1/" + await path(signal), method: "GET",
        headers: Object.freeze({ Accept: "application/json, application/problem+json" }),
        maximumResponseBytes: this.wireLimits.maximumBodyBytes, maximumErrorBytes: 65536 }),
      decode: async (response, bytes, signal) => {
        const contentType = response.headers["content-type"]?.split(";", 1)[0]?.trim().toLowerCase();
        try {
          if (response.status >= 200 && response.status < 300) {
            if (contentType !== "application/json") { throw new ClientProtocolFailure("response.content_type"); }
            const value = await decodeScheduler<T>(schema, bytes, "response", this.wireLimits, signal);
            check?.(value); return { value };
          }
          if (contentType !== "application/problem+json") {
            if (bytes.length === 0) { return {}; }
            throw new ClientProtocolFailure("response.problem_content_type");
          }
          const problem = decodeManagement<Problem>("Problem", bytes, "response");
          if (problem.status !== response.status) { throw new ClientProtocolFailure("response.problem_status_mismatch"); }
          return { problem };
        } catch (error) {
          if (error instanceof SchedulerWireError || error instanceof ManagementWireError) {
            signal.throwIfAborted(); throw new ClientProtocolFailure(error.code);
          }
          throw error;
        }
      }
    });
    return Object.freeze(result);
  }
}
export type ManagementApplicationCatalogClientPort = Pick<ManagementApplicationCatalogClient,
  "getCapabilityDefinitions" | "getCapabilityDefinition" | "getCapabilityParticipation">;

function prefix(applicationId: string): string {
  if (!isUuid(applicationId)) { throw new TypeError("Supply a canonical nonempty lowercase application UUID."); }
  return "applications/" + applicationId;
}
function pageParameters(page: PageRequest = {}): URLSearchParams {
  const size = page.pageSize ?? 100;
  if (!Number.isSafeInteger(size) || size < 1 || size > 500 || page.cursor !== undefined && !isCursor(page.cursor)) {
    throw new TypeError("Supply a finite page size and opaque same-endpoint cursor.");
  }
  const fields = new URLSearchParams({ pageSize: String(size) });
  if (page.cursor !== undefined) { fields.set("cursor", page.cursor); }
  return fields;
}
function echo(valid: boolean): void { if (!valid) { throw new ClientProtocolFailure("response.catalog_echo_mismatch"); } }
