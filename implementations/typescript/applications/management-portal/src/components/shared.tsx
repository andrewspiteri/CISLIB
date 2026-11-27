"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import type { PortalReply } from "../portal-model";
import { capturePortalContext, portalContextHeader, portalContextKey } from "../portal-context.ts";
import { ProtectedStateOwner } from "../protected-state.ts";
import type { ProtectedTarget, ProtectedView } from "../protected-state.ts";
import { useProtectedPortal } from "./protected-context";

export { reportedLabel } from "../portal-model.ts";

const pagingStorage = "cis.management.portal.pages";
export interface ReadState<T> {
  readonly url: string | null;
  readonly epoch: number;
  readonly targetKey: string;
  readonly busy: boolean;
  readonly reply?: PortalReply<T>;
  readonly stale?: boolean;
  readonly receivedAtMs?: number;
}

export function usePortalRead<T>(url: string | null, revision = 0, target?: ProtectedTarget): ReadState<T> {
  const { owner: portal, snapshot: session } = useProtectedPortal();
  const [reader] = useState(() => new ProtectedStateOwner<T>({ context: session.context, receipts: portal.receipts }));
  const targetKey = JSON.stringify(target ?? null);
  const [state, setState] = useState<ReadState<T>>({ url, epoch: session.epoch, targetKey, busy: url !== null });
  const previousRevision = useRef(revision);
  useEffect(() => portal.register(reader), [portal, reader]);
  useEffect(() => {
    let alive = true;
    let acceptedEpoch = session.epoch;
    const explicitRefresh = previousRevision.current !== revision;
    previousRevision.current = revision;
    if (url === null) { reader.selectTarget(undefined); setState({ url, epoch: acceptedEpoch, targetKey, busy: false }); return; }
    if (session.blocked) {
      if (session.blockReason === "clock" && explicitRefresh && portal.resumeClock()) { return; }
      setState({ url, epoch: acceptedEpoch, targetKey, busy: false, reply: session.blockReason === "clock"
        ? { state: "unavailable", message: "The clock could not be trusted. Correct it, then Refresh." }
        : { state: "unauthenticated", message: "Sign in to inspect applications." } });
      return;
    }
    if (session.context !== undefined) { reader.setContext(session.context); }
    try {
      if (target !== undefined && target.requestKey !== url) { throw new TypeError("The view target does not match its request."); }
      reader.selectTarget(target ?? readTarget(url));
    }
    catch { setState({ url, epoch: acceptedEpoch, targetKey, busy: false, reply: { state: "invalid", message: "The selected view is invalid." } }); return; }
    setState(readState(url, acceptedEpoch, targetKey, reader.view, true));
    void (async () => {
      let message: string | undefined;
      try {
        await reader.request(async ticket => {
          try {
            const headers = ticket.context === undefined ? {} : { [portalContextHeader]: portalContextKey(ticket.context) };
            const response = await fetch(url, { headers, signal: ticket.signal, credentials: "same-origin", cache: "no-store", redirect: "error" });
            const body: unknown = await response.json();
            if (typeof body !== "object" || body === null || !("state" in body)
              || !["ready", "unauthenticated", "denied", "not-found", "invalid", "unavailable"].includes(String(body.state))) {
              throw new Error("Invalid portal reply.");
            }
            const reply = body as PortalReply<T>;
            if (reply.state === "unauthenticated") {
              if (portal.getSnapshot().epoch === session.epoch) { clearPaging(); portal.signOut(); }
              return { state: "unauthenticated" };
            }
            if (reply.context !== undefined) {
              const observation = portal.observe(reply.context, session.epoch, reader);
              if (!observation.accepted) { return { state: "unavailable" }; }
              acceptedEpoch = observation.epoch;
            }
            if (reply.state === "ready") {
              const context = capturePortalContext(reply.context);
              if (context === undefined) {
                if (portal.getSnapshot().epoch === session.epoch) { portal.signOut(); }
                return { state: "unauthenticated" };
              }
              return { state: "ready", context, value: reply.value };
            }
            message = reply.message;
            return { state: reply.state, ...(reply.context === undefined ? {} : { context: reply.context }) };
          } catch (error) {
            if (ticket.signal.aborted) { throw error; }
            return { state: "unavailable" };
          }
        });
      } catch {
        message = "The service could not be reached. Try Refresh.";
      }
      if (alive && portal.getSnapshot().epoch === acceptedEpoch) { setState(readState(url, acceptedEpoch, targetKey, reader.view, false, message)); }
    })();
    return () => { alive = false; reader.cancelRequests(); };
  }, [url, revision, session.epoch, portal, reader, targetKey]);
  return state.url === url && state.epoch === session.epoch && state.targetKey === targetKey && previousRevision.current === revision ? state
    : { url, epoch: session.epoch, targetKey, busy: url !== null };
}

