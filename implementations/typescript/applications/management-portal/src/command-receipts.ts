import { OpaqueJsonObject, isOperationKey, isOrderedInteger, isScalarText, isUuid } from "@cislib/management-client";

export const commandReceiptLimits = Object.freeze({ maximumReceipts: 16, maximumStorageBytes: 32768, maximumAgeMs: 86400000 });
const storageKey = "cis.management.command-receipts.v1";
const maximumTime = 8640000000000000;

/** Opaque nonsecret associations supplied by the current authenticated portal owner. */
export interface CommandReceiptContext { readonly operatorId: string; readonly serviceAssociation: string; }
export type CommandReceiptIntent =
  | { readonly family: "management.metadata.replace"; readonly target: { readonly applicationId: string } }
  | { readonly family: "management.configuration.create"; readonly target: { readonly applicationId: string } }
  | { readonly family: "management.configuration.source.publish"; readonly target: { readonly sourceId: string } }
  | { readonly family: "management.configuration.release.create" | "management.configuration.assignment.replace";
      readonly target: { readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string } }
  | { readonly family: "management.scheduler.trigger.write"; readonly target: { readonly applicationId: string; readonly triggerId: string } }
  | { readonly family: "management.scheduler.task.write"; readonly target: { readonly applicationId: string; readonly taskId: string } }
  | { readonly family: "management.scheduler.run.write"; readonly target: { readonly applicationId: string; readonly runId: string } };
export type CommandReceipt = CommandReceiptIntent & CommandReceiptContext & { readonly key: string; readonly createdAtMs: number };
type StoredReceipt = CommandReceiptIntent & { readonly key: string; readonly createdAtMs: number };
interface Envelope extends CommandReceiptContext { readonly version: 1; readonly receipts: readonly StoredReceipt[]; }
export interface CommandReceiptStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}
export type CommandReceiptBlockReason = "context-unavailable" | "context-mismatch" | "storage-unavailable" | "storage-invalid"
  | "clock-untrusted" | "references-expired" | "capacity" | "key-unavailable" | "invalid-receipt" | "not-retained" | "busy";
export interface CommandReceiptBlocked { readonly kind: "blocked"; readonly reason: CommandReceiptBlockReason; }
export type CommandReceiptLoad = CommandReceiptBlocked | { readonly kind: "ready"; readonly receipts: readonly CommandReceipt[] };
export type CommandReceiptPrepared = CommandReceiptBlocked | { readonly kind: "prepared"; readonly receipt: CommandReceipt };
export type CommandReceiptForgotten = CommandReceiptBlocked | { readonly kind: "forgotten" };
export type CommandReceiptCleared = CommandReceiptBlocked | { readonly kind: "cleared" };
export type CommandReceiptRecovery<T> = CommandReceiptBlocked | { readonly kind: "settled"; readonly receipt: CommandReceipt; readonly value: T }
  | { readonly kind: "stale" | "canceled" };
export interface CommandReceiptStoreOptions {
  readonly context: CommandReceiptContext | undefined;
  /** Lazy access also catches browsers which deny access to the sessionStorage property itself. */
  readonly storage?: () => CommandReceiptStorage;
  readonly now?: () => number;
  readonly createKey?: () => string;
}
interface Snapshot {
  readonly kind: "ready";
  readonly generation: number;
  readonly context: CommandReceiptContext;
  readonly storage: CommandReceiptStorage;
  readonly receipts: readonly StoredReceipt[];
  readonly now: number;
}
interface Selection { readonly receipt: CommandReceipt; readonly controller: AbortController; readonly generation: number; }

/** One tab/document owner. It stores no command body, outcome, credential, draft or server-side ledger. */
export class CommandReceiptStore {
  private context: CommandReceiptContext | undefined;
  private generation = 0;
  private selection: Selection | undefined;
  private readonly storage: () => CommandReceiptStorage;
  private readonly now: () => number;
  private readonly createKey: () => string;

  constructor(options: CommandReceiptStoreOptions) {
    this.storage = options.storage ?? (() => window.sessionStorage);
    this.now = options.now ?? (() => Date.now());
    this.createKey = options.createKey ?? (() => globalThis.crypto.randomUUID());
    this.context = captureContext(options.context);
  }

  /** A changed/unknown operator or service association cancels selection without releasing its pending work. */
  setContext(context: CommandReceiptContext | undefined): void {
    const next = captureContext(context);
    if (sameContext(this.context, next)) { return; }
    this.context = next;
    this.generation++;
    this.selection?.controller.abort();
  }
  get recoveryBusy(): boolean { return this.selection !== undefined; }
  get selectedRecovery(): CommandReceipt | undefined {
    const now = this.now();
    return this.selection !== undefined && this.selection.generation === this.generation && isTime(now)
      && this.selection.receipt.createdAtMs <= now && now - this.selection.receipt.createdAtMs < commandReceiptLimits.maximumAgeMs
      ? this.selection.receipt : undefined;
  }

