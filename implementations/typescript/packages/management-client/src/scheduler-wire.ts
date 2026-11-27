import { freezeWire, ManagementWireError, parseWireJson, stringifyWire, validUnicode } from "./wire-json.js";
import { validateScheduler } from "./scheduler-wire-validation.js";

/** A fixed portable DTO inventory, not a runtime schema interpreter. */
export type SchedulerSchema = "MessageContract" | "CommandMessageContract" | "MessageOperationReference" | "CommandMessageOperationReference"
  | "OperationReference" | "TransactionFacts" | "OperationRejection" | "WorkerBuildReference" | "CommandWorkerBuildReference"
  | "JobExecutionSupport" | "CommandJobExecutionSupport" | "CapabilityKey" | "CommandCapabilityKey" | "ConfigurationMember"
  | "CommandConfigurationMember" | "ConfigurationShape" | "CommandConfigurationShape" | "ConfigurationContractReference"
  | "CommandConfigurationContractReference" | "FormFieldHint" | "CommandFormFieldHint" | "FormGroup" | "CommandFormGroup"
  | "NamedLookupReference" | "CommandNamedLookupReference" | "FormDescription" | "CommandFormDescription"
  | "CapabilityDefinition" | "CommandCapabilityDefinition" | "CapabilityParticipation" | "CapabilityDefinitionPage" | "CapabilityParticipationPage"
  | "SchedulerTriggerReference" | "CommandSchedulerTriggerReference" | "SchedulerSettingsSelection" | "CommandSchedulerSettingsSelection"
  | "SchedulerInputMapping" | "CommandSchedulerInputMapping" | "SchedulerResourceMapping" | "CommandSchedulerResourceMapping"
  | "SchedulerRetryPolicy" | "CommandSchedulerRetryPolicy" | "SchedulerCalendar" | "CommandSchedulerCalendar"
  | "SchedulerTriggerDraft" | "CommandSchedulerTriggerDraft" | "SchedulerTaskDraft" | "CommandSchedulerTaskDraft"
  | "SchedulerProgress" | "CommandSchedulerProgress" | "SchedulerCapturedSettings" | "SchedulerExecutionIdentity" | "SchedulerActor"
  | "SchedulerTrigger" | "SchedulerTask" | "SchedulerTriggerCommand" | "SchedulerTaskCommand" | "SchedulerStateCommand"
  | "SchedulerTriggerStateCommand" | "SchedulerTaskStateCommand" | "SchedulerRunCancellationCommand" | "SchedulerTaskPreviewCommand"
  | "SchedulerCalendarPreviewCommand" | "SchedulerValidationIssue" | "SchedulerAffectedTask" | "SchedulerTaskPreview"
  | "SchedulerEventCommand" | "SchedulerEventReceipt" | "SchedulerManualRunCommand" | "SchedulerRunCapsule" | "SchedulerRun"
  | "SchedulerAttempt" | "SchedulerAttemptReport" | "SchedulerAttemptCommand" | "SchedulerAttemptAdmission" | "SchedulerRunDetail"
  | "SchedulerEvent" | "SchedulerHistoryQuery" | "SchedulerTriggerPage" | "SchedulerTaskPage" | "SchedulerRunPage" | "SchedulerAttemptPage"
  | "SchedulerDecision" | "SchedulerOperationResult" | "SchedulerProblem" | "SchedulerExecutionSettings" | "CommandSchedulerExecutionSettings"
  | "CapabilityQuery" | "AdministrativeAction" | "ConfigurationActions" | "ConfigurationCohortChoice" | "ConfigurationCohortChoicePage";

