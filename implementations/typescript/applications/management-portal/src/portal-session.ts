import { CommandReceiptStore } from "./command-receipts.ts";
import type { CommandReceiptBlockReason } from "./command-receipts.ts";
import { capturePortalContext, samePortalSession } from "./portal-context.ts";
import type { PortalSessionContext } from "./portal-context.ts";
import type { PortalSessionCheckResult } from "./portal-session-check.ts";

export interface PortalStateHandle {
  setContext(context: PortalSessionContext | undefined): void;
  signOut(): void;
  cancelRequests(): void;
  discardDraft(): void;
  checkExpiry(): void;
  readonly view: { readonly navigationBlocked: boolean; readonly state: string };
}
export interface PortalSessionSnapshot {
  readonly context: PortalSessionContext | undefined;
  readonly epoch: number;
  readonly blocked: boolean;
  readonly blockReason?: "session" | "clock";
  readonly verification?: "checking" | "unavailable";
}

/** One document's identity boundary across concurrent list, detail and editor owners. */
export class PortalSessionCoordinator {
  readonly receipts: CommandReceiptStore;
  private current: PortalSessionSnapshot = Object.freeze({ context: undefined, epoch: 0, blocked: false });
  private readonly owners = new Set<PortalStateHandle>();
  private readonly listeners = new Set<() => void>();
  private readonly now: () => number;
  private transitions = 0;
  private receiptObservation: CommandReceiptBlockReason | undefined;
  private sessionCheck: { readonly controller: AbortController; readonly promise: Promise<void> } | undefined;