  load(): CommandReceiptLoad {
    const snapshot = this.read();
    return snapshot.kind === "blocked" ? snapshot : Object.freeze({ kind: "ready", receipts: publicReceipts(snapshot) });
  }

  /** Returns a usable original key only after exact bounded storage has been written and read back. */
  prepare(intent: CommandReceiptIntent): CommandReceiptPrepared {
    const captured = captureIntent(intent);
    if (captured === undefined) { return blocked("invalid-receipt"); }
    const snapshot = this.read();
    if (snapshot.kind === "blocked") { return snapshot; }
    if (snapshot.receipts.length >= commandReceiptLimits.maximumReceipts) { return blocked("capacity"); }
    let key: string;
    try { key = this.createKey(); } catch { return blocked("key-unavailable"); }
    if (!isUuid(key) || snapshot.receipts.some(row => row.key === key)) { return blocked("key-unavailable"); }
    const receipt: StoredReceipt = Object.freeze({ ...captured, key, createdAtMs: snapshot.now });
    const rows = Object.freeze([...snapshot.receipts, receipt]);
    if (!this.current(snapshot)) { return this.changedContext(); }
    const failure = persist(snapshot.storage, snapshot.context, rows);
    if (!this.current(snapshot)) { return this.changedContext(); }
    return failure ?? Object.freeze({ kind: "prepared", receipt: withContext(receipt, snapshot.context) });
  }

  /** Explicitly discards one exact local reference; this never removes backend duplicate protection. */
  forgetExact(receipt: CommandReceipt): CommandReceiptForgotten {
    const captured = capturePublicReceipt(receipt);
    if (captured === undefined) { return blocked("invalid-receipt"); }
    const snapshot = this.read();
    if (snapshot.kind === "blocked") { return snapshot; }
    if (!sameContext(snapshot.context, captured)) { return blocked("context-mismatch"); }
    if (this.selection !== undefined && sameReceipt(this.selection.receipt, captured)) { return blocked("busy"); }
    const index = snapshot.receipts.findIndex(row => sameStoredReceipt(row, captured));
    if (index < 0) { return blocked("not-retained"); }
    const rows = Object.freeze(snapshot.receipts.filter((_, position) => position !== index));
    const failure = persist(snapshot.storage, snapshot.context, rows);
    return !this.current(snapshot) ? this.changedContext() : failure ?? Object.freeze({ kind: "forgotten" });
  }

  /** An explicit local lifecycle action, never proof that any original operation failed or rolled back. */
  clearLocalReferences(): CommandReceiptCleared {
    this.generation++;
    this.selection?.controller.abort();
    try {
      const storage = this.storage();
      storage.removeItem(storageKey);
      return storage.getItem(storageKey) === null ? Object.freeze({ kind: "cleared" }) : blocked("storage-unavailable");
    } catch { return blocked("storage-unavailable"); }
  }

  /** A callback must return the complete typed original-result lookup including its owned SDK cleanup. */
  async recover<T>(receipt: CommandReceipt, lookup: (receipt: CommandReceipt, signal: AbortSignal) => Promise<T>): Promise<CommandReceiptRecovery<T>> {
    if (this.selection !== undefined) { return blocked("busy"); }
    const captured = capturePublicReceipt(receipt);
    if (captured === undefined || typeof lookup !== "function") { return blocked("invalid-receipt"); }
    const snapshot = this.read();
    if (snapshot.kind === "blocked") { return snapshot; }
    if (!sameContext(snapshot.context, captured)) { return blocked("context-mismatch"); }
    const retained = snapshot.receipts.find(row => sameStoredReceipt(row, captured));
    if (retained === undefined) { return blocked("not-retained"); }
    const selected: Selection = { receipt: withContext(retained, snapshot.context), controller: new AbortController(), generation: this.generation };
    this.selection = selected;
    try {
      const value = await lookup(selected.receipt, selected.controller.signal);
      if (selected.generation !== this.generation) { return Object.freeze({ kind: "stale" }); }
      if (selected.controller.signal.aborted) { return Object.freeze({ kind: "canceled" }); }
      const now = this.now();
      if (!isTime(now) || selected.receipt.createdAtMs > now) { return blocked("clock-untrusted"); }
      if (now - selected.receipt.createdAtMs >= commandReceiptLimits.maximumAgeMs) { return blocked("references-expired"); }
      return Object.freeze({ kind: "settled", receipt: selected.receipt, value });
    } finally {
      // Abort/timeout/context changes cannot clear this slot before the actual callback settles.
      if (this.selection === selected) { this.selection = undefined; }
    }
  }
  /** The owner also calls this when its volatile session marker changes under the same persistent association. */
  cancelRecovery(): void { this.selection?.controller.abort(); }