function readTarget(url: string): ProtectedTarget {
  const parsed = new URL(url, "https://portal.invalid");
  const parts = parsed.pathname.split("/");
  const applicationIndex = parts.indexOf("applications");
  const applicationId = applicationIndex < 0 ? undefined : parts[applicationIndex + 1];
  const environment = parsed.searchParams.get("environment") ?? undefined;
  return { requestKey: url, ...(applicationId === undefined ? {} : { applicationId }),
    ...(environment === undefined ? {} : { environment }) };
}

function readState<T>(url: string, epoch: number, targetKey: string, view: ProtectedView<T>, busy: boolean, message?: string): ReadState<T> {
  if (view.snapshot !== undefined && ["ready", "loading", "stale"].includes(view.state)) {
    return { url, epoch, targetKey, busy, reply: { state: "ready", value: view.snapshot.value, context: view.snapshot.context },
      stale: view.state === "stale", receivedAtMs: view.snapshot.receivedAtMs };
  }
  if (view.state === "empty" || view.state === "loading") { return { url, epoch, targetKey, busy }; }
  const state = view.state === "expired" ? "unauthenticated" : view.state === "clock-untrusted" ? "unavailable"
    : view.state === "stale" || view.state === "ready" ? "unavailable" : view.state;
  return { url, epoch, targetKey, busy, reply: { state, message: message ?? (state === "unauthenticated"
    ? "Sign in to inspect applications." : state === "denied" || state === "not-found"
      ? "The selected target is unavailable." : "The view could not be refreshed. Try Refresh.") } };
}

export function ready<T>(state: ReadState<T>): T | undefined { return state.reply?.state === "ready" ? state.reply.value : undefined; }
export function isUnauthenticated(...states: readonly ReadState<unknown>[]): boolean {
  return states.some(state => state.reply?.state === "unauthenticated");
}

export function Shell({ children, signedIn = true, footer = "Management portal", wide = false }: { children: ReactNode; signedIn?: boolean; footer?: string; wide?: boolean }) {
  return <div className="portal-shell">
    <aside className="rail"><Link className="brand" href="/applications">CIS<span>Management</span></Link>
      <nav aria-label="Main navigation"><Link href="/applications" className="rail-link" aria-current="page">Applications</Link></nav>
      <span className="rail-footer">{footer}</span>
    </aside>
    <div className="workspace"><header className="topbar"><strong>Management</strong>
      <a className="account-link" href={signedIn ? "/api/auth/signout" : "/api/auth/signin"} onClick={clearPaging}>{signedIn ? "Sign out" : "Sign in"}</a>
    </header><main id="main-content" className={wide ? "canvas configuration-canvas" : "canvas"}>{children}</main></div>
  </div>;
}

export function ReadNotice({ state, restart }: { state: ReadState<unknown>; restart?: () => void }) {
  const reply = state.reply;
  if (reply === undefined) { return <div className="notice" role="status">Loading…</div>; }
  if (reply.state === "ready") {
    if (state.stale && state.receivedAtMs !== undefined) {
      return <div className="notice" role="status">Showing an earlier observation from <time dateTime={new Date(state.receivedAtMs).toISOString()}>{new Date(state.receivedAtMs).toLocaleString("en-GB")}</time>. Refresh before making changes.</div>;
    }
    return state.busy ? <p className="muted" role="status">Refreshing…</p> : null;
  }
  return <div className="notice" role="alert"><strong>{({ unauthenticated: "Sign in required", denied: "Access unavailable", "not-found": "Item unavailable", invalid: "The list request changed", unavailable: "Service unavailable" })[reply.state]}</strong>
    <p>{reply.message}</p>{reply.state === "unauthenticated" ? <a className="button primary" href="/api/auth/signin">Sign in</a> : null}
    {reply.state === "invalid" && restart !== undefined ? <button onClick={restart}>Start from the first page</button> : null}
  </div>;
}

