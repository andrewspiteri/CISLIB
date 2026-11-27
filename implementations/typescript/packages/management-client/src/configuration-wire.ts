import { freezeWire, ManagementWireError, parseWireJson, stringifyWire, validUnicode } from "./wire-json.js";
import type { ConfigurationDesiredContext, ConfigurationSnapshot, ConfigurationSnapshotRead, ConfigurationSnapshotTarget } from "./configuration-models.js";
import { validateConfiguration, validateDesired, validateSnapshotContent, validateTarget, sameConfigurationTarget } from "./configuration-wire-validation.js";

export type ConfigurationSchema = "ConfigurationDefinition" | "ConfigurationDefinitionPage" | "ConfigurationEditorContext"
  | "ConfigurationCreationContext" | "ConfigurationCreationCommand" | "ConfigurationSummary" | "ConfigurationSummaryPage"
  | "ConfigurationCandidatePreviewCommand" | "ConfigurationCandidatePreview" | "ConfigurationCatalog"
  | "ConfigurationCatalogCommand" | "ConfigurationSource" | "ConfigurationPublicationMetadata" | "ConfigurationPublicationPage"
  | "ConfigurationPublication" | "ConfigurationPublicationCommand" | "ConfigurationRelease" | "ConfigurationReleasePage" | "ConfigurationReleaseCommand"
  | "ConfigurationAssignmentMap" | "ConfigurationAssignmentCommand" | "ConfigurationSnapshot" | "ConfigurationValidationCommand"
  | "ConfigurationValidationResult" | "ConfigurationPreviewCommand" | "ConfigurationPreview" | "ConfigurationStatusReport"
  | "ConfigurationAdoptionPage" | "ConfigurationDecision" | "ConfigurationOperationResult" | "ConfigurationProblem";
export interface ConfigurationWireLimits {
  readonly maximumBodyBytes: number; readonly maximumSourceBytes: number; readonly maximumJsonDepth: number;
  readonly maximumDefinitions: number; readonly maximumOverrides: number;
}
export const defaultConfigurationWireLimits: ConfigurationWireLimits = Object.freeze({
  maximumBodyBytes: 1048576, maximumSourceBytes: 262144, maximumJsonDepth: 16, maximumDefinitions: 256, maximumOverrides: 16
});
export function captureConfigurationWireLimits(input: Partial<ConfigurationWireLimits> = {}): ConfigurationWireLimits {
  const captured = { ...defaultConfigurationWireLimits, ...input };
  for (const key of Object.keys(defaultConfigurationWireLimits) as (keyof ConfigurationWireLimits)[]) {
    if (!Number.isSafeInteger(captured[key]) || captured[key] < 1 || captured[key] > defaultConfigurationWireLimits[key]) {
      throw new RangeError("Select positive configuration limits within the portable ceilings.");
    }
  }
  return Object.freeze(captured);
}
export class ConfigurationWireError extends Error {
  constructor(readonly code = "configuration.wire.invalid") {
    super("The value does not conform to the bounded configuration wire contract.");
    this.name = "ConfigurationWireError";
  }
}
export interface ConfigurationIntegrityCheck { readonly payload: string; readonly sha256: string; }
const encoder = new TextEncoder();

/** The native digest is joined even when an operation is canceled; no content work survives return. */
export async function computeConfigurationPayloadSha256(payload: string, inputLimits: Partial<ConfigurationWireLimits> = {},
  signal?: AbortSignal): Promise<string> {
  const limits = captureConfigurationWireLimits(inputLimits);
  const bytes = configurationSourceBytes(payload, limits);
  signal?.throwIfAborted();
  const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes as Uint8Array<ArrayBuffer>);
  signal?.throwIfAborted();
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}

export async function decodeConfiguration<T>(schema: ConfigurationSchema, input: Uint8Array | string,
  direction: "request" | "response", inputLimits: Partial<ConfigurationWireLimits> = {}, signal?: AbortSignal): Promise<T> {
  if (direction !== "request" && direction !== "response") { throw new TypeError("Select the request or response wire direction."); }
  const limits = captureConfigurationWireLimits(inputLimits);
  const parsed = parseConfiguration(input, limits);
  validateIntegerTokens(schema, parsed.nonIntegerNumbers);
  const checks: ConfigurationIntegrityCheck[] = [];
  validateConfiguration(schema, parsed.value, direction === "request", limits, checks);
  await integrity(checks, limits, signal);
  signal?.throwIfAborted();
  return freezeWire(parsed.value as T);
}

export async function encodeConfiguration(schema: ConfigurationSchema, value: unknown, direction: "request" | "response",
  inputLimits: Partial<ConfigurationWireLimits> = {}, signal?: AbortSignal): Promise<Uint8Array> {
  const limits = captureConfigurationWireLimits(inputLimits);
  let text: string;
  try { text = stringifyWire(value, limits.maximumJsonDepth, limits.maximumBodyBytes); }
  catch (error) { throw translate(error); }
  const bytes = encoder.encode(text);
  await decodeConfiguration(schema, bytes, direction, limits, signal);
  return bytes;
}

