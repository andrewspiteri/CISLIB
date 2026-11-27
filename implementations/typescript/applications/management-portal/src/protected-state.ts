import { isOrderedInteger, isScalarText, isUuid, OpaqueJsonObject } from "@cislib/management-client";
import type { CommandReceiptStore } from "./command-receipts.ts";
import { capturePortalContext, samePortalSession } from "./portal-context.ts";
import type { PortalSessionContext } from "./portal-context.ts";

export const protectedStateLimits = Object.freeze({ maximumDraftBytes: 262144, maximumSnapshotBytes: 8 * 1024 * 1024 + 4096,
  maximumRequestKeyLength: 16384, maximumPendingRequests: 4 });

/** Exact list/detail selection. requestKey includes the current path, query and cursor. */
export interface ProtectedTarget {
  readonly requestKey: string;
  readonly applicationId?: string;
  readonly environment?: string;
  readonly definitionId?: string;
  readonly schemaVersion?: string;
}
export interface ProtectedRequestTicket {
  readonly generation: number;
  readonly context: PortalSessionContext | undefined;
  readonly target: ProtectedTarget;
  readonly signal: AbortSignal;
}
export type ProtectedReadResult<T> =
  | { readonly state: "ready"; readonly context: PortalSessionContext; readonly value: T }
  | { readonly state: "denied" | "not-found" | "invalid" | "unavailable"; readonly context?: PortalSessionContext }
  | { readonly state: "unauthenticated" };
export interface ProtectedSnapshot<T> {
  readonly value: T;
  readonly receivedAtMs: number;
  readonly context: PortalSessionContext;
  readonly target: ProtectedTarget;
}
export interface ProtectedDraft {
  readonly text: string;
  readonly dirty: boolean;
  readonly updatedAtMs: number;
}
export type ProtectedViewState = "empty" | "loading" | "ready" | "stale" | "denied" | "not-found" | "invalid"
  | "unavailable" | "unauthenticated" | "expired" | "clock-untrusted";
export interface ProtectedView<T> {
  readonly state: ProtectedViewState;
  readonly context: PortalSessionContext | undefined;
  readonly target: ProtectedTarget | undefined;
  readonly snapshot: ProtectedSnapshot<T> | undefined;
  readonly draft: ProtectedDraft | undefined;
  /** Non-payload editor choices stay memory-only and share this owner's navigation/lifecycle guard. */
  readonly intentDirty: boolean;
  /** Local freshness gate only. Every actual write still needs fresh backend authorization/preconditions. */
  readonly canWrite: boolean;
  readonly navigationBlocked: boolean;
  readonly pendingRequests: number;
}
export type ProtectedRequestResult = { readonly kind: "applied" | "stale" | "canceled" }
  | { readonly kind: "blocked"; readonly reason: "no-target" | "busy" | "clock-untrusted" };
export type ProtectedDraftResult = { readonly kind: "changed" }
  | { readonly kind: "blocked"; readonly reason: "not-ready" | "too-large" | "dirty" };
export interface ProtectedStateOptions {
  readonly context: PortalSessionContext | undefined;
  readonly receipts: CommandReceiptStore;
  readonly now?: () => number;
}
interface PendingRequest {
  readonly generation: number;
  readonly context: PortalSessionContext | undefined;
  readonly target: ProtectedTarget;
  readonly controller: AbortController;
}

/** One memory-only read/draft owner, with actual request custody retained until each callback settles. */
export class ProtectedStateOwner<T> {
  private context: PortalSessionContext | undefined;
  private target: ProtectedTarget | undefined;
  private snapshot: ProtectedSnapshot<T> | undefined;
  private draft: ProtectedDraft | undefined;
  private draftBaseline: string | undefined;
  private intentChangedAtMs: number | undefined;
  private state: ProtectedViewState = "empty";
  private generation = 0;
  private readonly pending = new Set<PendingRequest>();
  private readonly receipts: CommandReceiptStore;
  private readonly now: () => number;