  constructor(receipts = new CommandReceiptStore({ context: undefined }), now = () => Date.now()) {
    this.receipts = receipts;
    this.now = now;
  }
  readonly getSnapshot = (): PortalSessionSnapshot => this.current;
  get receiptNotice(): CommandReceiptBlockReason | undefined { return this.receiptObservation; }
  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };

  register(owner: PortalStateHandle): () => void {
    if (this.owners.size >= 16) { throw new Error("The protected portal owner limit was reached."); }
    this.owners.add(owner);
    if (this.current.blocked) { owner.signOut(); }
    else if (this.current.context !== undefined) { owner.setContext(this.current.context); }
    return () => { owner.cancelRequests(); this.owners.delete(owner); };
  }

  /** Only the still-current trusted BFF request may establish or change this document's context. */
  observe(value: unknown, requestEpoch: number, origin: PortalStateHandle): { readonly accepted: boolean; readonly epoch: number } {
    if (requestEpoch !== this.current.epoch || this.current.blocked) { return { accepted: false, epoch: this.current.epoch }; }
    const context = capturePortalContext(value);
    const now = this.now();
    if (!Number.isSafeInteger(now) || now < 0) {
      this.clockUnavailable();
      return { accepted: false, epoch: this.current.epoch };
    }
    if (context === undefined || context.expiresAtMs <= now) {
      this.signOut();
      return { accepted: false, epoch: this.current.epoch };
    }
    if (samePortalSession(this.current.context, context)) {
      if (this.current.context?.expiresAtMs !== context.expiresAtMs) {
        for (const owner of this.owners) { owner.setContext(context); }
        this.publish(Object.freeze({ ...this.current, context }));
      }
      return { accepted: true, epoch: this.current.epoch };
    }
    // Bound automatic rebasings when replicas/configuration disagree. No writes or lookups are queued.
    if (++this.transitions > 4) { this.signOut(); return { accepted: false, epoch: this.current.epoch }; }
    const previous = this.current.context;
    if (previous !== undefined && (previous.operatorId !== context.operatorId || previous.serviceAssociation !== context.serviceAssociation)) {
      this.receipts.clearLocalReferences();
    }
    this.receipts.cancelRecovery();
    this.receipts.setContext({ operatorId: context.operatorId, serviceAssociation: context.serviceAssociation });
    const restored = this.receipts.load();
    if (restored.kind === "blocked") {
      this.receiptObservation = restored.reason;
      if (restored.reason === "context-mismatch") { this.receipts.clearLocalReferences(); }
    }
    const next = Object.freeze({ context, epoch: this.current.epoch + 1, blocked: false });
    // Invalidate other bootstrap requests before notifying React. The origin consumes its matched reply itself.
    for (const owner of this.owners) { if (owner !== origin || previous !== undefined) { owner.setContext(context); } }
    this.publish(next);
    return { accepted: true, epoch: next.epoch };
  }

  signOut(): void {
    if (this.current.blocked && this.current.blockReason === "session") { return; }
    this.sessionCheck?.controller.abort();
    for (const owner of this.owners) { owner.signOut(); }
    this.receipts.setContext(undefined);
    this.receipts.clearLocalReferences();
    this.publish(Object.freeze({ context: undefined, epoch: this.current.epoch + 1, blocked: true, blockReason: "session" }));
  }

  /** Revalidate one active document without discarding a still-valid local draft. */
  revalidateSession(read: (context: PortalSessionContext, signal: AbortSignal) => Promise<PortalSessionCheckResult>): Promise<void> {
    if (this.sessionCheck !== undefined) { return this.sessionCheck.promise; }
    this.checkExpiry();
    const context = this.current.context;
    if (context === undefined || this.current.blocked) { return Promise.resolve(); }
    const epoch = this.current.epoch;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const isCurrent = () => this.current.epoch === epoch && !this.current.blocked && samePortalSession(this.current.context, context);
    const unavailable = () => {
      if (isCurrent()) { this.publish(Object.freeze({ ...this.current, verification: "unavailable" })); }
    };
    const promise = Promise.resolve().then(async () => {
      try {
        const result = await read(context, controller.signal);
        if (!isCurrent()) { return; }
        if (controller.signal.aborted) { unavailable(); return; }
        if (result.state !== "current") {
          if (result.state === "unauthenticated" || result.state === "changed") { this.signOut(); }
          else { unavailable(); }
          return;
        }
        const fresh = capturePortalContext(result.context);
        const now = this.now();
        if (!Number.isSafeInteger(now) || now < 0) { this.clockUnavailable(); return; }
        if (fresh === undefined || !samePortalSession(context, fresh) || fresh.expiresAtMs <= now) { this.signOut(); return; }
        for (const owner of this.owners) { owner.setContext(fresh); }
        this.publish(Object.freeze({ context: fresh, epoch, blocked: false }));
      } catch { unavailable(); }
      finally {
        if (timer !== undefined) { clearTimeout(timer); }
        if (this.sessionCheck?.controller === controller) { this.sessionCheck = undefined; }
      }
    });
    this.sessionCheck = { controller, promise };
    timer = setTimeout(() => { controller.abort(); unavailable(); }, 15000);
    this.publish(Object.freeze({ ...this.current, verification: "checking" }));
    return promise;
  }

  /** Abort visibility work, retaining callback custody until its actual settlement. */
  cancelSessionCheck(): Promise<void> {
    const pending = this.sessionCheck;
    pending?.controller.abort();
    return pending?.promise ?? Promise.resolve();
  }
  /** Explicit fresh-read recovery only; no original operation or cached protected value is restored. */
  resumeClock(): boolean {
    const now = this.now();
    if (this.current.blockReason !== "clock" || !Number.isSafeInteger(now) || now < 0 || now > 8640000000000000) { return false; }
    this.publish(Object.freeze({ context: undefined, epoch: this.current.epoch + 1, blocked: false }));
    return true;
  }
  checkExpiry(): void {
    if (this.current.blocked && this.current.blockReason === "session") { return; }
    const now = this.now();
    if (!Number.isSafeInteger(now) || now < 0) { this.clockUnavailable(); }
    else if (this.current.context !== undefined && this.current.context.expiresAtMs <= now) { this.signOut(); }
    else {
      for (const owner of this.owners) { owner.checkExpiry(); }
      if ([...this.owners].some(owner => owner.view.state === "clock-untrusted")) { this.clockUnavailable(); }
    }
  }
  get navigationBlocked(): boolean { return [...this.owners].some(owner => owner.view.navigationBlocked); }

  /** An explicit accepted leave decision discards local edits only, never original references or request custody. */
  discardLocalDrafts(): void { for (const owner of this.owners) { owner.discardDraft(); } }

  private clockUnavailable(): void {
    for (const owner of this.owners) { owner.checkExpiry(); }
    this.receipts.cancelRecovery();
    this.receipts.setContext(undefined);
    if (this.current.blockReason !== "clock") {
      this.publish(Object.freeze({ context: undefined, epoch: this.current.epoch + 1, blocked: true, blockReason: "clock" }));
    }
  }
  private publish(snapshot: PortalSessionSnapshot): void {
    this.current = snapshot;
    for (const listener of this.listeners) { listener(); }
  }
}