export function Empty({ title, children }: { title: string; children: ReactNode }) {
  return <div className="empty"><h3>{title}</h3><p>{children}</p></div>;
}
export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "neutral" | "good" | "warning" }) {
  return <span className={`badge ${tone}`}>{children}</span>;
}
export function Facts({ children }: { children: ReactNode }) { return <dl className="facts">{children}</dl>; }
export function Fact({ label, children, wide = false }: { label: string; children: ReactNode; wide?: boolean }) {
  return <div className={wide ? "fact wide" : "fact"}><dt>{label}</dt><dd>{children ?? "Not reported"}</dd></div>;
}
/** Exact already-disclosed identities remain available without making opaque values the primary label. */
export function TechnicalDetails({ fields, label = "Technical details" }: {
  fields: readonly { readonly label: string; readonly value: string | undefined }[]; label?: string
}) {
  const identity = JSON.stringify(fields.map(field => [field.label, field.value ?? null]));
  const current = useRef(identity);
  const generation = useRef(0);
  const [status, setStatus] = useState({ identity, message: "" });
  if (current.current !== identity) { current.current = identity; generation.current++; }
  useEffect(() => () => { generation.current++; }, []);
  async function copy(field: { readonly label: string; readonly value: string | undefined }) {
    const request = ++generation.current;
    if (field.value === undefined || field.value.length > 8192) { return; }
    let message: string;
    try {
      if (navigator.clipboard === undefined) { throw new Error("Clipboard unavailable."); }
      await navigator.clipboard.writeText(field.value);
      message = `${field.label} copied.`;
    } catch { message = "Copy is unavailable. Select the value to copy it."; }
    if (current.current === identity && generation.current === request) { setStatus({ identity, message }); }
  }
  return <details className="technical-details" key={identity}><summary>{label}</summary>
    <dl>{fields.map(field => <div className="technical-field" key={field.label}><dt>{field.label}</dt><dd>
      <code>{field.value ?? "Not reported"}</code>{field.value === undefined ? null
        : <button type="button" aria-label={`Copy ${field.label}`} disabled={field.value.length > 8192} onClick={() => { void copy(field); }}>Copy</button>}
    </dd></div>)}</dl><p className="copy-status" role="status" aria-live="polite" aria-atomic="true">{status.identity === identity ? status.message : ""}</p>
  </details>;
}

export function Timestamp({ value, compact = false }: { value: string | undefined; compact?: boolean }) {
  if (value === undefined) { return <>Not reported</>; }
  return <time dateTime={value} title={value}>{new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "medium", timeZone: "UTC" }).format(new Date(value))} UTC{compact ? null : <span className="raw-value">{value}</span>}</time>;
}

interface PageTrail { readonly current: string | null; readonly previous: readonly (string | null)[] }
function clearPaging() { try { sessionStorage.removeItem(pagingStorage); } catch { /* Browser context storage is optional. */ } }
function readTrail(scope: string, current: string | undefined, owner: string | undefined): readonly (string | undefined)[] {
  if (owner === undefined) { return []; }
  try {
    const raw = sessionStorage.getItem(pagingStorage);
    if (raw === null || raw.length > 131072) { return []; }
    const saved = JSON.parse(raw) as { version?: unknown; owner?: unknown; pages?: Record<string, PageTrail> };
    if (saved.version !== 1 || saved.owner !== owner || typeof saved.pages !== "object" || saved.pages === null || Array.isArray(saved.pages)) {
      clearPaging(); return [];
    }
    const item = saved.pages[scope];
    if (item === undefined || item.current !== (current ?? null) || !Array.isArray(item.previous) || item.previous.length > 32
      || item.previous.some(value => value !== null && (typeof value !== "string" || value.length > 2048 || /[^\x21-\x7e]/u.test(value)))) { return []; }
    return item.previous.map(value => value ?? undefined);
  } catch { return []; }
}
function saveTrail(scope: string, current: string | undefined, previous: readonly (string | undefined)[], owner: string | undefined) {
  if (owner === undefined) { return; }
  try {
    const raw = sessionStorage.getItem(pagingStorage);
    const existing = raw !== null && raw.length <= 131072 ? JSON.parse(raw) as { version?: unknown; owner?: unknown; pages?: unknown } : undefined;
    const entries = existing?.version === 1 && existing.owner === owner && typeof existing.pages === "object"
      && existing.pages !== null && !Array.isArray(existing.pages) ? Object.entries(existing.pages).slice(-7) : [];
    const all = Object.fromEntries(entries);
    all[scope] = { current: current ?? null, previous: previous.map(value => value ?? null) };
    const encoded = JSON.stringify({ version: 1, owner, pages: all });
    if (encoded.length <= 131072) { sessionStorage.setItem(pagingStorage, encoded); }
  } catch { /* Paging still works in memory when storage is unavailable. */ }
}