  private read(): Snapshot | CommandReceiptBlocked {
    const context = this.context;
    const generation = this.generation;
    if (context === undefined) { return blocked("context-unavailable"); }
    const now = this.now();
    if (!isTime(now)) { return blocked("clock-untrusted"); }
    let storage: CommandReceiptStorage, raw: string | null;
    try { storage = this.storage(); raw = storage.getItem(storageKey); }
    catch { return blocked("storage-unavailable"); }
    let receipts: readonly StoredReceipt[] = Object.freeze([]);
    if (raw !== null) {
      const envelope = parseEnvelope(raw);
      if (envelope === undefined) { return blocked("storage-invalid"); }
      if (!sameContext(context, envelope)) { return blocked("context-mismatch"); }
      if (envelope.receipts.some(row => row.createdAtMs > now)) { return blocked("clock-untrusted"); }
      receipts = Object.freeze(envelope.receipts.filter(row => now - row.createdAtMs < commandReceiptLimits.maximumAgeMs));
      if (receipts.length !== envelope.receipts.length) {
        if (generation !== this.generation || !sameContext(context, this.context)) { return this.changedContext(); }
        const failure = persist(storage, context, receipts);
        if (generation !== this.generation || !sameContext(context, this.context)) { return this.changedContext(); }
        return failure ?? blocked("references-expired");
      }
    }
    const snapshot: Snapshot = { kind: "ready", generation, context, storage, receipts, now };
    return this.current(snapshot) ? snapshot : this.changedContext();
  }

  private current(snapshot: Snapshot): boolean { return snapshot.generation === this.generation && sameContext(snapshot.context, this.context); }
  private changedContext(): CommandReceiptBlocked { return blocked(this.context === undefined ? "context-unavailable" : "context-mismatch"); }
}