export interface SchedulerWireLimits {
  readonly maximumBodyBytes: number; readonly maximumJsonDepth: number; readonly maximumPayloadBytes: number;
  readonly maximumCapsuleBytes: number; readonly maximumDefinitionBytes: number; readonly maximumSummaryBytes: number;
  readonly maximumPageSize: number; readonly maximumMappings: number; readonly maximumFanout: number; readonly maximumAffectedTasks: number;
}
export const defaultSchedulerWireLimits: SchedulerWireLimits = Object.freeze({
  maximumBodyBytes: 1048576, maximumJsonDepth: 16, maximumPayloadBytes: 65536, maximumCapsuleBytes: 262144,
  maximumDefinitionBytes: 65536, maximumSummaryBytes: 16384, maximumPageSize: 500, maximumMappings: 64,
  maximumFanout: 64, maximumAffectedTasks: 4096
});
export function captureSchedulerWireLimits(input: Partial<SchedulerWireLimits> = {}): SchedulerWireLimits {
  const captured = { ...defaultSchedulerWireLimits, ...input };
  for (const key of Object.keys(defaultSchedulerWireLimits) as (keyof SchedulerWireLimits)[]) {
    if (!Number.isSafeInteger(captured[key]) || captured[key] < 1 || captured[key] > defaultSchedulerWireLimits[key]) {
      throw new RangeError("Select positive scheduler limits within the portable ceilings.");
    }
  }
  return Object.freeze(captured);
}
export class SchedulerWireError extends Error {
  constructor(readonly code = "scheduler.wire.invalid") {
    super("The value does not conform to the bounded scheduler wire contract."); this.name = "SchedulerWireError";
  }
}
export interface SchedulerIntegrityCheck { readonly payload: string; readonly sha256: string; }
const encoder = new TextEncoder();

/** Native digests are awaited to settlement even when cancellation arrives during hashing. */
export async function decodeScheduler<T>(schema: SchedulerSchema, input: Uint8Array | string, direction: "request" | "response",
  inputLimits: Partial<SchedulerWireLimits> = {}, signal?: AbortSignal): Promise<T> {
  if (direction !== "request" && direction !== "response") { throw new TypeError("Select the request or response wire direction."); }
  const limits = captureSchedulerWireLimits(inputLimits);
  signal?.throwIfAborted();
  let text: string;
  if (typeof input === "string") {
    if (!validUnicode(input)) { throw new SchedulerWireError(); }
    if (input.length > limits.maximumBodyBytes || encoder.encode(input).length > limits.maximumBodyBytes) {
      throw new SchedulerWireError("scheduler.wire.tooLarge");
    }
    text = input;
  } else {
    if (input.length > limits.maximumBodyBytes) { throw new SchedulerWireError("scheduler.wire.tooLarge"); }
    try { text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(input); }
    catch { throw new SchedulerWireError(); }
  }
  try {
    const captures = new Set<string>();
    if (["CapabilityDefinition", "CommandCapabilityDefinition", "SchedulerRunCapsule"].includes(schema)) { captures.add(""); }
    if (schema === "SchedulerRunDetail") { captures.add("/capsule"); }
    if (schema === "CapabilityDefinitionPage") { for (let index = 0; index < limits.maximumPageSize; index++) { captures.add("/items/" + index); } }
    const parsed = parseWireJson(text, limits.maximumJsonDepth, captures);
    const checks: SchedulerIntegrityCheck[] = [];
    validateScheduler(schema, parsed.value, direction === "request", limits, checks, parsed.nonIntegerNumbers, parsed.rawValues);
    for (const check of checks) {
      signal?.throwIfAborted();
      const digest = await globalThis.crypto.subtle.digest("SHA-256", encoder.encode(check.payload) as Uint8Array<ArrayBuffer>);
      signal?.throwIfAborted();
      if ([...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("") !== check.sha256) {
        throw new SchedulerWireError("scheduler.integrity");
      }
    }
    signal?.throwIfAborted();
    return freezeWire(parsed.value as T);
  } catch (error) { throw translate(error); }
}

export async function encodeScheduler(schema: SchedulerSchema, value: unknown, direction: "request" | "response",
  inputLimits: Partial<SchedulerWireLimits> = {}, signal?: AbortSignal): Promise<Uint8Array> {
  const limits = captureSchedulerWireLimits(inputLimits);
  let text: string;
  try { text = stringifyWire(value, limits.maximumJsonDepth, limits.maximumBodyBytes); }
  catch (error) { throw translate(error); }
  const bytes = encoder.encode(text);
  await decodeScheduler(schema, bytes, direction, limits, signal);
  return bytes;
}
function translate(error: unknown): unknown {
  return error instanceof ManagementWireError
    ? new SchedulerWireError(error.code === "wire.too_large" ? "scheduler.wire.tooLarge" : "scheduler.wire.invalid") : error;
}