/** Bounded navigation context only; no records, credentials or permission hints are stored. */
export function usePages(scope: string, initial: string | undefined) {
  const { snapshot } = useProtectedPortal();
  const identity = snapshot.context === undefined ? undefined : portalContextKey(snapshot.context);
  const owner = snapshot.context === undefined ? undefined : `${snapshot.context.operatorId}.${snapshot.context.serviceAssociation}`;
  const previousIdentity = useRef(identity);
  const [page, setPage] = useState<{ scope: string; identity: string | undefined; cursor: string | undefined; previous: readonly (string | undefined)[] }>({ scope, identity, cursor: initial, previous: [] });
  useEffect(() => {
    const changed = snapshot.blocked || previousIdentity.current !== undefined && previousIdentity.current !== identity;
    previousIdentity.current = identity;
    if (changed) { clearPaging(); }
    setPage(previous => {
      const cursor = !changed && previous.scope === scope ? previous.cursor : undefined;
      return { scope, identity, cursor, previous: snapshot.blocked ? [] : readTrail(scope, cursor, owner) };
    });
  }, [scope, identity, owner, snapshot.blocked]);
  const value = page.scope === scope && !snapshot.blocked && (page.identity === undefined || page.identity === identity)
    ? page : { scope, identity, cursor: undefined, previous: [] };
  function change(cursor: string | undefined, previous: readonly (string | undefined)[]) {
    saveTrail(scope, cursor, previous, owner); setPage({ scope, identity, cursor, previous });
  }
  return { cursor: value.cursor, hasPrevious: value.previous.length > 0,
    next(cursor: string) { change(cursor, [...value.previous.slice(-31), value.cursor]); },
    previous() { if (value.previous.length > 0) { change(value.previous.at(-1), value.previous.slice(0, -1)); } },
    reset() { change(undefined, []); },
    restore(cursor: string | undefined) {
      const previousIndex = value.previous.lastIndexOf(cursor);
      change(cursor, previousIndex >= 0 ? value.previous.slice(0, previousIndex) : readTrail(scope, cursor, owner));
    }
  };
}
export function Pager({ pages, nextCursor, busy, label = "Current page", changed }: {
  pages: ReturnType<typeof usePages>; nextCursor: string | undefined; busy: boolean; label?: string; changed?: () => void
}) {
  return <nav className="pager" aria-label={`${label} pagination`}><span>{label}</span>
    <button disabled={busy || !pages.hasPrevious} onClick={() => { changed?.(); pages.previous(); }}>Previous</button>
    <button disabled={busy || nextCursor === undefined} onClick={() => { if (nextCursor !== undefined) { changed?.(); pages.next(nextCursor); } }}>Next</button>
    {pages.cursor !== undefined && !pages.hasPrevious ? <button disabled={busy} onClick={() => { changed?.(); pages.reset(); }}>First page</button> : null}
  </nav>;
}

export function queryUrl(path: string, values: Record<string, string | undefined>) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) { if (value !== undefined && value !== "") { query.set(key, value); } }
  return path + (query.size === 0 ? "" : `?${query.toString()}`);
}
export function Panel({ open, close, title, eyebrow, subtitle, children, footer, origin, fallback, wide = false }: {
  open: boolean; close: () => void; title: string; eyebrow: string; subtitle: string; children: ReactNode; footer?: ReactNode;
  origin: HTMLElement | null; fallback: HTMLElement | null; wide?: boolean
}) {
  const { snapshot } = useProtectedPortal();
  const masked = snapshot.verification !== undefined;
  return <Dialog.Root modal={!masked} open={open} onOpenChange={value => { if (!value) { close(); } }}>
    <Dialog.Portal><Dialog.Overlay className="panel-shade" data-session-masked={masked ? "true" : undefined} />
      <Dialog.Content className={wide ? "detail-panel detail-panel-wide" : "detail-panel"}
        data-session-masked={masked ? "true" : undefined} inert={masked} onCloseAutoFocus={event => {
      event.preventDefault(); (origin?.isConnected ? origin : fallback)?.focus();
    }}>
      <header className="panel-header"><span className="eyebrow">{eyebrow}</span><Dialog.Close asChild><button className="close-panel">Close details</button></Dialog.Close>
        <Dialog.Title asChild><h2>{title}</h2></Dialog.Title><Dialog.Description>{subtitle}</Dialog.Description></header>
      <div className="panel-content">{children}</div><footer className="panel-footer">{footer ?? "Read-only details"}</footer>
    </Dialog.Content></Dialog.Portal>
  </Dialog.Root>;
}