function blocked(reason: CommandReceiptBlockReason): CommandReceiptBlocked { return Object.freeze({ kind: "blocked", reason }); }
function isTime(value: unknown): value is number { return typeof value === "number" && Number.isSafeInteger(value) && value >= 0 && value <= maximumTime; }
function shape(value: unknown, fields: readonly string[]): value is Record<string, unknown> {
  if (value === null || typeof value !== "object" || Array.isArray(value)
    || Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null) { return false; }
  const keys = Reflect.ownKeys(value);
  return keys.length === fields.length && keys.every(key => typeof key === "string" && fields.includes(key))
    && Object.values(Object.getOwnPropertyDescriptors(value)).every(descriptor => descriptor.get === undefined && descriptor.set === undefined);
}
function captureContext(value: unknown): CommandReceiptContext | undefined {
  return shape(value, ["operatorId", "serviceAssociation"]) && isScalarText(value.operatorId, 256) && isScalarText(value.serviceAssociation, 256)
    ? Object.freeze({ operatorId: value.operatorId as string, serviceAssociation: value.serviceAssociation as string }) : undefined;
}
function sameContext(left: CommandReceiptContext | undefined, right: CommandReceiptContext | undefined): boolean {
  return left === undefined || right === undefined ? left === right
    : left.operatorId === right.operatorId && left.serviceAssociation === right.serviceAssociation;
}
function captureIntent(value: unknown): CommandReceiptIntent | undefined {
  if (!shape(value, ["family", "target"])) { return undefined; }
  const target = value.target;
  switch (value.family) {
    case "management.metadata.replace": case "management.configuration.create":
      return shape(target, ["applicationId"]) && isUuid(target.applicationId)
        ? Object.freeze({ family: value.family, target: Object.freeze({ applicationId: target.applicationId as string }) }) : undefined;
    case "management.configuration.source.publish":
      return shape(target, ["sourceId"]) && isUuid(target.sourceId)
        ? Object.freeze({ family: value.family, target: Object.freeze({ sourceId: target.sourceId as string }) }) : undefined;
    case "management.configuration.release.create": case "management.configuration.assignment.replace":
      return shape(target, ["applicationId", "configurationId", "schemaVersion"]) && isUuid(target.applicationId)
        && isOperationKey(target.configurationId) && isOrderedInteger(target.schemaVersion)
        ? Object.freeze({ family: value.family, target: Object.freeze({ applicationId: target.applicationId as string,
          configurationId: target.configurationId as string, schemaVersion: target.schemaVersion as string }) }) : undefined;
    case "management.scheduler.trigger.write":
      return shape(target, ["applicationId", "triggerId"]) && isUuid(target.applicationId) && isUuid(target.triggerId)
        ? Object.freeze({ family: value.family, target: Object.freeze({ applicationId: target.applicationId as string, triggerId: target.triggerId as string }) }) : undefined;
    case "management.scheduler.task.write":
      return shape(target, ["applicationId", "taskId"]) && isUuid(target.applicationId) && isUuid(target.taskId)
        ? Object.freeze({ family: value.family, target: Object.freeze({ applicationId: target.applicationId as string, taskId: target.taskId as string }) }) : undefined;
    case "management.scheduler.run.write":
      return shape(target, ["applicationId", "runId"]) && isUuid(target.applicationId) && isUuid(target.runId)
        ? Object.freeze({ family: value.family, target: Object.freeze({ applicationId: target.applicationId as string, runId: target.runId as string }) }) : undefined;
    default: return undefined;
  }
}
function captureStoredReceipt(value: unknown): StoredReceipt | undefined {
  if (!shape(value, ["family", "target", "key", "createdAtMs"]) || !isOperationKey(value.key) || !isTime(value.createdAtMs)) { return undefined; }
  const intent = captureIntent({ family: value.family, target: value.target });
  return intent === undefined ? undefined : Object.freeze({ ...intent, key: value.key as string, createdAtMs: value.createdAtMs });
}
function capturePublicReceipt(value: unknown): CommandReceipt | undefined {
  if (!shape(value, ["operatorId", "serviceAssociation", "family", "target", "key", "createdAtMs"])) { return undefined; }
  const context = captureContext({ operatorId: value.operatorId, serviceAssociation: value.serviceAssociation });
  const stored = captureStoredReceipt({ family: value.family, target: value.target, key: value.key, createdAtMs: value.createdAtMs });
  return context === undefined || stored === undefined ? undefined : withContext(stored, context);
}
function parseEnvelope(raw: unknown): Envelope | undefined {
  if (typeof raw !== "string" || raw.length > commandReceiptLimits.maximumStorageBytes
    || new TextEncoder().encode(raw).byteLength > commandReceiptLimits.maximumStorageBytes) { return undefined; }
  try {
    new OpaqueJsonObject(raw);
    const value: unknown = JSON.parse(raw);
    if (!shape(value, ["version", "operatorId", "serviceAssociation", "receipts"]) || value.version !== 1
      || !Array.isArray(value.receipts) || value.receipts.length > commandReceiptLimits.maximumReceipts) { return undefined; }
    const context = captureContext({ operatorId: value.operatorId, serviceAssociation: value.serviceAssociation });
    if (context === undefined) { return undefined; }
    const rows: StoredReceipt[] = [];
    for (const input of value.receipts) {
      const row = captureStoredReceipt(input);
      if (row === undefined || rows.some(prior => prior.key === row.key)) { return undefined; }
      rows.push(row);
    }
    return Object.freeze({ version: 1, ...context, receipts: Object.freeze(rows) });
  } catch { return undefined; }
}
function persist(storage: CommandReceiptStorage, context: CommandReceiptContext, receipts: readonly StoredReceipt[]): CommandReceiptBlocked | undefined {
  const raw = JSON.stringify({ version: 1, ...context, receipts });
  if (new TextEncoder().encode(raw).byteLength > commandReceiptLimits.maximumStorageBytes) { return blocked("capacity"); }
  try {
    if (receipts.length === 0) { storage.removeItem(storageKey); return storage.getItem(storageKey) === null ? undefined : blocked("storage-unavailable"); }
    storage.setItem(storageKey, raw);
    return storage.getItem(storageKey) === raw ? undefined : blocked("storage-unavailable");
  } catch { return blocked("storage-unavailable"); }
}
function withContext(receipt: StoredReceipt, context: CommandReceiptContext): CommandReceipt { return Object.freeze({ ...receipt, ...context }); }
function publicReceipts(snapshot: Snapshot): readonly CommandReceipt[] { return Object.freeze(snapshot.receipts.map(row => withContext(row, snapshot.context))); }
function sameStoredReceipt(left: StoredReceipt, right: StoredReceipt): boolean {
  return left.family === right.family && left.key === right.key && left.createdAtMs === right.createdAtMs
    && JSON.stringify(left.target) === JSON.stringify(right.target);
}
function sameReceipt(left: CommandReceipt, right: CommandReceipt): boolean { return sameContext(left, right) && sameStoredReceipt(left, right); }