  constructor(options: ProtectedStateOptions) {
    this.receipts = options.receipts;
    this.now = options.now ?? (() => Date.now());
    this.context = capturePortalContext(options.context);
    if (this.context !== undefined) { this.adoptReceiptContext(this.context); }
    this.checkExpiry();
  }

  get view(): ProtectedView<T> {
    this.checkExpiry();
    return Object.freeze({
      state: this.state, context: this.context, target: this.target, snapshot: this.snapshot, draft: this.draft,
      canWrite: this.state === "ready" && this.context !== undefined && this.snapshot !== undefined,
      intentDirty: this.intentChangedAtMs !== undefined,
      navigationBlocked: this.draft?.dirty === true || this.intentChangedAtMs !== undefined, pendingRequests: this.pending.size
    });
  }

  /** Unknown means a real invalidation, even if a bootstrap request started with no known context. */
  setContext(value: PortalSessionContext | undefined): void {
    const next = capturePortalContext(value);
    if (next === undefined) { this.clearSession("unauthenticated"); return; }
    const now = this.now();
    if (!isTime(now)) { this.clearSession("clock-untrusted"); return; }
    if (next.expiresAtMs <= now) { this.clearSession("expired"); return; }
    if (samePortalSession(this.context, next)) { this.context = next; return; }
    const changedPersistentAssociation = this.context !== undefined
      && (this.context.operatorId !== next.operatorId || this.context.serviceAssociation !== next.serviceAssociation);
    this.invalidate();
    this.clearValues();
    this.context = next;
    this.state = "empty";
    this.receipts.cancelRecovery();
    if (changedPersistentAssociation) { this.receipts.clearLocalReferences(); }
    this.adoptReceiptContext(next);
  }

  signOut(): void { this.clearSession("unauthenticated"); }

  /** Call before selecting a new route/definition; the UI owns any dirty-navigation confirmation. */
  selectTarget(value: ProtectedTarget | undefined): void {
    const next = value === undefined ? undefined : captureTarget(value);
    if (value !== undefined && next === undefined) { throw new TypeError("Invalid protected target."); }
    if (sameTarget(this.target, next)) { return; }
    this.invalidate();
    this.receipts.cancelRecovery();
    this.target = next;
    this.clearValues();
    this.state = "empty";
    this.checkExpiry();
  }

  /** Clears protected content but preserves same-user payload-free original references. */
  permissionLost(): void {
    this.invalidate();
    this.receipts.cancelRecovery();
    this.clearValues();
    this.state = "denied";
    this.checkExpiry();
  }

  /** Same-scope allowed snapshots may remain dated; this queues nothing and disables fresh submissions. */
  transientUnavailable(): void {
    this.invalidate();
    this.state = this.snapshot === undefined ? "unavailable" : "stale";
    this.checkExpiry();
  }

  cancelRequests(): void {
    this.invalidate();
    this.state = this.snapshot === undefined ? "empty" : "stale";
    this.checkExpiry();
  }

  checkExpiry(): void {
    const now = this.now();
    if (!isTime(now)) {
      if (this.state !== "clock-untrusted" || this.context !== undefined) { this.clearSession("clock-untrusted"); }
    } else if (this.snapshot !== undefined && this.snapshot.receivedAtMs > now
      || this.draft !== undefined && this.draft.updatedAtMs > now
      || this.intentChangedAtMs !== undefined && this.intentChangedAtMs > now) {
      this.clearSession("clock-untrusted");
    } else if (this.context !== undefined && this.context.expiresAtMs <= now) {
      this.clearSession("expired");
    }
  }

