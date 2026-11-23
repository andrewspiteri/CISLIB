import { attachOpaqueObjects, freezeWire, ManagementWireError, parseWireJson, stringifyWire, validUnicode } from "./wire-json.js";
import { captureWireLimits } from "./wire-limits.js";
import type { ManagementWireLimits } from "./wire-limits.js";
import { validateManagement } from "./wire-validation.js";
import type { ManagementSchema } from "./wire-validation.js";

export function decodeManagement<T>(schema: ManagementSchema, input: Uint8Array | string,
  direction: "request" | "response", inputLimits: Partial<ManagementWireLimits> = {}): T {
  const limits = captureWireLimits(inputLimits);
  if (direction !== "request" && direction !== "response") { throw new TypeError("Select the request or response wire direction."); }
  const maximum = bodyLimit(schema, direction, limits);
  let text: string;
  if (typeof input === "string") {
    if (input.length > maximum) { throw new ManagementWireError("wire.too_large"); }
    if (!validUnicode(input)) { throw new ManagementWireError("wire.invalid"); }
    if (new TextEncoder().encode(input).length > maximum) { throw new ManagementWireError("wire.too_large"); }
    text = input;
  } else {
    if (input.length > maximum) { throw new ManagementWireError("wire.too_large"); }
    try { text = new TextDecoder("utf-8", { fatal: true }).decode(input); } catch { throw new ManagementWireError(); }
  }
  const parsed = parseWireJson(text, limits.maximumJsonDepth);
  validateIntegerTokens(schema, parsed.nonIntegerNumbers);
  validateManagement(schema, parsed.value, direction === "request", limits);
  attachOpaqueObjects(parsed.value, "", parsed.objects);
  return freezeWire(parsed.value as T);
}

function validateIntegerTokens(schema: ManagementSchema, tokens: ReadonlySet<string>): void {
  const paths: Partial<Record<ManagementSchema, RegExp>> = {
    RegistrationRequest: /^\/protocolMajor$/u, MetadataCommand: /^\/commandVersion$/u,
    ServiceInfo: /^\/(?:protocolMajors\/[0-9]+|features\/[0-9]+\/version|maximumInventoryRequestBytes)$/u,
    ControlConfirmation: /^\/validForMs$/u,
    Capabilities: /^\/implementedApis\/[0-9]+\/version$/u,
    Instance: /^\/protocolMajor$/u, InstancePage: /^\/items\/[0-9]+\/protocolMajor$/u,
    MetadataResult: /^\/result\/commandVersion$/u,
    Problem: /^(?:\/status|\/result\/result\/commandVersion)$/u
  };
  const selected = paths[schema];
  if (selected !== undefined && [...tokens].some(path => selected.test(path))) { throw new ManagementWireError(); }
}

export function encodeManagement(schema: ManagementSchema, value: unknown, direction: "request" | "response",
  inputLimits: Partial<ManagementWireLimits> = {}): Uint8Array {
  const limits = captureWireLimits(inputLimits);
  validateManagement(schema, value, direction === "request", limits);
  const text = stringifyWire(value, limits.maximumJsonDepth, bodyLimit(schema, direction, limits));
  const bytes = new TextEncoder().encode(text);
  // One detached encoded body is reused for the entire call. This pass also checks
  // lexical Unicode/duplicates inside any explicitly supplied opaque fragment.
  decodeManagement(schema, bytes, direction, limits);
  return bytes;
}

export function bodyLimit(schema: ManagementSchema, direction: "request" | "response", limits: ManagementWireLimits): number {
  const inventory = schema === "InventorySnapshot" || schema === "InventoryPage" || schema === "InventoryItem";
  return direction === "request" ? inventory ? limits.maximumInventoryRequestBytes : limits.maximumRegularRequestBytes
    : inventory ? limits.maximumInventoryResponseBytes : limits.maximumRegularResponseBytes;
}

export { ManagementWireError, OpaqueJsonObject } from "./wire-json.js";
export { captureWireLimits, defaultWireLimits } from "./wire-limits.js";
export type { ManagementWireLimits } from "./wire-limits.js";
export type { ManagementSchema } from "./wire-validation.js";
export { isUuid, isOperationKey, isStrongETag, isOrderedInteger, isTimestamp, isScalarText, isUtf8Text, isCursor } from "./wire-validation.js";