/** Target/envelope validity is established independently before content can identify a rejected desired release. */
export async function decodeConfigurationSnapshot(input: Uint8Array | string, expected: ConfigurationSnapshotTarget,
  inputLimits: Partial<ConfigurationWireLimits> = {}, signal?: AbortSignal): Promise<ConfigurationSnapshotRead> {
  validateTarget(expected);
  const limits = captureConfigurationWireLimits(inputLimits);
  let desired: ConfigurationDesiredContext | undefined;
  try {
    const parsed = parseConfiguration(input, limits);
    const outer = validateDesired(parsed.value);
    if (!sameConfigurationTarget(outer.desired, expected)) { throw new ConfigurationWireError("configuration.target.mismatch"); }
    desired = freezeWire(outer.desired);
    const checks: ConfigurationIntegrityCheck[] = [];
    validateSnapshotContent(parsed.value, limits, checks);
    await integrity(checks, limits, signal);
    signal?.throwIfAborted();
    return Object.freeze({ snapshot: freezeWire(parsed.value as ConfigurationSnapshot), desired });
  } catch (error) {
    if (!(error instanceof ConfigurationWireError)) { throw error; }
    signal?.throwIfAborted();
    return Object.freeze({ ...(desired === undefined ? {} : { desired }), failureCode: error.code });
  }
}

export function configurationSourceBytes(payload: unknown, limits: ConfigurationWireLimits): Uint8Array {
  if (typeof payload !== "string") {
    throw new ConfigurationWireError("configuration.content.invalid");
  }
  if (payload.length > limits.maximumSourceBytes) { throw new ConfigurationWireError("configuration.content.tooLarge"); }
  if (!validUnicode(payload)) { throw new ConfigurationWireError("configuration.content.invalid"); }
  const bytes = encoder.encode(payload);
  if (bytes.length > limits.maximumSourceBytes) { throw new ConfigurationWireError("configuration.content.tooLarge"); }
  try {
    const parsed = parseWireJson(payload, limits.maximumJsonDepth).value;
    if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) { throw new ConfigurationWireError("configuration.content.invalid"); }
  } catch (error) {
    if (error instanceof ManagementWireError) { throw new ConfigurationWireError("configuration.content.invalid"); }
    throw error;
  }
  return bytes;
}

async function integrity(checks: readonly ConfigurationIntegrityCheck[], limits: ConfigurationWireLimits, signal?: AbortSignal): Promise<void> {
  for (const check of checks) {
    const digest = await computeConfigurationPayloadSha256(check.payload, limits, signal);
    signal?.throwIfAborted();
    if (digest !== check.sha256) { throw new ConfigurationWireError("configuration.integrity"); }
  }
}

function parseConfiguration(input: Uint8Array | string, limits: ConfigurationWireLimits): ReturnType<typeof parseWireJson> {
  let text: string;
  if (typeof input === "string") {
    if (input.length > limits.maximumBodyBytes) { throw new ConfigurationWireError("configuration.wire.tooLarge"); }
    if (!validUnicode(input)) { throw new ConfigurationWireError(); }
    if (encoder.encode(input).length > limits.maximumBodyBytes) {
      throw new ConfigurationWireError("configuration.wire.tooLarge");
    }
    text = input;
  } else {
    if (input.length > limits.maximumBodyBytes) { throw new ConfigurationWireError("configuration.wire.tooLarge"); }
    try { text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(input); }
    catch { throw new ConfigurationWireError(); }
  }
  try { return parseWireJson(text, limits.maximumJsonDepth); }
  catch (error) { throw translate(error); }
}

function translate(error: unknown): unknown {
  return error instanceof ManagementWireError
    ? new ConfigurationWireError(error.code === "wire.too_large" ? "configuration.wire.tooLarge" : "configuration.wire.invalid") : error;
}

function validateIntegerTokens(schema: ConfigurationSchema, tokens: ReadonlySet<string>): void {
  let selected: RegExp | undefined;
  switch (schema) {
    case "ConfigurationCatalog": selected = /^\/(?:definitionCount|sourceCount|bindingCount|quotaBytes|usedBytes)$/u; break;
    case "ConfigurationCatalogCommand": case "ConfigurationPublicationCommand": case "ConfigurationReleaseCommand":
    case "ConfigurationAssignmentCommand": case "ConfigurationValidationCommand": selected = /^\/commandVersion$/u; break;
    case "ConfigurationCandidatePreviewCommand": case "ConfigurationCreationCommand": selected = /^\/commandVersion$/u; break;
    case "ConfigurationPreviewCommand": selected = /^\/(?:commandVersion|assignment\/commandVersion)$/u; break;
    case "ConfigurationPreview": selected = /^\/proposedAssignment\/commandVersion$/u; break;
    case "ConfigurationDecision": selected = /^\/commandVersion$/u; break;
    case "ConfigurationOperationResult": selected = /^\/result\/commandVersion$/u; break;
    case "ConfigurationProblem": selected = /^(?:\/status|\/result\/result\/commandVersion)$/u; break;
  }
  if (selected !== undefined && [...tokens].some(path => selected.test(path))) { throw new ConfigurationWireError(); }
}

export { sameConfigurationTarget } from "./configuration-wire-validation.js";