  /** A protected BFF read may bootstrap unknown identity; only a current ready reply enables local editing. */
  async request(work: (ticket: ProtectedRequestTicket) => Promise<ProtectedReadResult<T>>): Promise<ProtectedRequestResult> {
    this.checkExpiry();
    if (!isTime(this.now())) { return Object.freeze({ kind: "blocked", reason: "clock-untrusted" }); }
    const target = this.target;
    if (target === undefined) { return Object.freeze({ kind: "blocked", reason: "no-target" }); }
    if (this.pending.size >= protectedStateLimits.maximumPendingRequests) { return Object.freeze({ kind: "blocked", reason: "busy" }); }
    this.invalidate();
    const selected: PendingRequest = { generation: this.generation, context: this.context, target, controller: new AbortController() };
    this.pending.add(selected);
    this.state = "loading";
    try {
      const reply = await work(Object.freeze({
        generation: selected.generation, context: selected.context, target: selected.target, signal: selected.controller.signal
      }));
      this.checkExpiry();
      if (selected.generation !== this.generation || !sameTarget(this.target, selected.target)) { return Object.freeze({ kind: "stale" }); }
      if (selected.controller.signal.aborted) { return Object.freeze({ kind: "canceled" }); }
      if (reply === null || typeof reply !== "object"
        || !["ready", "denied", "not-found", "invalid", "unavailable", "unauthenticated"].includes(reply.state)) {
        throw new TypeError("Invalid protected read result.");
      }
      if (reply.state === "unauthenticated") {
        this.clearSession("unauthenticated");
        return Object.freeze({ kind: "applied" });
      }
      if (reply.state !== "ready" && reply.context === undefined) {
        if (reply.state === "unavailable") {
          this.state = this.snapshot === undefined ? "unavailable" : "stale";
        } else {
          this.clearValues();
          this.receipts.cancelRecovery();
          this.state = reply.state;
        }
        return Object.freeze({ kind: "applied" });
      }
      const context = capturePortalContext(reply.context);
      const now = this.now();
      if (context === undefined || !isTime(now) || context.expiresAtMs <= now) {
        this.clearSession(!isTime(now) ? "clock-untrusted" : context === undefined ? "unauthenticated" : "expired");
        return Object.freeze({ kind: "applied" });
      }
      if (selected.context !== undefined && !samePortalSession(selected.context, context)) {
        this.setContext(context);
        return Object.freeze({ kind: "stale" });
      }
      this.context = context;
      this.adoptReceiptContext(context);
      if (reply.state === "ready") {
        const value = captureSnapshotValue(reply.value);
        this.snapshot = Object.freeze({ value, receivedAtMs: now, context, target: selected.target });
        this.state = "ready";
      } else if (reply.state === "unavailable") {
        this.state = this.snapshot === undefined ? "unavailable" : "stale";
      } else {
        this.clearValues();
        this.receipts.cancelRecovery();
        this.state = reply.state;
      }
      return Object.freeze({ kind: "applied" });
    } finally {
      // Cancellation changes visibility, never actual callback/SDK cleanup custody.
      this.pending.delete(selected);
      if (selected.generation === this.generation && this.state === "loading") {
        this.state = this.snapshot === undefined ? "unavailable" : "stale";
      }
    }
  }

  startDraft(text: string): ProtectedDraftResult {
    const view = this.view;
    if (!view.canWrite) { return Object.freeze({ kind: "blocked", reason: "not-ready" }); }
    if (this.draft?.dirty === true || this.intentChangedAtMs !== undefined) { return Object.freeze({ kind: "blocked", reason: "dirty" }); }
    if (!boundedDraft(text)) { return Object.freeze({ kind: "blocked", reason: "too-large" }); }
    this.draftBaseline = text;
    this.draft = Object.freeze({ text, dirty: false, updatedAtMs: this.now() });
    return Object.freeze({ kind: "changed" });
  }

  editDraft(text: string): ProtectedDraftResult {
    this.checkExpiry();
    if (this.context === undefined || this.draft === undefined || !["ready", "stale", "loading"].includes(this.state)) {
      return Object.freeze({ kind: "blocked", reason: "not-ready" });
    }
    if (!boundedDraft(text)) { return Object.freeze({ kind: "blocked", reason: "too-large" }); }
    this.draft = Object.freeze({ text, dirty: text !== this.draftBaseline, updatedAtMs: this.now() });
    return Object.freeze({ kind: "changed" });
  }

  /** Track exact reference/reason choices without rewriting or wrapping the payload draft. */
  setIntentDirty(dirty: boolean): ProtectedDraftResult {
    if (typeof dirty !== "boolean") { throw new TypeError("Supply a dirty-intent flag."); }
    this.checkExpiry();
    if (this.context === undefined || this.target === undefined || this.snapshot === undefined || !["ready", "stale", "loading"].includes(this.state)) {
      return Object.freeze({ kind: "blocked", reason: "not-ready" });
    }
    this.intentChangedAtMs = dirty ? this.now() : undefined;
    return Object.freeze({ kind: "changed" });
  }

  discardDraft(): void { this.draft = undefined; this.draftBaseline = undefined; this.intentChangedAtMs = undefined; }

  private invalidate(): void {
    this.generation++;
    for (const pending of this.pending) { pending.controller.abort(); }
  }

  private clearValues(): void { this.snapshot = undefined; this.discardDraft(); }

  private clearSession(state: "unauthenticated" | "expired" | "clock-untrusted"): void {
    this.invalidate();
    this.clearValues();
    this.context = undefined;
    this.state = state;
    this.receipts.setContext(undefined);
    if (state !== "clock-untrusted") { this.receipts.clearLocalReferences(); }
  }

  private adoptReceiptContext(context: PortalSessionContext): void {
    this.receipts.setContext({ operatorId: context.operatorId, serviceAssociation: context.serviceAssociation });
  }
}

function isTime(value: unknown): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0 && value <= 8640000000000000;
}

function captureTarget(value: unknown): ProtectedTarget | undefined {
  if (value === null || typeof value !== "object" || Array.isArray(value)
    || Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null) { return undefined; }
  const input = value as Record<string, unknown>;
  const fields = ["requestKey", "applicationId", "environment", "definitionId", "schemaVersion"];
  if (Reflect.ownKeys(value).some(key => typeof key !== "string" || !fields.includes(key))
    || Object.values(Object.getOwnPropertyDescriptors(value)).some(field => field.get !== undefined || field.set !== undefined)
    || !isScalarText(input.requestKey, protectedStateLimits.maximumRequestKeyLength)
    || input.applicationId !== undefined && !isUuid(input.applicationId)
    || input.environment !== undefined && !isScalarText(input.environment, 255)
    || input.definitionId !== undefined && !isScalarText(input.definitionId, 512)
    || input.schemaVersion !== undefined && !isOrderedInteger(input.schemaVersion)) { return undefined; }
  return Object.freeze({ requestKey: input.requestKey,
    ...(input.applicationId === undefined ? {} : { applicationId: input.applicationId }),
    ...(input.environment === undefined ? {} : { environment: input.environment }),
    ...(input.definitionId === undefined ? {} : { definitionId: input.definitionId }),
    ...(input.schemaVersion === undefined ? {} : { schemaVersion: input.schemaVersion }) }) as ProtectedTarget;
}

function sameTarget(left: ProtectedTarget | undefined, right: ProtectedTarget | undefined): boolean {
  return left === undefined || right === undefined ? left === right : left.requestKey === right.requestKey
    && left.applicationId === right.applicationId && left.environment === right.environment
    && left.definitionId === right.definitionId && left.schemaVersion === right.schemaVersion;
}

function boundedDraft(text: unknown): text is string {
  return typeof text === "string" && text.length <= protectedStateLimits.maximumDraftBytes
    && new TextEncoder().encode(text).byteLength <= protectedStateLimits.maximumDraftBytes;
}

/** Only already-projected JSON values cross this memory boundary; this does not interpret a field schema. */
function captureSnapshotValue<T>(input: T): T {
  const raw = JSON.stringify({ value: input });
  if (raw.length > protectedStateLimits.maximumSnapshotBytes
    || new TextEncoder().encode(raw).byteLength > protectedStateLimits.maximumSnapshotBytes) {
    throw new RangeError("Protected snapshot exceeds its memory bound.");
  }
  new OpaqueJsonObject(raw);
  const parsed = JSON.parse(raw) as { value?: T };
  if (!Object.hasOwn(parsed, "value")) { throw new TypeError("Protected snapshot must be a JSON value."); }
  return freezeJson(parsed.value) as T;
}

function freezeJson(value: unknown): unknown {
  if (value !== null && typeof value === "object") {
    for (const child of Object.values(value)) { freezeJson(child); }
    Object.freeze(value);
  }
  return value;
}

